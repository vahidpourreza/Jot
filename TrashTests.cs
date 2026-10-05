using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyTrash(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="trash-"+name,passed});
        var store=new NoteStore(Path.Combine(testOutput,"trash-storage"));
        var id=await store.Create();const string html="<p dir=\"rtl\"><b>سلام</b> Jot</p><p><img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=\"></p>";
        await store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Title different from file",group="Projects",color="blue",icon="emoji:☕"}));
        await store.SaveNote(JsonSerializer.SerializeToElement(new{id,html,plain="سلام Jot",updatedAt=12345L}));
        await store.SaveNotePreferences(id,JsonSerializer.SerializeToElement(new{fontSize=21,lineHeight=1.4,toolbarVisible=false,pinned=true}));
        await store.UpdateLibraryNotes([id],"pin",true);
        var external=Path.Combine(store.Root,"original.jot");await File.WriteAllTextAsync(external,"External file must not change");
        await store.BindNoteFile(id,external,"jot","original","original");await store.Delete(id);
        var archived=(await store.LoadTrash())[0];var key=archived.GetProperty("key").GetString()!;
        Check("list-shows-deleted-note-without-rich-body",await store.CountTrash()==1&&archived.GetProperty("title").GetString()=="Title different from file"&&archived.GetProperty("canRestore").GetBoolean()&&!archived.TryGetProperty("html",out _));
        await store.RemoveFolder("Projects");
        // Simulate a crash/locked file between the durable restore and cleanup.
        using(var locked=new FileStream(Path.Combine(store.Root,"trash",key),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Check("restore-commits-despite-archive-cleanup-lock",await store.RestoreTrash([key])==1&&File.Exists(Path.Combine(store.Root,"trash",key))&&await store.CountTrash()==0);
        }
        var restored=(await store.LoadNote(id))!.Value;
        Check("restore-preserves-rich-text-image-title-emoji-folder",restored.GetProperty("html").GetString()==html&&restored.GetProperty("plain").GetString()=="سلام Jot"&&restored.GetProperty("title").GetString()=="Title different from file"&&restored.GetProperty("icon").GetString()=="emoji:☕"&&restored.GetProperty("group").GetString()=="Projects");
        Check("restore-preserves-typography-and-library-pin",restored.GetProperty("view").GetProperty("fontSize").GetInt32()==21&&restored.GetProperty("view").GetProperty("lineHeight").GetDouble()==1.4&&!restored.GetProperty("view").GetProperty("toolbarVisible").GetBoolean()&&restored.GetProperty("view").GetProperty("pinned").GetBoolean()&&restored.GetProperty("libraryPinned").GetBoolean());
        Check("restore-does-not-reconnect-or-overwrite-external-file",!restored.TryGetProperty("noteFile",out _)&&!restored.TryGetProperty("fileDirty",out _)&&await File.ReadAllTextAsync(external)=="External file must not change");
        Check("restore-retry-is-idempotent",await store.RestoreTrash([key])==0&&!File.Exists(Path.Combine(store.Root,"trash",key))&&(await store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==1);
        await store.Delete(id);var secondKey=(await store.LoadTrash())[0].GetProperty("key").GetString()!;
        // A stale archive must never overwrite a live note sharing the old ID.
        await store.Save(JsonSerializer.SerializeToElement(new{version=2,notes=new[]{new{id,title="Live replacement",html="<p>Keep me</p>",plain="Keep me",updatedAt=9L}},prefs=NoteStore.Defaults()}));
        await store.RestoreTrash([secondKey]);
        Check("id-collision-restores-as-copy-without-overwrite",(await store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Keep me"&&(await store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==2);

        var a=await store.Create();var b=await store.Create();await store.UpdateLibraryNotes([a,b],"delete");
        var pending=(await store.LoadTrash()).EnumerateArray().Select(item=>item.GetProperty("key").GetString()!).ToArray();
        await File.WriteAllTextAsync(Path.Combine(store.Root,"trash",pending[1]),"{broken archive");
        Check("corrupt-copy-stays-visible-but-is-not-restorable",(await store.LoadTrash()).EnumerateArray().Any(item=>!item.GetProperty("canRestore").GetBoolean()));
        var rejected=false;try{await store.RestoreTrash(pending);}catch(JsonException){rejected=true;}
        Check("bulk-validation-failure-keeps-every-archive-and-note",rejected&&await store.CountTrash()==2&&!await store.Contains(a)&&!await store.Contains(b));
        rejected=false;try{await store.DeleteTrash(["../original.jot"]);}catch(InvalidDataException){rejected=true;}
        Check("path-traversal-is-rejected",rejected&&await File.ReadAllTextAsync(external)=="External file must not change");
        Check("permanent-delete-removes-only-selected-recovery-copy",await store.DeleteTrash([pending[1]])==1&&await store.CountTrash()==1&&await File.ReadAllTextAsync(external)=="External file must not change");
        await SetStoreTrigger(store,"CREATE TRIGGER fail_trash_restore BEFORE UPDATE OF initialized ON app_state BEGIN SELECT RAISE(ABORT,'isolated restore rollback');END;");
        rejected=false;try{await store.RestoreTrash([pending[0]]);}catch(Microsoft.Data.Sqlite.SqliteException){rejected=true;}
        await SetStoreTrigger(store,"DROP TRIGGER fail_trash_restore;");
        Check("restore-transaction-failure-keeps-recovery-copy",rejected&&await store.CountTrash()==1&&!await store.Contains(a)&&!await store.Contains(b));

        var legacy=new NoteStore(Path.Combine(testOutput,"trash-legacy-id"));var legacyId=Guid.NewGuid().ToString("N");
        await legacy.Save(JsonSerializer.SerializeToElement(new{version=2,notes=new[]{new{id=legacyId,html="<p>Legacy ID</p>",plain="Legacy ID",updatedAt=1L}},prefs=NoteStore.Defaults()}));
        await legacy.Delete(legacyId);var canonical=(await legacy.LoadTrash())[0].GetProperty("key").GetString()!;
        Check("new-archive-normalizes-legacy-id",canonical.StartsWith(Guid.Parse(legacyId).ToString("D"),StringComparison.Ordinal));
        var legacyKey=legacyId+canonical[36..];File.Move(Path.Combine(legacy.Root,"trash",canonical),Path.Combine(legacy.Root,"trash",legacyKey));
        Check("legacy-guid-n-archive-is-discoverable-and-restorable",(await legacy.LoadTrash())[0].GetProperty("canRestore").GetBoolean()&&await legacy.RestoreTrash([legacyKey])==1&&await legacy.Contains(legacyId));
        var invalidId=Guid.NewGuid().ToString();var invalidKey=invalidId+"-"+Guid.NewGuid().ToString("N")+".json";
        await File.WriteAllTextAsync(Path.Combine(legacy.Root,"trash",invalidKey),JsonSerializer.Serialize(new{deletedAt=DateTimeOffset.UtcNow,note=new{id=invalidId,html="<p>Bad metadata</p>",plain="Bad metadata",color="not-a-color"}}));
        Check("invalid-metadata-is-contained-to-one-trash-item",(await legacy.LoadTrash()).EnumerateArray().Single().GetProperty("canRestore").GetBoolean()==false);

        var sessionFixture=new JotSession(true,Path.Combine(testOutput,"trash-ui")){ExerciseLifecycle=true};
        try
        {
            var deleted=await sessionFixture.Store.Create();await sessionFixture.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=deleted,title="Recover this note"}));
            await sessionFixture.Store.SaveNote(JsonSerializer.SerializeToElement(new{id=deleted,html="<p>A draft stays safe here until you restore or remove it.</p>",plain="A draft stays safe here until you restore or remove it.",updatedAt=123L}));await sessionFixture.Store.Delete(deleted);
            var home=sessionFixture.Home();await home.WaitFor("window.jotReady===true");home.Width=900;home.Height=650;home.UpdateLayout();
            await home.Script("document.querySelector('#sidebarChoices [data-scope=trash]').click()");
            await home.WaitFor("!document.getElementById('trashStatus').getAttribute('aria-busy')||document.getElementById('trashStatus').getAttribute('aria-busy')==='false'");
            await home.WaitFor("document.querySelectorAll('#trashCards .library-trash-item').length===1");
            Check("navigation-and-recovery-actions-are-visible",await home.Script("document.getElementById('folderLabel').textContent==='Trash'&&document.getElementById('cards').hidden&&!document.getElementById('trashCards').hidden&&!!document.querySelector('#folderChoices [data-scope=trash]')") =="true");
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{320d,520d,900d})
            {
                await sessionFixture.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await sessionFixture.Changed();
                home.MinWidth=320;home.Width=width;home.UpdateLayout();await Task.Delay(150);await home.WaitFor("document.querySelectorAll('#trashCards .library-trash-item').length===1");
                Check("responsive-trash-actions-"+theme+"-"+width,await home.Script("(()=>{const row=document.querySelector('.library-trash-item');return document.body.scrollWidth<=innerWidth&&[...row.querySelectorAll('button')].every(b=>b.getBoundingClientRect().right<=innerWidth);})()") =="true");
                if(width!=520)await home.Capture("trash-"+theme+"-"+width);
            }
            await home.Script("document.querySelector('#trashCards [data-action=delete]').click()");await home.WaitFor("document.getElementById('libraryConfirm').open");
            Check("permanent-delete-requires-explicit-confirmation",await sessionFixture.Store.CountTrash()==1&&await home.Script("document.getElementById('confirmDescription').textContent.includes('cannot be undone')") =="true");
            await home.Script("document.getElementById('confirmCancel').click();document.querySelector('#trashCards [data-action=restore]').click()");
            await home.WaitFor("document.querySelectorAll('#trashCards .library-trash-item').length===0&&!document.getElementById('homeEmpty').hidden");
            Check("restore-updates-home-and-empty-state",await sessionFixture.Store.Contains(deleted)&&await home.Script("homeData.notes.some(note=>note.title==='Recover this note')&&document.getElementById('emptyTitle').textContent==='Trash is empty'")=="true");
            await sessionFixture.Store.Delete(deleted);await sessionFixture.Changed(false);
            await home.WaitFor("document.querySelectorAll('#trashCards .library-trash-item').length===1");
            await home.Script("document.querySelector('#trashCards .trash-select').click();document.getElementById('bulkDelete').click()");await home.WaitFor("document.getElementById('libraryConfirm').open");
            await home.Script("document.getElementById('confirmApply').click()");await home.WaitFor("!document.getElementById('libraryConfirm').open&&document.querySelectorAll('#trashCards .library-trash-item').length===0");
            Check("bulk-permanent-delete-updates-trash",await sessionFixture.Store.CountTrash()==0&&!await sessionFixture.Store.Contains(deleted));
            Check("no-runtime-errors-or-user-visible-test-windows",home.RuntimeErrors.Count==0&&home.Left< -10000&&home.Top< -10000&&home.Opacity==0&&!home.ShowInTaskbar);
        }
        finally{foreach(var window in sessionFixture.Windows.ToArray())window.ClosePermanently();}

        var unavailable=new JotSession(true,Path.Combine(testOutput,"trash-unavailable-home")){ExerciseLifecycle=true};
        try
        {
            var live=await unavailable.Store.Create();await File.WriteAllTextAsync(Path.Combine(unavailable.Store.Root,"trash"),"Unavailable Trash folder fixture");
            var home=unavailable.Home();await home.WaitFor("window.jotReady===true");
            Check("unavailable-trash-does-not-block-normal-home",await home.Script("homeData.notes.length===1&&document.querySelectorAll('#cards .note-card').length===1")=="true"&&await unavailable.Store.Contains(live));
            await home.Script("document.querySelector('#sidebarChoices [data-scope=trash]').click()");
            await home.WaitFor("!document.getElementById('trashRetry').hidden");
            Check("unavailable-trash-has-retry-and-preserves-library",await home.Script("document.getElementById('trashStatusText').textContent.includes('unavailable')")=="true");
            await home.Script("window.trashRefreshResult=null;refresh().then(()=>window.trashRefreshResult=true,()=>window.trashRefreshResult=false)");await home.WaitFor("window.trashRefreshResult!==null");
            Check("trash-view-load-failure-does-not-reject-home-initialization",await home.Script("window.trashRefreshResult")=="true");
            await home.Script("document.querySelector('#sidebarChoices [data-scope=all]').click()");
            Check("can-return-to-notes-after-trash-error",await home.Script("!document.getElementById('cards').hidden&&document.querySelectorAll('#cards .note-card').length===1")=="true");
        }
        finally{foreach(var window in unavailable.Windows.ToArray())window.ClosePermanently();}
    }
}
