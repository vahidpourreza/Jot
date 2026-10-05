using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

public partial class MainWindow
{
    private static Task SetStoreTrigger(NoteStore target,string sql)=>Task.Run(()=>{
        using var connection=target.Connect(target.FilePath);using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();
    });
    private static Task<object?> StoreScalar(NoteStore target,string sql)=>Task.Run(()=>{
        using var connection=target.Connect(target.FilePath);using var command=connection.CreateCommand();command.CommandText=sql;return command.ExecuteScalar();
    });
    private static async Task<string> Digest(string path)=>Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
    private async Task VerifySqliteStorage(List<object> checks)
    {
        var directory=Path.Combine(testOutput,"sqlite-storage");Directory.CreateDirectory(directory);
        void Check(string name,bool passed)=>checks.Add(new{name="sqlite-"+name,passed});
        var fresh=new NoteStore(Path.Combine(directory,"fresh"));
        Check("fresh-store-has-no-imported-data",await fresh.Load() is null);
        var engine=(string)(await StoreScalar(fresh,"SELECT sqlite_version();"))!;
        Check("bundled-engine-includes-wal-corruption-fix",Version.Parse(engine)>=new Version(3,51,3));
        checks.Add(new{name="sqlite-runtime-version",passed=true,version=engine});
        Check("wal-and-foreign-keys-enabled",(string)(await StoreScalar(fresh,"PRAGMA journal_mode;"))! =="wal"&&Convert.ToInt64(await StoreScalar(fresh,"PRAGMA foreign_keys;"))==1);
        Check("normalized-storage-tables",Convert.ToInt64(await StoreScalar(fresh,"SELECT count(*) FROM sqlite_schema WHERE type='table' AND name IN('notes','groups','settings','app_state');"))==4);
        var first=Guid.NewGuid().ToString();var second=Guid.NewGuid().ToString();
        const string html="<p dir=\"rtl\">ورودی <bdi dir=\"ltr\">Money.Currency</bdi> حذف شد.</p><p><img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=\"></p>";
        var fixture=JsonSerializer.SerializeToElement(new{
            version=2,activeId=second,customRoot="kept",
            prefs=new{theme="light",fontSize=17,lineHeight=1.75,accent="teal",toolbarVisible=false,language="fa",customSetting="kept"},
            notes=new object[]{new{id=first,title="عنوان Persian",group="Project ' A",html,plain="ورودی Money.Currency حذف شد.",legacyTitle="Old title",updatedAt=123L,customNote="kept"},
                new{id=second,title="English",group="Project ' A",color="blue",html="<p>English & فارسی</p>",plain="English & فارسی",updatedAt=456L}}
        });
        var migrated=new NoteStore(Path.Combine(directory,"migration"));Directory.CreateDirectory(migrated.Root);
        await File.WriteAllTextAsync(migrated.LegacyFilePath,fixture.GetRawText(),new UTF8Encoding(false));
        await File.WriteAllTextAsync(migrated.LegacyFilePath+".bak","original backup sentinel");
        var jsonHash=await Digest(migrated.LegacyFilePath);var oldBackupHash=await Digest(migrated.LegacyFilePath+".bak");
        var data=(await migrated.Load())!.Value;var a=data.GetProperty("notes")[0];var b=data.GetProperty("notes")[1];
        Check("migration-original-json-and-backup-byte-identical",jsonHash==await Digest(migrated.LegacyFilePath)&&oldBackupHash==await Digest(migrated.LegacyFilePath+".bak"));
        Check("migration-real-sqlite-header",Encoding.ASCII.GetString((await File.ReadAllBytesAsync(migrated.FilePath))[..16])=="SQLite format 3\0");
        Check("migration-preserves-order-ids-active-note",data.GetProperty("notes").GetArrayLength()==2&&a.GetProperty("id").GetString()==first&&b.GetProperty("id").GetString()==second&&data.GetProperty("activeId").GetString()==second);
        Check("migration-preserves-mixed-text-rich-html-embedded-image",a.GetProperty("html").GetString()==html&&a.GetProperty("plain").GetString()=="ورودی Money.Currency حذف شد.");
        Check("migration-preserves-title-group-timestamps",a.GetProperty("title").GetString()=="عنوان Persian"&&a.GetProperty("group").GetString()=="Project ' A"&&a.GetProperty("legacyTitle").GetString()=="Old title"&&a.GetProperty("updatedAt").GetInt64()==123);
        Check("migration-preserves-extensions",data.GetProperty("customRoot").GetString()=="kept"&&a.GetProperty("customNote").GetString()=="kept"&&data.GetProperty("prefs").GetProperty("customSetting").GetString()=="kept");
        Check("migration-preserves-colors-and-neutral-english-ui",a.GetProperty("color").GetString()=="teal"&&b.GetProperty("color").GetString()=="blue"&&data.GetProperty("prefs").GetProperty("accent").GetString()=="neutral"&&data.GetProperty("prefs").GetProperty("language").GetString()=="en");
        Check("migration-preserves-writing-preferences",data.GetProperty("prefs").GetProperty("fontSize").GetInt32()==17&&data.GetProperty("prefs").GetProperty("lineHeight").GetDouble()==1.75&&!data.GetProperty("prefs").GetProperty("toolbarVisible").GetBoolean());
        Check("groups-deduplicated-with-parameterized-names",Convert.ToInt64(await StoreScalar(migrated,"SELECT count(*) FROM groups;"))==1);
        Check("migration-published-only-validated-data",(string)(await StoreScalar(migrated,"PRAGMA integrity_check;"))! =="ok"&&File.Exists(migrated.BackupPath)&&Directory.GetFiles(migrated.Root,"jot-initializing-*").Length==0);
        var metadata=await migrated.LoadIndex();
        Check("index-excludes-embedded-html",!metadata!.Value.GetProperty("notes")[0].TryGetProperty("html",out _)&&metadata.Value.GetProperty("notes")[0].GetProperty("hasImage").GetBoolean());
        await SetStoreTrigger(migrated,"CREATE TABLE changes(id TEXT);CREATE TRIGGER count_content_update AFTER UPDATE OF html ON notes BEGIN INSERT INTO changes(id) VALUES(new.id);END;");
        await migrated.SaveMetadata(JsonSerializer.SerializeToElement(new{id=first,title="Renamed",group="New group",color="amber"}));
        await migrated.SaveNote(JsonSerializer.SerializeToElement(new{id=first,title="Stale title",group="Stale group",color="red",html="<p>Updated فارسی</p>",plain="Updated فارسی",updatedAt=999}));
        a=(await migrated.LoadNote(first))!.Value;
        Check("single-note-update-does-not-rewrite-other-notes",Convert.ToInt64(await StoreScalar(migrated,"SELECT count(*) FROM changes;"))==1&&(await migrated.LoadNote(second))!.Value.GetRawText()==b.GetRawText());
        Check("content-save-preserves-index-owned-metadata",a.GetProperty("title").GetString()=="Renamed"&&a.GetProperty("group").GetString()=="New group"&&a.GetProperty("color").GetString()=="amber");
        var prior=a.GetRawText();
        await SetStoreTrigger(migrated,"CREATE TRIGGER fail_transaction BEFORE UPDATE OF active_id ON app_state BEGIN SELECT RAISE(ABORT,'synthetic failure after note update');END;");
        bool rejected=false;
        try{await migrated.SaveNote(JsonSerializer.SerializeToElement(new{id=first,html="must rollback",plain="must rollback",updatedAt=1000}));}catch(SqliteException){rejected=true;}
        await SetStoreTrigger(migrated,"DROP TRIGGER fail_transaction;");
        Check("transaction-rolls-back-earlier-statements",rejected&&(await migrated.LoadNote(first))!.Value.GetRawText()==prior);
        await File.WriteAllTextAsync(migrated.LegacyFilePath,"changed legacy JSON must never overwrite SQLite");
        var reopened=new NoteStore(migrated.Root);Check("migration-is-one-time-and-database-authoritative",(await reopened.LoadNote(first))!.Value.GetRawText()==prior);
        await reopened.Import(fixture);Check("legacy-import-cannot-overwrite-initialized-database",(await reopened.LoadNote(first))!.Value.GetRawText()==prior);
        await migrated.Backup();
        using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=migrated.BackupPath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            backup.Open();using var command=backup.CreateCommand();command.CommandText="PRAGMA integrity_check;";Check("backup-is-independent-valid-sqlite",(string)command.ExecuteScalar()! =="ok");
            command.CommandText="SELECT html FROM notes WHERE id=$id;";command.Parameters.AddWithValue("$id",first);Check("backup-includes-latest-committed-note",(string)command.ExecuteScalar()! =="<p>Updated فارسی</p>");
        }
        var backupHash=await Digest(migrated.BackupPath);Directory.CreateDirectory(migrated.BackupPath+".tmp");rejected=false;
        try{await migrated.Backup();}catch(SqliteException){rejected=true;}
        Check("failed-backup-keeps-last-good-copy",rejected&&await Digest(migrated.BackupPath)==backupHash);Directory.Delete(migrated.BackupPath+".tmp");
        // Concurrent windows and multiple store instances share one per-root worker gate.
        var ids=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>fresh.Create()));
        await Task.WhenAll(ids.Select(async(id,index)=>{
            var writer=new NoteStore(fresh.Root);
            await Task.WhenAll(writer.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Title "+index,group="Shared"})),fresh.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>"+index+" فارسی</p>",plain=index+" فارسی",updatedAt=index})));
        }));
        var parallel=(await fresh.Load())!.Value.GetProperty("notes").EnumerateArray().ToArray();
        Check("concurrent-window-writes-have-no-lost-updates",parallel.Length==12&&parallel.All(n=>n.GetProperty("title").GetString()=="Title "+Array.IndexOf(ids,n.GetProperty("id").GetString()!)&&n.GetProperty("plain").GetString()==Array.IndexOf(ids,n.GetProperty("id").GetString()!)+" فارسی"));
        Check("concurrent-group-creation-remains-unique",Convert.ToInt64(await StoreScalar(fresh,"SELECT count(*) FROM groups WHERE name='Shared';"))==1);
        await fresh.SaveMetadata(JsonSerializer.SerializeToElement(new{id=ids[0],group="*"}));Check("literal-star-group-retained",(await fresh.LoadNote(ids[0]))!.Value.GetProperty("group").GetString()=="*");
        await fresh.Delete(ids[0]);rejected=false;try{await fresh.SaveNote(JsonSerializer.SerializeToElement(new{id=ids[0],html="stale",plain="stale",updatedAt=0}));}catch(InvalidDataException){rejected=true;}
        Check("deleted-note-cannot-be-resurrected",rejected&&!await fresh.Contains(ids[0])&&await fresh.LoadNote(ids[0]) is null);
        Check("empty-folder-is-kept",Convert.ToInt64(await StoreScalar(fresh,"SELECT count(*) FROM groups WHERE name='*';"))==1);
        var contentBeforeLock=(await fresh.LoadNote(ids[1]))!.Value.GetRawText();
        using(var lockConnection=fresh.Connect(fresh.FilePath))
        using(var writeLock=lockConnection.BeginTransaction())
        {
            var impatient=new NoteStore(fresh.Root){BusyTimeoutSeconds=1};var watch=Stopwatch.StartNew();rejected=false;
            try{await impatient.SaveNote(JsonSerializer.SerializeToElement(new{id=ids[1],html="busy",plain="busy",updatedAt=9}));}catch(SqliteException ex) when(ex.SqliteErrorCode is 5 or 6){rejected=true;}
            Check("busy-database-fails-with-bounded-wait",rejected&&watch.Elapsed.TotalSeconds<8);writeLock.Rollback();
        }
        Check("busy-save-preserves-existing-content",(await fresh.LoadNote(ids[1]))!.Value.GetRawText()==contentBeforeLock);
        // Reopening after disposing an uncommitted transaction recovers the committed state.
        using(var connection=fresh.Connect(fresh.FilePath))
        using(var tx=connection.BeginTransaction())
        {using var command=connection.CreateCommand();command.Transaction=tx;command.CommandText="UPDATE notes SET html='uncommitted';";command.ExecuteNonQuery();}
        Check("abandoned-transaction-does-not-persist",(await new NoteStore(fresh.Root).LoadNote(ids[1]))!.Value.GetRawText()==contentBeforeLock);
        foreach(var kind in new[]{"invalid-json","duplicate-id","future-json","invalid-timestamp"})
        {
            var bad=new NoteStore(Path.Combine(directory,kind));Directory.CreateDirectory(bad.Root);var broken=JsonNode.Parse(fixture.GetRawText())!.AsObject();
            if(kind=="duplicate-id")broken["notes"]!.AsArray().Add(broken["notes"]![0]!.DeepClone());
            if(kind=="future-json")broken["version"]=99;
            if(kind=="invalid-timestamp")broken["notes"]![0]!["updatedAt"]="invalid";
            await File.WriteAllTextAsync(bad.LegacyFilePath,kind=="invalid-json"?"invalid legacy":broken.ToJsonString());var beforeHash=await Digest(bad.LegacyFilePath);rejected=false;
            try{await bad.Load();}catch(Exception ex) when(ex is JsonException or InvalidDataException){rejected=true;}
            Check("rejects-"+kind+"-without-publishing-or-changing-original",rejected&&!File.Exists(bad.FilePath)&&await Digest(bad.LegacyFilePath)==beforeHash);
        }
        // A database from a newer app, or a damaged database, must never trigger JSON fallback.
        await SetStoreTrigger(migrated,"PRAGMA user_version=999;");rejected=false;
        try{await new NoteStore(migrated.Root).Load();}catch(InvalidDataException){rejected=true;}
        Check("future-schema-rejected-without-downgrade",rejected&&Convert.ToInt64(await StoreScalar(migrated,"PRAGMA user_version;"))==999);await SetStoreTrigger(migrated,"PRAGMA user_version=3;");
        var missing=migrated.FilePath+".moved-for-test";File.Move(migrated.FilePath,missing);rejected=false;
        try{await new NoteStore(migrated.Root).Load();}catch(InvalidDataException){rejected=true;}
        Check("missing-database-never-reimports-stale-json-over-backup",rejected&&!File.Exists(migrated.FilePath)&&File.Exists(migrated.BackupPath));File.Move(missing,migrated.FilePath);
        await File.WriteAllTextAsync(migrated.FilePath,"damaged database sentinel");rejected=false;
        try{await new NoteStore(migrated.Root).Load();}catch(SqliteException){rejected=true;}
        Check("corrupt-database-left-untouched-no-json-fallback",rejected&&await File.ReadAllTextAsync(migrated.FilePath)=="damaged database sentinel");
        // An obstructed destination must not consume the legacy source. A later retry works.
        var blocked=new NoteStore(Path.Combine(directory,"blocked-migration"));Directory.CreateDirectory(blocked.Root);await File.WriteAllTextAsync(blocked.LegacyFilePath,fixture.GetRawText());Directory.CreateDirectory(blocked.FilePath);rejected=false;
        try{await blocked.Load();}catch(IOException){rejected=true;}
        Check("blocked-migration-preserves-original",rejected&&await File.ReadAllTextAsync(blocked.LegacyFilePath)==fixture.GetRawText());Directory.Delete(blocked.FilePath);
        Check("failed-migration-can-retry",(await blocked.Load())!.Value.GetProperty("notes").GetArrayLength()==2);
        await VerifyAbruptSqliteExit(checks,directory);
    }

    private async Task VerifyAbruptSqliteExit(List<object> checks,string directory)
    {
        foreach(var mode in new[]{"committed","uncommitted"})
        {
            var target=new NoteStore(Path.Combine(directory,"crash-probe-"+mode));var id=await target.Create();
            await target.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>Original</p>",plain="Original",updatedAt=0}));
            var nonce=Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(Path.Combine(target.Root,"probe-owner.txt"),nonce);
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"--sqlite-crash-probe",target.Root,nonce,mode})info.ArgumentList.Add(arg);
            using var child=Process.Start(info)??throw new IOException("Could not start the isolated SQLite probe.");
            try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}
            catch{if(!child.HasExited)child.Kill();throw;}
            bool leftWal=File.Exists(target.FilePath+"-wal");var note=(await new NoteStore(target.Root).LoadNote(id))!.Value;
            checks.Add(new{name="sqlite-forced-process-exit-"+mode+"-recovers-correct-content",passed=leftWal&&child.ExitCode is not (20 or 21 or 22 or 23 or 24)&&note.GetProperty("plain").GetString()==(mode=="committed"?"committed":"Original")&&(string)(await StoreScalar(target,"PRAGMA integrity_check;"))! =="ok"});
        }
    }
}
