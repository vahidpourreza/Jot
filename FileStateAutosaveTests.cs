using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyFileStateAutosave(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-state-"+name,passed});
        var root=Path.Combine(testOutput,"file-state-autosave");var files=Path.Combine(root,"files");Directory.CreateDirectory(files);
        var target=new JotSession(true,root){ExerciseLifecycle=true};
        // The native-only fixture has no WebView editor, so a stale DOM cannot
        // overwrite a deliberate storage edit when Save reads the latest note.
        var host=new MainWindow(target,"home");
        async Task Edit(string id,string html,string plain)
        {
            var note=JsonNode.Parse((await target.Store.LoadNote(id))!.Value.GetRawText())!.AsObject();
            note["html"]=html;note["plain"]=plain;note["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await target.Store.SaveNote(JsonSerializer.SerializeToElement(note));
        }
        async Task<JsonElement> State(string id)=>(await target.Store.LoadFileState(id))!.Value;
        async Task WaitForFile(string path,string expected)
        {
            for(var attempt=0;attempt<200;attempt++)
            {
                if(File.Exists(path)&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()==expected)return;
                await Task.Delay(30);
            }
            throw new TimeoutException("The isolated auto-save file did not receive the expected text: "+Path.GetFileName(path));
        }
        try
        {
            var defaults=await target.Store.LoadPreferences();
            Check("tabs-default-auto-save-opt-in",defaults.GetProperty("newNoteTarget").GetString()=="tab"&&!defaults.GetProperty("autoSaveFiles").GetBoolean());
            var id=await target.Store.Create();await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Original library title"}));
            await Edit(id,"<p><b>Initial rich text</b></p>","Initial rich text");
            Check("unlinked-note-has-no-file-dirty-state",(await State(id)).GetProperty("path").ValueKind==JsonValueKind.Null&&!(await State(id)).GetProperty("dirty").GetBoolean());
            var path=Path.Combine(files,"My working note.jot");host.TestSaveNoteFilePath=path;await host.SaveNoteFile(id,true);
            var state=await State(id);var saved=await NoteFileFormat.Read(path);
            Check("save-as-preserves-title-and-separate-filename",state.GetProperty("title").GetString()=="Original library title"&&state.GetProperty("name").GetString()=="My working note.jot"&&state.GetProperty("path").GetString()==path&&state.GetProperty("format").GetString()=="jot"&&!state.GetProperty("dirty").GetBoolean()&&saved.Note["title"]!.GetValue<string>()=="Original library title");
            await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Renamed document title"}));
            Check("renaming-linked-title-marks-rich-file-dirty-without-renaming-file",(await State(id)).GetProperty("dirty").GetBoolean()&&(await State(id)).GetProperty("path").GetString()==path&&(await NoteFileFormat.Read(path)).Note["title"]!.GetValue<string>()=="Original library title");
            await host.SaveNoteFile(id);
            Check("save-persists-title-with-same-filename",!(await State(id)).GetProperty("dirty").GetBoolean()&&(await NoteFileFormat.Read(path)).Note["title"]!.GetValue<string>()=="Renamed document title");
            var original=await File.ReadAllBytesAsync(path);
            await Edit(id,"<p><i>Changed in library</i></p>","Changed in library");await target.FileNoteChanged(id);
            Check("manual-mode-shows-dirty-and-leaves-file-unchanged",(await State(id)).GetProperty("dirty").GetBoolean()&&(await File.ReadAllBytesAsync(path)).SequenceEqual(original));
            var reopened=new NoteStore(target.Store.Root);Check("dirty-state-persists-across-store-reopen",(await reopened.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean());
            await host.SaveNoteFile(id);Check("manual-save-updates-same-file-and-clears-dirty",!(await State(id)).GetProperty("dirty").GetBoolean()&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()=="Changed in library");
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=true}));
            await Edit(id,"<p>First queued edit</p>","First queued edit");await target.FileNoteChanged(id);
            await Task.Delay(200);await Edit(id,"<p>Latest queued edit</p>","Latest queued edit");await target.FileNoteChanged(id);
            Check("auto-save-waits-for-idle",(await State(id)).GetProperty("dirty").GetBoolean()&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()=="Changed in library");
            await WaitForFile(path,"Latest queued edit");await target.WaitForNoteFiles();
            Check("auto-save-debounces-to-latest-content",!(await State(id)).GetProperty("dirty").GetBoolean());
            var external=NoteFileFormat.Encode(JsonSerializer.SerializeToElement(new{title="External",html="<p>Outside editor</p>",plain="Outside editor",color="teal"}),"jot");await File.WriteAllBytesAsync(path,external);
            await Edit(id,"<p>Keep my local work</p>","Keep my local work");await target.FileNoteChanged(id);await Task.Delay(1350);
            Check("external-change-is-not-overwritten-and-local-draft-is-retained",(await File.ReadAllBytesAsync(path)).SequenceEqual(external)&&(await State(id)).GetProperty("dirty").GetBoolean()&&(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Keep my local work");
            var recovery=Path.Combine(files,"Recovered work.jot");host.TestSaveNoteFilePath=recovery;await host.SaveNoteFile(id,true);
            Check("save-as-recovers-conflict-with-new-filename-and-original-title",!(await State(id)).GetProperty("dirty").GetBoolean()&&(await State(id)).GetProperty("title").GetString()=="Renamed document title"&&(await File.ReadAllBytesAsync(path)).SequenceEqual(external));
            await Edit(id,"<p>Saving works after recovery</p>","Saving works after recovery");await target.FileNoteChanged(id);await WaitForFile(recovery,"Saving works after recovery");await target.WaitForNoteFiles();
            Check("successful-save-as-resumes-auto-save",!(await State(id)).GetProperty("dirty").GetBoolean());
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{autoSaveFiles=false}));
            await Edit(id,"<p>Manual again</p>","Manual again");await target.FileNoteChanged(id);await Task.Delay(1150);
            Check("disabling-auto-save-keeps-manual-file-save",(await State(id)).GetProperty("dirty").GetBoolean()&&(await NoteFileFormat.Read(recovery)).Note["plain"]!.GetValue<string>()=="Saving works after recovery");
            await host.SaveNoteFile(id);
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{fontSize=22,lineHeight=1.5}));
            Check("inherited-writing-defaults-mark-rich-file-dirty",(await State(id)).GetProperty("dirty").GetBoolean()&&!(await target.Store.LoadNote(id))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));
            await host.SaveNoteFile(id);saved=await NoteFileFormat.Read(recovery);
            Check("rich-file-saves-effective-defaults-without-local-override",saved.Note["view"]!["fontSize"]!.GetValue<int>()==22&&saved.Note["view"]!["lineHeight"]!.GetValue<double>()==1.5&&!(await State(id)).GetProperty("dirty").GetBoolean());
            var textId=await target.Store.Create();await Edit(textId,"<p>Plain unchanged</p>","Plain unchanged");var textPath=Path.Combine(files,"Plain note.txt");host.TestSaveNoteFilePath=textPath;await host.SaveNoteFile(textId,true);
            await Edit(textId,"<p><b>Plain unchanged</b></p>","Plain unchanged");await target.Store.SaveNotePreferences(textId,JsonSerializer.SerializeToElement(new{fontSize=20,lineHeight=2.2}));
            Check("txt-ignores-formatting-only-edits",!(await State(textId)).GetProperty("dirty").GetBoolean()&&await File.ReadAllTextAsync(textPath)=="Plain unchanged");
            await Edit(textId,"<p><b>Plain changed</b></p>","Plain changed");
            Check("txt-tracks-text-edits",(await State(textId)).GetProperty("dirty").GetBoolean());await host.SaveNoteFile(textId);
            Check("txt-save-clears-dirty",!(await State(textId)).GetProperty("dirty").GetBoolean()&&await File.ReadAllTextAsync(textPath)=="Plain changed");
            Check("native-fixtures-have-no-real-windows-or-global-keys",target.Windows.All(w=>!w.IsVisible&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Opacity==0)&&!JsonSerializer.SerializeToElement(target.ShortcutStatus).GetProperty("active").GetBoolean());
        }
        finally{target.StopFileAutoSave();await target.WaitForNoteFiles();target.DisposeGlobalShortcuts();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        await VerifyWorkspaceCloseAutoSave(checks);
        await VerifyFileAutoSaveTabTransfers(checks);
    }
    private async Task VerifyWorkspaceCloseAutoSave(List<object> checks)
    {
        var root=Path.Combine(testOutput,"file-state-workspace-close");Directory.CreateDirectory(root);var target=new JotSession(true,root){ExerciseLifecycle=true};
        try
        {
            await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="tab",autoSaveFiles=true}));
            var home=target.Home();await home.WaitFor("window.jotReady===true");await target.NewTab(home);await home.WaitFor("JotWorkspace.view==='note'&&editor.isContentEditable");var id=home.NoteId!;
            home.TestSaveNoteFilePath=Path.Combine(root,"Close flush.jot");await home.SaveNoteFile(id,true);
            const string finalText="Latest edit survives workspace X and updates its linked file";
            await home.Script("editor.innerHTML="+JsonSerializer.Serialize("<p>"+finalText+"</p>")+";onEdit();clearTimeout(saveTimer)");
            await home.HideAfterSaving();await target.WaitForNoteFiles();
            checks.Add(new{name="file-state-workspace-x-flushes-latest-file-edit-before-exit",passed=(await NoteFileFormat.Read(home.TestSaveNoteFilePath)).Note["plain"]!.GetValue<string>()==finalText&&!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean()&&(await target.Store.LoadWorkspaceSession())?.Tabs.Contains(id)==true});
        }
        finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }
    private async Task VerifyFileAutoSaveTabTransfers(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-state-"+name,passed});
        var root=Path.Combine(testOutput,"file-state-tab-transfers");Directory.CreateDirectory(root);
        var target=new JotSession(true,root){ExerciseLifecycle=true};
        async Task Edit(MainWindow owner,string text)
        {await owner.Script("editor.innerHTML="+JsonSerializer.Serialize("<p>"+text+"</p>")+";onEdit();clearTimeout(saveTimer)");}
        const string ready="window.jotReady===true&&editor.isContentEditable&&!editorLockedByHost";
        try
        {
            await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="tab",autoSaveFiles=true}));
            var home=target.Home();await home.WaitFor("window.jotReady===true");await target.NewTab(home);await home.WaitFor(ready);var id=home.NoteId!;
            var path=Path.Combine(root,"Tab close and transfers.jot");home.TestSaveNoteFilePath=path;await home.SaveNoteFile(id,true);
            const string closingText="Tab X saves the latest draft before removing ownership";
            await Edit(home,closingText);home.TestSaveDelayMs=120;
            var closes=home.TestHostActions.Count(action=>action=="tab-close");
            await home.ClickControl(".tab-close[data-workspace-id='"+id+"']");
            for(var attempt=0;attempt<200&&home.TestHostActions.Count(action=>action=="tab-close")==closes;attempt++)await Task.Delay(30);
            home.TestSaveDelayMs=0;
            Check("tab-x-preflight-flushes-and-auto-saves-without-queue-deadlock",home.TestHostActions.Count(action=>action=="tab-close")==closes+1&&!home.NoteTabIds.Contains(id)&&home.IsVisible&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()==closingText&&!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean());

            await target.OpenAsTab(id);await home.WaitFor(ready);
            // A separate-window default can legitimately contain an explicitly
            // opened tab. Reopening its file must transfer it without nesting
            // a second file-save operation inside the already-held file gate.
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"})).WaitAsync(TimeSpan.FromSeconds(12));
            await target.OpenAsTab(id).WaitAsync(TimeSpan.FromSeconds(12));await home.WaitFor(ready);
            const string reopeningText="Reopening a file in window mode retains the tab draft";
            await Edit(home,reopeningText);
            var opened=await home.OpenNoteFile(path).WaitAsync(TimeSpan.FromSeconds(12));
            var separate=target.Windows.Single(window=>window.ContainsNote(id));await separate.WaitFor(ready);
            Check("file-open-window-transfer-does-not-deadlock-file-gate",opened==id&&separate.Mode=="note"&&!home.NoteTabIds.Contains(id)&&target.Windows.Count(window=>window.ContainsNote(id))==1&&await separate.Script("editor.textContent==="+JsonSerializer.Serialize(reopeningText))=="true");
            await target.SaveAutoFilesFor(new[]{id}).WaitAsync(TimeSpan.FromSeconds(12));
            Check("file-open-transfer-keeps-auto-save-attached-to-new-owner",(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()==reopeningText&&!(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean());

            await target.OpenAsTab(id);await home.WaitFor(ready);
            const string concurrentText="Explicit Save and Open window preserve one current editor";
            await Edit(home,concurrentText);home.TestSaveDelayMs=150;
            var save=home.SaveNoteFile(id);var transfer=target.OpenNoteWindow(id);
            await Task.WhenAll(save,transfer).WaitAsync(TimeSpan.FromSeconds(12));home.TestSaveDelayMs=0;
            separate=await transfer;await separate.WaitFor(ready);
            Check("manual-save-and-window-transfer-finish-with-one-owner",target.Windows.Count(window=>window.ContainsNote(id))==1&&separate.Mode=="note"&&(await NoteFileFormat.Read(path)).Note["plain"]!.GetValue<string>()==concurrentText&&await separate.Script("editor.textContent==="+JsonSerializer.Serialize(concurrentText))=="true");

            await target.OpenAsTab(id);await home.WaitFor(ready);
            var external=NoteFileFormat.Encode(JsonSerializer.SerializeToElement(new{title="External",html="<p>External version</p>",plain="External version",color="teal"}),"jot");await File.WriteAllBytesAsync(path,external);
            const string local="Do not close a tab whose automatic file save failed";await Edit(home,local);
            bool refused=false;try{await home.CloseNoteTab(id).WaitAsync(TimeSpan.FromSeconds(12));}catch(IOException){refused=true;}
            Check("failed-close-auto-save-keeps-tab-draft-and-external-file",refused&&home.NoteTabIds.Contains(id)&&home.NoteId==id&&home.IsVisible&&(await File.ReadAllBytesAsync(path)).SequenceEqual(external)&&(await target.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()==local&&(await target.Store.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean());
            Check("close-and-transfer-tests-remain-offscreen",target.Windows.All(window=>window.Left< -10000&&window.Top< -10000&&!window.Topmost&&!window.ShowActivated&&!window.ShowInTaskbar&&window.Opacity==0));
        }
        finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }
}
