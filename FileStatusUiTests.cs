using System.IO;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyFileStatusUi(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-status-ui-"+name,passed});
        foreach(var mode in new[]{"tab","window"}){
            var root=Path.Combine(testOutput,"file-status-ui-"+mode);Directory.CreateDirectory(root);
            var s=new JotSession(true,root){ExerciseLifecycle=true};
            try{
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode}));
                var note=await s.NewNote();await note.WaitFor("window.jotReady===true&&!!activeNote()&&editor.isContentEditable");var id=note.NoteId!;
                Check("unlinked-notes-have-no-file-badge-"+mode,await note.Script("!JotNoteFiles.isDirty(activeNote())&&document.getElementById('fileIdentity').hidden")=="true");
                await note.Script("editor.innerHTML='<p>My saved file</p>';onEdit();clearTimeout(saveTimer)");
                await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="My document title"}));await s.Changed(false,id);
                var path=Path.Combine(root,"A recognizable file.jot");note.TestSaveNoteFilePath=path;await note.SaveNoteFile(id,true);
                await note.WaitFor("activeNote().noteFile?.path&&JotNoteFiles.name(activeNote())==='My document title'&&!JotNoteFiles.isDirty(activeNote())");
                if(mode=="window")Check("floating-note-shows-filename-and-path",await note.Script("!document.getElementById('fileIdentity').hidden&&document.querySelector('#fileIdentity .file-name').textContent==='A recognizable file.jot'&&document.getElementById('fileIdentity').title.includes(activeNote().noteFile.path)")=="true");
                else Check("tab-shows-title-with-separate-filename",await note.Script("document.querySelector('.workspace-note-tabs .tab-title').textContent==='My document title'&&document.querySelector('.workspace-note-tabs [role=tab]').title.includes(activeNote().noteFile.path)&&!document.getElementById('fileIdentity').hidden&&document.querySelector('#fileIdentity .file-name').textContent==='A recognizable file.jot'")=="true");
                await note.Script("editor.innerHTML='<p>Unsaved external change</p>';onEdit();clearTimeout(saveTimer)");
                Check("dirty-marker-is-immediate-before-library-save-"+mode,await note.Script(mode=="tab"?"JotNoteFiles.isDirty(activeNote())&&!document.querySelector('.workspace-note-tabs .tab-file-status').hidden":"JotNoteFiles.isDirty(activeNote())&&document.getElementById('fileIdentity').dataset.dirty==='true'")=="true");
                await note.Flush();await note.WaitFor("JotNoteFiles.state(activeNote()).dirty===true");
                Check("library-auto-save-does-not-clear-file-marker-"+mode,await note.Script("JotNoteFiles.isDirty(activeNote())")=="true"&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()=="My saved file");
                await note.Script("JotNoteFiles.run('file-save').then(()=>window.fileUiSaved=true)");await note.WaitFor("window.fileUiSaved===true&&!JotNoteFiles.isDirty(activeNote())");
                Check("save-clears-marker-after-file-write-"+mode,(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()=="Unsaved external change");
                await s.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=true}));
                var beforeAutoFlush=note.TestHostActions.Count(action=>action=="flush-complete");
                await note.Script("setNoteColor('teal').then(()=>window.autoColorReady=true)");await note.WaitFor("window.autoColorReady===true&&JotNoteFiles.state(activeNote()).dirty");
                await note.WaitFor("!JotNoteFiles.state(activeNote()).dirty&&!JotNoteFiles.state(activeNote()).saving");
                Check("idle-auto-save-does-not-lock-or-flush-editor-"+mode,note.TestHostActions.Count(action=>action=="flush-complete")==beforeAutoFlush&&await note.Script("!app.inert&&!editorLockedByHost")=="true");
                await s.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=false}));
                await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,icon="emoji:📝"}));await s.Changed(false,id);
                var home=s.Home();await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'&&!!document.querySelector('[data-note-id=\""+id+"\"]')&&!document.getElementById('workspaceHome').hidden");
                Check("library-uses-document-title-and-chosen-emoji-"+mode,await home.Script("(()=>{const c=document.querySelector('[data-note-id=\""+id+"\"]');return c.querySelector('.card-title').textContent==='My document title'&&c.querySelector('.card-note-icon').textContent.includes('📝')})()")=="true");
                await home.RightClickControl("[data-note-id='"+id+"'] .card-open");
                Check("file-backed-rename-edits-document-title-"+mode,await home.Script("document.querySelector('#contentContextMenu [data-action=rename]').textContent==='Rename'")=="true");await home.Script("JotMenus.close()");
                home.Width=640;home.Height=530;await home.Capture("file-note-library-"+mode);
                home.Width=360;await Task.Delay(90);await home.RightClickControl(".workspace-tab[data-home=true] [role=tab]");
                Check("compact-workspace-retains-pin-in-app-menu-"+mode,await home.Script("getComputedStyle(document.getElementById('workspacePin')).display==='none'&&!!document.querySelector('#contentContextMenu [data-action=workspace-pin]')")=="true");
                await home.ClickControl("#contentContextMenu [data-action=workspace-pin]");await home.WaitFor("JotWorkspace.pinned===true");
                Check("compact-menu-pin-controls-whole-workspace-"+mode,home.WorkspacePinned&&!home.Topmost);await home.SetWorkspacePinned(false);
                var editorWindow=await s.OpenNoteDefault(id);await editorWindow.WaitFor("window.jotReady===true&&!!activeNote()&&editor.isContentEditable");
                await editorWindow.Script("editor.innerHTML='<p>A visible unsaved marker</p>';onEdit();clearTimeout(saveTimer)");
                editorWindow.Width=mode=="tab"?640:360;editorWindow.Height=500;await editorWindow.Capture("file-note-unsaved-"+mode);
                Check("no-runtime-errors-"+mode,s.Windows.All(window=>window.RuntimeErrors.Count==0));
            }finally{s.StopFileAutoSave();await s.WaitForNoteFiles();foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
        }
    }
}
