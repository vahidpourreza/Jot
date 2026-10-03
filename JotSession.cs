using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

internal sealed partial class JotSession(bool testing, string output)
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
    private readonly SemaphoreSlim noteOwnershipGate=new(1,1);
    internal void RememberNoteWindow(MainWindow window){}
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
    internal async Task ExecuteTaskbarAction(TaskbarAction action)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        switch(action)
        {
            case TaskbarAction.Home:Home();break;
            case TaskbarAction.NewNote:await NewNote();break;
            case TaskbarAction.Settings:Settings();break;
            default:throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
    internal Task StartFromLaunch(TaskbarAction? action,bool trayOnly=false)=>action is { } command?ExecuteTaskbarAction(command):trayOnly?Task.CompletedTask:StartWork();
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
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        if (appearance) return Settings();
        var window = Windows.FirstOrDefault(window => window.Mode == "home") ?? new MainWindow(this, "home");
        window.ShowHomeView(false);
        window.Reveal();
        return window;
    }
    public MainWindow Settings()
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var window = Windows.FirstOrDefault(window => window.Mode == "home") ?? new MainWindow(this, "home");
        window.ShowHomeView(true);
        window.Reveal();
        return window;
    }
    public Task<MainWindow> NewNote(string? folder=null)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=NewNoteCore(folder);openingNotes.Add(pending);
        return ObserveCreation(pending);
    }
    private async Task<MainWindow> ObserveCreation(Task<MainWindow> pending)
    {try{return await pending;}finally{openingNotes.Remove(pending);}}
    private async Task<MainWindow> NewNoteCore(string? folder)
    {
        await noteOwnershipGate.WaitAsync();
        try{return await CreatePreferredNote(folder);}
        finally{noteOwnershipGate.Release();}
    }
    private async Task<MainWindow> CreatePreferredNote(string? folder)
    {
        // Capture Home before the first await so closing it while creation is
        // pending is respected. Reading the preference remains part of the
        // accepted openingNotes operation that close/quit already waits for.
        var workspace=Windows.FirstOrDefault(window=>window.Mode=="home");
        var workspaceWasVisible=workspace?.IsVisible==true;
        var prefs=await Store.LoadPreferences();
        var id = await Store.Create();
        if(!string.IsNullOrEmpty(folder))await Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,group=folder}));
        await Changed(preferences: false);
        if(prefs.GetProperty("newNoteTarget").GetString()=="tab")
        {
            if(workspace is not null&&(!Windows.Contains(workspace)||workspaceWasVisible&&(workspace.HideRequested||!workspace.IsVisible)))return OpenNoteCore(id);
            workspace??=Windows.FirstOrDefault(window=>window.Mode=="home")??new MainWindow(this,"home");
            // Reveal the workspace without resetting its current page. Its tab
            // queue saves the active editor before installing this new note.
            workspace.Reveal();
            try{await workspace.SwitchNoteTab(id);}
            catch when(workspace.HideRequested||!workspace.IsVisible||!Windows.Contains(workspace))
            {return await OpenCreatedNoteAfterWorkspaceClose(workspace,id);}
            if(workspace.HideRequested||!workspace.IsVisible||!Windows.Contains(workspace))return await OpenCreatedNoteAfterWorkspaceClose(workspace,id);
            return workspace;
        }
        return OpenNoteCore(id);
    }
    private async Task<MainWindow> OpenCreatedNoteAfterWorkspaceClose(MainWindow workspace,string id)
    {
        // Close can arrive during the queued switch, after the new tab has
        // acquired the note. Release that ownership before opening its window.
        if(Windows.Contains(workspace)&&workspace.ContainsNote(id))await workspace.DetachNoteTab(id);
        return OpenNoteCore(id);
    }
    public MainWindow OpenNote(string id,SavedNoteWindow? layout=null)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        return OpenNoteCore(id,layout);
    }
    private MainWindow OpenNoteCore(string id,SavedNoteWindow? layout=null)
    {
        var window=Windows.FirstOrDefault(window=>window.ContainsNote(id));
        if(window is null){window=new MainWindow(this,"note",id);if(layout is not null)window.RestoreNoteLayout(layout);}
        window.Reveal();
        if(window.NoteId!=id)_=ActivateTab(window,id);
        RememberNoteWindow(window);
        return window;
    }
    private async Task ActivateTab(MainWindow window,string id)
    {try{await window.SwitchNoteTab(id);}catch(Exception error){Log.Error("activate-tab",error);window.Post(new{@event="warning",message="Could not switch notes. Your saved notes are unchanged."});}}
    internal Task<MainWindow> OpenAsTab(string id)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=OpenAsTabCore(id);openingNotes.Add(pending);return ObserveCreation(pending);
    }
    private async Task<MainWindow> OpenAsTabCore(string id)
    {
        await noteOwnershipGate.WaitAsync();
        try{return await OpenAsTabOwned(id);}
        finally{noteOwnershipGate.Release();}
    }
    private async Task<MainWindow> OpenAsTabOwned(string id)
    {
        if(!Guid.TryParse(id,out _)||!await Store.Contains(id))throw new InvalidDataException("Note not found.");
        var owner=Windows.FirstOrDefault(w=>w.ContainsNote(id));
        var target=Windows.FirstOrDefault(w=>w.Mode=="home")??new MainWindow(this,"home");
        target.Reveal();
        if(owner is not null&&owner!=target)await owner.DetachNoteTab(id);
        // Home may close while the source finishes saving. Keep that close
        // and return the saved note to a visible ordinary window instead.
        if(target.HideRequested||!target.IsVisible||!Windows.Contains(target))return OpenNoteCore(id);
        try{await target.SwitchNoteTab(id);return target;}
        catch{if(owner is not null&&owner!=target&&!Windows.Any(w=>w.ContainsNote(id)))OpenNoteCore(id);throw;}
    }
    internal Task<MainWindow> OpenNoteWindow(string id)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=OpenNoteWindowCore(id);openingNotes.Add(pending);return ObserveCreation(pending);
    }
    private async Task<MainWindow> OpenNoteWindowCore(string id)
    {
        await noteOwnershipGate.WaitAsync();
        try{return await OpenNoteWindowOwned(id);}
        finally{noteOwnershipGate.Release();}
    }
    private async Task<MainWindow> OpenNoteWindowOwned(string id)
    {
        if(!Guid.TryParse(id,out _)||!await Store.Contains(id))throw new InvalidDataException("Note not found.");
        var owner=Windows.FirstOrDefault(w=>w.ContainsNote(id));
        if(owner is {Mode:"home"})await owner.DetachNoteTab(id);
        return OpenNoteCore(id);
    }
    internal Task<MainWindow> NewTab(MainWindow owner,string? folder=null)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        if(owner.Mode!="home")throw new InvalidOperationException("Tabs are available only in Home.");
        async Task<MainWindow> Create()
        {
            await noteOwnershipGate.WaitAsync();
            try
            {
                var id=await Store.Create();
                if(!string.IsNullOrEmpty(folder))await Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,group=folder}));
                if(!Windows.Contains(owner)||owner.HideRequested)return OpenNoteCore(id);
                await owner.SwitchNoteTab(id);await Changed(false);return owner;
            }
            finally{noteOwnershipGate.Release();}
        }
        var pending=Create();openingNotes.Add(pending);return ObserveCreation(pending);
    }
    internal async Task StartWork()
    {
        var pending=startingWork??=RestoreWork();
        try{await pending;}catch{if(ReferenceEquals(startingWork,pending))startingWork=null;throw;}
    }
    private async Task RestoreWork()
    {
        var saved=await Store.LoadWindowSession();
        var workspace=await Store.LoadWorkspaceSession();
        if(saved.Length==0&&(workspace is null||workspace.Tabs.Length==0&&!workspace.SettingsOpen)){if(!quitting)await NewNote();return;}
        var tabs=(await Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab";
        MainWindow? restoredWorkspace=null;
        if(workspace is not null)
        {
            if(tabs||workspace.SettingsOpen)
            {
                restoredWorkspace=Windows.FirstOrDefault(window=>window.Mode=="home")??new MainWindow(this,"home");
                await restoredWorkspace.RestoreSavedWorkspace(tabs?workspace:workspace with{Tabs=[],ActiveId=workspace.ActiveId=="settings"?"settings":"home"});
            }
            if(!tabs)foreach(var id in workspace.Tabs)OpenNoteCore(id,new(id,workspace.X,workspace.Y,workspace.Width,workspace.Height));
        }
        foreach(var note in saved)
            await RestoreNoteFromSession(note,tabs);
        if(tabs&&restoredWorkspace is not null&&workspace is not null)await restoredWorkspace.SwitchNoteTab(workspace.ActiveId);
    }
    internal Task<MainWindow> RestoreNoteFromSession(SavedNoteWindow saved,bool preferTabs)
    {
        var workspace=Windows.FirstOrDefault(window=>window.Mode=="home");
        // Explicit separate windows can coexist with a full workspace. Keep
        // those notes open on restart without exceeding the workspace limit.
        if(preferTabs&&(workspace is null||workspace.NoteTabIds.Count<200||workspace.NoteTabIds.Contains(saved.NoteId)))return OpenAsTabCore(saved.NoteId);
        return Task.FromResult(OpenNoteCore(saved.NoteId,saved));
    }
    internal async Task ActivateWork()
    {
        if(quitting)return;
        if(startingWork is null&&!Windows.Any(window=>window.Mode=="note"||window.Mode=="home"&&(window.NoteTabIds.Count>0||window.SettingsTabOpen))){await StartWork();return;}
        if(startingWork is not null)await startingWork;
        var visible=Windows.Where(window=>(window.Mode=="note"||window.Mode=="home"&&(window.NoteTabIds.Count>0||window.SettingsTabOpen))&&window.IsVisible).ToArray();
        if(visible.Length==0)await NewNote();else foreach(var window in visible)window.Reveal();
    }
    internal async Task PrepareQuit()
    {
        await FlushNotes();
        await SaveAutoFilesFor(Windows.SelectMany(window=>window.Mode=="home"?window.NoteTabIds:window.NoteId is {} id?new List<string>{id}:new List<string>()));
        var home=Windows.FirstOrDefault(window=>window.Mode=="home"&&(window.IsVisible&&!window.HideRequested||window.RememberWorkspaceOnQuit));
        await Store.SaveWindowSession(CaptureStandaloneSession(),home?.CaptureWorkspace());
        await Store.Backup();
    }
    private SavedNoteWindow[] CaptureStandaloneSession()=>Windows.Where(window=>window.Mode=="note"&&window.IsVisible&&!window.HideRequested&&window.WindowState!=System.Windows.WindowState.Minimized).Select(window=>window.CaptureNoteLayout()).ToArray();
    internal async Task SaveClosingWorkspace(MainWindow workspace)
    {
        // Snapshot before hiding: a later Quit must retain closed workspace tabs,
        // while explicit tab close updates the retained workspace membership.
        await Store.SaveWindowSession(CaptureStandaloneSession(),workspace.CaptureWorkspace());
        workspace.RememberWorkspaceOnQuit=true;
    }
    public MainWindow Image(string src)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var window = new MainWindow(this, "image") { ImageSource = src };
        window.Reveal();
        return window;
    }
    public async Task Changed(bool preferences = true, string? noteId = null)
    {
        JsonElement? prefs=preferences?await Store.LoadPreferences():null;
        if(prefs is not null)UpdateTrayTheme(prefs.Value.GetProperty("theme").GetString()??"dark");
        var color=noteId is not null?await Store.LoadNoteColor(noteId):null;
        var identities=noteId is not null?await Store.LoadTabHeaders(new[]{noteId}):Array.Empty<JsonElement>();
        foreach (var window in Windows.ToArray())
        {
            if(prefs is not null){window.ApplyWorkspacePin(prefs.Value);window.Post(new{@event="preferences",prefs=prefs.Value});}
            if(identities.Length>0)window.Post(new{@event="note-identity",note=identities[0]});
            if(window.HasNoteEditor&&window.NoteId==noteId&&color is not null)window.Post(new{@event="note-color",color});
            if (window.Mode == "home") window.Post(new { @event = "notes-changed" });
            if(noteId is not null&&window.ContainsNote(noteId))await window.RefreshTabHeaders();
        }
    }
    internal async Task FlushNotes()
    {
        foreach (var window in Windows.Where(window => window.Mode is "note" or "home").ToArray()) await window.Flush(quitting);
    }
    internal async Task<JsonElement> ReadLatestNote(string id)
    {
        if(!Guid.TryParse(id,out _))throw new InvalidDataException("Invalid note.");
        var editor=Windows.FirstOrDefault(w=>w.ContainsNote(id));
        if(editor is not null)await editor.Flush();
        return await Store.LoadNote(id)??throw new InvalidDataException("Note not found.");
    }
    internal async Task DeleteLibraryNote(string id)
    {
        if(!Guid.TryParse(id,out _))throw new InvalidDataException("Invalid note.");
        var editor=Windows.FirstOrDefault(w=>w.ContainsNote(id));
        if(editor is not null){await editor.DeleteNoteTab(id);return;}
        await Store.Delete(id);
        await Changed(false);
    }
    internal async Task QuitIfNoOpenWindows()
    {
        // A hidden tray host/cached note is not an open app window. Minimized
        // windows remain IsVisible and must keep the app alive.
        if(Testing&&!ExerciseLifecycle&&!ExitProbe)return;
        if(quitting||Windows.Any(window=>window.IsVisible))return;
        // A New note accepted before the close must finish before deciding
        // whether there is anything left. Re-check after every async boundary.
        if(startingWork is {IsCompleted:false})await startingWork;
        if(openingNotes.Count>0)await Task.WhenAll(openingNotes.ToArray());
        await WaitForOpeningModeChange();
        await WaitForNoteFiles();
        await WaitForLibraryOperations();
        if(quitting||Windows.Any(window=>window.IsVisible))return;
        await Quit();
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
        StopFileAutoSave();
        try
        {
            if(startingWork is not null)await startingWork;
            if(openingNotes.Count>0)await Task.WhenAll(openingNotes.ToArray());
            await WaitForOpeningModeChange();
            await WaitForNoteFiles();
            await WaitForLibraryOperations();
            foreach(var window in Windows.Where(window=>window.HasNoteEditor))window.Post(new{@event="prepare-quit"});
            await PrepareQuit();
            DisposeGlobalShortcuts();
            TrayMenu?.Close();TrayMenu=null;
            foreach (var window in Windows.ToArray()) window.ClosePermanently();
            DisposeWebViewInputProtection();
            if(!Testing||!ExerciseLifecycle)System.Windows.Application.Current.Shutdown();
        }
        catch { quitting = false;foreach(var window in Windows.Where(window=>window.HasNoteEditor))window.Post(new{@event="resume-editing"});throw; }
    }
}
