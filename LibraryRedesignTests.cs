using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyLibraryRedesign(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="library-redesign-"+name,passed});
        var s=new JotSession(true,Path.Combine(testOutput,"library-redesign")){ExerciseLifecycle=true};
        try
        {
            // This fixture deliberately keeps a separate draft editor beside Home.
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var ids=new List<string>();
            foreach(var title in new[]{"Project decisions","Release checklist","Ideas for next week"})
            {
                var id=await s.Store.Create();ids.Add(id);
                await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title}));
                await s.Store.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>"+title+"</p><p>Keep the details close and the next step clear.</p>",plain=title+"\nKeep the details close and the next step clear.",updatedAt=1000*ids.Count}));
            }
            await s.Store.CreateFolder("Projects");await s.Store.CreateFolder("Personal");
            var home=s.Home();await home.WaitFor("window.jotReady===true");home.MinWidth=320;
            Check("file-open-is-not-a-toolbar-button",await home.Script("!document.getElementById('homeOpenFile')")=="true");
            await home.RightClickControl("[data-note-id='"+ids[0]+"'] .card-open");
            await home.ClickControl("#contentContextMenu [data-action=pin-library]");
            await home.WaitFor("homeData.notes.find(n=>n.id==='"+ids[0]+"').libraryPinned===true");
            Check("pin-moves-note-to-top-and-shows-marker",await home.Script("document.querySelector('#cards>.note-card').dataset.noteId==='"+ids[0]+"'&&!document.querySelector('[data-note-id=\""+ids[0]+"\"] .card-pin').hidden")=="true");
            var persisted=(await new NoteStore(s.Store.Root).LoadIndex())!.Value.GetProperty("notes").EnumerateArray().Single(n=>n.GetProperty("id").GetString()==ids[0]);
            Check("library-pin-persists-and-does-not-enable-always-on-top",persisted.GetProperty("libraryPinned").GetBoolean()&&!(await s.Store.LoadNote(ids[0]))!.Value.GetProperty("view").GetProperty("pinned").GetBoolean());

            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{320d,520d,900d})
            {
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await s.Changed();home.Width=width;home.Height=590;home.UpdateLayout();await Task.Delay(100);
                Check("body-and-actions-fit-"+theme+"-"+width,await home.Script("(()=>{const body=document.getElementById('workspaceHome'),bar=document.querySelector('.library-appbar');return body.scrollWidth<=innerWidth&&bar.scrollWidth<=bar.clientWidth&&[...bar.querySelectorAll('button')].filter(b=>b.getBoundingClientRect().width).every(b=>b.getBoundingClientRect().right<=innerWidth);})()")=="true");
                Check("visible-folder-navigation-"+theme+"-"+width,await home.Script(width>=720?"getComputedStyle(document.getElementById('librarySidebar')).display!=='none'&&document.querySelectorAll('#sidebarChoices [data-scope=folder]').length===2&&document.querySelector('#sidebarChoices [data-scope=pinned] small').textContent==='1'":"getComputedStyle(document.getElementById('folderButton')).display!=='none'&&document.getElementById('folderButton').textContent.includes('Folders')")=="true");
                if(width<720)
                {
                    await home.ClickControl("#folderButton");
                    Check("compact-folders-panel-stays-on-screen-"+theme+"-"+width,await home.Script("(()=>{const p=document.getElementById('folderPanel').getBoundingClientRect();return p.left>=0&&p.right<=innerWidth&&p.top>=0&&p.bottom<=innerHeight&&document.querySelectorAll('#folderChoices [data-scope=folder]').length===2;})()")=="true");
                    await home.Capture("library-folders-"+theme+"-"+width);await home.ClickControl("#closeFolders");
                }
                await home.Capture("library-redesign-"+theme+"-"+width);
            }
            await home.ClickControl("#sidebarChoices [data-scope=pinned]");
            Check("pinned-view-has-only-pinned-notes",await home.Script("document.getElementById('folderLabel').textContent==='Pinned'&&document.querySelectorAll('#cards>.note-card').length===1")=="true");
            await home.ClickControl("#sidebarChoices [data-scope=all]");await home.ClickControl("#selectNotes");await home.ClickControl("#selectAllNotes");
            Check("select-all-visible-notes-shows-count-and-no-bulk-rename",await home.Script("document.getElementById('selectionCount').textContent==='3 selected'&&document.querySelectorAll('.card-select:checked').length===3&&!document.querySelector('#bulkActions [data-action=rename],#bulkRename')")=="true");
            await home.ClickControl("#bulkPin");await home.WaitFor("homeData.notes.every(n=>n.libraryPinned)&&document.getElementById('bulkPin').ariaBusy==='false'");
            Check("bulk-pin-label-becomes-unpin",await home.Script("document.getElementById('bulkPin').textContent.trim()==='Unpin'")=="true");
            await home.ClickControl("#bulkPin");await home.WaitFor("homeData.notes.every(n=>!n.libraryPinned)&&document.getElementById('bulkPin').ariaBusy==='false'");
            await home.ClickControl("#bulkMove");await home.ClickControl("#moveFolders button:last-child");await home.WaitFor("!document.getElementById('moveDialog').open&&homeData.notes.every(n=>n.group==='Projects')");
            Check("bulk-move-updates-all-notes-and-folder-counts",await home.Script("document.querySelector('#sidebarChoices [data-folder=Projects] small').textContent==='3'&&document.getElementById('selectionCount').textContent==='3 selected'")=="true");
            await s.OpenAsTab(ids[1]);
            await home.ClickControl("[role=tab][data-workspace-id=home]");
            await home.WaitFor("window.JotWorkspace?.view==='home'&&document.getElementById('selectionCount').textContent==='3 selected'");
            await home.ClickControl("#bulkDelete");
            Check("bulk-delete-confirmation-names-count-and-keeps-external-files",await home.Script("document.getElementById('confirmTitle').textContent==='Move 3 notes to Trash?'&&document.getElementById('confirmDescription').textContent.includes('Saved files stay on disk')")=="true");
            await home.ClickControl("#confirmCancel");Check("cancel-keeps-entire-selection",(await s.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==3);

            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_bulk_delete BEFORE DELETE ON notes WHEN OLD.id='"+ids[1]+"' BEGIN SELECT RAISE(ABORT,'synthetic bulk delete failure');END;");
            await home.ClickControl("#bulkDelete");await home.ClickControl("#confirmApply");await home.WaitFor("document.querySelector('#libraryConfirm .dialog-error').hidden===false&&!document.getElementById('confirmApply').disabled");
            Check("failed-delete-rolls-back-entire-batch",(await Task.WhenAll(ids.Select(s.Store.Contains))).All(exists=>exists)&&(await s.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==3);
            Check("recovery-copies-exist-before-any-deletion",Directory.GetFiles(Path.Combine(s.Store.Root,"trash")).Length==3);
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_bulk_delete;");

            var editorWindow=await s.OpenNoteDefault(ids[0]);await editorWindow.WaitFor("window.jotReady===true");
            await editorWindow.Script("editor.innerHTML='<p>Final draft before bulk delete.</p>';onEdit();clearTimeout(saveTimer)");
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_bulk_flush BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic bulk flush failure');END;");
            await home.ClickControl("#confirmApply");await home.WaitFor("!document.getElementById('confirmApply').disabled");await editorWindow.WaitFor("!editorLockedByHost&&!app.inert");
            Check("failed-flush-keeps-all-notes-and-live-draft",(await s.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==3&&editorWindow.IsVisible&&await editorWindow.Script("editor.textContent==='Final draft before bulk delete.'&&editor.isContentEditable")=="true");
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_bulk_flush;");
            var external=Path.Combine(s.Store.Root,"external-note.txt");await File.WriteAllTextAsync(external,"Saved independently");
            var binding=JsonSerializer.Serialize(new{noteFile=new{path=external,format="txt"}}).Replace("'","''");
            await SetStoreTrigger(s.Store,"UPDATE notes SET extra='"+binding+"' WHERE id='"+ids[0]+"';");
            await home.ClickControl("#confirmApply");await home.WaitFor("!document.getElementById('libraryConfirm').open&&homeData.notes.length===0");
            Check("successful-bulk-delete-closes-editors-and-keeps-saved-files",!s.Windows.Contains(editorWindow)&&home.NoteTabIds.Count==0&&await File.ReadAllTextAsync(external)=="Saved independently"&&home.IsVisible);
            var archived=Directory.GetFiles(Path.Combine(s.Store.Root,"trash")).Select(File.ReadAllText);
            Check("bulk-delete-recovery-has-latest-draft",archived.Any(json=>json.Contains("Final draft before bulk delete.")));
            Check("no-runtime-errors-or-foreground-windows",s.Windows.All(w=>w.RuntimeErrors.Count==0&&w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowInTaskbar&&!w.Topmost));
        }
        finally{foreach(var window in s.Windows.ToArray())window.ClosePermanently();}

        var tabs=new JotSession(true,Path.Combine(testOutput,"library-bulk-tabs")){ExerciseLifecycle=true};
        try
        {
            var a=await tabs.Store.Create();var keepTab=await tabs.Store.Create();var b=await tabs.Store.Create();
            var home=await tabs.OpenAsTab(a);await tabs.OpenAsTab(keepTab);await tabs.OpenAsTab(b);
            await home.Script("editor.innerHTML='<p>Latest selected tab draft.</p>';onEdit();clearTimeout(saveTimer)");
            await tabs.UpdateLibraryNotes([a,b],"delete");
            Check("bulk-delete-active-and-inactive-tabs-keeps-surviving-editor",home.NoteTabIds.SequenceEqual([keepTab])&&home.NoteId==keepTab&&home.IsVisible&&await home.Script("activeNote().id==='"+keepTab+"'&&!app.inert")=="true");
            Check("selected-active-tab-is-archived-after-saving",Directory.GetFiles(Path.Combine(tabs.Store.Root,"trash")).Select(File.ReadAllText).Any(json=>json.Contains("Latest selected tab draft.")));
        }
        finally{foreach(var window in tabs.Windows.ToArray())window.ClosePermanently();}

        var blocked=new NoteStore(Path.Combine(testOutput,"library-recovery-blocked"));var keep=await blocked.Create();
        await File.WriteAllTextAsync(Path.Combine(blocked.Root,"trash"),"Block recovery directory for isolated test.");
        var rejected=false;try{await blocked.UpdateLibraryNotes([keep],"delete");}catch(IOException){rejected=true;}
        Check("recovery-write-failure-cannot-delete-any-note",rejected&&await blocked.Contains(keep));
    }
}
