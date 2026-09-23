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
        await Changed();
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
    public async Task Changed()
    {
        var data = await Store.Load();
        foreach (var window in Windows.ToArray())
        {
            if (data is not null)
                window.Post(new { @event = "preferences", prefs = data.Value.GetProperty("prefs") });
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
            foreach (var window in Windows.ToArray()) window.ClosePermanently();
            System.Windows.Application.Current.Shutdown();
        }
        catch { quitting = false; throw; }
    }
}
