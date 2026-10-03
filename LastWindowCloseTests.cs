using System.IO;
using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task RunLastCloseProbe(string scenario)
    {
        await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
        await session.StartWork();var a=session.Windows.Single(w=>w.Mode=="note");await a.WaitFor("window.jotReady===true");
        var b=await session.NewNote();await b.WaitFor("window.jotReady===true");
        foreach(var n in new[]{a,b})await n.Script("editor.innerHTML='<p>Final process draft فارسی English '+model.activeId+'</p>';onEdit();clearTimeout(saveTimer)");
        MainWindow last=b;string closeSelector="#hideButton";
        if(scenario is "last-home" or "last-settings")
        {last=scenario=="last-home"?session.Home():session.Settings();await last.WaitFor("window.jotReady===true");closeSelector="#workspaceClose";}
        else if(scenario=="last-image")
        {last=session.Image(JsonSerializer.Deserialize<string>(await a.Script("document.createElement('canvas').toDataURL()"))!);await last.WaitFor("window.jotReady===true");closeSelector="#imageClose";}
        await a.ClickControl("#hideButton");await WaitHidden(a);
        if(a.windowClosed||!b.IsVisible)throw new InvalidOperationException("Closing a non-last note must keep the app running.");
        if(last!=b){await b.ClickControl("#hideButton");await WaitHidden(b);}
        var safety=session.Windows.All(w=>!w.IsVisible||w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowInTaskbar&&!w.ShowActivated&&!w.Topmost);
        if(!safety)throw new InvalidOperationException("Refusing a last-close probe on the desktop.");
        await File.WriteAllTextAsync(Path.Combine(testOutput,"probe-expected.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,scenario,expected=Array.Empty<string>(),originalIds=scenario=="last-delete"?new[]{a.NoteId}:new[]{a.NoteId,b.NoteId},deletedId=scenario=="last-delete"?b.NoteId:null,offscreen=safety}));
        if(scenario=="last-delete")
        {await b.ClickControl("#menuButton");await b.ClickControl("#deleteButton");await b.ClickControl("#deleteConfirm");}
        else await last.ClickControl(closeSelector);
    }

    private async Task VerifyLastWindowClose(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="last-window-"+name,passed});
        async Task Until(Func<bool> condition){for(int i=0;i<300;i++){if(condition())return;await Task.Delay(40);}throw new TimeoutException("Last window scenario did not finish.");}
        foreach(var scenario in new[]{"minimized-note","minimized-home","new-note-race","reopen-race","parallel-close","session-failure","image-session-failure","pending-image"})
        {
            var path=Path.Combine(testOutput,"last-window",scenario);var s=new JotSession(true,path){ExerciseLifecycle=true};
            new MainWindow(s,"home").StartInTray();
            try
            {
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
                await s.StartWork();var a=s.Windows.Single(w=>w.Mode=="note");await a.WaitFor("window.jotReady===true");
                await a.Script("editor.innerHTML='<p>Final last-window draft</p>';onEdit();clearTimeout(saveTimer)");
                if(scenario.StartsWith("minimized-"))
                {
                    var other=scenario=="minimized-note"?await s.NewNote():s.Home();await other.WaitFor("window.jotReady===true");
                    other.WindowState=WindowState.Minimized;
                    await a.HideAfterSaving();
                    Check(scenario+"-keeps-app-running",s.Windows.Contains(other)&&other.IsVisible&&!other.windowClosed&&!a.IsVisible&&!a.windowClosed);
                    other.WindowState=WindowState.Normal;await other.HideAfterSaving();
                    Check(scenario+"-last-close-quits",s.Windows.Count==0);
                }
                else if(scenario=="new-note-race")
                {
                    a.TestSaveDelayMs=150;var closing=a.HideAfterSaving();var opening=s.NewNote();var b=await opening;await closing;
                    await b.WaitFor("window.jotReady===true");
                    Check("accepted-new-note-prevents-premature-exit",b.IsVisible&&!b.windowClosed&&!a.IsVisible);
                    await b.HideAfterSaving();Check("new-note-final-close-quits",s.Windows.Count==0);
                }
                else if(scenario=="reopen-race")
                {
                    a.TestSaveDelayMs=150;var closing=a.HideAfterSaving();s.OpenNote(a.NoteId!);await closing;
                    Check("reopen-during-save-wins",a.IsVisible&&!a.HideRequested&&!a.windowClosed);
                    await a.HideAfterSaving();Check("reopened-final-close-quits",s.Windows.Count==0);
                }
                else if(scenario=="parallel-close")
                {
                    var b=await s.NewNote();await b.WaitFor("window.jotReady===true");a.TestSaveDelayMs=b.TestSaveDelayMs=120;
                    await b.Script("editor.innerHTML='<p>Parallel final draft</p>';onEdit();clearTimeout(saveTimer)");
                    await Task.WhenAll(a.HideAfterSaving(),b.HideAfterSaving());await Until(()=>s.Windows.Count==0);
                    Check("parallel-closes-save-both-notes",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Final last-window draft")&&(await s.Store.LoadNote(b.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Parallel final draft"));
                }
                else if(scenario=="session-failure")
                {
                    await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_last_session BEFORE UPDATE OF extra ON app_state BEGIN SELECT RAISE(ABORT,'synthetic session failure');END;");
                    bool failed=false;try{await a.HideAfterSaving();}catch(Microsoft.Data.Sqlite.SqliteException){failed=true;}
                    await a.WaitFor("!app.inert");
                    Check("failed-shutdown-restores-editable-visible-note",failed&&a.IsVisible&&!a.windowClosed&&!a.HideRequested&&await a.Script("editor.textContent==='Final last-window draft'")=="true");
                    await SetStoreTrigger(s.Store,"DROP TRIGGER fail_last_session;");await a.HideAfterSaving();
                    Check("failed-shutdown-can-retry",s.Windows.Count==0);
                }
                else if(scenario=="image-session-failure")
                {
                    var viewer=s.Image(JsonSerializer.Deserialize<string>(await a.Script("document.createElement('canvas').toDataURL()"))!);await viewer.WaitFor("window.jotReady===true");
                    await a.HideAfterSaving();
                    await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_last_image_session BEFORE UPDATE OF extra ON app_state BEGIN SELECT RAISE(ABORT,'synthetic session failure');END;");
                    await viewer.ClickControl("#imageClose");await viewer.WaitFor("!document.getElementById('imageError').hidden");
                    Check("failed-image-shutdown-restores-viewer",viewer.IsVisible&&!viewer.windowClosed);
                    Check("failed-image-shutdown-displays-retry-message",await viewer.Script("document.getElementById('imageError').textContent.includes('still open')")=="true");
                    await SetStoreTrigger(s.Store,"DROP TRIGGER fail_last_image_session;");await viewer.ClickControl("#imageClose");await Until(()=>s.Windows.Count==0);
                    Check("failed-image-shutdown-can-retry",s.Windows.Count==0&&viewer.RuntimeErrors.Count==0);
                }
                else
                {
                    await a.Script("editor.focus();getSelection().selectAllChildren(editor);getSelection().collapseToEnd();const canvas=document.createElement('canvas');canvas.width=240;canvas.height=120;const src=canvas.toDataURL();readImage=()=>new Promise(resolve=>window.finishImage=()=>resolve(src));insertImages([new File(['fixture'],'fixture.png',{type:'image/png'})])");
                    var closing=a.HideAfterSaving();await a.WaitFor("editorLockedByHost");
                    Check("pending-image-delays-final-close",!closing.IsCompleted&&a.IsVisible);
                    await a.Script("window.finishImage()");await closing;
                    Check("pending-image-is-saved-before-exit",s.Windows.Count==0&&(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("html").GetString()!.Contains("data:image/png;base64,"));
                }
                Check(scenario+"-session-has-no-closed-notes",(await s.Store.LoadWindowSession()).Length==0);
                Check(scenario+"-no-renderer-errors",a.RuntimeErrors.Count==0);
            }
            finally{foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
        }
    }
}
