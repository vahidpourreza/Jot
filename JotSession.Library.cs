using System.IO;

namespace Jot;

internal sealed partial class JotSession
{
    private readonly HashSet<Task<int>> libraryTasks=[];
    internal bool UpdatingLibrary=>libraryTasks.Any(task=>!task.IsCompleted);
    internal async Task WaitForLibraryOperations()
    {foreach(var task in libraryTasks.ToArray())try{await task;}catch{}}
    internal Task<int> UpdateTrash(IEnumerable<string> keys,string action)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        if(action is not ("restore" or "delete"))throw new InvalidDataException("Choose a valid Trash action.");
        var selected=NoteStore.TrashKeys(keys);var task=UpdateTrashCore(selected,action);libraryTasks.Add(task);
        async Task<int> Observe(){try{return await task;}finally{libraryTasks.Remove(task);}}
        return Observe();
    }
    private async Task<int> UpdateTrashCore(string[] keys,string action)
    {
        await noteOwnershipGate.WaitAsync();
        try{return action=="restore"?await Store.RestoreTrash(keys):await Store.DeleteTrash(keys);}
        finally
        {
            noteOwnershipGate.Release();
            try{await Changed(false);}catch(Exception error){Log.Error("trash-update-notification",error);}
        }
    }
    internal Task<int> UpdateLibraryNotes(IEnumerable<string> ids,string action,bool? pinned=null,string? folder=null)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var selected=NoteStore.LibraryIds(ids);
        var task=UpdateLibraryNotesCore(selected,action,pinned,folder);libraryTasks.Add(task);
        async Task<int> Observe(){try{return await task;}finally{libraryTasks.Remove(task);}}
        return Observe();
    }
    private async Task<int> UpdateLibraryNotesCore(string[] ids,string action,bool? pinned,string? folder)
    {
        await noteOwnershipGate.WaitAsync();
        var owners=Windows.Where(window=>ids.Any(window.ContainsNote)).Distinct().ToArray();
        try
        {
            if(action=="delete")foreach(var owner in owners)await owner.Flush(true);
            var count=await Store.UpdateLibraryNotes(ids,action,pinned,folder);
            if(action=="delete")
            {
                var deleted=ids.ToHashSet();
                foreach(var owner in owners)
                {
                    if(owner.Mode=="note")owner.ClosePermanently();
                    else await owner.RemoveDeletedLibraryTabs(deleted);
                }
            }
            try{await Changed(false);}catch(Exception error){Log.Error("library-update-notification",error);}
            return count;
        }
        finally
        {
            foreach(var owner in owners)owner.Post(new{@event="resume-editing"});
            noteOwnershipGate.Release();
        }
    }
}

public partial class MainWindow
{
    internal async Task RemoveDeletedLibraryTabs(HashSet<string> deleted)
    {
        try{await tabOperation;}catch{}
        var wasDeleted=NoteId is not null&&deleted.Contains(NoteId);
        NoteTabIds.RemoveAll(deleted.Contains);
        if(wasDeleted){NoteId=null;IsSettingsView=false;}
        try
        {
            await QueueTabOperation(async()=>
            {
                if(wasDeleted)
                {
                    var next=NoteTabIds.FirstOrDefault();
                    if(next is not null&&await store.LoadNote(next) is { } note){NoteId=next;await InstallWorkspaceView(note);}
                    else await InstallWorkspaceView();
                }
                else await RefreshTabHeaders();
                return true;
            });
        }
        catch(Exception error)
        {
            // Native ownership has already been reconciled even if the renderer
            // failed. Reloading Home cannot resurrect a deleted tab or editor.
            if(wasDeleted)NoteId=null;
            session.Log.Error("library-delete-view",error);
            ShowWarning("The notes were deleted. Reopen Home to refresh this window.");
        }
    }
}
