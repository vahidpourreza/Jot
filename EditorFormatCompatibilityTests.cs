using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyEditorFormatCompatibility(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="editor-format-"+name,passed,evidence});
        var root=Path.Combine(testOutput,"editor-format-compatibility");Directory.CreateDirectory(root);
        static object? RawScalar(string path,string sql)
        {
            using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;return command.ExecuteScalar();
        }
        static byte[] VersionOne(JsonElement note)
        {
            var document=JsonNode.Parse(NoteFileFormat.Encode(note,"jot"))!.AsObject();document["version"]=1;
            return JsonSerializer.SerializeToUtf8Bytes(document,new JsonSerializerOptions{WriteIndented=true});
        }

        var source=new NoteStore(Path.Combine(root,"v2-database"));var id=await source.Create();
        const string html="<p dir=\"rtl\" data-jot-direction=\"rtl\">متن می‌شود <strong>original</strong></p><p><img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=\"></p>";
        await source.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Independent title",color="teal",icon="emoji:📝"}));
        await source.SaveNote(JsonSerializer.SerializeToElement(new{id,html,plain="متن می‌شود original",updatedAt=17}));
        await source.SaveNotePreferences(id,JsonSerializer.SerializeToElement(new{fontSize=19,lineHeight=2.2}));
        var prior=(await source.LoadNote(id))!.Value;
        var path=Path.Combine(root,"Different old filename.jot");
        var comparable=NoteStore.FileDocument(prior,await source.LoadPreferences(),path,"jot");
        var oldBytes=VersionOne(comparable);await File.WriteAllBytesAsync(path,oldBytes);
        await source.BindNoteFile(id,path,"jot",NoteFileFormat.Digest(oldBytes),NoteFileFormat.Digest(oldBytes));
        prior=(await source.LoadNote(id))!.Value;
        var priorBinding=prior.GetProperty("noteFile").GetRawText();
        await SetStoreTrigger(source,"PRAGMA user_version=2;");
        var upgraded=new NoteStore(source.Root);var after=(await upgraded.LoadNote(id))!.Value;
        var backup=source.FilePath+".before-v3.bak";
        Check("v2-upgrades-to-v3-without-changing-note-fields",Convert.ToInt64(await StoreScalar(upgraded,"PRAGMA user_version;"))==3&&after.GetRawText()==prior.GetRawText());
        Check("permanent-backup-is-valid-independent-v2",File.Exists(backup)&&Convert.ToInt64(RawScalar(backup,"PRAGMA user_version;"))==2&&RawScalar(backup,"PRAGMA integrity_check;") as string=="ok"&&RawScalar(backup,"SELECT html FROM notes LIMIT 1;") as string==html);
        Check("old-reader-version-contract-refuses-upgraded-database",Convert.ToInt64(await StoreScalar(upgraded,"PRAGMA user_version;"))!=2);
        Check("upgrade-keeps-linked-v1-file-clean-and-byte-identical",!(await upgraded.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean()&&(await File.ReadAllBytesAsync(path)).SequenceEqual(oldBytes)&&after.GetProperty("noteFile").GetRawText()==priorBinding);
        // Force an actual dirty recomputation: matching body data must still use
        // the historical digest, not the new file envelope's different bytes.
        await upgraded.SaveNote(JsonSerializer.SerializeToElement(new{id,html,plain="متن می‌شود original",updatedAt=17}));
        Check("clean-v1-digest-survives-next-library-save",!(await upgraded.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean());
        var backupDigest=await Digest(backup);
        await upgraded.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>new editor change</p>",plain="new editor change"}));await upgraded.Backup();
        Check("permanent-v2-backup-survives-editing-and-normal-backups",await Digest(backup)==backupDigest&&RawScalar(backup,"SELECT html FROM notes LIMIT 1;") as string==html);

        var blocked=new NoteStore(Path.Combine(root,"blocked-backup"));var blockedId=await blocked.Create();
        await blocked.SaveNote(JsonSerializer.SerializeToElement(new{id=blockedId,html="<p>untouched</p>",plain="untouched"}));await SetStoreTrigger(blocked,"PRAGMA user_version=2;");
        var obstruction=blocked.FilePath+".before-v3.bak.tmp";Directory.CreateDirectory(obstruction);
        bool rejected=false;try{await new NoteStore(blocked.Root).Load();}catch(Exception error) when(error is IOException or SqliteException or UnauthorizedAccessException){rejected=true;}
        Check("backup-failure-prevents-version-bump-and-content-write",rejected&&Convert.ToInt64(await StoreScalar(blocked,"PRAGMA user_version;"))==2&&RawScalar(blocked.FilePath,"SELECT html FROM notes LIMIT 1;") as string=="<p>untouched</p>"&&!File.Exists(blocked.FilePath+".before-v3.bak"));
        Directory.Delete(obstruction);
        await new NoteStore(blocked.Root).Load();
        Check("failed-backup-upgrade-can-retry-safely",Convert.ToInt64(await StoreScalar(blocked,"PRAGMA user_version;"))==3);

        var invalidBackup=new NoteStore(Path.Combine(root,"invalid-backup"));await invalidBackup.Create();await SetStoreTrigger(invalidBackup,"PRAGMA user_version=2;");
        var invalidPath=invalidBackup.FilePath+".before-v3.bak";await File.WriteAllTextAsync(invalidPath,"do not overwrite this file");
        var invalidDigest=await Digest(invalidPath);rejected=false;
        try{await new NoteStore(invalidBackup.Root).Load();}catch(Exception error) when(error is IOException or SqliteException){rejected=true;}
        Check("invalid-existing-backup-blocks-without-overwrite",rejected&&Convert.ToInt64(await StoreScalar(invalidBackup,"PRAGMA user_version;"))==2&&await Digest(invalidPath)==invalidDigest);

        var retryStore=new NoteStore(Path.Combine(root,"retry-with-old-editor-edits"));var retryId=await retryStore.Create();
        await retryStore.SaveNote(JsonSerializer.SerializeToElement(new{id=retryId,html="<p>earliest source</p>",plain="earliest source"}));
        await SetStoreTrigger(retryStore,"PRAGMA user_version=2;");await new NoteStore(retryStore.Root).Load();
        var earliest=retryStore.FilePath+".before-v3.bak";var earliestDigest=await Digest(earliest);
        // Model a snapshot already created by a failed attempt, followed by a
        // legitimate old-editor write before retrying the same upgrade.
        await SetStoreTrigger(retryStore,"PRAGMA user_version=2;UPDATE notes SET html='<p>latest old editor edit</p>',plain='latest old editor edit',title='Latest title',updated_at=99;UPDATE note_preferences SET value=json_set(value,'$.fontSize',23);");
        var retryLoaded=(await new NoteStore(retryStore.Root).LoadNote(retryId))!.Value;
        var currentSnapshots=Directory.GetFiles(retryStore.Root,"jot.db.before-v3-current-*.bak");
        Check("retry-retains-earliest-immutable-backup",await Digest(earliest)==earliestDigest&&RawScalar(earliest,"SELECT html FROM notes LIMIT 1;") as string=="<p>earliest source</p>");
        Check("retry-snapshots-latest-body-and-metadata-before-bump",currentSnapshots.Length==1&&RawScalar(currentSnapshots[0],"SELECT html FROM notes LIMIT 1;") as string=="<p>latest old editor edit</p>"&&RawScalar(currentSnapshots[0],"SELECT title FROM notes LIMIT 1;") as string=="Latest title"&&Convert.ToInt64(RawScalar(currentSnapshots[0],"PRAGMA user_version;"))==2&&Convert.ToInt64(RawScalar(currentSnapshots[0],"SELECT json_extract(value,'$.fontSize') FROM note_preferences LIMIT 1;"))==23&&retryLoaded.GetProperty("title").GetString()=="Latest title"&&retryLoaded.GetProperty("html").GetString()=="<p>latest old editor edit</p>"&&Convert.ToInt64(await StoreScalar(retryStore,"PRAGMA user_version;"))==3);

        var versionOne=new NoteStore(Path.Combine(root,"v1-database"));var legacyId=await versionOne.Create();
        await versionOne.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=19,lineHeight=2.2}));
        await versionOne.SaveNote(JsonSerializer.SerializeToElement(new{id=legacyId,html,plain="old source"}));
        await SetStoreTrigger(versionOne,"DROP TABLE note_preferences;PRAGMA user_version=1;");
        var migrated=(await new NoteStore(versionOne.Root).LoadNote(legacyId))!.Value;
        Check("v1-upgrades-through-v2-and-retains-writing-values",Convert.ToInt64(await StoreScalar(versionOne,"PRAGMA user_version;"))==3&&migrated.GetProperty("html").GetString()==html&&migrated.GetProperty("view").GetProperty("fontSize").GetInt32()==19&&migrated.GetProperty("view").GetProperty("lineHeight").GetDouble()==2.2);
        Check("v1-and-v2-recovery-snapshots-are-both-retained",Convert.ToInt64(RawScalar(versionOne.FilePath+".before-v2.bak","PRAGMA user_version;"))==1&&Convert.ToInt64(RawScalar(versionOne.FilePath+".before-v3.bak","PRAGMA user_version;"))==2);

        await SetStoreTrigger(upgraded,"PRAGMA user_version=99;");rejected=false;
        try{await new NoteStore(upgraded.Root).Load();}catch(InvalidDataException){rejected=true;}
        Check("future-database-stays-fail-closed",rejected&&Convert.ToInt64(await StoreScalar(upgraded,"PRAGMA user_version;"))==99&&await Digest(backup)==backupDigest);

        var oldRead=await NoteFileFormat.Read(path);
        Check("v1-file-still-reads-with-exact-content-and-independent-title",oldRead.Note["html"]!.GetValue<string>()==html&&oldRead.Note["title"]!.GetValue<string>()=="Independent title"&&oldRead.Note["view"]!["fontSize"]!.GetValue<int>()==19);
        var importedStore=new NoteStore(Path.Combine(root,"v1-file-import"));var importedId=await importedStore.ImportNoteFile(oldRead,path);
        Check("importing-v1-file-is-clean-and-does-not-rewrite-file",!(await importedStore.LoadFileState(importedId))!.Value.GetProperty("dirty").GetBoolean()&&(await File.ReadAllBytesAsync(path)).SequenceEqual(oldBytes));
        Check("content-fingerprint-retains-legacy-envelope-encoding",NoteFileFormat.ContentDigest(comparable,"jot")==NoteFileFormat.Digest(oldBytes));

        const string rich="<ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"true\"><p>Keep checked</p></li></ul><p><sub>2</sub><sup>3</sup></p>";
        var richNote=JsonSerializer.SerializeToElement(new{title="Rich title",html=rich,plain="Keep checked\n23",icon="emoji:🧠",color="teal",view=new{fontSize=19,lineHeight=2.2}});
        var newBytes=NoteFileFormat.Encode(richNote,"jot");
        using(var document=JsonDocument.Parse(newBytes)){
            Check("new-file-envelope-is-v2-and-old-v1-reader-would-reject",document.RootElement.GetProperty("version").GetInt32()==2&&document.RootElement.GetProperty("format").GetString()=="jot-note");
            Check("new-file-preserves-all-content-fields",document.RootElement.GetProperty("note").GetProperty("html").GetString()==rich&&document.RootElement.GetProperty("note").EnumerateObject().Count()==6);
        }
        var richPath=Path.Combine(root,"New rich file.jot");await File.WriteAllBytesAsync(richPath,newBytes);var richRead=await NoteFileFormat.Read(richPath);
        Check("new-v2-file-roundtrips-task-state-and-marks",richRead.Note["html"]!.GetValue<string>()==rich&&richRead.Note["title"]!.GetValue<string>()=="Rich title"&&richRead.Note["icon"]!.GetValue<string>()=="emoji:🧠");
        Check("plain-text-export-unchanged",NoteFileFormat.Encode(richNote,"txt").SequenceEqual(new UTF8Encoding(false).GetBytes("Keep checked\n23")));
        var future=JsonNode.Parse(newBytes)!.AsObject();future["version"]=3;var futurePath=Path.Combine(root,"Future file.jot");await File.WriteAllTextAsync(futurePath,future.ToJsonString());rejected=false;
        try{await NoteFileFormat.Read(futurePath);}catch(InvalidDataException){rejected=true;}
        Check("future-jot-file-rejected",rejected&&JsonNode.Parse(await File.ReadAllTextAsync(futurePath))!["version"]!.GetValue<int>()==3);
    }
}
