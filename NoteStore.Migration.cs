using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

internal sealed partial class NoteStore
{
    private void EnsureDatabase()
    {
        if(File.Exists(FilePath)){observedDatabase=true;return;}
        if(observedDatabase||File.Exists(BackupPath))throw new InvalidDataException("Jot's database is missing. Restore a database backup; the old JSON will not be imported over newer notes.");
        if(Directory.Exists(FilePath))throw new IOException("Jot's database path is occupied by a folder.");
        Directory.CreateDirectory(Root);
        JsonElement? legacy=null;
        if(File.Exists(LegacyFilePath))
        {
            if(new FileInfo(LegacyFilePath).Length>64L*1024*1024)throw new InvalidDataException("The legacy notes file is too large to import automatically. It was left untouched.");
            using var input=new FileStream(LegacyFilePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            using var document=JsonDocument.Parse(input);Validate(document.RootElement);legacy=document.RootElement.Clone();
        }
        var staging=Path.Combine(Root,"jot-initializing-"+Guid.NewGuid().ToString("N")+".db");
        using(File.Open(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)){ }
        try
        {
            using(var connection=Connect(staging,true))
            {
                VerifyEngine(connection);
                Execute(connection,null,"""
                    CREATE TABLE groups(id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE COLLATE BINARY);
                    CREATE TABLE notes(
                      id TEXT PRIMARY KEY NOT NULL, title TEXT NOT NULL DEFAULT '',
                      group_id INTEGER REFERENCES groups(id), color TEXT NOT NULL,
                      html TEXT NOT NULL, plain TEXT NOT NULL, legacy_title TEXT NOT NULL DEFAULT '',
                      updated_at INTEGER NOT NULL, position INTEGER NOT NULL,
                      has_image INTEGER NOT NULL CHECK(has_image IN(0,1)), extra TEXT NOT NULL DEFAULT '{}');
                    CREATE INDEX notes_group ON notes(group_id);
                    CREATE INDEX notes_position ON notes(position,id);
                    CREATE INDEX notes_updated ON notes(updated_at DESC);
                    CREATE TABLE settings(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL);
                    CREATE TABLE app_state(singleton INTEGER PRIMARY KEY CHECK(singleton=1), initialized INTEGER NOT NULL DEFAULT 0,
                      active_id TEXT, extra TEXT NOT NULL DEFAULT '{}');
                    INSERT INTO app_state(singleton) VALUES(1);
                    """);
                Execute(connection,null,NotePreferencesSchema);
                Execute(connection,null,$"PRAGMA application_id={ApplicationId};PRAGMA user_version={SchemaVersion};");
                if(legacy is { } data)
                {
                    ReplaceModel(connection,PreserveLegacyWritingDefaults(data));
                    VerifyMigration(data,ReadModel(connection,null,false));
                }
                if(Scalar(connection,null,"PRAGMA quick_check;") as string!="ok")throw new InvalidDataException("The new database failed its integrity check. The original JSON was left untouched.");
                using var foreignKeys=Command(connection,null,"PRAGMA foreign_key_check;");using var reader=foreignKeys.ExecuteReader();
                if(reader.Read())throw new InvalidDataException("Database migration failed its relationship check.");
            }
            // Publish only a fully validated database. Never overwrite an existing one.
            FlushFile(staging);File.Move(staging,FilePath);observedDatabase=true;
            using var published=Connect(FilePath);EnableWal(published);
            if(legacy is not null){BackupCore(published);backedUpThisSession=true;}
        }
        finally
        {
            // These are unique files owned by this initialization attempt, never user originals.
            foreach(var suffix in new[]{"","-journal","-wal","-shm"})
                try{if(File.Exists(staging+suffix))File.Delete(staging+suffix);}catch(IOException){ }catch(UnauthorizedAccessException){ }
        }
    }
    private static void VerifyEngine(SqliteConnection connection)
    {
        var value=Scalar(connection,null,"SELECT sqlite_version();") as string;
        if(!Version.TryParse(value,out var version)||version<new Version(3,51,3))throw new InvalidOperationException("Jot requires SQLite 3.51.3 or newer. Use the complete, current app package.");
    }
    private static void EnableWal(SqliteConnection connection)
    {
        VerifyEngine(connection);
        if(!string.Equals(Scalar(connection,null,"PRAGMA journal_mode=WAL;") as string,"wal",StringComparison.OrdinalIgnoreCase))throw new IOException("Could not enable the database journal.");
    }
    private static void VerifyMigration(JsonElement source,JsonElement migrated)
    {
        var before=source.GetProperty("notes").EnumerateArray().ToArray();var after=migrated.GetProperty("notes").EnumerateArray().ToArray();
        if(before.Length!=after.Length)throw new InvalidDataException("Migration did not preserve every note.");
        for(int i=0;i<before.Length;i++)
            foreach(var key in new[]{"id","html","plain","title","group","legacyTitle","updatedAt"})
                if(before[i].TryGetProperty(key,out var old)&&old.ValueKind!=JsonValueKind.Null&&(!after[i].TryGetProperty(key,out var current)||!JsonNode.DeepEquals(JsonNode.Parse(old.GetRawText()),JsonNode.Parse(current.GetRawText()))))
                    throw new InvalidDataException("Migration verification failed. The original notes file was left untouched.");
    }
    private void BackupCore(SqliteConnection source,string? destinationPath=null)
    {
        var backupPath=destinationPath??BackupPath;
        var staging=backupPath+".tmp";
        // Fail on an obstructed backup path rather than silently claiming success.
        if(File.Exists(staging))File.Delete(staging);
        try
        {
            using(var destination=Connect(staging,true))
            {
                source.BackupDatabase(destination);
                Scalar(destination,null,"PRAGMA journal_mode=DELETE;");
                if(Scalar(destination,null,"PRAGMA quick_check;") as string!="ok")throw new InvalidDataException("Database backup verification failed.");
            }
            FlushFile(staging);
            if(File.Exists(backupPath))File.Replace(staging,backupPath,null);else File.Move(staging,backupPath);
        }
        finally{try{if(File.Exists(staging))File.Delete(staging);}catch(IOException){ }catch(UnauthorizedAccessException){ }}
    }
    private static void FlushFile(string path)
    {using var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);stream.Flush(true);}
}
