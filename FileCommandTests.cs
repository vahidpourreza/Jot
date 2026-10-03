using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyFileCommandRouting(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-command-"+name,passed});
        var root=Path.Combine(testOutput,"file-commands");Directory.CreateDirectory(root);
        var path=Path.Combine(root,"A separate text note.txt");await File.WriteAllTextAsync(path,"Opened from a separate process.");
        Check("raw-and-explicit-file-arguments",FileCommands.Parse([path])!.Single()==path&&FileCommands.Parse(["--open-file",path])!.Single()==path&&FileCommands.Parse([]) is null&&FileCommands.Parse(["--tray"]) is null);
        bool invalid=false;try{FileCommands.Parse(["--open-file","unsafe.html"]);}catch(InvalidDataException){invalid=true;}
        Check("unsupported-file-arguments-rejected",invalid);
        var session=new JotSession(true,root){ExerciseLifecycle=true};
        await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
        var host=new MainWindow(session,"home");host.StartInTray();
        var token=Guid.NewGuid().ToString("N");var pipe="Jot-test-files-"+token;int requests=0;
        using var server=new FileCommands(pipe,paths=>Dispatcher.InvokeAsync(async()=>{foreach(var item in paths){requests++;await host.OpenNoteFile(item);}}).Task.Unwrap(),error=>session.Log.Error("file-test",error));
        try
        {
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var argument in new[]{"--file-command-probe",token,path})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)??throw new IOException("Could not start the file command probe.");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
            var note=session.Windows.Single(w=>w.Mode=="note");await note.WaitFor("window.jotReady===true");
            Check("second-process-opens-requested-file",child.ExitCode==0&&requests==1&&await note.Script("editor.textContent==='Opened from a separate process.'")=="true");
            await FileCommands.Send(pipe,[path]);
            Check("repeat-file-command-reuses-editor",requests==2&&session.Windows.Count(w=>w.Mode=="note")==1);
            Check("file-commands-stay-offscreen",session.Windows.All(w=>!w.IsVisible||w.Left< -10000&&w.Opacity==0&&!w.ShowInTaskbar&&!w.ShowActivated));
        }
        finally{server.Dispose();await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}
