using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Jot;

public partial class MainWindow : Window
{
    internal readonly JotSession session;
    private readonly bool testing;
    private readonly string testOutput;
    private readonly NoteStore store;
    private readonly bool runTests;
    private readonly Stopwatch startup = Stopwatch.StartNew();
    private HwndSource? source;
    private System.Windows.Forms.NotifyIcon? tray;
    private bool initialized, allowClose;
    private bool closingPermanently, browserDisposed, windowClosed;
    private CoreWebView2? webView;
    private readonly CancellationTokenSource windowLifetime = new();
    internal bool InitializationFinished { get; private set; }
    internal bool ContentReady { get; private set; }
    internal long ContentReadyMs { get; private set; }
    private string pageUri = "";
    private ulong activeNavigationId;
    private TaskCompletionSource? flushCompletion;
    private string? flushId;
    private TaskCompletionSource<bool> editorReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? hideTask;
    private bool forceNativeHide;
    internal bool HideRequested { get; private set; }
    internal int TestSaveDelayMs;
    private System.Drawing.Icon? themedIcon;
    private bool nativeReady;
    internal string NativeIconKey { get; private set; } = "";
    internal readonly List<string> TestHostActions = [];
    internal readonly List<string> RuntimeErrors = [];
    internal ClipboardContent TestClipboardContent=new("","",null);
    internal bool TestClipboardWriteAccepted=true;
    internal int TestClipboardDelayMs;
    internal System.Windows.DataObject? TestClipboardData;
    internal string Mode { get; }
    internal string? NoteId { get; private set; }
    internal string? ImageSource { get; set; }
    internal bool IsSettingsView { get; private set; }
    private bool startupWarning;
    private string? startupMessage;
    internal void ShowWarning(string message){startupMessage=message;Post(new{@event="warning",message});}
    internal void ShowStartupWarning()
    {
        startupWarning=true;
        Post(new{@event="warning",message="Could not restore your note windows. Your notes are unchanged; open them from Home."});
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hWnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    internal static string DirectionForLanguage(int languageId)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(languageId).TextInfo.IsRightToLeft ? "rtl" : "ltr"; }
        catch (System.Globalization.CultureNotFoundException) { return "ltr"; }
    }
    private string InputDirection() => DirectionForLanguage((int)((long)GetKeyboardLayout(0) & 0xffff));
    private void NotifyInputLanguage() => Post(new { @event = "input-language", direction = InputDirection() });
    private void OnInputLanguageChanged(object sender, System.Windows.Input.InputLanguageEventArgs e) => NotifyInputLanguage();
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (closingPermanently) return;
        session.RememberNoteWindow(this);
        NotifyInputLanguage(); Post(new { @event = "active-window", active = true });
    }
    private void OnWindowDeactivated(object? sender, EventArgs e) => Post(new { @event = "active-window", active = false });

    internal MainWindow(JotSession session, string mode = "home", string? noteId = null, bool runTests = false)
    {
        this.session = session; Mode = mode; NoteId = noteId; this.runTests = runTests;
        if(mode=="note"&&noteId is not null)NoteTabIds.Add(noteId);
        testing = session.Testing; testOutput = session.Output; store = session.Store;
        InitializeComponent();
        ContentReady = mode != "note";
        if (!ContentReady) { Opacity = 0; ShowActivated = false; }
        ShowInTaskbar=!testing;
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(23,23,23);
        UpdateNativeIcon();
        if (mode == "home") { Width = 520; Height = 540; MinWidth = 360; MinHeight = 280; }
        if (mode == "image") { Width = 920; Height = 680; MinWidth = 360; MinHeight = 280; }
        if (testing) { ShowActivated = false; ShowInTaskbar = false; Left = -32000; Top = -32000; Opacity = 0; }
        else
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width - 24); Height = Math.Min(Height, area.Height - 24);
            int offset = session.Windows.Count(window => window.Mode == "note") % 6 * 24;
            Left = Math.Max(area.Left + 12, area.Right - Width - 24 - offset);
            Top = Math.Max(area.Top + 12, area.Bottom - Height - 24 - offset);
        }
        session.Windows.Add(this);
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        IsVisibleChanged += OnWindowVisibilityChanged;
        StateChanged += OnBrowserHostStateChanged;
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnSystemPreferenceChanged;
    }

    internal void Reveal()
    {
        if (closingPermanently) return;
        HideRequested=false;
        Show();
        if (!testing) { RestoreVisibleWindowState(); ApplyPendingNoteLayout(); if (ContentReady) Activate(); }
        Post(new { @event = "focus" });
    }
    private void OnWindowVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        UpdateBrowserHostVisibility();
        if(Mode=="home")Post(new{@event="window-visibility",visible=IsVisible});
    }
    internal void StartInTray()
    {
        try { PrepareNativeHost(); }
        catch(Exception error){LogFailure(error);}
    }
    private void RevealReadyContent()
    {
        if (closingPermanently || ContentReady || TransferPending) return;
        ContentReady = true; ContentReadyMs = startup.ElapsedMilliseconds;
        if (!testing) { Opacity = 1; ShowActivated = true; if (IsVisible) Activate(); }
    }
    private void PrepareNativeHost()
    {
        if(nativeReady||closingPermanently)return;
        var handle=new WindowInteropHelper(this).EnsureHandle();
        source=HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);nativeReady=true;
        InitializeWindowPresentation();
        if(!testing&&Mode=="home")
        {
            CreateTray();
        }
    }
    private void OnSystemPreferenceChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args)
    {
        if(closingPermanently||Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.BeginInvoke(()=> {
            foreach(var window in session.Windows.ToArray())window.UpdateNativeIcon();
            (System.Windows.Application.Current as App)?.RefreshTaskbarMenu();
        });
    }
    internal void ShowHomeView(bool settings)
    {
        if(closingPermanently||Mode!="home")return;
        // A cold Settings launch must supply its destination to the first
        // renderer context instead of briefly presenting Home first.
        if(!initialized){IsSettingsView=settings;if(settings)SettingsTabOpen=true;}
        _=ShowHomeViewSafely(settings);
    }
    private async Task ShowHomeViewSafely(bool settings)
    {
        try{await SwitchHomeView(settings);}
        catch(Exception error)
        {
            session.Log.Error("home-navigation",error,Mode);
            ShowWarning("Could not save this note before changing pages. Your draft is still open; please try again.");
        }
    }
    internal bool Post(object message)
    {
        // CoreWebView2's getter itself throws after Dispose. Never query a dead control.
        if (closingPermanently || browserDisposed || webView is null) return false;
        try { webView.PostWebMessageAsJson(JsonSerializer.Serialize(message)); return true; }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
        {
            session.Log.Error("webview-post", ex, Mode);
            return false;
        }
    }
    private void RoundWindow()
    {
        if (closingPermanently || Browser is null || ActualWidth < 20 || ActualHeight < 20) return;
        if(IsWindowFullscreen||IsFullscreenTransitioning){Browser.Clip=null;return;}
        Browser.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth-18), Math.Max(0, ActualHeight-18)), 5, 5);
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => RoundWindow();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized || closingPermanently) return;
        initialized = true;
        try
        {
            PrepareNativeHost();RoundWindow();
            ApplyWorkspacePin(await store.LoadPreferences());
            var environment = await session.EnvironmentAsync().WaitAsync(windowLifetime.Token);
            if (closingPermanently) return;
            await Browser.EnsureCoreWebView2Async(environment).WaitAsync(windowLifetime.Token);
            if (closingPermanently) return;
            var core = Browser.CoreWebView2;
            session.ProtectWebViewInput(core);
            UpdateBrowserHostVisibility();
            webView = core;
            core.Settings.AreDevToolsEnabled = testing;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += (_, args) => {
                if (args.Uri != pageUri) { args.Cancel = true; return; }
                BeginWorkspaceNavigation();
                activeNavigationId=args.NavigationId;
            };
            core.ProcessFailed += (_, args) => { FailWorkspace("The Home workspace stopped responding.");session.Log.Event("webview-process-failed", args.ProcessFailedKind.ToString()); };
            if (testing)
            {
                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}").WaitAsync(windowLifetime.Token);
                if (closingPermanently) return;
                core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, args) => RuntimeErrors.Add(args.ParameterObjectAsJson);
            }
            core.NavigationCompleted += async (_, args) =>
            {
                if (closingPermanently || args.NavigationId!=activeNavigationId) return;
                if (!args.IsSuccess) { FailWorkspace("The Home workspace could not open.");session.Log.Event("navigation-failed", args.WebErrorStatus.ToString()); editorReadyCompletion.TrySetResult(false);RevealReadyContent(); return; }
                if (runTests) await RunSelfTests();
                else if(testing&&session.PackageSmoke&&Mode=="home")await RunPackageSmoke();
            };
            var page = Mode == "home" ? "workspace.html" : Mode == "image" ? "image.html" : "index.html";
            pageUri = new Uri(Path.Combine(AppContext.BaseDirectory, page)).AbsoluteUri;
            core.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            if (closingPermanently) return;
            FailWorkspace("The Home workspace could not open.");
            editorReadyCompletion.TrySetResult(false);
            LogFailure(ex);
            RevealReadyContent();
            if (testing) System.Windows.Application.Current.Shutdown(1);
            else Title = "Jot — initialization failed; see logs";
        }
        finally { InitializationFinished = true; }
    }

    private void CreateTray()
    {
        tray = new System.Windows.Forms.NotifyIcon { Text = "Jot", Visible = true, Icon = themedIcon };
        tray.MouseClick += async (_, args) => {
            try
            {
                if(args.Button==System.Windows.Forms.MouseButtons.Left){session.TrayMenu?.Dismiss();session.Home();}
                else if(args.Button==System.Windows.Forms.MouseButtons.Right)
                {
                    var anchor=System.Windows.Forms.Cursor.Position;
                    await session.ShowTrayMenu(anchor,TrayIconBounds.Resolve(tray!,anchor));
                }
            }
            catch(Exception error){session.Log.Error("tray-open",error);Post(new{@event="warning",message="Could not open Jot. Please try again."});}
        };
    }
    internal async Task Flush(bool holdEditing=false)
    {
        if(!tabOperation.IsCompleted)try{await tabOperation;}catch{ /* The tab operation already reports its own failure. */ }
        await FlushEditor(holdEditing);
    }
    private async Task FlushEditor(bool holdEditing=false)
    {
        if (closingPermanently || !HasNoteEditor) return;
        if(Mode=="home")await WaitForWorkspace();
        if(!await editorReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(16)))return;
        if(holdEditing)Post(new{@event="prepare-quit"});
        if (flushCompletion is not null) { await flushCompletion.Task.WaitAsync(TimeSpan.FromSeconds(16)); return; }
        flushCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);flushId=Guid.NewGuid().ToString("N");
        try
        {
            if (!Post(new { @event = "flush", intent=flushId,holdEditing })) throw new IOException("The editor is unavailable. Your note window has been kept open.");
            await flushCompletion.Task.WaitAsync(TimeSpan.FromSeconds(16));
        }
        finally { flushCompletion = null;flushId=null; }
    }
    internal Task HideAfterSaving(bool forceHide=false)
    {
        if(session.UpdatingLibrary)return HideAfterLibraryUpdate(forceHide);
        if(session.ChangingOpeningMode)return HideAfterModeChange(forceHide);
        HideRequested=true;
        forceNativeHide|=forceHide;
        return hideTask is {IsCompleted:false}?hideTask:hideTask=HideAfterSavingCore();
    }
    private async Task HideAfterModeChange(bool forceHide)
    {await session.WaitForOpeningModeChange();if(!closingPermanently)await HideAfterSaving(forceHide);}
    private async Task HideAfterLibraryUpdate(bool forceHide)
    {await session.WaitForLibraryOperations();if(!closingPermanently)await HideAfterSaving(forceHide);}
    private async Task HideAfterSavingCore()
    {
        try
        {
            await Flush(true);
            await session.SaveAutoFilesFor(Mode=="home"?NoteTabIds:NoteId is {} id?new[]{id}:Array.Empty<string>());
            if(Mode=="home"&&HideRequested)await session.SaveClosingWorkspace(this);
            if(!closingPermanently&&HideRequested&&(forceNativeHide||!testing||session.ExerciseLifecycle||session.ExitProbe))
            {
                Hide();
                await session.QuitIfNoOpenWindows();
                if(Mode=="image"&&!closingPermanently)ClosePermanently();
            }
            if(testing&&!session.ExerciseLifecycle&&!session.ExitProbe&&!forceNativeHide){HideRequested=false;Post(new{@event="resume-editing"});}
        }
        catch{HideRequested=false;if(!closingPermanently&&!IsVisible)Reveal();Post(new{@event="resume-editing"});throw;}
        finally{forceNativeHide=false;}
    }
    internal void ClosePermanently() { if (closingPermanently) return; allowClose = true; Close(); }
    private async void CloseImageFromUi()
    {
        try{await HideAfterSaving(true);}
        catch(Exception error){session.Log.Error("close-image",error);Post(new{@event="warning",message="Could not finish saving. Jot is still open; try closing again."});}
    }

    private async void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closingPermanently) return;
        int id = 0;
        string action = "parse-message";
        string messageSource = "";
        string? documentId=null;
        try
        {
            messageSource = e.Source;
            if (messageSource != pageUri) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var message = doc.RootElement;
            if(message.TryGetProperty("documentId",out var document)&&document.ValueKind==JsonValueKind.String)documentId=document.GetString();
            if (message.TryGetProperty("id", out var ident)) id = ident.GetInt32();
            action = message.GetProperty("action").GetString() ?? "";
            var payload = message.TryGetProperty("payload", out var value) ? value : default;
            object? result = null;
            switch (action)
            {
                case "context": result = new { mode = Mode, view=WorkspaceView,noteId = NoteId, activeId=ActiveWorkspaceTab,settingsOpen=SettingsTabOpen,canReopenTab=CanReopenClosedTab, workspace=Mode=="home",tabbed=Tabbed,tabs=await TabHeaders(),image = ImageSource, appearance = IsSettingsView, inputDirection = InputDirection(), active = IsActive, visible = IsVisible, fullscreen=IsWindowFullscreen, startupWarning,warning=startupMessage };startupMessage=null;break;
                case "workspace-layout":
                    RequireHome();
                    workspaceDocumentId=documentId;
                    result=new{home=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"home.html")),editor=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"index.html")),settings=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"settings.html"))};
                    break;
                case "input-language": result = InputDirection(); break;
                case "log-error": session.Log.Renderer(payload); result = true; break;
                case "load": result = await store.Load(); break;
                case "index-load": result = await store.LoadIndex(); break;
                case "open-note-file": result=await ChooseNoteFile();break;
                case "save-note-file": result=await SaveNoteFile(payload.GetProperty("id").GetString()!,payload.TryGetProperty("saveAs",out var fileSaveAs)&&fileSaveAs.GetBoolean());break;
                case "note-file-state":
                    var fileState=await store.LoadFileState(payload.GetString()!);result=fileState;
                    if(fileState is {} caption&&caption.GetProperty("id").GetString()==NoteId)SetNoteFileCaption(caption);break;
                case "folder-create":
                    RequireHome();await store.CreateFolder(payload.GetString()!);await session.Changed(false);result=true;break;
                case "folder-rename":
                    RequireHome();await store.RenameFolder(payload.GetProperty("name").GetString()!,payload.GetProperty("replacement").GetString()!);await session.Changed(false);result=true;break;
                case "folder-remove":
                    RequireHome();await store.RemoveFolder(payload.GetString()!);await session.Changed(false);result=true;break;
                case "library-copy": result=await CopyLibraryNote(payload.GetString()!);break;
                case "trash-count": RequireHome();result=await store.CountTrash();break;
                case "trash-load": RequireHome();result=await store.LoadTrash();break;
                case "trash-update": result=await UpdateTrash(payload);break;
                case "library-export": await ExportLibraryNote(payload.GetString()!);result=true;break;
                case "library-delete": RequireHome();await session.DeleteLibraryNote(payload.GetString()!);result=true;break;
                case "library-pin":
                    RequireHome();result=await session.UpdateLibraryNotes([payload.GetProperty("id").GetString()!],"pin",pinned:payload.GetProperty("pinned").GetBoolean());break;
                case "library-bulk":
                    RequireHome();result=await session.UpdateLibraryNotes(payload.GetProperty("ids").EnumerateArray().Select(value=>value.GetString()!),payload.GetProperty("action").GetString()!,
                        payload.TryGetProperty("pinned",out var libraryPinned)?libraryPinned.GetBoolean():null,payload.TryGetProperty("folder",out var libraryFolder)?libraryFolder.GetString():null);break;
                case "window-fullscreen": RequireHome();result=await SetWindowFullscreen(payload.GetBoolean());break;
                case "note-load":
                    if (!HasNoteEditor) throw new InvalidOperationException("Only an active note can load its editor.");
                    var ownNote = await store.LoadNote(NoteId!) ?? throw new InvalidDataException("Note not found.");
                    result = new { context = new { noteId = NoteId,activeId=NoteId,workspace=Mode=="home",view=WorkspaceView,inputDirection = InputDirection(), active = !testing, fullscreen = IsWindowFullscreen, tabbed=Tabbed, tabs=await TabHeaders() },
                        model = new { version = 2, activeId = NoteId, notes = new[] { ownNote }, prefs = await store.LoadPreferences() } };
                    break;
                case "editor-ready": editorReadyCompletion.TrySetResult(true);RevealReadyContent(); break;
                case "editor-failed": editorReadyCompletion.TrySetResult(false);RevealReadyContent(); break;
                case "workspace-ready": WorkspaceReady(documentId); break;
                case "workspace-view-ready": if(IsWorkspaceDocument(documentId))WorkspaceViewReady(payload); break;
                case "workspace-view-failed":
                    if(IsWorkspaceDocument(documentId)&&payload.ValueKind==JsonValueKind.Object&&payload.TryGetProperty("intent",out var failedViewIntent)&&failedViewIntent.GetString()==workspaceViewIntent)
                        workspaceViewReady?.TrySetException(new IOException("The requested workspace view could not open."));
                    break;
                case "workspace-failed": if(IsWorkspaceDocument(documentId)){FailWorkspace("The Home workspace could not open.");RevealReadyContent();}break;
                case "preferences-load": result = await store.LoadPreferences(); break;
                case "shortcuts-status": result=session.ShortcutStatus;break;
                case "shortcuts-config": result=ShortcutBindings.Config(await store.LoadPreferences());break;
                case "shortcuts-layout": result=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"shortcuts.html"));break;
                case "shortcuts": await session.OpenShortcuts();result=true;break;
                case "app-shortcut": await HandleAppShortcut(payload.GetString()!);result=true;break;
                case "workspace-pin": result=await SetWorkspacePinned(payload.GetBoolean());break;
                case "import": await store.Import(payload); break;
                case "new-note": result = (await session.NewNote(payload.ValueKind==JsonValueKind.Object&&payload.TryGetProperty("folder",out var newFolder)?newFolder.GetString():null)).NoteId; break;
                case "open-note-tab": RequireHome();result=(await session.OpenAsTab(payload.GetString()!)).NoteId;break;
                case "new-tab": RequireHome();result=(await session.NewTab(this,payload.ValueKind==JsonValueKind.Object&&payload.TryGetProperty("folder",out var tabFolder)?tabFolder.GetString():null)).NoteId;break;
                case "tab-switch":
                    await session.WaitForLibraryOperations();
                    await session.WaitForOpeningModeChange();
                    var requestedTab=payload.GetString()!;
                    if(requestedTab!="home"&&!(requestedTab=="settings"&&SettingsTabOpen)&&!NoteTabIds.Contains(requestedTab))throw new InvalidOperationException("This tab is no longer open.");
                    await SwitchNoteTab(requestedTab);result=true;break;
                case "tab-close": await session.WaitForLibraryOperations();await session.WaitForOpeningModeChange();await CloseNoteTab(payload.GetString()!);result=true;break;
                case "tab-reopen": RequireHome();await session.ReopenClosedTab(this);result=true;break;
                case "tab-pin": RequireHome();await SetTabPinned(payload.GetProperty("id").GetString()!,payload.GetProperty("pinned").GetBoolean());result=true;break;
                case "tab-reorder": RequireHome();await ReorderNoteTabs(payload.GetProperty("ids").EnumerateArray().Select(value=>value.GetString()!).ToArray());result=true;break;
                case "tab-ready": break; // Accepted for standalone editor compatibility; workspace-view-ready owns tab readiness.
                case "open-note":
                    var noteId = payload.GetString()!;
                    if (!Guid.TryParse(noteId, out _)) throw new InvalidDataException("یادداشت معتبر نیست.");
                    if (!await store.Contains(noteId))
                        throw new InvalidDataException("یادداشت پیدا نشد.");
                    await session.OpenNoteWindow(noteId); break;
                case "open-note-default": RequireHome();result=(await session.OpenNoteDefault(payload.GetString()!)).NoteId;break;
                case "home": session.Home(payload.ValueKind == JsonValueKind.True); break;
                case "settings": session.Settings(); break;
                case "note-metadata": await store.SaveMetadata(payload); await session.Changed(false, payload.GetProperty("id").GetString());await session.FileNoteChanged(payload.GetProperty("id").GetString()!); result = true; break;
                case "note-delete":
                    if (!HasNoteEditor || payload.GetString() != NoteId) throw new InvalidDataException("Delete a note from its own editor.");
                    await DeleteNoteTab(NoteId!);result=true;
                    break;
                case "save":
                    if (!HasNoteEditor || payload.GetProperty("id").GetString() != NoteId) throw new InvalidDataException("This window can save only its active note.");
                    if(testing&&TestSaveDelayMs>0)await Task.Delay(TestSaveDelayMs);
                    await store.SaveNote(payload);await session.NoteContentChanged(NoteId!);result=true;break;
                case "preferences":
                    if(Mode!="home")throw new InvalidOperationException("Change app defaults from Settings, not a note window.");
                    result = await session.ApplyPreferences(payload); break;
                case "app-theme":
                    if(!HasNoteEditor)throw new InvalidOperationException("Use Settings to change the app theme.");
                    result=(await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme=payload.GetString()}))).GetProperty("prefs");
                    await session.Changed();break;
                case "note-preferences":
                    if(!HasNoteEditor||payload.GetProperty("id").GetString()!=NoteId)throw new InvalidDataException("Change a note's settings from its own editor.");
                    result=await store.SaveNotePreferences(NoteId!,payload.GetProperty("settings"));await session.FileNoteChanged(NoteId!);break;
                case "theme":
                    bool light = payload.GetProperty("mode").GetString() == "light";
                    if(Mode=="home")session.UpdateTrayTheme(light?"light":"dark");
                    Surface.Background = new SolidColorBrush(light ? Colors.White : Color.FromRgb(23,23,23));
                    Browser.DefaultBackgroundColor = light ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(23,23,23);
                    UpdateNativeIcon();
                    break;
                case "pin":
                    if(Mode!="note"){result=false;break;}
                    if (!testing) Topmost = payload.GetBoolean();
                    result = payload.GetBoolean(); break;
                case "drag":
                    await DragHeader(payload);
                    break;
                case "window-toggle-maximize":
                    if(Mode is not ("home" or "note"))throw new InvalidOperationException("Only notes and Home can be maximized.");
                    result=await SetWindowFullscreen(!IsWindowFullscreen);break;
                case "image-fullscreen": result=await SetImageFullscreen(payload.GetBoolean());break;
                case "note-fullscreen":
                    if(!HasNoteEditor)throw new InvalidOperationException("Only an active note can use note fullscreen.");
                    result=await SetWindowFullscreen(payload.ValueKind==JsonValueKind.Object?payload.GetProperty("enabled").GetBoolean():payload.GetBoolean());break;
                case "hide":
                    if (Mode == "image") _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, CloseImageFromUi);
                    else await HideAfterSaving();
                    break;
                case "minimize":
                    if(!testing)WindowState=WindowState.Minimized;
                    result=true;break;
                case "quit":
                    if (Mode == "note") throw new InvalidOperationException("Quit Jot from Home, Settings, or the tray.");
                    if (testing&&!session.ExerciseLifecycle&&!session.ExitProbe) await session.FlushNotes();
                    else _ = Dispatcher.InvokeAsync(async () => { try { await session.Quit(); } catch (Exception ex) { session.Log.Error("quit",ex,Mode);Post(new { @event = "quit-failed", message = ex.Message }); } });
                    break;
                case "flush-complete": if(payload.ValueKind==JsonValueKind.String&&payload.GetString()==flushId)flushCompletion?.TrySetResult(); break;
                case "flush-failed": if(payload.ValueKind==JsonValueKind.String&&payload.GetString()==flushId)flushCompletion?.TrySetException(new IOException("Could not save a note. Jot is still open.")); break;
                case "view-image":
                    var src = payload.GetString() ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(src, @"^data:image/(png|jpeg|webp|gif);base64,") || src.Length > 12*1024*1024)
                        throw new InvalidDataException("تصویر معتبر نیست.");
                    session.Image(src); break;
                case "clipboard-write":
                    var clipboard = RichClipboard.Create(payload);
                    if(testing){TestClipboardData=clipboard;if(TestClipboardDelayMs>0)await Task.Delay(TestClipboardDelayMs);}
                    result = testing?TestClipboardWriteAccepted:await RichClipboard.WriteAsync(clipboard, session.Log);break;
                case "clipboard-read":
                    if(!HasNoteEditor)throw new InvalidOperationException("Paste is available only in a note editor.");
                    result=testing?TestClipboardContent:await ClipboardReader.ReadAsync(session.Log);break;
                case "clipboard-read-text": result=testing?TestClipboardContent.text:await ClipboardReader.ReadTextAsync(session.Log);break;
                case "clipboard-text":
                    if (!testing)
                    {
                        var plain = new System.Windows.DataObject();
                        plain.SetData(System.Windows.DataFormats.UnicodeText, payload.GetString() ?? "");
                        await RichClipboard.WriteAsync(plain, session.Log);
                    }
                    result = true; break;
                case "import-image": result = await RichClipboard.ImportImage(payload.GetString()!); break;
                case "export":
                    if (!testing)
                    {
                        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "HTML note (*.html)|*.html", FileName = "Jot-note.html" };
                        if (dialog.ShowDialog(this) == true)
                        {
                            var html = ExportDocument(payload.GetProperty("html").GetString()!);
                            await File.WriteAllTextAsync(dialog.FileName, html);
                        }
                    }
                    break;
                default: throw new InvalidOperationException("Unsupported action.");
            }
            if (testing) TestHostActions.Add(action);
            if (id > 0 && !closingPermanently && messageSource == pageUri) Post(new { id, documentId,ok = true, value = result });
        }
        catch (Exception ex)
        {
            session.Log.Error(action, ex, Mode);
            var busy = RichClipboard.IsBusy(ex);
            var message = busy ? "کلیپ‌بورد موقتاً مشغول است. دوباره کپی کنید؛ یادداشت شما محفوظ است." : ex.Message;
            if(ex is Microsoft.Data.Sqlite.SqliteException sqlite)message=sqlite.SqliteErrorCode switch{
                5 or 6=>"The notes database is busy. Try saving again; your draft is still in this window.",
                11 or 26=>"Jot could not read its database. Your files were left untouched; restore a verified backup.",
                13=>"There is not enough disk space to save. Your draft is still in this window.",
                _=>"The database operation failed. Your draft is still in this window; see the local error log."
            };
            if (id > 0 && !closingPermanently && messageSource == pageUri)
                Post(new { id, documentId,ok = false, error = message, operation = action, code = busy ? "clipboard-busy" : "operation-failed", logged = true });
        }
    }

    internal static string ExportDocument(string fragment) => "<!doctype html><html><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\"><title>Jot</title><style>body{max-width:760px;margin:40px auto;padding:20px;font:16px/1.9 system-ui}img{max-width:100%}p,li,blockquote,td,th{unicode-bidi:isolate}pre{white-space:pre-wrap}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #aaa}</style><body>" + fragment + "</body></html>";

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if(HandleWindowPresentationMessage(hwnd,msg,wParam,lParam,ref handled))return 0;
        if (closingPermanently) return 0;
        // Shell/taskbar Close (including Close all windows) is an app shutdown,
        // not a sequence of note-header X actions. Intercept before WPF hides any
        // window so Quit can snapshot the original visible-note set exactly once.
        // Header X uses the explicit bridge hide action and never enters here.
        if(!allowClose&&(!testing||session.ExitProbe)&&(msg==0x0010||msg==0x0112&&(wParam.ToInt64()&0xFFF0)==0xF060))
        {
            handled=true;session.RequestSystemQuit();return 0;
        }
        if (msg == 0x0051) _ = Dispatcher.BeginInvoke(NotifyInputLanguage);
        if (msg == 0x0084&&!IsWindowFullscreen&&!IsFullscreenTransitioning)
        {
            int packed = unchecked((int)lParam);
            var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)(packed >> 16)));
            bool left = point.X < 13, right = point.X >= ActualWidth-13, top = point.Y < 13, bottom = point.Y >= ActualHeight-13;
            int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 1;
            if (hit != 1) { handled = true; return hit; }
        }
        return 0;
    }
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closingPermanently) return;
        if (!allowClose && Mode != "image")
        {
            e.Cancel = true;
            try { await HideAfterSaving(true); }
            catch (Exception ex) { session.Log.Error("close-window",ex,Mode);Post(new { @event = "warning", message = ex.Message }); }
            return;
        }
        // Close the message boundary before disposal or any late activation callbacks.
        closingPermanently = true;
        StopWindowPresentation();
        windowLifetime.Cancel();
        workspaceReadyCompletion.TrySetResult(false);
        workspaceViewReady?.TrySetCanceled();
        session.Windows.Remove(this);
        Activated -= OnWindowActivated; Deactivated -= OnWindowDeactivated;
        IsVisibleChanged -= OnWindowVisibilityChanged;
        StateChanged -= OnBrowserHostStateChanged;
        Loaded -= OnLoaded; SizeChanged -= OnSizeChanged;
        if (source is not null) source.RemoveHook(WndProc);
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnSystemPreferenceChanged;
        if (webView is not null) webView.WebMessageReceived -= OnWebMessage;
        webView = null;
        editorReadyCompletion.TrySetResult(false);
        // Release the composition controller while its native parent still exists.
        if (!browserDisposed) { Browser.Dispose(); browserDisposed = true; }
        if(session.Windows.Count==0)session.DisposeWebViewInputProtection();
    }
    protected override void OnClosed(EventArgs e)
    {
        webView = null;
        flushCompletion?.TrySetException(new IOException("The note window closed before saving completed."));
        tray?.Dispose(); tray = null;
        themedIcon?.Dispose(); themedIcon = null;
        ImageSource = null;
        windowClosed = true;
        windowLifetime.Dispose();
        base.OnClosed(e);
    }
    private void UpdateNativeIcon()
    {
        if (closingPermanently) return;
        if (Icon is null)
        {
            using var colorStream=new MemoryStream(AppIcon.LoadColor());
            var decoder=new IconBitmapDecoder(colorStream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            var largest=decoder.Frames.MaxBy(frame=>frame.PixelWidth)!;
            largest.Freeze();Icon=largest;
        }
        bool light=AppIcon.WindowsUsesLightTray();
        var size=System.Windows.Forms.SystemInformation.SmallIconSize;
        var key=(light?"monochrome-dark":"monochrome-light")+"-"+size.Width;
        if (key == NativeIconKey) return;
        var bytes = AppIcon.LoadMonochrome(light);
        using var stream = new MemoryStream(bytes);
        var next = new System.Drawing.Icon(stream,size.Width,size.Height);
        var previous = themedIcon;
        themedIcon = next;
        if (tray is not null) tray.Icon = next;
        previous?.Dispose();
        NativeIconKey = key;
    }
    private void LogFailure(Exception error)
    {
        session.Log.Error("initialization", error, Mode);
        if (testing)
        {
            Directory.CreateDirectory(testOutput);
            File.WriteAllText(Path.Combine(testOutput, "startup-error.txt"), error.ToString());
        }
    }
}
