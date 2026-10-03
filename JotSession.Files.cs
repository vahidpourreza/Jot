namespace Jot;

internal sealed partial class JotSession
{
    private readonly SemaphoreSlim noteFileGate=new(1,1);
    private readonly HashSet<Task> noteFileOperations=[];
    internal Task<T> RunNoteFileOperation<T>(Func<Task<T>> operation,bool allowClosing=false)
    {
        if(quitting&&!allowClosing)throw new InvalidOperationException("Jot is closing.");
        async Task<T> Run(){await noteFileGate.WaitAsync();try{return await operation();}finally{noteFileGate.Release();}}
        var pending=Run();noteFileOperations.Add(pending);
        async Task<T> Observe(){try{return await pending;}finally{noteFileOperations.Remove(pending);}}
        return Observe();
    }
    internal async Task WaitForNoteFiles()
    {
        while(noteFileOperations.Count>0)
        {
            try{await Task.WhenAll(noteFileOperations.ToArray());}
            finally{noteFileOperations.RemoveWhere(task=>task.IsCompleted);}
        }
    }
    internal async Task<MainWindow> OpenAcceptedNoteFile(string id)
    {
        await noteOwnershipGate.WaitAsync();
        try{return (await Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab"?await OpenAsTabOwned(id):await OpenNoteWindowOwned(id);}
        finally{noteOwnershipGate.Release();}
    }
}
