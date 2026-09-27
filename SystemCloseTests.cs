using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;

namespace Jot;
public partial class MainWindow
{
    [DllImport("user32.dll",EntryPoint="PostMessageW")] private static extern bool PostProbeClose(nint window,uint message,nint wParam,nint lParam);
    internal async Task RunExitProbe(string[] args)
    {
        if(!testing||!session.ExitProbe)throw new InvalidOperationException("Exit probes require isolated test mode.");
        Directory.CreateDirectory(testOutput);
        int index=Array.IndexOf(args,"--probe-scenario");var scenario=index>=0&&index+1<args.Length?args[index+1]:"";
        if(scenario=="tray-anchor")
        {
            var checks=new List<object>();VerifyTrayEdges(checks);VerifyTrayOverflow(checks);await VerifyTrayNativeAnchor(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"tray-anchor-results.json"),json);
            using var result=JsonDocument.Parse(json);bool passed=result.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(!new[]{"quit","taskbar","wm-close","home-only","one-hidden","notes-only","restore"}.Contains(scenario))throw new InvalidDataException("Unknown exit probe.");
        await session.StartWork();
        var a=session.Windows.First(w=>w.Mode=="note");
        await a.WaitFor("window.jotReady===true");
        if(scenario=="restore")
        {
            var notes=session.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var n in notes)await n.WaitFor("window.jotReady===true");
            var restored=new List<object>();
            foreach(var n in notes)restored.Add(new{id=n.NoteId,text=JsonSerializer.Deserialize<string>(await n.Script("editor.textContent"))});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"probe-restored.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,homeVisible=session.Windows.Any(w=>w.Mode=="home"&&w.IsVisible),notes=restored}));
            await session.Quit();return;
        }
        var b=await session.NewNote();await b.WaitFor("window.jotReady===true");
        foreach(var n in new[]{a,b})await n.Script("editor.innerHTML='<p>Final process draft فارسی English '+model.activeId+'</p>';onEdit();clearTimeout(saveTimer)");
        if(scenario is "home-only" or "one-hidden")
        {
            await a.ClickControl("#hideButton");await WaitHidden(a);
            if(scenario=="home-only"){await b.ClickControl("#hideButton");await WaitHidden(b);}
        }
        if(scenario!="notes-only")
        {
            var home=session.Home();await home.WaitFor("window.jotReady===true");
        }
        var expected=session.Windows.Where(w=>w.Mode=="note"&&w.IsVisible).Select(w=>w.NoteId).ToArray();
        var safety=session.Windows.All(w=>!w.IsVisible||w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowInTaskbar&&!w.ShowActivated&&!w.Topmost);
        if(!safety)throw new InvalidOperationException("Refusing a probe that could touch the desktop.");
        await File.WriteAllTextAsync(Path.Combine(testOutput,"probe-expected.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,scenario,expected,originalIds=new[]{a.NoteId,b.NoteId},offscreen=safety}));
        if(scenario=="quit")
        {
            var settings=session.Settings();await settings.WaitFor("window.jotReady===true&&!!document.getElementById('settingsQuit')");
            await settings.ClickControl("#settingsQuit");return;
        }
        // Deliver the actual system messages to ONLY these owned, verified
        // offscreen windows. No shell automation or live-app HWND is involved.
        foreach(var window in session.Windows.Where(w=>w.IsVisible).ToArray())
        {
            var handle=new WindowInteropHelper(window).Handle;
            if(handle==0||!PostProbeClose(handle,scenario=="wm-close"?0x0010u:0x0112u,scenario=="wm-close"?0:(nint)0xF063,0))
                throw new InvalidOperationException("Could not deliver the owned-window close message.");
        }
    }
    private static async Task WaitHidden(MainWindow window)
    {
        for(int i=0;i<200;i++){if(!window.IsVisible)return;await Task.Delay(30);}throw new TimeoutException("Note did not hide.");
    }
    private async Task VerifySystemCloseProcesses(List<object> checks)
    {
        async Task<int> Run(string path,string scenario)
        {
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"--exit-probe","--probe-scenario",scenario,"--test-output",path})start.ArgumentList.Add(arg);
            using var child=Process.Start(start)??throw new IOException("Could not start the isolated exit probe.");
            try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));}
            catch{if(!child.HasExited)child.Kill();throw;}
            if(child.ExitCode!=0)throw new IOException("Isolated exit probe failed: "+scenario+". See its probe-error.txt.");
            return child.Id;
        }
        foreach(var scenario in new[]{"quit","taskbar","wm-close","home-only","one-hidden","notes-only"})
        {
            var path=Path.Combine(testOutput,"system-close",scenario);Directory.CreateDirectory(path);
            int firstPid=await Run(path,scenario);
            using var expected=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-expected.json")));
            using var exited=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-exit.json")));
            checks.Add(new{name="process-exit-"+scenario+"-really-terminates-with-no-windows",passed=exited.RootElement.GetProperty("processId").GetInt32()==firstPid&&exited.RootElement.GetProperty("exitCode").GetInt32()==0&&exited.RootElement.GetProperty("remainingWindows").GetInt32()==0&&expected.RootElement.GetProperty("offscreen").GetBoolean()});
            var ids=expected.RootElement.GetProperty("expected").EnumerateArray().Select(n=>n.GetString()!).ToHashSet();
            var store=new NoteStore(Path.Combine(path,"data"));
            checks.Add(new{name="process-exit-"+scenario+"-preserves-pre-close-note-set",passed=(await store.LoadWindowSession()).Select(w=>w.NoteId).ToHashSet().SetEquals(ids)});
            foreach(var id in expected.RootElement.GetProperty("originalIds").EnumerateArray().Select(n=>n.GetString()!))
                checks.Add(new{name="process-exit-"+scenario+"-flushes-last-draft-"+id,passed=(await store.LoadNote(id))!.Value.GetProperty("plain").GetString()!.Contains("Final process draft فارسی English "+id)});
            int secondPid=await Run(path,"restore");
            using var restored=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-restored.json")));
            var reopened=restored.RootElement.GetProperty("notes").EnumerateArray().ToArray();
            bool correct=ids.Count>0?reopened.Select(n=>n.GetProperty("id").GetString()!).ToHashSet().SetEquals(ids):reopened.Length==1&&reopened[0].GetProperty("text").GetString()=="";
            checks.Add(new{name="process-relaunch-"+scenario+"-restores-exactly-notes-not-home",passed=firstPid!=secondPid&&correct&&!restored.RootElement.GetProperty("homeVisible").GetBoolean()});
        }
    }
}
