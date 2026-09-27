using System.IO;
using System.Threading;

namespace Jot;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationListener;
    internal static bool ShouldStartInTray(string[] args)=>args.Contains("--tray")&&!args.Contains("--self-test")&&!args.Contains("--package-smoke");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // A self-test child deliberately terminates itself with a synthetic store.
        // No window, tray, hotkey, normal profile, or single-instance signal is created.
        if(e.Args.Length>0&&e.Args[0]=="--sqlite-crash-probe")
        {
            Shutdown(SqliteCrashProbe.Run(e.Args));return;
        }
        // Build-time ICO packaging: no windows, tray, user data, or single-instance activation.
        if(e.Args.Length==2&&e.Args[0]=="--export-icon")
        {
            File.WriteAllBytes(Path.GetFullPath(e.Args[1]),AppIcon.RenderColor());
            var iconDirectory=Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!;
            File.WriteAllBytes(Path.Combine(iconDirectory,"jot-tray-dark.ico"),AppIcon.RenderMonochrome(true));
            File.WriteAllBytes(Path.Combine(iconDirectory,"jot-tray-light.ico"),AppIcon.RenderMonochrome(false));
            Shutdown();return;
        }
        bool smoke=e.Args.Contains("--package-smoke");
        bool test = e.Args.Contains("--self-test")||smoke;
        if (!test)
        {
            instance = new Mutex(true, @"Local\Jot-personal-notes-v2", out ownsInstance);
            if (!ownsInstance)
            {
                try { using var existing = EventWaitHandle.OpenExisting(@"Local\Jot-activate-v2"); existing.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Jot-activate-v2");
        }
        var outputIndex = Array.IndexOf(e.Args, "--test-output");
        var output = outputIndex >= 0 && e.Args.Length > outputIndex + 1 ? e.Args[outputIndex + 1] : null;
        output ??= Path.Combine(Path.GetTempPath(), "Jot-tests-" + Guid.NewGuid().ToString("N"));
        var session = new JotSession(test, output){PackageSmoke=smoke};
        DispatcherUnhandledException += (_, args) => session.Log.Error("dispatcher-unhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            if (args.ExceptionObject is Exception exception) session.Log.Error("process-unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => session.Log.Error("task-unobserved", args.Exception);
        if (activate is not null)
            activationListener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Dispatcher.BeginInvoke(() => session.Home()), null, Timeout.Infinite, false);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow(session, "home", runTests: test&&!smoke);
        MainWindow = window;
        if (test)
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Left = -32000;
            window.Top = -32000;
            window.Opacity = 0;
        }
        if(ShouldStartInTray(e.Args))window.StartInTray();
        else window.Reveal();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        activationListener?.Unregister(null);
        activate?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
