using System.IO;
using System.Threading;

namespace Jot;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationListener;
    private JotSession? exitProbeSession;
    private JotSession? appSession;
    private TaskbarCommands? taskbarCommands;
    private TaskbarMenu? taskbarMenu;
    private FileCommands? fileCommands;
    internal void RefreshTaskbarMenu()=>taskbarMenu?.Refresh();
    internal static bool ShouldStartInTray(string[] args)=>args.Contains("--tray")&&!args.Contains("--self-test")&&!args.Contains("--package-smoke")&&!args.Contains("--exit-probe");

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        if(e.Args.Length==3&&e.Args[0]=="--file-command-probe")
        {
            try{Shutdown(await FileCommands.Send("Jot-test-files-"+Guid.ParseExact(e.Args[1],"N").ToString("N"),[e.Args[2]])?0:1);}
            catch{Shutdown(1);}return;
        }
        // A separate-process IPC test uses a GUID-scoped test pipe only. Never
        // touches the normal instance, Jump List, tray, clipboard or note store.
        if(e.Args.Length==3&&e.Args[0]=="--taskbar-command-probe")
        {
            try
            {
                var action=TaskbarCommands.Parse([TaskbarCommands.Flag,e.Args[2]])!.Value;
                Shutdown(await TaskbarCommands.Send(TaskbarCommands.TestPipeName(e.Args[1]),action)?0:1);
            }
            catch{Shutdown(1);}return;
        }
        // A self-test child deliberately terminates itself with a synthetic store.
        // No window, tray, hotkey, normal profile, or single-instance signal is created.
        if(e.Args.Length>0&&e.Args[0]=="--sqlite-crash-probe")
        {
            Shutdown(SqliteCrashProbe.Run(e.Args));return;
        }
        // Build-time ICO packaging: no windows, tray, user data, or single-instance activation.
        if(e.Args.Length==2&&e.Args[0]=="--export-file-icon")
        {NoteFileIcon.Export(Path.GetFullPath(e.Args[1]));Shutdown();return;}
        if(e.Args.Length==2&&e.Args[0]=="--export-icon")
        {
            File.WriteAllBytes(Path.GetFullPath(e.Args[1]),AppIcon.RenderColor());
            var iconDirectory=Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!;
            File.WriteAllBytes(Path.Combine(iconDirectory,"jot-tray-dark.ico"),AppIcon.RenderMonochrome(true));
            File.WriteAllBytes(Path.Combine(iconDirectory,"jot-tray-light.ico"),AppIcon.RenderMonochrome(false));
            TaskbarMenu.ExportIcons(Path.Combine(iconDirectory,"taskbar"));
            Shutdown();return;
        }
        bool smoke=e.Args.Contains("--package-smoke");
        bool exitProbe=e.Args.Contains("--exit-probe");
        bool test = e.Args.Contains("--self-test")||smoke||exitProbe;
        TaskbarAction? launchAction=null;
        try{launchAction=TaskbarCommands.Parse(e.Args);}catch(ArgumentException){Shutdown(2);return;}
        string[]? launchFiles=null;
        if(!test)try{launchFiles=FileCommands.Parse(e.Args);}catch(Exception error) when(error is ArgumentException or InvalidDataException){Shutdown(2);return;}
        if (!test)
        {
            instance = new Mutex(true, @"Local\Jot-personal-notes-v2", out ownsInstance);
            if (!ownsInstance)
            {
                if(launchFiles is not null)
                {
                    try{if(!await FileCommands.Send(FileCommands.PipeName,launchFiles,true)){Shutdown(1);return;}}
                    catch{System.Windows.MessageBox.Show("Quit the older running Jot, then open the updated version and try the file again.","Jot",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Information);Shutdown(1);return;}
                }
                else if(launchAction is { } action)
                {
                    try{if(!await TaskbarCommands.Send(TaskbarCommands.PipeName,action,requestForeground:true)){Shutdown(1);return;}}
                    catch(Exception)
                    {
                        System.Windows.MessageBox.Show("Jot could not receive the taskbar action. Quit the older running version, then open the latest Jot and try again.","Jot",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Information);
                        Shutdown(1);return;
                    }
                }
                else
                {
                    try { using var existing = EventWaitHandle.OpenExisting(@"Local\Jot-activate-v2"); existing.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                }
                Shutdown(); return;
            }
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Jot-activate-v2");
        }
        var outputIndex = Array.IndexOf(e.Args, "--test-output");
        var output = outputIndex >= 0 && e.Args.Length > outputIndex + 1 ? e.Args[outputIndex + 1] : null;
        output ??= Path.Combine(Path.GetTempPath(), "Jot-tests-" + Guid.NewGuid().ToString("N"));
        var session = new JotSession(test, output){PackageSmoke=smoke,ExitProbe=exitProbe};
        appSession=session;
        var startupReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? fileHost=null;
        if(!test)
        {
            try{TaskbarMenu.SetIdentity();taskbarMenu=new TaskbarMenu(this,session.Log);}
            catch(Exception error){session.Log.Error("taskbar-identity",error);}
            taskbarCommands=new TaskbarCommands(TaskbarCommands.PipeName,async action=>{
                await startupReady.Task;
                await Dispatcher.InvokeAsync(async()=>{
                    try{await session.ExecuteTaskbarAction(action);}
                    catch{session.Home().ShowWarning("Could not complete the taskbar action. Your notes are unchanged; please try again.");throw;}
                }).Task.Unwrap();
            },error=>session.Log.Error("taskbar-action",error));
            fileCommands=new FileCommands(FileCommands.PipeName,async paths=>{
                await startupReady.Task;
                await Dispatcher.InvokeAsync(async()=>{
                    try{foreach(var path in paths)await fileHost!.OpenNoteFile(path);}
                    catch(Exception error){session.Home().ShowWarning(error.Message);throw;}
                }).Task.Unwrap();
            },error=>session.Log.Error("open-note-file",error));
        }
        if(exitProbe)exitProbeSession=session;
        DispatcherUnhandledException += (_, args) => session.Log.Error("dispatcher-unhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            if (args.ExceptionObject is Exception exception) session.Log.Error("process-unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => session.Log.Error("task-unobserved", args.Exception);
        if (activate is not null)
            activationListener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Dispatcher.BeginInvoke(async () => {
                await startupReady.Task;
                try{await session.ActivateWork();}catch(Exception error){session.Log.Error("activate-work",error);session.Home().ShowStartupWarning();}
            }), null, Timeout.Infinite, false);
        var window = new MainWindow(session, "home", runTests: test&&!smoke&&!exitProbe);
        fileHost=window;
        MainWindow = window;
        if (test)
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Left = -32000;
            window.Top = -32000;
            window.Opacity = 0;
        }
        if(exitProbe)
        {
            window.StartInTray();
            try{await window.RunExitProbe(e.Args);}
            catch(Exception error){session.Log.Error("exit-probe",error);await File.WriteAllTextAsync(Path.Combine(output,"probe-error.txt"),error.ToString());foreach(var probeWindow in session.Windows.ToArray())probeWindow.ClosePermanently();Shutdown(1);}
        }
        else if(test)window.Reveal();
        else
        {
            window.StartInTray(); // Keep the tray host without loading/showing Home.
            RefreshTaskbarMenu();
            try{if(launchFiles is not null){foreach(var path in launchFiles)await window.OpenNoteFile(path);}else await session.StartFromLaunch(launchAction,ShouldStartInTray(e.Args));}
            catch(Exception error)
            {
                session.Log.Error(launchAction is null?"restore-work":"taskbar-start",error);
                if(launchFiles is not null)window.ShowWarning(error.Message);
                else if(launchAction is null)window.ShowStartupWarning();
                else window.ShowWarning("Could not complete the taskbar action. Your notes are unchanged; please try again.");
                window.Reveal();
            }
            finally{startupReady.TrySetResult();}
            await session.RefreshGlobalShortcuts();
            await session.ConfigureFileAutoSave();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        appSession?.DisposeGlobalShortcuts();appSession?.StopFileAutoSave();appSession?.DisposeWebViewInputProtection();
        if(exitProbeSession is { } probe)
            File.WriteAllText(Path.Combine(probe.Output,"probe-exit.json"),System.Text.Json.JsonSerializer.Serialize(new{processId=Environment.ProcessId,exitCode=e.ApplicationExitCode,remainingWindows=probe.Windows.Count}));
        activationListener?.Unregister(null);
        taskbarCommands?.Dispose();
        fileCommands?.Dispose();
        activate?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
