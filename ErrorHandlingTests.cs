using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyErrorHandling(List<object> checks)
    {
        var log=new ErrorLog(Path.Combine(testOutput,"error-log-tests"));
        int attempts=0;var waits=new List<int>();
        await RichClipboard.RetryAsync(()=>{
            attempts++;
            if(attempts<=3)throw new ExternalException("PRIVATE-CLIPBOARD-SENTINEL",RichClipboard.ClipboardBusyHResult);
        },log,delay:milliseconds=>{waits.Add(milliseconds);return Task.CompletedTask;});
        checks.Add(new{name="clipboard-lock-retries-and-recovers",passed=attempts==4&&waits.SequenceEqual(new[]{50,90,150})});
        attempts=0;bool bounded=false;
        try{
            await RichClipboard.RetryAsync(()=>{attempts++;throw new ExternalException("secret",RichClipboard.ClipboardBusyHResult);},log,delay:_=>Task.CompletedTask);
        }catch(ExternalException){bounded=true;}
        checks.Add(new{name="clipboard-retries-bounded",passed=bounded&&attempts==7});
        attempts=0;
        try{await RichClipboard.RetryAsync(()=>{attempts++;throw new InvalidOperationException("PRIVATE-NOTE-SENTINEL");},log,delay:_=>Task.CompletedTask);}
        catch(InvalidOperationException){ }
        checks.Add(new{name="unrelated-errors-not-retried",passed=attempts==1});
        attempts=0;
        await RichClipboard.RetryAsync(()=>attempts++,log,()=>true,delay:_=>Task.CompletedTask);
        checks.Add(new{name="stale-copy-cancelled-before-writing",passed=attempts==0});
        log.Error("test-error",new IOException("PRIVATE-NOTE-SENTINEL https://private.invalid/?token=PRIVATE-TOKEN"));
        using(var entry=JsonDocument.Parse("""{"name":"Error","operation":"copy","code":"clipboard-busy","source":"renderer.js","line":25,"message":"PRIVATE-NOTE-SENTINEL","payload":"PRIVATE-CLIPBOARD-SENTINEL"}"""))log.Renderer(entry.RootElement);
        var text=await File.ReadAllTextAsync(log.FilePath);
        checks.Add(new{name="logs-contain-diagnostics-not-private-content",passed=text.Contains("0x800401D0")&&text.Contains("renderer.js")&&text.Contains("recovered")&&!text.Contains("PRIVATE-")&&!text.Contains("private.invalid")});
        var rotation=new ErrorLog(Path.Combine(testOutput,"log-rotation"),700);
        for(int i=0;i<80;i++)rotation.Event("rotation-test","done",i);
        var files=Directory.GetFiles(rotation.DirectoryPath,"jot-errors*.jsonl");
        bool valid=files.Length==4;
        foreach(var file in files){
            valid&=new FileInfo(file).Length<=700;
            foreach(var line in await File.ReadAllLinesAsync(file)){using var json=JsonDocument.Parse(line);}
        }
        checks.Add(new{name="local-logs-rotate-and-stay-bounded",passed=valid});
        using(var stream=new FileStream(Path.Combine(testOutput,"not-a-directory"),FileMode.Create,FileAccess.Write)){
            new ErrorLog(stream.Name).Error("unwritable-log",new IOException("private"));
        }
        checks.Add(new{name="logging-failure-does-not-crash-app",passed=true});
    }
}
