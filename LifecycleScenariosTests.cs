using System.IO;
using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyLifecycleScenarios(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="close-matrix-"+name,passed});
        async Task Until(Func<bool> condition){for(int i=0;i<240;i++){if(condition())return;await Task.Delay(50);}throw new TimeoutException("Lifecycle scenario did not finish.");}
        JotSession Create(string path){var s=new JotSession(true,path){ExerciseLifecycle=true};new MainWindow(s,"home").StartInTray();return s;}
        void Cleanup(JotSession s){s.TrayMenu?.Close();foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
        foreach(var kind in new[]{"home-and-notes","settings-and-notes","notes-only","home-only","last-note-closed","one-closed","reopened","native-close","minimized-note"})
        {
            var path=Path.Combine(testOutput,"close-matrix",kind);var s=Create(path);JotSession? restart=null;
            try
            {
                await s.StartWork();var a=s.Windows.Single(w=>w.Mode=="note");await a.WaitFor("window.jotReady===true");
                var notes=new List<MainWindow>{a};if(kind is not ("home-only" or "last-note-closed" or "reopened" or "minimized-note")){var b=await s.NewNote();notes.Add(b);await b.WaitFor("window.jotReady===true");}
                foreach(var n in notes)await n.Script("editor.innerHTML='<p>Final draft فارسی English '+model.activeId+'</p>';onEdit();clearTimeout(saveTimer)");
                if(kind is "home-only" or "reopened"){var h=s.Home();await h.WaitFor("window.jotReady===true");}
                if(kind is "home-only" or "last-note-closed" or "one-closed" or "reopened")
                {
                    await a.ClickControl("#hideButton");await Until(()=>!a.IsVisible);
                    Check(kind+"-x-really-hides-and-saves",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Final draft"));
                    if(kind=="reopened"){s.OpenNote(a.NoteId!);await a.WaitFor("!app.inert");}
                }
                if(kind=="native-close"){a.Close();await Until(()=>!a.IsVisible);}
                if(kind=="minimized-note")a.WindowState=WindowState.Minimized;
                if(kind is "home-and-notes" or "home-only" or "one-closed" or "reopened"){var h=s.Home();await h.WaitFor("window.jotReady===true");}
                var expected=notes.Where(n=>n.IsVisible&&!n.HideRequested&&n.WindowState!=WindowState.Minimized).Select(n=>n.NoteId!).ToHashSet();
                if(kind=="settings-and-notes")
                {
                    var settings=s.Settings();await settings.WaitFor("window.jotReady===true&&!!document.getElementById('settingsQuit')");
                    await settings.ClickControl("#settingsQuit");await Until(()=>s.Windows.Count==0);
                }
                else if(kind=="last-note-closed")await Until(()=>s.Windows.Count==0);
                else await s.ExecuteTrayAction("quit");
                Check(kind+"-real-quit-closes-all-own-windows",s.Windows.Count==0);
                foreach(var n in notes)Check(kind+"-complete-last-text-"+n.NoteId,(await s.Store.LoadNote(n.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Final draft فارسی English "+n.NoteId));
                restart=Create(path);await restart.StartWork();var opened=restart.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var w in opened)await w.WaitFor("window.jotReady===true");
                Check(kind+"-restart-never-opens-home",restart.Windows.Single(w=>w.Mode=="home") is {IsVisible:false} dormantHome&&dormantHome.Browser.CoreWebView2 is null);
                Check(kind+"-exact-note-set-or-one-new-blank",expected.Count>0?opened.Select(w=>w.NoteId!).ToHashSet().SetEquals(expected):opened.Length==1&&!notes.Any(n=>n.NoteId==opened[0].NoteId)&&await opened[0].Script("editor.textContent.trim()===''")=="true");
                Check(kind+"-always-offscreen",opened.All(w=>w.Left< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.Topmost));
            }
            finally{Cleanup(s);if(restart is not null)Cleanup(restart);}
        }
        foreach(var kind in new[]{"close-quit-race","reopen-during-close","late-input","pending-image","failed-save","new-note-quit"})
        {
            var path=Path.Combine(testOutput,"close-matrix",kind);var s=Create(path);JotSession? restart=null;
            try
            {
                await s.StartWork();var a=s.Windows.Single(w=>w.Mode=="note");await a.WaitFor("window.jotReady===true");a.TestSaveDelayMs=250;
                await a.Script("editor.innerHTML='<p>Before</p>';onEdit();saveNow().then(()=>window.baseSaved=true)");await a.WaitFor("window.baseSaved===true");
                if(kind is "close-quit-race" or "reopen-during-close")
                {
                    await a.Script("editor.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true}));editor.innerHTML='<p>Final composition فارسی English</p>';document.getElementById('hideButton').click()");
                    await Until(()=>a.HideRequested);
                    if(kind=="reopen-during-close"){s.OpenNote(a.NoteId!);await a.WaitFor("!app.inert");await a.Flush();Check(kind+"-last-open-wins",a.IsVisible&&!a.HideRequested);}
                    await s.ExecuteTrayAction("quit");
                    Check(kind+"-ime-text-is-not-lost",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Final composition فارسی English"));
                }
                else if(kind=="late-input")
                {
                    var writes=a.TestHostActions.Count(x=>x=="save");
                    await a.Script("editor.innerHTML='<p>Earlier draft</p>';onEdit();saveNow();clearTimeout(saveTimer)");await Until(()=>a.TestHostActions.Count(x=>x=="save")>writes);
                    await a.Script("editor.innerHTML='<p>Full final text فارسی English 📝</p>'"); // Intentionally no input notification.
                    await s.Quit();Check(kind+"-latest-dom-beats-pending-old-save",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Full final text فارسی English 📝"));
                }
                else if(kind=="pending-image")
                {
                    await a.Script("editor.focus();getSelection().selectAllChildren(editor);getSelection().collapseToEnd();const canvas=document.createElement('canvas');canvas.width=240;canvas.height=120;const source=canvas.toDataURL();readImage=()=>new Promise(resolve=>window.finishImage=()=>resolve(source));insertImages([new File(['fixture'],'fixture.png',{type:'image/png'})]);");
                    var closing=s.Quit();await a.WaitFor("editorLockedByHost");Check(kind+"-does-not-close-before-insert-finishes",!closing.IsCompleted&&a.IsVisible);await a.Script("window.finishImage()");await closing;
                    Check(kind+"-quit-waits-for-original-image",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("html").GetString()!.Contains("data:image/png;base64,"));
                }
                else if(kind=="failed-save")
                {
                    await a.Script("editor.innerHTML='<p>New flush must ignore old acknowledgements</p>';onEdit();clearTimeout(saveTimer)");
                    var flushing=a.Flush();await a.Script("JotBridge.send('flush-complete','stale-flush-token')");await Task.Delay(30);
                    Check("old-flush-ack-cannot-finish-a-new-save",!flushing.IsCompleted);await flushing;
                    await s.PrepareQuit();var snapshot=JsonSerializer.Serialize(await s.Store.LoadWindowSession());
                    await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_close_save BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic failure');END;");
                    await a.Script("editor.innerHTML='<p>Retry keeps my complete draft</p>';onEdit();clearTimeout(saveTimer)");
                    bool failed=false;try{await s.Quit();}catch(IOException){failed=true;}
                    await a.WaitFor("!app.inert");Check(kind+"-failure-keeps-open-editable-draft-and-session",failed&&a.IsVisible&&!a.windowClosed&&await a.Script("editor.textContent.includes('complete draft')")=="true"&&JsonSerializer.Serialize(await s.Store.LoadWindowSession())==snapshot);
                    await SetStoreTrigger(s.Store,"DROP TRIGGER fail_close_save;");await s.Quit();Check(kind+"-retry-writes-complete-draft",(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("complete draft"));
                }
                else
                {
                    var opening=s.NewNote();var closing=s.Quit();var again=s.Quit();Check("repeated-quit-shares-the-same-operation",ReferenceEquals(closing,again));var b=await opening;await closing;
                    Check(kind+"-accepted-new-note-is-not-orphaned",(await s.Store.LoadWindowSession()).Select(w=>w.NoteId).ToHashSet().SetEquals([a.NoteId!,b.NoteId!]));
                }
                Check(kind+"-quit-completes",s.Windows.Count==0);
                restart=Create(path);await restart.StartWork();var visible=restart.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var w in visible)await w.WaitFor("window.jotReady===true");
                Check(kind+"-restart-respects-close-intent",kind=="close-quit-race"?visible.Length==1&&visible[0].NoteId!=a.NoteId:visible.Any(w=>w.NoteId==a.NoteId));
                Check(kind+"-home-stays-closed",!restart.Windows.Single(w=>w.Mode=="home").IsVisible);
            }
            finally{Cleanup(s);if(restart is not null)Cleanup(restart);}
        }
    }
}
