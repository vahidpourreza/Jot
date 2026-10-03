using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Jot;
internal sealed class FileCommands : IDisposable
{
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    internal static string PipeName=>TaskbarCommands.PipeName+"-files";
    internal static string[]? Parse(string[] args)
    {
        if(args.Length==0)return null;
        if(args[0]=="--open-file")return Validate(args.Skip(1).ToArray());
        return args[0].StartsWith("--",StringComparison.Ordinal)?null:Validate(args);
    }
    private static string[] Validate(string[] paths)
    {
        if(paths.Length is <1 or >16)throw new ArgumentException("Open between one and sixteen note files.");
        return paths.Select(path=>{if(string.IsNullOrWhiteSpace(path)||path.Length>32767)throw new ArgumentException("Invalid file path.");NoteFileFormat.Format(path);return Path.GetFullPath(path);}).ToArray();
    }
    private readonly CancellationTokenSource stopping=new();
    private readonly Task listener;
    internal Task Completion=>listener;
    internal FileCommands(string name,Func<string[],Task> dispatch,Action<Exception> failed)
    {
        listener=Task.Run(async()=>{
            while(!stopping.IsCancellationRequested)
            {
                try
                {
                    using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stopping.Token);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var header=new byte[4];await pipe.ReadExactlyAsync(header,timeout.Token);int length=BitConverter.ToInt32(header);
                    if(length is <2 or >65536)throw new InvalidDataException("Invalid file request.");
                    var data=new byte[length];await pipe.ReadExactlyAsync(data,timeout.Token);byte result=0;
                    try{await dispatch(Validate(JsonSerializer.Deserialize<string[]>(data)??[]));result=1;}catch(Exception error){failed(error);}
                    await pipe.WriteAsync(new[]{result},stopping.Token);
                }
                catch(OperationCanceledException) when(stopping.IsCancellationRequested){break;}
                catch(Exception error){if(stopping.IsCancellationRequested)break;failed(error);try{await Task.Delay(100,stopping.Token);}catch(OperationCanceledException){break;}}
            }
        });
    }
    internal static async Task<bool> Send(string name,string[] paths,bool foreground=false)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(Validate(paths));if(bytes.Length>65536)throw new ArgumentException("The file request is too large.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000,timeout.Token);
        if(foreground&&GetNamedPipeServerProcessId(client.SafePipeHandle,out var id))AllowSetForegroundWindow(id);
        await client.WriteAsync(BitConverter.GetBytes(bytes.Length),timeout.Token);await client.WriteAsync(bytes,timeout.Token);
        var reply=new byte[1];await client.ReadExactlyAsync(reply,timeout.Token);return reply[0]==1;
    }
    public void Dispose()=>stopping.Cancel();
}
