using System.Text.Json;
using System.IO;
using System.Windows.Threading;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyWindowLifetimes(List<object> checks,MainWindow a,MainWindow b,string image)
    {
        void Progress(string stage)=>File.AppendAllText(Path.Combine(testOutput,"lifecycle-progress.log"),DateTimeOffset.UtcNow.ToString("O")+" "+stage+Environment.NewLine);
        Progress("start");
        var baseline=session.Windows.Count;
        var beforeA=await a.Script("editor.innerHTML");
        var beforeB=await b.Script("editor.innerHTML");
        var closedViewers=new List<MainWindow>();
        for(int i=0;i<6;i++)
        {
            Progress("open "+i);
            var viewer=session.Image(image);closedViewers.Add(viewer);
            await viewer.WaitFor("window.jotReady===true");
            Progress("ready "+i);
            var originalImage=await viewer.Script("document.getElementById('fullImage').naturalWidth===600&&document.getElementById('fullImage').naturalHeight===200")=="true";
            await viewer.ClickControl("#actualButton");await viewer.ClickControl("#fitButton");
            // Closing via the real image X now exercises the production lifetime path in tests.
            Progress("close "+i);
            viewer.Closed+=(_,_)=>Progress("closed-event "+i);
            await viewer.ClickControl("#imageClose");
            Progress("close-dispatched "+i);
            for(int wait=0;wait<250&&!viewer.windowClosed;wait++)await Task.Delay(20);
            if(!viewer.windowClosed)throw new TimeoutException("Image window did not finish closing.");
            EventHandler lateDeactivation=viewer.OnWindowDeactivated;
            lateDeactivation(viewer,EventArgs.Empty);
            viewer.OnWindowActivated(viewer,EventArgs.Empty);
            viewer.NotifyInputLanguage();viewer.UpdateNativeIcon();viewer.RoundWindow();
            var ignored=!viewer.Post(new { @event="late-message" });
            viewer.ClosePermanently();viewer.Reveal(); // Repeated close/reveal must be harmless.
            await session.Changed();
            checks.Add(new{name="image-close-late-focus-safe-cycle-"+(i+1),passed=originalImage&&ignored&&viewer.closingPermanently&&viewer.browserDisposed&&viewer.webView is null&&viewer.ImageSource is null&&!session.Windows.Contains(viewer)&&viewer.RuntimeErrors.Count==0});
            checks.Add(new{name="image-loads-preferences-without-copying-all-notes-"+(i+1),passed=viewer.TestHostActions.Contains("preferences-load")&&!viewer.TestHostActions.Contains("load")});
        }

        var queued=session.Image(image);closedViewers.Add(queued);
        Progress("queued-focus");
        await queued.WaitFor("window.jotReady===true");
        var navigation=queued.Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{
            queued.OnWindowDeactivated(queued,EventArgs.Empty);
            queued.NotifyInputLanguage();
            return queued.Post(new { @event="queued-after-close" });
        });
        queued.ClosePermanently();
        await navigation.Task;
        var delivered=navigation.Result;
        checks.Add(new{name="queued-focus-callback-after-disposal-is-ignored",passed=delivered is false&&queued.browserDisposed});

        for(int i=0;i<4;i++)
        {
            Progress("startup-close "+i);
            var early=session.Image(image);closedViewers.Add(early);
            // Yield once so initialization may be waiting on the shared browser environment/controller.
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);
            early.ClosePermanently();
            for(int wait=0;wait<100&&early.initialized&&!early.InitializationFinished;wait++)await Task.Delay(20);
            early.OnWindowDeactivated(early,EventArgs.Empty);
            checks.Add(new{name="close-during-image-startup-safe-"+(i+1),passed=early.browserDisposed&&(!early.initialized||early.InitializationFinished)&&!early.Post(new { @event="late-startup" }),initialized=early.initialized,finished=early.InitializationFinished,closed=early.windowClosed});
        }

        var bad=session.Image("data:image/png;base64,AAAA");closedViewers.Add(bad);
        Progress("bad-image");
        await bad.WaitFor("document.getElementById('imageError')!==null&&!document.getElementById('imageError').hidden");
        checks.Add(new{name="undecodable-image-shows-local-error-without-crashing",passed=bad.RuntimeErrors.Count==0&&session.Windows.Contains(a)&&session.Windows.Contains(b)});
        await bad.ClickControl("#imageClose");
        for(int wait=0;wait<250&&!bad.windowClosed;wait++)await Task.Delay(20);

        // A normal note close is still save/hide, not destruction of the editor.
        a.Close();
        Progress("note-hide");
        for(int wait=0;wait<100&&a.IsVisible;wait++)await Task.Delay(20);
        checks.Add(new{name="hidden-note-retains-live-message-channel",passed=!a.IsVisible&&!a.browserDisposed&&!a.closingPermanently&&a.Post(new { @event="active-window",active=false })});
        a.Reveal();await a.Flush();
        checks.Add(new{name="image-window-stress-preserves-notes-and-session",passed=session.Windows.Count==baseline&&beforeA==await a.Script("editor.innerHTML")&&beforeB==await b.Script("editor.innerHTML")&&closedViewers.All(window=>window.browserDisposed&&!session.Windows.Contains(window))});
        checks.Add(new{name="image-window-tests-stay-offscreen",passed=closedViewers.All(window=>!window.ShowInTaskbar&&!window.ShowActivated&&!window.Topmost&&window.Left< -10000)});
        Progress("complete");
    }
}
