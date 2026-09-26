using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

internal sealed class JotSession(bool testing, string output)
{
    public bool Testing { get; } = testing;
    public string Output { get; } = output;
    public NoteStore Store { get; } = new(testing ? Path.Combine(output, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jot"));
    public ErrorLog Log { get; } = new(testing ? Path.Combine(output, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jot"));
    public List<MainWindow> Windows { get; } = [];
    private Task<CoreWebView2Environment>? environment;
    private bool quitting;
    internal TrayMenuWindow? TrayMenu { get; private set; }
    internal readonly List<string> TestTrayActions=[];
    internal async Task ShowTrayMenu(System.Drawing.Point anchor)
    {
        if(quitting)return;
        JsonElement? data=null;
        try{data=await Store.Load();}
        catch(Exception error){Log.Error("tray-theme",error);}
        if(quitting)return;
        TrayMenu??=new TrayMenuWindow(Testing,ExecuteTrayAction,Log);
        var prefs=data?.GetProperty("prefs");
        TrayMenu.ApplyTheme(prefs is { } p&&p.TryGetProperty("theme",out var theme)?theme.GetString()??"dark":"dark");
        TrayMenu.OpenAt(anchor);
    }
    internal void UpdateTrayTheme(string theme)=>TrayMenu?.ApplyTheme(theme);
    internal void OpenTrayVisibilitySettings()
    {
        if(Testing){TestTrayActions.Add("tray-visibility");return;}
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar"){UseShellExecute=true});
    }
    internal async Task ExecuteTrayAction(string action)
    {
        if(Testing&&action!="tray-visibility")TestTrayActions.Add(action);
        switch(action)
        {
            case "home":Home();break;
            case "new-note":await NewNote();break;
            case "settings":Settings();break;
            case "tray-visibility":OpenTrayVisibilitySettings();break;
            case "quit":if(Testing)await FlushNotes();else await Quit();break;
            default:throw new InvalidOperationException("Unknown tray action.");
        }
    }
    public Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (environment is not null) return environment;
        var profile = Path.Combine(Store.Root, "webview");
        if (!Testing && !File.Exists(Store.FilePath))
        {
            var legacy = new[] { Path.Combine(AppContext.BaseDirectory, "Jot.exe.WebView2"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "bin", "Release", "net10.0-windows", "win-x64", "publish", "Jot.exe.WebView2")) }.FirstOrDefault(Directory.Exists);
            if (legacy is not null) profile = legacy;
        }
        return environment = CoreWebView2Environment.CreateAsync(null, profile,
            Testing ? new CoreWebView2EnvironmentOptions("--disable-backgrounding-occluded-windows") : null);
    }
    public MainWindow Home(bool appearance = false)
    {
        if (appearance) return Settings();
        var window = Windows.FirstOrDefault(window => window.Mode == "home") ?? new MainWindow(this, "home");
        window.ShowHomeView(false);
        window.Reveal();
        return window;
    }
    public MainWindow Settings()
    {
        var window = Windows.FirstOrDefault(window => window.Mode == "home") ?? new MainWindow(this, "home");
        window.ShowHomeView(true);
        window.Reveal();
        return window;
    }
    public async Task<MainWindow> NewNote()
    {
        var id = await Store.Create();
        await Changed(preferences: false);
        return OpenNote(id);
    }
    public MainWindow OpenNote(string id)
    {
        var window = Windows.FirstOrDefault(window => window.Mode == "note" && window.NoteId == id) ?? new MainWindow(this, "note", id);
        window.Reveal();
        return window;
    }
    public MainWindow Image(string src)
    {
        var window = new MainWindow(this, "image") { ImageSource = src };
        window.Reveal();
        return window;
    }
    public async Task Changed(bool preferences = true, string? noteId = null)
    {
        var data = preferences || noteId is not null ? await Store.Load() : null;
        foreach (var window in Windows.ToArray())
        {
            if (data is not null)
            {
                if (preferences) window.Post(new { @event = "preferences", prefs = data.Value.GetProperty("prefs") });
                if (window.Mode == "note" && (preferences || window.NoteId == noteId))
                {
                    var note = data.Value.GetProperty("notes").EnumerateArray().FirstOrDefault(n => n.GetProperty("id").GetString() == window.NoteId);
                    if (note.ValueKind == JsonValueKind.Object)
                        window.Post(new { @event = "note-color", color = note.GetProperty("color").GetString() });
                }
            }
            if (window.Mode == "home") window.Post(new { @event = "notes-changed" });
        }
    }
    internal async Task FlushNotes()
    {
        foreach (var window in Windows.Where(window => window.Mode == "note").ToArray()) await window.Flush();
    }
    public async Task Quit()
    {
        if (quitting) return;
        quitting = true;
        try
        {
            await FlushNotes();
            TrayMenu?.Close();TrayMenu=null;
            foreach (var window in Windows.ToArray()) window.ClosePermanently();
            System.Windows.Application.Current.Shutdown();
        }
        catch { quitting = false; throw; }
    }
}
