using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Jot;

internal enum TaskbarAction : byte { Home=1, NewNote=2, Settings=3 }

// Tiny, bounded, same-user IPC. A JumpTask launches Jot with one whitelisted
// action; it must not turn every second-instance launch into "activate notes".
internal sealed class TaskbarCommands : IDisposable
{
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    internal const string Flag="--taskbar-action";
    internal static bool IsSupported(TaskbarAction action)=>action is TaskbarAction.Home or TaskbarAction.NewNote or TaskbarAction.Settings;
    internal static string Argument(TaskbarAction action)=>action switch
    {TaskbarAction.Home=>"home",TaskbarAction.NewNote=>"new-note",TaskbarAction.Settings=>"settings",_=>throw new ArgumentOutOfRangeException(nameof(action))};
    internal static TaskbarAction? Parse(string[] args)
    {
        if(!args.Contains(Flag))return null;
        if(args.Length!=2||args[0]!=Flag)throw new ArgumentException("Invalid taskbar arguments.");
        return args[1] switch {"home"=>TaskbarAction.Home,"new-note"=>TaskbarAction.NewNote,"settings"=>TaskbarAction.Settings,_=>throw new ArgumentException("Unknown taskbar action.")};
    }
    internal static string PipeName
    {
        get
        {
            using var identity=WindowsIdentity.GetCurrent();
            var owner=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User!.Value)))[..24];
            using var process=Process.GetCurrentProcess();
            return "Jot-taskbar-v1-"+owner+"-"+process.SessionId;
        }
    }
    internal static string TestPipeName(string token)=>"Jot-test-taskbar-"+Guid.ParseExact(token,"N").ToString("N");
    private readonly string name;
    private readonly Func<TaskbarAction,Task> dispatch;
    private readonly Action<Exception> failed;
    private readonly CancellationTokenSource stopping=new();
    private readonly Task listener;
    internal Task Completion=>listener;
    internal TaskbarCommands(string name,Func<TaskbarAction,Task> dispatch,Action<Exception> failed)
    {this.name=name;this.dispatch=dispatch;this.failed=failed;listener=Task.Run(Listen);}

    private async Task Listen()
    {
        while(!stopping.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var readLimit=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                readLimit.CancelAfter(TimeSpan.FromSeconds(3));
                var request=new byte[2];await pipe.ReadExactlyAsync(request,readLimit.Token);
                byte result=0;
                if(request[0]==1&&IsSupported((TaskbarAction)request[1]))
                {
                    try{await dispatch((TaskbarAction)request[1]);result=1;}
                    catch(Exception error){failed(error);}
                }
                await pipe.WriteAsync(new[]{result},stopping.Token);
            }
            catch(OperationCanceledException)when(stopping.IsCancellationRequested){break;}
            catch(Exception error)
            {
                if(stopping.IsCancellationRequested)break;
                failed(error);
                try{await Task.Delay(100,stopping.Token);}catch(OperationCanceledException){break;}
            }
        }
    }
    internal static async Task<bool> Send(string name,TaskbarAction action,bool requestForeground=false)
    {
        if(!IsSupported(action))throw new ArgumentOutOfRangeException(nameof(action));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000,timeout.Token);
        // The taskbar-launched child owns the user's foreground permission.
        // Grant it only to our same-user server, never to all processes.
        // Offscreen tests leave this disabled.
        if(requestForeground&&GetNamedPipeServerProcessId(client.SafePipeHandle,out var processId))AllowSetForegroundWindow(processId);
        // No automatic resend after writing: an acknowledgement lost during
        // shutdown must not create a duplicate note on retry.
        await client.WriteAsync(new byte[]{1,(byte)action},timeout.Token);
        var answer=new byte[1];await client.ReadExactlyAsync(answer,timeout.Token);
        return answer[0]==1;
    }
    public void Dispose(){stopping.Cancel();}
}
