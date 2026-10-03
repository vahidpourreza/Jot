using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Shell;

namespace Jot;

public partial class MainWindow
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="ExtractIconExW")]
    private static extern uint ExtractTaskIcon(string file,int index,out nint large,out nint small,uint count);
    [DllImport("user32.dll",EntryPoint="DestroyIcon")] private static extern bool DestroyTaskIcon(nint icon);

    private async Task VerifyTaskbarActions(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="taskbar-menu-"+name,passed});
        foreach(bool light in new[]{false,true})
        {
            var list=TaskbarMenu.Create(Environment.ProcessPath!,AppContext.BaseDirectory,light);
            var tasks=list.JumpItems.Cast<JumpTask>().ToArray();
            Check("three-actions-in-order-"+light,tasks.Select(t=>t.Title).SequenceEqual(new[]{"Home","New note","Settings"}));
            Check("no-extra-categories-or-shortcuts-"+light,!list.ShowRecentCategory&&!list.ShowFrequentCategory&&tasks.All(t=>t.CustomCategory is null&&!t.Title.Contains("Ctrl")));
            foreach(var task in tasks)
            {
                Check("safe-launch-target-"+light+"-"+task.Title,task.ApplicationPath==Environment.ProcessPath&&task.WorkingDirectory==AppContext.BaseDirectory&&TaskbarCommands.Parse(task.Arguments.Split(' ')) is not null&&File.Exists(task.IconResourcePath));
                var count=ExtractTaskIcon(task.IconResourcePath,0,out var large,out var small,1);
                Check("native-icon-resource-"+light+"-"+task.Title,count==1&&large!=0&&small!=0);
                if(large!=0)DestroyTaskIcon(large);if(small!=0)DestroyTaskIcon(small);
            }
        }
        foreach(var args in new[]{new[]{TaskbarCommands.Flag},new[]{TaskbarCommands.Flag,"quit"},new[]{TaskbarCommands.Flag,"new-note","extra"},new[]{"extra",TaskbarCommands.Flag,"home"}})
        {
            bool rejected=false;try{TaskbarCommands.Parse(args);}catch(ArgumentException){rejected=true;}
            Check("reject-malformed-"+string.Join('-',args),rejected);
        }
        Check("ordinary-launch-unmodified",TaskbarCommands.Parse([]) is null&&TaskbarCommands.Parse(["--tray"]) is null);
        // Create data-only sessions first. Explicit shell actions must not also
        // restore old windows or create the automatic startup blank note.
        foreach(var action in new[]{TaskbarAction.Home,TaskbarAction.NewNote,TaskbarAction.Settings})
        {
            var s=new JotSession(true,Path.Combine(testOutput,"taskbar-cold",action.ToString()));
            new MainWindow(s,"home").StartInTray();
            try
            {
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
                var saved=await s.Store.Create();await s.Store.SaveWindowSession([new(saved,20,20,440,500)]);
                await s.StartFromLaunch(TaskbarCommands.Parse([TaskbarCommands.Flag,TaskbarCommands.Argument(action)]));
                foreach(var w in s.Windows.Where(w=>w.IsVisible))await w.WaitFor("window.jotReady===true");
                var notes=s.Windows.Where(w=>w.Mode=="note").ToArray();var home=s.Windows.Single(w=>w.Mode=="home");
                Check("cold-"+action+"-opens-only-requested-surface",action==TaskbarAction.NewNote?notes.Length==1&&notes[0].NoteId!=saved&&!home.IsVisible:notes.Length==0&&home.IsVisible&&home.IsSettingsView==(action==TaskbarAction.Settings));
                Check("cold-"+action+"-no-unwanted-blank-note",(await s.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==(action==TaskbarAction.NewNote?2:1));
                Check("cold-"+action+"-does-not-overwrite-restore-snapshot",(await s.Store.LoadWindowSession()).Single().NoteId==saved);
                Check("cold-"+action+"-test-isolation",s.Windows.All(w=>w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
            }
            finally{foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
        }
        var running=new JotSession(true,Path.Combine(testOutput,"taskbar-running"));
        new MainWindow(running,"home").StartInTray();
        try
        {
            await running.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            await running.StartFromLaunch(null);var first=running.Windows.Single(w=>w.Mode=="note");await first.WaitFor("window.jotReady===true");
            await first.Script("editor.innerHTML='<p>Taskbar draft</p>';onEdit();clearTimeout(saveTimer)");
            var ordinaryId=first.NoteId!;var home=running.Home();await home.WaitFor("window.jotReady===true");
            await running.NewTab(home);await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost");
            var tabs=home.NoteTabIds.ToArray();
            await running.ExecuteTaskbarAction(TaskbarAction.Home);await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            await running.ExecuteTaskbarAction(TaskbarAction.Settings);await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
            Check("running-home-settings-reuse-one-window",running.Windows.Count(w=>w.Mode=="home")==1&&home.IsSettingsView&&home.IsVisible);
            Check("running-navigation-preserves-home-tabs-and-separate-note",first.IsVisible&&first.NoteId==ordinaryId&&!first.Tabbed&&home.NoteTabIds.SequenceEqual(tabs));
            var token=Guid.NewGuid().ToString("N");var name=TaskbarCommands.TestPipeName(token);
            int received=0,failures=0;bool failNext=false;
            using var server=new TaskbarCommands(name,action=>Dispatcher.InvokeAsync(async()=>{
                received++;if(failNext){failNext=false;throw new IOException("Synthetic taskbar failure.");}
                await running.ExecuteTaskbarAction(action);
            }).Task.Unwrap(),_=>Interlocked.Increment(ref failures));
            async Task<int> Child(TaskbarAction action)
            {
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                foreach(var arg in new[]{"--taskbar-command-probe",token,TaskbarCommands.Argument(action)})start.ArgumentList.Add(arg);
                using var child=Process.Start(start)??throw new IOException("Could not start taskbar probe.");
                try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}
                catch{if(!child.HasExited)child.Kill();throw;}
                return child.ExitCode;
            }
            Check("second-process-home-dispatched",await Child(TaskbarAction.Home)==0&&!home.IsSettingsView);
            Check("second-process-settings-dispatched",await Child(TaskbarAction.Settings)==0&&home.IsSettingsView);
            int countBefore=(await running.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength();
            Check("second-process-new-note-dispatched",await Child(TaskbarAction.NewNote)==0&&(await running.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==countBefore+1);
            var burst=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>TaskbarCommands.Send(name,TaskbarAction.NewNote)));
            Check("rapid-new-note-clicks-never-coalesce",burst.All(ok=>ok)&&(await running.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==countBefore+4);
            int beforeInvalid=received;
            using(var invalid=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly))
            {
                await invalid.ConnectAsync(5000);await invalid.WriteAsync(new byte[]{1,99});var response=new byte[1];
                await invalid.ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Check("invalid-ipc-command-rejected",response[0]==0&&received==beforeInvalid);
            }
            failNext=true;Check("failure-is-acknowledged-not-replayed",!await TaskbarCommands.Send(name,TaskbarAction.NewNote)&&failures==1&&(await running.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==countBefore+4);
            Check("server-recovers-after-command-failure",await TaskbarCommands.Send(name,TaskbarAction.Home)&&!home.IsSettingsView);
            server.Dispose();await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Check("ipc-stops-cleanly",server.Completion.IsCompletedSuccessfully);
            foreach(var w in running.Windows.Where(w=>w.Mode=="note"))await w.WaitFor("window.jotReady===true");
            await running.PrepareQuit();
            var remembered=(await running.Store.LoadWindowSession()).Select(w=>w.NoteId).Concat((await running.Store.LoadWorkspaceSession())?.Tabs??[]).ToHashSet();
            Check("quit-remembers-workspace-and-separate-notes",tabs.All(remembered.Contains)&&remembered.Contains(ordinaryId));
            Check("existing-draft-retained",(await running.Store.LoadNote(ordinaryId))!.Value.GetProperty("plain").GetString()=="Taskbar draft");
            Check("all-ipc-windows-stay-offscreen",running.Windows.All(w=>w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
        }
        finally{foreach(var w in running.Windows.ToArray())w.ClosePermanently();}
    }
}
