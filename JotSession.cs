using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

internal sealed class JotSession(bool testing, string output)
{
    public bool Testing { get; } = testing;
    public string Output { get; } = output;
    internal bool PackageSmoke { get; init; }
    internal bool ExerciseLifecycle { get; init; }
    internal bool ExitProbe { get; init; }
    public NoteStore Store { get; } = new(testing ? Path.Combine(output, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jot"));
    public ErrorLog Log { get; } = new(testing ? Path.Combine(output, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jot"));
    public List<MainWindow> Windows { get; } = [];
    private Task<CoreWebView2Environment>? environment;
    private bool quitting;
    private Task? quitTask;
    private Task? startingWork;
    private readonly HashSet<Task<MainWindow>> openingNotes=[];
    internal TrayMenuWindow? TrayMenu { get; private set; }
    internal readonly List<string> TestTrayActions=[];
    internal async Task ShowTrayMenu(System.Drawing.Point anchor,System.Drawing.Rectangle? iconBounds=null)
    {
        if(quitting)return;
        JsonElement? data=null;
        try{data=await Store.LoadPreferences();}
        catch(Exception error){Log.Error("tray-theme",error);}
        if(quitting)return;
        TrayMenu??=new TrayMenuWindow(Testing,ExecuteTrayAction,Log);
        var prefs=data;
        TrayMenu.ApplyTheme(prefs is { } p&&p.TryGetProperty("theme",out var theme)?theme.GetString()??"dark":"dark");
        TrayMenu.OpenAt(anchor,iconBounds);
    }
    internal void UpdateTrayTheme(string theme)=>TrayMenu?.ApplyTheme(theme);
    internal async Task ExecuteTrayAction(string action)
    {
        if(Testing)TestTrayActions.Add(action);
        switch(action)
        {
            case "home":Home();break;
            case "new-note":await NewNote();break;
            case "settings":Settings();break;
            case "quit":if(Testing&&!ExerciseLifecycle&&!ExitProbe)await FlushNotes();else await Quit();break;
            default:throw new InvalidOperationException("Unknown tray action.");
        }
    }
    public Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (environment is not null) return environment;
        var profile = Path.Combine(Store.Root, "webview");
        if (!Testing && !File.Exists(Store.LegacyFilePath) && !Directory.Exists(profile))
        {
            var legacy = new[] { Path.Combine(AppContext.BaseDirectory, "Jot.exe.WebView2"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "bin", "Release", "net10.0-windows", "win-x64", "publish", "Jot.exe.WebView2")) }.FirstOrDefault(Directory.Exists);
            if (legacy is not null) profile = legacy;
        }
        return environment = BrowserRuntime.CreateAsync(profile,Testing);
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
    public Task<MainWindow> NewNote()
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=NewNoteCore();openingNotes.Add(pending);
        return ObserveCreation(pending);
    }
    private async Task<MainWindow> ObserveCreation(Task<MainWindow> pending)
    {try{return await pending;}finally{openingNotes.Remove(pending);}}
    private async Task<MainWindow> NewNoteCore()
    {
        var id = await Store.Create();
        await Changed(preferences: false);
        return OpenNoteCore(id);
    }
    public MainWindow OpenNote(string id,SavedNoteWindow? layout=null)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        return OpenNoteCore(id,layout);
    }
    private MainWindow OpenNoteCore(string id,SavedNoteWindow? layout=null)
    {
        var window=Windows.FirstOrDefault(window=>window.Mode=="note"&&window.NoteId==id);
        if(window is null){window=new MainWindow(this,"note",id);if(layout is not null)window.RestoreNoteLayout(layout);}
        window.Reveal();
        return window;
    }
    internal async Task StartWork()
    {
        var pending=startingWork??=RestoreWork();
        try{await pending;}catch{if(ReferenceEquals(startingWork,pending))startingWork=null;throw;}
    }
    private async Task RestoreWork()
    {
        var saved=await Store.LoadWindowSession();
        if(saved.Length==0){if(!quitting)await NewNote();return;}
        foreach(var note in saved)OpenNoteCore(note.NoteId,note);
    }
    internal async Task ActivateWork()
    {
        if(quitting)return;
        if(startingWork is null&&!Windows.Any(window=>window.Mode=="note")){await StartWork();return;}
        if(startingWork is not null)await startingWork;
        var visible=Windows.Where(window=>window.Mode=="note"&&window.IsVisible).ToArray();
        if(visible.Length==0)await NewNote();else foreach(var window in visible)window.Reveal();
    }
    internal async Task PrepareQuit()
    {
        await FlushNotes();
        await Store.SaveWindowSession(Windows.Where(window=>window.Mode=="note"&&window.IsVisible&&!window.HideRequested&&window.WindowState!=System.Windows.WindowState.Minimized).Select(window=>window.CaptureNoteLayout()));
        await Store.Backup();
    }
    public MainWindow Image(string src)
    {
        var window = new MainWindow(this, "image") { ImageSource = src };
        window.Reveal();
        return window;
    }
    public async Task Changed(bool preferences = true, string? noteId = null)
    {
        JsonElement? prefs=preferences?await Store.LoadPreferences():null;
        if(prefs is not null)UpdateTrayTheme(prefs.Value.GetProperty("theme").GetString()??"dark");
        var color=noteId is not null?await Store.LoadNoteColor(noteId):null;
        foreach (var window in Windows.ToArray())
        {
            if(prefs is not null)window.Post(new{@event="preferences",prefs=prefs.Value});
            if(window.Mode=="note"&&window.NoteId==noteId&&color is not null)window.Post(new{@event="note-color",color});
            if (window.Mode == "home") window.Post(new { @event = "notes-changed" });
        }
    }
    internal async Task FlushNotes()
    {
        foreach (var window in Windows.Where(window => window.Mode == "note").ToArray()) await window.Flush(quitting);
    }
    public Task Quit()
    {
        if(quitTask is {IsCompleted:false})return quitTask;
        if(quitting)return Task.CompletedTask;
        return quitTask=QuitCore();
    }
    internal async void RequestSystemQuit()
    {
        try{await Quit();}
        catch(Exception error)
        {
            Log.Error("system-close",error);
            foreach(var window in Windows.ToArray())window.Post(new{@event="warning",message="Could not save all notes. Jot is still open; please try Quit again."});
        }
    }
    private async Task QuitCore()
    {
        quitting = true;
        try
        {
            if(startingWork is not null)await startingWork;
            if(openingNotes.Count>0)await Task.WhenAll(openingNotes.ToArray());
            foreach(var window in Windows.Where(window=>window.Mode=="note"))window.Post(new{@event="prepare-quit"});
            await PrepareQuit();
            TrayMenu?.Close();TrayMenu=null;
            foreach (var window in Windows.ToArray()) window.ClosePermanently();
            if(!Testing||!ExerciseLifecycle)System.Windows.Application.Current.Shutdown();
        }
        catch { quitting = false;foreach(var window in Windows.Where(window=>window.Mode=="note"))window.Post(new{@event="resume-editing"});throw; }
    }
}
