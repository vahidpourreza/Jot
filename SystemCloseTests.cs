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
        if(scenario is "responsiveness" or "desktop-input" or "browser-tabs" or "tab-colors" or "selection-appearance" or "document-ui" or "document-files" or "emoji-picker")
        {
            var checks=new List<object>();
            try{
                if(scenario=="desktop-input"){await VerifyWebViewInputGuard(checks);await VerifyBrowserHostVisibility(checks);}
                if(scenario=="responsiveness"){await VerifyResponsiveness(checks);await VerifyIncrementalContent(checks);await VerifyToastIdle(checks);}
                if(scenario=="browser-tabs")await VerifyBrowserTabs(checks);
                if(scenario=="selection-appearance")await VerifySelectionAppearance(checks);
                if(scenario=="document-ui")await VerifyDocumentEditing(checks);
                if(scenario=="document-files")await VerifyDocumentFileIdentity(checks);
                if(scenario=="emoji-picker")await VerifyEmojiPicker(checks);
                if(scenario is "browser-tabs" or "tab-colors"){await VerifyTabColors(checks);await VerifyReopenShortcutMigration(checks);}
            }catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,scenario+"-results.json"),json);
            using var report=JsonDocument.Parse(json);var passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario is "editing-controls" or "toast-design" or "custom-shortcuts")
        {
            var checks=new List<object>();
            try{
                if(scenario=="editing-controls")await VerifyStandardEditing(checks);
                if(scenario is "editing-controls" or "toast-design")await VerifyToastDesign(checks);
                if(scenario is "editing-controls" or "custom-shortcuts")await VerifyCustomShortcuts(checks);
            }catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"editing-controls-results.json"),json);
            using var report=JsonDocument.Parse(json);var passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="standard-editing")
        {
            var checks=new List<object>();
            try{await VerifyStandardEditing(checks);}catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"editing-results.json"),json);
            using var report=JsonDocument.Parse(json);var passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario is "workspace-files-20260929" or "file-state" or "personalization" or "shortcuts-settings" or "native-presentation")
        {
            var checks=new List<object>();
            try{
                if(scenario is "workspace-files-20260929" or "file-state"){await VerifyFileWriteRaces(checks);await VerifyLegacyFileState(checks);await VerifyFileStateAutosave(checks);await VerifyFileStatusUi(checks);}
                if(scenario is "workspace-files-20260929" or "native-presentation")await VerifyNativeWindowPresentation(checks);
                if(scenario is "workspace-files-20260929" or "personalization")await VerifyNotePersonalization(checks);
                if(scenario is "workspace-files-20260929" or "shortcuts-settings")await VerifyShortcutsSettings(checks);
            }catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"feature-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="taskbar-menu")
        {
            var checks=new List<object>();await VerifyTaskbarActions(checks);await VerifySessionRestore(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"taskbar-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="icons")
        {
            var checks=new List<object>();VerifyIconSizes(checks);await VerifyApprovedIconAssets(checks);await VerifyTrayNativeAnchor(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"icon-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            session.TrayMenu?.Close();ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario is "library-refresh" or "library-redesign" or "writing-defaults" or "workspace-session")
        {
            var checks=new List<object>();
            try
            {
                if(scenario is "library-refresh" or "library-redesign")await VerifyLibraryRedesign(checks);
                if(scenario is "library-refresh" or "writing-defaults"){await VerifyPasteSpacing(checks);await VerifyInheritedWriting(checks);}
                if(scenario is "library-refresh" or "workspace-session")await VerifyWorkspaceSession(checks);
                if(scenario=="library-refresh"){await VerifyFileMenusAndIcon(checks);await VerifyTabOverflow(checks);}
            }
            catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"library-refresh-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario is "editing-files" or "tab-overflow" or "opening-mode" or "undo-selection" or "note-files")
        {
            var checks=new List<object>();
            try
            {
                if(scenario is "editing-files" or "tab-overflow")await VerifyTabOverflow(checks);
                if(scenario is "editing-files" or "opening-mode")await VerifyOpeningMode(checks);
                if(scenario is "editing-files" or "undo-selection")await VerifyUndoSelection(checks);
                if(scenario is "editing-files" or "note-files"){await VerifyNoteFiles(checks);await VerifyFileCommandRouting(checks);}
            }
            catch{await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));throw;}
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"editing-files-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="new-note-settings")
        {
            var checks=new List<object>();await VerifyNewNoteDestination(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"new-note-settings-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="library")
        {
            var checks=new List<object>();await VerifyLibrary(checks);await VerifyTabbedNotes(checks);await VerifyWorkspaceMotion(checks);await VerifyWorkspaceReadiness(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"library-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="close-last")
        {
            var checks=new List<object>();await VerifyLastWindowClose(checks);await VerifySystemCloseProcesses(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"close-last-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario is "last-note" or "last-home" or "last-settings" or "last-image" or "last-delete")
        {await RunLastCloseProbe(scenario);return;}
        if(scenario is "note-fullscreen" or "note-motion")
        {
            var checks=new List<object>();await VerifyNoteTools(checks);await VerifyNoteFullscreen(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"note-fullscreen-results.json"),json);
            using var report=JsonDocument.Parse(json);bool passed=report.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(scenario=="tray-anchor")
        {
            var checks=new List<object>();VerifyTrayEdges(checks);VerifyTrayOverflow(checks);await VerifyTrayNativeAnchor(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(Path.Combine(testOutput,"tray-anchor-results.json"),json);
            using var result=JsonDocument.Parse(json);bool passed=result.RootElement.EnumerateArray().All(c=>c.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);return;
        }
        if(!new[]{"quit","taskbar","wm-close","home-only","one-hidden","notes-only","restore"}.Contains(scenario))throw new InvalidDataException("Unknown exit probe.");
        if(scenario!="restore")await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
        await session.StartWork();
        if(scenario=="restore")
        {
            var notes=session.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var n in notes)await n.WaitFor("window.jotReady===true");
            var workspace=session.Windows.FirstOrDefault(w=>w.Mode=="home"&&w.IsVisible);
            if(workspace is not null)await workspace.WaitFor("window.jotReady===true");
            var restored=new List<object>();
            foreach(var n in notes)restored.Add(new{id=n.NoteId,text=JsonSerializer.Deserialize<string>(await n.Script("editor.textContent"))});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"probe-restored.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,homeVisible=workspace is not null,settingsVisible=workspace?.IsSettingsView==true,notes=restored}));
            await session.Quit();return;
        }
        var a=session.Windows.First(w=>w.Mode=="note");
        await a.WaitFor("window.jotReady===true");
        var b=await session.NewNote();await b.WaitFor("window.jotReady===true");
        foreach(var n in new[]{a,b})await n.Script("editor.innerHTML='<p>Final process draft فارسی English '+model.activeId+'</p>';onEdit();clearTimeout(saveTimer)");
        if(scenario is "home-only" or "one-hidden")
        {
            if(scenario=="home-only"){var home=session.Home();await home.WaitFor("window.jotReady===true");}
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
            var settings=session.Settings();await settings.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
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
        foreach(var scenario in new[]{"quit","taskbar","wm-close","home-only","one-hidden","notes-only","last-note","last-home","last-settings","last-image","last-delete"})
        {
            var path=Path.Combine(testOutput,"system-close",scenario);Directory.CreateDirectory(path);
            int firstPid=await Run(path,scenario);
            using var expected=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-expected.json")));
            using var exited=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-exit.json")));
            checks.Add(new{name="process-exit-"+scenario+"-really-terminates-with-no-windows",passed=exited.RootElement.GetProperty("processId").GetInt32()==firstPid&&exited.RootElement.GetProperty("exitCode").GetInt32()==0&&exited.RootElement.GetProperty("remainingWindows").GetInt32()==0&&expected.RootElement.GetProperty("offscreen").GetBoolean()});
            var ids=expected.RootElement.GetProperty("expected").EnumerateArray().Select(n=>n.GetString()!).ToHashSet();
            var store=new NoteStore(Path.Combine(path,"data"));
            if(expected.RootElement.TryGetProperty("deletedId",out var deletedId)&&deletedId.ValueKind==JsonValueKind.String)
                checks.Add(new{name="process-exit-last-delete-does-not-resurrect-note",passed=!await store.Contains(deletedId.GetString()!)});
            checks.Add(new{name="process-exit-"+scenario+"-preserves-pre-close-note-set",passed=(await store.LoadWindowSession()).Select(w=>w.NoteId).ToHashSet().SetEquals(ids)});
            foreach(var id in expected.RootElement.GetProperty("originalIds").EnumerateArray().Select(n=>n.GetString()!))
                checks.Add(new{name="process-exit-"+scenario+"-flushes-last-draft-"+id,passed=(await store.LoadNote(id))!.Value.GetProperty("plain").GetString()!.Contains("Final process draft فارسی English "+id)});
            int secondPid=await Run(path,"restore");
            using var restored=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"probe-restored.json")));
            var reopened=restored.RootElement.GetProperty("notes").EnumerateArray().ToArray();
            var keepsSettings=scenario is "quit" or "last-settings";
            bool correct=ids.Count>0?reopened.Select(n=>n.GetProperty("id").GetString()!).ToHashSet().SetEquals(ids):keepsSettings?reopened.Length==0:reopened.Length==1&&reopened[0].GetProperty("text").GetString()=="";
            checks.Add(new{name="process-relaunch-"+scenario+"-restores-exact-work-and-settings",passed=firstPid!=secondPid&&correct&&restored.RootElement.GetProperty("homeVisible").GetBoolean()==keepsSettings&&restored.RootElement.GetProperty("settingsVisible").GetBoolean()==keepsSettings});
        }
    }
}
