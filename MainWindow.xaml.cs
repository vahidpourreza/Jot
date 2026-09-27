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
    private TaskCompletionSource? flushCompletion;
    private string? flushId;
    private readonly TaskCompletionSource<bool> editorReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    internal string? NoteId { get; }
    internal string? ImageSource { get; set; }
    internal bool IsSettingsView { get; private set; }
    private bool startupWarning;
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
        NotifyInputLanguage(); Post(new { @event = "active-window", active = true });
    }
    private void OnWindowDeactivated(object? sender, EventArgs e) => Post(new { @event = "active-window", active = false });

    internal MainWindow(JotSession session, string mode = "home", string? noteId = null, bool runTests = false)
    {
        this.session = session; Mode = mode; NoteId = noteId; this.runTests = runTests;
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
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnSystemPreferenceChanged;
    }

    internal void Reveal()
    {
        if (closingPermanently) return;
        HideRequested=false;
        Show();
        if (!testing) { WindowState = WindowState.Normal; ApplyPendingNoteLayout(); if (ContentReady) Activate(); }
        Post(new { @event = "focus" });
    }
    private void OnWindowVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(Mode=="home")Post(new{@event="window-visibility",visible=IsVisible});
    }
    internal void StartInTray()
    {
        try { PrepareNativeHost(); }
        catch(Exception error){LogFailure(error);}
    }
    private void RevealReadyContent()
    {
        if (closingPermanently || ContentReady) return;
        ContentReady = true; ContentReadyMs = startup.ElapsedMilliseconds;
        if (!testing) { Opacity = 1; ShowActivated = true; if (IsVisible) Activate(); }
    }
    private void PrepareNativeHost()
    {
        if(nativeReady||closingPermanently)return;
        var handle=new WindowInteropHelper(this).EnsureHandle();
        source=HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);nativeReady=true;
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
        });
    }
    internal void ShowHomeView(bool settings)
    {
        if (closingPermanently || Mode != "home" || IsSettingsView == settings) return;
        IsSettingsView = settings;
        if (webView is not null)
            _ = Dispatcher.BeginInvoke(() => {
                if (closingPermanently || webView is null) return;
                var next = new Uri(Path.Combine(AppContext.BaseDirectory, IsSettingsView ? "settings.html" : "home.html")).AbsoluteUri;
                if (next == pageUri) return;
                pageUri = next;
                webView.Navigate(pageUri);
            });
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
            var environment = await session.EnvironmentAsync().WaitAsync(windowLifetime.Token);
            if (closingPermanently) return;
            await Browser.EnsureCoreWebView2Async(environment).WaitAsync(windowLifetime.Token);
            if (closingPermanently) return;
            var core = Browser.CoreWebView2;
            webView = core;
            core.Settings.AreDevToolsEnabled = testing;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += (_, args) => { if (args.Uri != pageUri) args.Cancel = true; };
            core.ProcessFailed += (_, args) => session.Log.Event("webview-process-failed", args.ProcessFailedKind.ToString());
            if (testing)
            {
                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}").WaitAsync(windowLifetime.Token);
                if (closingPermanently) return;
                core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, args) => RuntimeErrors.Add(args.ParameterObjectAsJson);
            }
            core.NavigationCompleted += async (_, args) =>
            {
                if (closingPermanently) return;
                if (!args.IsSuccess) { session.Log.Event("navigation-failed", args.WebErrorStatus.ToString()); RevealReadyContent(); return; }
                if (runTests) await RunSelfTests();
                else if(testing&&session.PackageSmoke&&Mode=="home")await RunPackageSmoke();
            };
            var page = Mode == "home" ? (IsSettingsView ? "settings.html" : "home.html") : Mode == "image" ? "image.html" : "index.html";
            pageUri = new Uri(Path.Combine(AppContext.BaseDirectory, page)).AbsoluteUri;
            core.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            if (closingPermanently) return;
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
        if (closingPermanently || Mode != "note") return;
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
        HideRequested=true;
        forceNativeHide|=forceHide;
        return hideTask is {IsCompleted:false}?hideTask:hideTask=HideAfterSavingCore();
    }
    private async Task HideAfterSavingCore()
    {
        try
        {
            await Flush(true);
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
    private async void CloseDeletedNote()
    {
        // Deleted editors must be disposed before Quit flushes other notes.
        ClosePermanently();
        try{await session.QuitIfNoOpenWindows();}
        catch(Exception error){session.Log.Error("close-deleted-note",error);session.Home().Post(new{@event="warning",message="The note was deleted, but Jot could not finish saving its session. Please try Quit again."});}
    }

    private async void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closingPermanently) return;
        int id = 0;
        string action = "parse-message";
        string messageSource = "";
        try
        {
            messageSource = e.Source;
            if (messageSource != pageUri) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var message = doc.RootElement;
            if (message.TryGetProperty("id", out var ident)) id = ident.GetInt32();
            action = message.GetProperty("action").GetString() ?? "";
            var payload = message.TryGetProperty("payload", out var value) ? value : default;
            object? result = null;
            switch (action)
            {
                case "context": result = new { mode = Mode, noteId = NoteId, image = ImageSource, appearance = IsSettingsView, inputDirection = InputDirection(), active = IsActive, visible = IsVisible, startupWarning }; break;
                case "input-language": result = InputDirection(); break;
                case "log-error": session.Log.Renderer(payload); result = true; break;
                case "load": result = await store.Load(); break;
                case "index-load": result = await store.LoadIndex(); break;
                case "note-load":
                    if (Mode != "note") throw new InvalidOperationException("Only a note window can load its editor.");
                    var ownNote = await store.LoadNote(NoteId!) ?? throw new InvalidDataException("Note not found.");
                    result = new { context = new { noteId = NoteId, inputDirection = InputDirection(), active = !testing, fullscreen = IsNoteFullscreen },
                        model = new { version = 2, activeId = NoteId, notes = new[] { ownNote }, prefs = await store.LoadPreferences() } };
                    break;
                case "editor-ready": editorReadyCompletion.TrySetResult(true);RevealReadyContent(); break;
                case "editor-failed": editorReadyCompletion.TrySetResult(false);RevealReadyContent(); break;
                case "preferences-load": result = await store.LoadPreferences(); break;
                case "import": await store.Import(payload); break;
                case "new-note": result = (await session.NewNote()).NoteId; break;
                case "open-note":
                    var noteId = payload.GetString()!;
                    if (!Guid.TryParse(noteId, out _)) throw new InvalidDataException("یادداشت معتبر نیست.");
                    if (!await store.Contains(noteId))
                        throw new InvalidDataException("یادداشت پیدا نشد.");
                    session.OpenNote(noteId); break;
                case "home": session.Home(payload.ValueKind == JsonValueKind.True); break;
                case "settings": session.Settings(); break;
                case "note-metadata": await store.SaveMetadata(payload); await session.Changed(false, payload.GetProperty("id").GetString()); result = true; break;
                case "note-delete":
                    if (Mode != "note" || payload.GetString() != NoteId) throw new InvalidDataException("Delete a note from its own window.");
                    await Flush();
                    await store.Delete(NoteId!);
                    // Once committed, a notification failure must not masquerade as a failed deletion.
                    try { await session.Changed(preferences: false); }
                    catch (Exception ex) { session.Log.Error("delete-notification", ex, Mode); }
                    result = true;
                    // Reply before disposal, then check whether the final app window closed.
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, CloseDeletedNote);
                    break;
                case "save":
                    if (Mode != "note" || payload.GetProperty("id").GetString() != NoteId) throw new InvalidDataException("این پنجره فقط یادداشت خودش را ذخیره می‌کند.");
                    if(testing&&TestSaveDelayMs>0)await Task.Delay(TestSaveDelayMs);
                    await store.SaveNote(payload); await session.Changed(preferences: false); result = true; break;
                case "preferences":
                    if(Mode!="home")throw new InvalidOperationException("Change app defaults from Settings, not a note window.");
                    result = (await store.SavePreferences(payload)).GetProperty("prefs"); await session.Changed(); break;
                case "app-theme":
                    if(Mode!="note")throw new InvalidOperationException("Use Settings to change the app theme.");
                    result=(await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme=payload.GetString()}))).GetProperty("prefs");
                    await session.Changed();break;
                case "note-preferences":
                    if(Mode!="note"||payload.GetProperty("id").GetString()!=NoteId)throw new InvalidDataException("Change a note's settings from its own window.");
                    result=await store.SaveNotePreferences(NoteId!,payload.GetProperty("settings"));break;
                case "theme":
                    bool light = payload.GetProperty("mode").GetString() == "light";
                    if(Mode=="home")session.UpdateTrayTheme(light?"light":"dark");
                    Surface.Background = new SolidColorBrush(light ? Colors.White : Color.FromRgb(23,23,23));
                    Browser.DefaultBackgroundColor = light ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(23,23,23);
                    UpdateNativeIcon();
                    break;
                case "pin":
                    if (!testing) Topmost = payload.GetBoolean();
                    result = payload.GetBoolean(); break;
                case "drag":
                    await DragHeader(payload);
                    break;
                case "image-fullscreen": result=await SetImageFullscreen(payload.GetBoolean());break;
                case "note-fullscreen":
                    result=await SetNoteFullscreen(payload.ValueKind==JsonValueKind.Object?payload.GetProperty("enabled").GetBoolean():payload.GetBoolean());break;
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
                    if(Mode!="note")throw new InvalidOperationException("Paste is available only in a note editor.");
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
            if (id > 0 && !closingPermanently && messageSource == pageUri) Post(new { id, ok = true, value = result });
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
                Post(new { id, ok = false, error = message, operation = action, code = busy ? "clipboard-busy" : "operation-failed", logged = true });
        }
    }

    internal static string ExportDocument(string fragment) => "<!doctype html><html><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\"><title>Jot</title><style>body{max-width:760px;margin:40px auto;padding:20px;font:16px/1.9 system-ui}img{max-width:100%}p,li,blockquote,td,th{unicode-bidi:isolate}pre{white-space:pre-wrap}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #aaa}</style><body>" + fragment + "</body></html>";

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
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
        windowLifetime.Cancel();
        session.Windows.Remove(this);
        Activated -= OnWindowActivated; Deactivated -= OnWindowDeactivated;
        IsVisibleChanged -= OnWindowVisibilityChanged;
        Loaded -= OnLoaded; SizeChanged -= OnSizeChanged;
        if (source is not null) source.RemoveHook(WndProc);
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnSystemPreferenceChanged;
        if (webView is not null) webView.WebMessageReceived -= OnWebMessage;
        webView = null;
        editorReadyCompletion.TrySetResult(false);
        // Release the composition controller while its native parent still exists.
        if (!browserDisposed) { Browser.Dispose(); browserDisposed = true; }
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
