using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private bool packageSmokeStarted;
    private async Task RunPackageSmoke()
    {
        if(packageSmokeStarted)return;packageSmokeStarted=true;
        var checks=new List<object>();Directory.CreateDirectory(testOutput);
        try
        {
            await WaitFor("window.jotReady===true");
            var root=Path.GetFullPath(AppContext.BaseDirectory);
            bool Local(string path)=>Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase);
            var browser=Process.GetProcessById((int)Browser.CoreWebView2.BrowserProcessId).MainModule?.FileName??"";
            var runtime=BrowserRuntime.Resolve();
            checks.Add(new{name="offline-package-requires-own-runtime",passed=BrowserRuntime.RequiresBundledRuntime&&runtime is not null});
            checks.Add(new{name="dotnet-runtime-loaded-from-app-folder",passed=Local(typeof(object).Assembly.Location)});
            checks.Add(new{name="wpf-runtime-loaded-from-app-folder",passed=Local(typeof(System.Windows.Window).Assembly.Location)});
            checks.Add(new{name="webview-process-uses-bundled-runtime",passed=runtime is not null&&browser.StartsWith(runtime+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),version=Browser.CoreWebView2.Environment.BrowserVersionString});
            var note=await session.NewNote();await note.WaitFor("window.jotReady===true");
            await note.Script("editor.innerHTML='<p>Offline package فارسی English</p>';onEdit();saveNow().then(()=>window.packageSaved=true)");await note.WaitFor("window.packageSaved===true");
            var saved=await store.LoadNote(note.NoteId!);
            checks.Add(new{name="bundled-sqlite-saves-bilingual-note",passed=saved?.GetProperty("plain").GetString()?.Contains("Offline package فارسی English")==true});
            await note.Reload();
            checks.Add(new{name="packaged-editor-reopens-saved-note",passed=await note.Script("editor.textContent==='Offline package فارسی English'")=="true"});
            checks.Add(new{name="smoke-windows-never-interrupt-desktop",passed=session.Windows.All(w=>w.Left< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost)});
            checks.Add(new{name="smoke-has-no-renderer-errors",passed=session.Windows.All(w=>w.RuntimeErrors.Count==0)});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"package-smoke.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            using var report=JsonDocument.Parse(JsonSerializer.Serialize(checks));
            if(report.RootElement.EnumerateArray().Any(c=>!c.GetProperty("passed").GetBoolean()))throw new InvalidOperationException("Offline package smoke checks failed.");
            await session.Quit();
        }
        catch(Exception error)
        {
            LogFailure(error);
            foreach(var window in session.Windows.ToArray())window.ClosePermanently();
            System.Windows.Application.Current.Shutdown(2);
        }
    }
}
