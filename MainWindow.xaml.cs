using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jot;

public partial class MainWindow : Window
{
    private const int HotkeyId = 4201;
    internal readonly JotSession session;
    private readonly bool testing;
    private readonly string testOutput;
    private readonly NoteStore store;
    private readonly bool runTests;
    private readonly Stopwatch startup = Stopwatch.StartNew();
    private HwndSource? source;
    private System.Windows.Forms.NotifyIcon? tray;
    private bool initialized, allowClose;
    private string pageUri = "";
    private TaskCompletionSource? flushCompletion;
    private System.Drawing.Icon? themedIcon;
    private bool nativeReady;
    private bool hotkeyUnavailable;
    internal string NativeIconKey { get; private set; } = "";
    internal readonly List<string> TestHostActions = [];
    internal readonly List<string> RuntimeErrors = [];
    internal string Mode { get; }
    internal string? NoteId { get; }
    internal string? ImageSource { get; set; }
    internal bool IsSettingsView { get; private set; }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
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

    internal MainWindow(JotSession session, string mode = "home", string? noteId = null, bool runTests = false)
    {
        this.session = session; Mode = mode; NoteId = noteId; this.runTests = runTests;
        testing = session.Testing; testOutput = session.Output; store = session.Store;
        InitializeComponent();
        ShowInTaskbar=!testing;
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(23,23,23);
        UpdateNativeIcon();
        if (mode == "home") { Width = 820; Height = 650; MinWidth = 520; MinHeight = 420; }
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
        Activated += (_, _) => { NotifyInputLanguage(); Post(new { @event = "active-window", active = true }); };
        Deactivated += (_, _) => Post(new { @event = "active-window", active = false });
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnSystemPreferenceChanged;
    }

    internal void Reveal()
    {
        Show();
        if (!testing) { WindowState = WindowState.Normal; Activate(); }
        Post(new { @event = "focus" });
    }
    internal void StartInTray()
    {
        try { PrepareNativeHost(); }
        catch(Exception error){LogFailure(error);}
    }
    private void PrepareNativeHost()
    {
        if(nativeReady)return;
        var handle=new WindowInteropHelper(this).EnsureHandle();
        source=HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);nativeReady=true;
        if(!testing&&Mode=="home")
        {
            CreateTray();
            hotkeyUnavailable=!RegisterHotKey(handle,HotkeyId,0x0002|0x0001|0x4000,0x4A);
            if(hotkeyUnavailable)session.Log.Event("hotkey-registration","unavailable");
        }
    }
    private void OnSystemPreferenceChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args)
    {
        if(Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.BeginInvoke(()=> {
            foreach(var window in session.Windows.ToArray())window.UpdateNativeIcon();
        });
    }
    internal void ShowHomeView(bool settings)
    {
        if (Mode != "home" || IsSettingsView == settings) return;
        IsSettingsView = settings;
        if (Browser.CoreWebView2 is not null)
            _ = Dispatcher.BeginInvoke(() => {
                var next = new Uri(Path.Combine(AppContext.BaseDirectory, IsSettingsView ? "settings.html" : "home.html")).AbsoluteUri;
                if (next == pageUri) return;
                pageUri = next;
                Browser.CoreWebView2.Navigate(pageUri);
            });
    }
    internal void Post(object message)
    {
        if (Browser.CoreWebView2 is not null)
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }
    private void RoundWindow()
    {
        if (Browser is null || ActualWidth < 20 || ActualHeight < 20) return;
        Browser.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth-18), Math.Max(0, ActualHeight-18)), 5, 5);
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => RoundWindow();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            PrepareNativeHost();RoundWindow();
            await Browser.EnsureCoreWebView2Async(await session.EnvironmentAsync());
            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = testing;
            core.Settings.AreDefaultContextMenusEnabled = true;
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
                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, args) => RuntimeErrors.Add(args.ParameterObjectAsJson);
            }
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess) { session.Log.Event("navigation-failed", args.WebErrorStatus.ToString()); return; }
                if (runTests) await RunSelfTests();
                else if (!testing && Mode == "home" && hotkeyUnavailable)
                {
                    await Task.Delay(800);
                    Post(new { @event = "warning", message = "Ctrl+Alt+J is used by another app. Use the tray icon." });
                }
            };
            var page = Mode == "home" ? (IsSettingsView ? "settings.html" : "home.html") : Mode == "image" ? "image.html" : "index.html";
            pageUri = new Uri(Path.Combine(AppContext.BaseDirectory, page)).AbsoluteUri;
            core.Navigate(pageUri);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            if (testing) System.Windows.Application.Current.Shutdown(1);
            else Title = "Jot — initialization failed; see logs";
        }
    }

    private void CreateTray()
    {
        tray = new System.Windows.Forms.NotifyIcon { Text = "Jot · Ctrl+Alt+J", Visible = true, Icon = themedIcon };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("یادداشت‌ها", null, (_, _) => Dispatcher.Invoke(() => session.Home()));
        menu.Items.Add("New note", null, async (_, _) => { try { await session.NewNote(); } catch (Exception ex) { session.Log.Error("tray-new-note",ex);Post(new { @event = "warning", message = ex.Message }); } });
        menu.Items.Add("Quit", null, async (_, _) => { try { await session.Quit(); } catch (Exception ex) { session.Log.Error("tray-quit",ex);Post(new { @event = "warning", message = ex.Message }); } });
        tray.ContextMenuStrip = menu;
        UpdateTrayLanguage();
        tray.MouseClick += (_, args) => { if(args.Button==System.Windows.Forms.MouseButtons.Left)Dispatcher.Invoke(()=>session.Home()); };
    }
    internal async Task Flush()
    {
        if (Mode != "note" || Browser.CoreWebView2 is null) return;
        if (flushCompletion is not null) { await flushCompletion.Task; return; }
        flushCompletion = new TaskCompletionSource();
        Post(new { @event = "flush", intent = "ack" });
        try { await flushCompletion.Task.WaitAsync(TimeSpan.FromSeconds(16)); }
        finally { flushCompletion = null; }
    }
    internal void ClosePermanently() { allowClose = true; Close(); }

    private async void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != pageUri) return;
        int id = 0;
        string action = "parse-message";
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var message = doc.RootElement;
            if (message.TryGetProperty("id", out var ident)) id = ident.GetInt32();
            action = message.GetProperty("action").GetString() ?? "";
            var payload = message.TryGetProperty("payload", out var value) ? value : default;
            object? result = null;
            switch (action)
            {
                case "context": result = new { mode = Mode, noteId = NoteId, image = ImageSource, appearance = IsSettingsView, inputDirection = InputDirection(), active = IsActive }; break;
                case "input-language": result = InputDirection(); break;
                case "log-error": session.Log.Renderer(payload); result = true; break;
                case "load": result = await store.Load(); break;
                case "import": await store.Import(payload); break;
                case "new-note": result = (await session.NewNote()).NoteId; break;
                case "open-note":
                    var noteId = payload.GetString()!;
                    if (!Guid.TryParse(noteId, out _)) throw new InvalidDataException("یادداشت معتبر نیست.");
                    var data = await store.Load();
                    if (data is null || !data.Value.GetProperty("notes").EnumerateArray().Any(note => note.GetProperty("id").GetString() == noteId))
                        throw new InvalidDataException("یادداشت پیدا نشد.");
                    session.OpenNote(noteId); break;
                case "home": session.Home(payload.ValueKind == JsonValueKind.True); break;
                case "settings": session.Settings(); break;
                case "note-metadata": await store.SaveMetadata(payload); await session.Changed(); result = true; break;
                case "note-delete":
                    if (Mode != "note" || payload.GetString() != NoteId) throw new InvalidDataException("Delete a note from its own window.");
                    await Flush();
                    await store.Delete(NoteId!);
                    // Once committed, a notification failure must not masquerade as a failed deletion.
                    try { await session.Changed(); }
                    catch (Exception ex) { session.Log.Error("delete-notification", ex, Mode); }
                    result = true;
                    // Reply before disposing this WebView. No other window is revealed or closed.
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, ClosePermanently);
                    break;
                case "save":
                    if (Mode != "note" || payload.GetProperty("id").GetString() != NoteId) throw new InvalidDataException("این پنجره فقط یادداشت خودش را ذخیره می‌کند.");
                    await store.SaveNote(payload); await session.Changed(); result = true; break;
                case "preferences": result = (await store.SavePreferences(payload)).GetProperty("prefs"); await session.Changed(); break;
                case "theme":
                    UpdateTrayLanguage();
                    bool light = payload.GetProperty("mode").GetString() == "light";
                    Surface.Background = new SolidColorBrush(light ? Colors.White : Color.FromRgb(23,23,23));
                    Browser.DefaultBackgroundColor = light ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(23,23,23);
                    UpdateNativeIcon();
                    break;
                case "pin":
                    if (!testing) Topmost = payload.GetBoolean();
                    result = payload.GetBoolean(); break;
                case "drag":
                    if (!testing) { ReleaseCapture(); SendMessage(source!.Handle, 0x00A1, 2, 0); }
                    break;
                case "hide":
                    if (!testing) { if (Mode == "image") _ = Dispatcher.BeginInvoke(ClosePermanently); else Hide(); }
                    break;
                case "quit":
                    if (Mode == "note") throw new InvalidOperationException("Quit Jot from Home, Settings, or the tray.");
                    if (testing) await session.FlushNotes();
                    else _ = Dispatcher.InvokeAsync(async () => { try { await session.Quit(); } catch (Exception ex) { session.Log.Error("quit",ex,Mode);Post(new { @event = "quit-failed", message = ex.Message }); } });
                    break;
                case "flush-complete": flushCompletion?.TrySetResult(); break;
                case "flush-failed": flushCompletion?.TrySetException(new IOException("ذخیره یکی از یادداشت‌ها انجام نشد.")); break;
                case "view-image":
                    var src = payload.GetString() ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(src, @"^data:image/(png|jpeg|webp|gif);base64,") || src.Length > 12*1024*1024)
                        throw new InvalidDataException("تصویر معتبر نیست.");
                    session.Image(src); break;
                case "clipboard-write":
                    var clipboard = RichClipboard.Create(payload);
                    if (!testing) await RichClipboard.WriteAsync(clipboard, session.Log);
                    result = true; break;
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
                            var html = "<!doctype html><html><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\"><title>Jot</title><style>body{max-width:760px;margin:40px auto;padding:20px;font:16px/1.9 system-ui}img{max-width:100%}p,li,blockquote{unicode-bidi:plaintext}pre{white-space:pre-wrap}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #aaa}</style><body>" + payload.GetProperty("html").GetString() + "</body></html>";
                            await File.WriteAllTextAsync(dialog.FileName, html);
                        }
                    }
                    break;
                default: throw new InvalidOperationException("Unsupported action.");
            }
            if (testing) TestHostActions.Add(action);
            if (id > 0 && Browser.CoreWebView2 is not null && e.Source == pageUri) Post(new { id, ok = true, value = result });
        }
        catch (Exception ex)
        {
            session.Log.Error(action, ex, Mode);
            var busy = RichClipboard.IsBusy(ex);
            var message = busy ? "کلیپ‌بورد موقتاً مشغول است. دوباره کپی کنید؛ یادداشت شما محفوظ است." : ex.Message;
            if (id > 0 && e.Source == pageUri)
                Post(new { id, ok = false, error = message, operation = action, code = busy ? "clipboard-busy" : "operation-failed", logged = true });
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0051) _ = Dispatcher.BeginInvoke(NotifyInputLanguage);
        if (msg == 0x0084)
        {
            int packed = unchecked((int)lParam);
            var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)(packed >> 16)));
            bool left = point.X < 13, right = point.X >= ActualWidth-13, top = point.Y < 13, bottom = point.Y >= ActualHeight-13;
            int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 1;
            if (hit != 1) { handled = true; return hit; }
        }
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            _ = ToggleNoteWindow();
            handled = true;
        }
        return 0;
    }
    private async Task ToggleNoteWindow()
    {
        try
        {
            var last = session.Windows.LastOrDefault(window => window.Mode == "note");
            if (last is null) await session.NewNote();
            else if (last.IsVisible) { await last.Flush(); last.Hide(); }
            else last.Reveal();
        }
        catch (Exception ex) { session.Log.Error("toggle-note",ex,Mode);Post(new { @event = "warning", message = ex.Message }); }
    }
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!allowClose && Mode != "image")
        {
            e.Cancel = true;
            try { await Flush(); Hide(); }
            catch (Exception ex) { session.Log.Error("close-window",ex,Mode);Post(new { @event = "warning", message = ex.Message }); }
            return;
        }
        if (source is not null) UnregisterHotKey(source.Handle, HotkeyId);
        System.Windows.Input.InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;
        if(!testing&&Mode=="home")Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnSystemPreferenceChanged;
        tray?.Dispose(); themedIcon?.Dispose(); Browser.Dispose(); session.Windows.Remove(this);
    }
    private void UpdateNativeIcon()
    {
        bool light=AppIcon.WindowsUsesLightTray();
        var key=light?"monochrome-dark":"monochrome-light";
        if (key == NativeIconKey) return;
        var bytes = AppIcon.RenderMonochrome(light);
        using var stream = new MemoryStream(bytes);
        var next = new System.Drawing.Icon(stream,32,32);
        using var wpfStream = new MemoryStream(bytes);
        Icon = BitmapFrame.Create(wpfStream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        var previous = themedIcon;
        themedIcon = next;
        if (tray is not null) tray.Icon = next;
        previous?.Dispose();
        NativeIconKey = key;
    }
    private void UpdateTrayLanguage()
    {
        if (tray?.ContextMenuStrip is not { } menu) return;
        var labels = new[] { "Notes", "New note", "Quit" };
        for (int i = 0; i < Math.Min(menu.Items.Count, labels.Length); i++) menu.Items[i].Text = labels[i];
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
