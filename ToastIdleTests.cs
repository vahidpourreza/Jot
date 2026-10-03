using System.IO;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyToastIdle(List<object> checks)
    {
        var target=new JotSession(true,Path.Combine(testOutput,"toast-idle")){ExerciseLifecycle=true};
        try{
            var note=new MainWindow(target,"note",await target.Store.Create());note.Reveal();await note.WaitFor("window.jotReady===true");
            await note.Script("JotToast.info('Transient fixture',{id:'idle-fixture',duration:0});JotToast.dismiss('idle-fixture')");
            await Task.Delay(150);
            await note.Script("window.toastIdleMutations=0;window.toastIdleObserver=new MutationObserver(records=>window.toastIdleMutations+=records.length);toastIdleObserver.observe(document.getElementById('jotToaster'),{attributes:true,attributeFilter:['hidden']})");
            await Task.Delay(400);
            var count=JsonSerializer.Deserialize<int>(await note.Script("toastIdleMutations"));await note.Script("toastIdleObserver.disconnect()");
            checks.Add(new{name="toast-idle-no-self-triggered-layout-after-dismiss",passed=count==0,hiddenMutations=count,observationMs=400});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"toast-idle.json"),JsonSerializer.Serialize(new{hiddenMutations=count,observationMs=400}));
        }finally{foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }
}
