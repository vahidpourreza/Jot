using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyLegacyFileState(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="legacy-file-state-"+name,passed});
        var directory=Path.Combine(testOutput,"legacy-file-state");
        var initial=new NoteStore(Path.Combine(directory,"data"));
        async Task<string> Legacy(string format,bool edited,string suffix)
        {
            var id=await initial.Create();
            await initial.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Original library title "+suffix}));
            await initial.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>Saved original.</p>",plain="Saved original.",updatedAt=1}));
            var note=(await initial.LoadNote(id))!.Value;
            var digest=NoteFileFormat.ContentDigest(note,format);
            var binding=JsonSerializer.Serialize(new{path=Path.Combine(directory,"Different file name "+suffix+"."+format),format,digest="external-byte-digest",contentDigest=digest});
            await Task.Run(()=>{
                using var connection=initial.Connect(initial.FilePath);using var command=connection.CreateCommand();
                command.CommandText="UPDATE notes SET extra=json_set(json_remove(extra,'$.fileDirty'),'$.noteFile',json($binding)),html=$html,plain=$plain WHERE id=$id;";
                command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$binding",binding);
                command.Parameters.AddWithValue("$html",edited?"<p>New unsaved edit.</p>":"<p>Saved original.</p>");
                command.Parameters.AddWithValue("$plain",edited?"New unsaved edit.":"Saved original.");command.ExecuteNonQuery();
            });
            return id;
        }
        var clean=await Legacy("jot",false,"clean");var dirty=await Legacy("jot",true,"dirty");
        var textClean=await Legacy("txt",false,"text-clean");var textDirty=await Legacy("txt",true,"text-dirty");
        var existing=await Legacy("jot",false,"known-state");
        await SetStoreTrigger(initial,"UPDATE notes SET extra=json_set(extra,'$.fileDirty',json('true')) WHERE id='"+existing+"';");
        var before=(await initial.LoadNote(clean))!.Value;
        var beforeBody=before.GetProperty("html").GetString();var beforeBinding=before.GetProperty("noteFile").GetRawText();
        // A fresh store instance exercises the normal first-write backup guard.
        var reopened=new NoteStore(initial.Root);
        var state=await reopened.LoadFileState(dirty);
        Check("first-state-read-detects-old-unsaved-edit",state is {} data&&data.GetProperty("dirty").GetBoolean());
        Check("first-migration-creates-recovery-backup",File.Exists(reopened.BackupPath));
        using(var backup=reopened.Connect(reopened.BackupPath))using(var command=backup.CreateCommand())
        {
            command.CommandText="SELECT COUNT(*) FROM notes WHERE json_type(extra,'$.noteFile')='object' AND json_type(extra,'$.fileDirty') IS NULL;";
            Check("backup-retains-all-pre-migration-bindings",Convert.ToInt64(command.ExecuteScalar())==4);
        }
        await reopened.InitializeFileDirtyState();
        Check("legacy-clean-jot-does-not-compare-new-filename-or-inherited-defaults",(await reopened.LoadFileState(clean))?.GetProperty("dirty").GetBoolean()==false);
        Check("legacy-text-clean-and-dirty-use-plain-text-digest",(await reopened.LoadFileState(textClean))?.GetProperty("dirty").GetBoolean()==false&&(await reopened.LoadFileState(textDirty))?.GetProperty("dirty").GetBoolean()==true);
        Check("existing-dirty-flags-are-not-reset",(await reopened.LoadFileState(existing))?.GetProperty("dirty").GetBoolean()==true);
        var after=(await reopened.LoadNote(clean))!.Value;
        Check("migration-does-not-rewrite-note-title-content-or-binding",after.GetProperty("title").GetString()==before.GetProperty("title").GetString()&&after.GetProperty("html").GetString()==beforeBody&&after.GetProperty("noteFile").GetRawText()==beforeBinding);
        var index=(await reopened.LoadIndex())!.Value.GetProperty("notes").EnumerateArray().ToArray();
        Check("library-projection-shows-initialized-flags",index.Single(n=>n.GetProperty("id").GetString()==dirty).GetProperty("fileDirty").GetBoolean()&&!index.Single(n=>n.GetProperty("id").GetString()==clean).GetProperty("fileDirty").GetBoolean());
        var tabs=await reopened.LoadTabHeaders([clean,dirty]);
        Check("tab-projection-shows-initialized-flags",!tabs[0].GetProperty("fileDirty").GetBoolean()&&tabs[1].GetProperty("fileDirty").GetBoolean());
        await reopened.InitializeFileDirtyState();
        Check("repeated-initialization-is-idempotent",(await reopened.LoadNote(clean))!.Value.GetRawText()==after.GetRawText());

        var transactionStore=new NoteStore(Path.Combine(directory,"transaction"));
        await transactionStore.Save(JsonSerializer.SerializeToElement(new{version=2,notes=new[]{before, (await initial.LoadNote(dirty))!.Value},prefs=NoteStore.Defaults()}));
        await SetStoreTrigger(transactionStore,"UPDATE notes SET extra=json_remove(extra,'$.fileDirty');CREATE TRIGGER fail_legacy_file_flags BEFORE UPDATE OF extra ON notes WHEN EXISTS(SELECT 1 FROM notes WHERE json_type(extra,'$.fileDirty') IN ('true','false')) BEGIN SELECT RAISE(ABORT,'synthetic legacy migration failure');END;");
        bool rejected=false;try{await transactionStore.InitializeFileDirtyState();}catch(Microsoft.Data.Sqlite.SqliteException){rejected=true;}
        Check("failed-migration-rolls-back-the-whole-batch",rejected&&Convert.ToInt64(await StoreScalar(transactionStore,"SELECT COUNT(*) FROM notes WHERE json_type(extra,'$.fileDirty') IS NULL;"))==2);
        await SetStoreTrigger(transactionStore,"DROP TRIGGER fail_legacy_file_flags;");
        await transactionStore.InitializeFileDirtyState();
        Check("failed-migration-can-retry-without-losing-unsaved-edits",(await transactionStore.LoadFileState(dirty))?.GetProperty("dirty").GetBoolean()==true&&(await transactionStore.LoadNote(dirty))!.Value.GetProperty("plain").GetString()=="New unsaved edit.");
    }
}
