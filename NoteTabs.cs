using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;
public partial class MainWindow
{
    internal List<string> NoteTabIds { get; }=[];
    internal HashSet<string> PinnedTabIds { get; }=[];
    private sealed record ClosedNoteTab(string Id,int Position,bool Pinned);
    private readonly List<ClosedNoteTab> closedNoteTabs=[];
    internal bool CanReopenClosedTab=>closedNoteTabs.Count>0;
    internal bool SettingsTabOpen { get; private set; }
    internal bool RememberWorkspaceOnQuit { get; set; }
    private string ActiveWorkspaceTab=>NoteId??(IsSettingsView?"settings":"home");
    internal bool Tabbed=>Mode=="home";
    internal bool HasNoteEditor=>NoteId is not null&&(Mode=="note"||Mode=="home");
    private Task tabOperation=Task.CompletedTask;
    private TaskCompletionSource<bool> workspaceReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? workspaceDocumentId;
    private TaskCompletionSource? workspaceViewReady;
    private string? workspaceViewIntent;
    private string WorkspaceView=>NoteId is not null?"note":IsSettingsView?"settings":"home";
    internal bool ContainsNote(string id)=>Mode=="note"?NoteId==id:Mode=="home"&&NoteTabIds.Contains(id);
    private Task<T> QueueTabOperation<T>(Func<Task<T>> run)
    {
        if(Mode!="home")throw new InvalidOperationException("Tabs are available only in Home.");
        var previous=tabOperation;
        async Task<T> Execute(){try{await previous;}catch{}if(closingPermanently)throw new OperationCanceledException();try{await WaitForWorkspace();return await run();}catch{Post(new{@event="resume-editing"});throw;}}
        var next=Execute();tabOperation=next;return next;
    }
    internal async Task<JsonElement[]> TabHeaders()
    {
        if(!Tabbed)return [];
        PinnedTabIds.IntersectWith(NoteTabIds);
        var headers=await store.LoadTabHeaders(NoteTabIds);
        return headers.Select(header=>{var note=JsonSerializer.SerializeToNode(header)!.AsObject();note["tabPinned"]=PinnedTabIds.Contains(header.GetProperty("id").GetString()!);return JsonSerializer.SerializeToElement(note);}).ToArray();
    }
    internal async Task RefreshTabHeaders()
    {if(Tabbed&&!closingPermanently)Post(new{@event="tabs-changed",tabs=await TabHeaders(),activeId=ActiveWorkspaceTab,settingsOpen=SettingsTabOpen,canReopenTab=CanReopenClosedTab,workspace=true,tabbed=true});}
    private async Task WaitForWorkspace()
    {
        while(true)
        {
            var completion=workspaceReadyCompletion;
            var ready=await completion.Task.WaitAsync(TimeSpan.FromSeconds(16),windowLifetime.Token);
            if(completion!=workspaceReadyCompletion)continue;
            if(!ready)throw new IOException("The Home workspace could not open.");
            return;
        }
    }
    private bool IsWorkspaceDocument(string? documentId)=>Mode=="home"&&documentId is not null&&documentId==workspaceDocumentId;
    private void BeginWorkspaceNavigation()
    {
        if(Mode!="home")return;
        workspaceDocumentId=null;
        // The first Home/Settings request can already be waiting before the
        // WebView starts navigating. Retain that initial pending promise.
        if(workspaceReadyCompletion.Task.IsCompleted)workspaceReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        if(editorReadyCompletion.Task.IsCompleted)editorReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceViewReady?.TrySetException(new IOException("The Home workspace is reloading."));
        flushCompletion?.TrySetException(new IOException("The note editor reloaded before saving completed."));
    }
    private void FailWorkspace(string message)
    {
        if(Mode!="home")return;
        workspaceDocumentId=null;
        if(workspaceReadyCompletion.Task.IsCompleted)workspaceReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceReadyCompletion.TrySetResult(false);
        if(editorReadyCompletion.Task.IsCompleted)editorReadyCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        editorReadyCompletion.TrySetResult(false);
        workspaceViewReady?.TrySetException(new IOException(message));
        flushCompletion?.TrySetException(new IOException(message));
    }
    private void WorkspaceReady(string? documentId)
    {
        if(!IsWorkspaceDocument(documentId)||closingPermanently)return;
        if(HasNoteEditor)editorReadyCompletion.TrySetResult(true);
        workspaceReadyCompletion.TrySetResult(true);
        RevealReadyContent();
    }
    private void WorkspaceViewReady(JsonElement payload)
    {
        if(Mode!="home"||workspaceViewIntent is null||payload.ValueKind!=JsonValueKind.Object||
           !payload.TryGetProperty("intent",out var intent)||intent.ValueKind!=JsonValueKind.String||intent.GetString()!=workspaceViewIntent)return;
        if(HasNoteEditor)editorReadyCompletion.TrySetResult(true);
        workspaceViewReady?.TrySetResult();
    }
    private async Task InstallWorkspaceView(JsonElement? note=null)
    {
        await WaitForWorkspace();
        workspaceViewReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
        workspaceViewIntent=Guid.NewGuid().ToString("N");
        try
        {
            if(!Post(new{@event="workspace-view",view=WorkspaceView,note,prefs=await store.LoadPreferences(),tabs=await TabHeaders(),
                activeId=ActiveWorkspaceTab,settingsOpen=SettingsTabOpen,canReopenTab=CanReopenClosedTab,workspace=true,tabbed=true,intent=workspaceViewIntent,inputDirection=InputDirection(),active=IsActive,fullscreen=IsWindowFullscreen}))
                throw new IOException("The Home workspace is unavailable.");
            await workspaceViewReady.Task.WaitAsync(TimeSpan.FromSeconds(16),windowLifetime.Token);
        }
        finally{workspaceViewReady=null;workspaceViewIntent=null;}
    }
    private async Task RestoreWorkspaceView(string? oldId,bool oldSettings)
    {
        NoteId=oldId is not null&&NoteTabIds.Contains(oldId)?oldId:null;
        IsSettingsView=NoteId is null&&oldSettings;
        if(closingPermanently)return;
        try{await InstallWorkspaceView(NoteId is null?null:await store.LoadNote(NoteId));}
        catch(Exception error){session.Log.Error("workspace-restore",error,Mode);}
    }
    private async Task InstallHomeView(bool settings=false)
    {
        await FlushEditor(true);
        var oldId=NoteId;var oldSettings=IsSettingsView;var wasOpen=SettingsTabOpen;
        if(settings)SettingsTabOpen=true;
        NoteId=null;IsSettingsView=settings;
        Title=settings?"Settings — Jot":"Jot";
        ApplyWorkspacePin(await store.LoadPreferences());
        try{await InstallWorkspaceView();}
        catch{SettingsTabOpen=wasOpen;await RestoreWorkspaceView(oldId,oldSettings);throw;}
    }
    internal Task SwitchHomeView(bool settings=false)=>QueueTabOperation(async()=>{
        if(NoteId is null&&IsSettingsView==settings)return true;
        await InstallHomeView(settings);return true;
    });
    private async Task InstallTab(JsonElement note)
    {
        var oldId=NoteId;var oldSettings=IsSettingsView;
        var id=note.GetProperty("id").GetString()!;
        NoteId=id;IsSettingsView=false;
        try
        {
            await InstallWorkspaceView(note);
        }
        catch
        {
            // The previous draft is already saved. Restore it in this same
            // document so the native and renderer identities agree again.
            await RestoreWorkspaceView(oldId,oldSettings);
            throw;
        }
    }
    internal Task SwitchNoteTab(string id)=>QueueTabOperation(async()=>{
        if(id=="home"){await InstallHomeView();return true;}
        if(id=="settings"){await InstallHomeView(true);return true;}
        if(!Guid.TryParse(id,out _))throw new InvalidDataException("Invalid note.");
        if(!NoteTabIds.Contains(id)&&NoteTabIds.Count>=200)throw new InvalidOperationException("Home already has 200 note tabs. Close a tab or open a separate note window.");
        if(id==NoteId){await RefreshTabHeaders();return true;}
        var next=await store.LoadNote(id)??throw new InvalidDataException("Note not found.");
        await FlushEditor(true);bool added=!NoteTabIds.Contains(id);if(added)NoteTabIds.Add(id);
        try{await InstallTab(next);return true;}
        catch{if(added)NoteTabIds.Remove(id);Post(new{@event="resume-editing"});throw;}
    });
    internal async Task CloseNoteTab(string id,bool saveExternal=true,bool rememberClosed=true)
    {
        if(saveExternal&&id!="settings"&&ContainsNote(id)){await Flush();await session.SaveAutoFilesFor(new[]{id});}
        await QueueTabOperation(async()=>{
            if(id=="settings")
            {
                if(!SettingsTabOpen)return false;
                if(IsSettingsView)
                {
                    var neighbor=NoteTabIds.LastOrDefault();
                    if(neighbor is null)await InstallHomeView();
                    else await InstallTab((await store.LoadNote(neighbor))??throw new InvalidDataException("Note not found."));
                }
                SettingsTabOpen=false;await RefreshTabHeaders();return true;
            }
            if(!NoteTabIds.Contains(id))return false;
            var closed=new ClosedNoteTab(id,NoteTabIds.IndexOf(id),PinnedTabIds.Contains(id));
            if(id==NoteId)
            {
                var position=NoteTabIds.IndexOf(id);
                var next=position>0?NoteTabIds[position-1]:NoteTabIds.Skip(1).FirstOrDefault();
                if(next is null)await InstallHomeView();
                else{await FlushEditor(true);await InstallTab((await store.LoadNote(next))??throw new InvalidDataException("Note not found."));}
            }
            NoteTabIds.Remove(id);PinnedTabIds.Remove(id);
            if(rememberClosed)
            {
                closedNoteTabs.RemoveAll(item=>item.Id==id);closedNoteTabs.Add(closed);
                if(closedNoteTabs.Count>20)closedNoteTabs.RemoveRange(0,closedNoteTabs.Count-20);
            }
            await RefreshTabHeaders();return true;
        });
    }
    internal Task SetTabPinned(string id,bool pinned)=>QueueTabOperation(async()=>{
        if(!NoteTabIds.Contains(id))throw new InvalidDataException("Note not found in Home.");
        if(PinnedTabIds.Contains(id)==pinned)return true;
        NoteTabIds.Remove(id);
        if(pinned)PinnedTabIds.Add(id);else PinnedTabIds.Remove(id);
        NoteTabIds.Insert(NoteTabIds.Count(PinnedTabIds.Contains),id);
        await RefreshTabHeaders();return true;
    });
    internal Task ReorderNoteTabs(string[] ids)=>QueueTabOperation(async()=>{
        if(ids.Length!=NoteTabIds.Count||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!NoteTabIds.Contains(id)))
            throw new InvalidDataException("The open tabs changed. Please drag the tab again.");
        // A drag cannot silently pin/unpin: both sections keep their own order.
        var ordered=ids.Where(PinnedTabIds.Contains).Concat(ids.Where(id=>!PinnedTabIds.Contains(id))).ToArray();
        NoteTabIds.Clear();NoteTabIds.AddRange(ordered);await RefreshTabHeaders();return true;
    });
    internal Task<bool> ReopenClosedNoteTab()=>QueueTabOperation(async()=>{
        if(NoteTabIds.Count>=200)throw new InvalidOperationException("Home already has 200 note tabs. Close a tab before reopening another.");
        while(closedNoteTabs.Count>0)
        {
            var closed=closedNoteTabs[^1];
            if(session.Windows.Any(window=>window.ContainsNote(closed.Id))||await store.LoadNote(closed.Id) is not {} note)
            {closedNoteTabs.RemoveAt(closedNoteTabs.Count-1);continue;}
            await FlushEditor(true);
            var pinnedCount=NoteTabIds.Count(PinnedTabIds.Contains);
            var position=closed.Pinned?Math.Clamp(closed.Position,0,pinnedCount):Math.Clamp(closed.Position,pinnedCount,NoteTabIds.Count);
            NoteTabIds.Insert(position,closed.Id);if(closed.Pinned)PinnedTabIds.Add(closed.Id);
            try{await InstallTab(note);}
            catch{NoteTabIds.Remove(closed.Id);PinnedTabIds.Remove(closed.Id);throw;}
            closedNoteTabs.RemoveAt(closedNoteTabs.Count-1);await RefreshTabHeaders();return true;
        }
        await RefreshTabHeaders();return false;
    });
    internal SavedWorkspace? CaptureWorkspace()
    {
        if(Mode!="home"||NoteTabIds.Count==0&&!SettingsTabOpen)return null;
        var bounds=IsWindowFullscreen?fullscreenRestorePixels:ReadWindowPixels();
        return new(NoteTabIds.ToArray(),ActiveWorkspaceTab,SettingsTabOpen,bounds.X,bounds.Y,bounds.Width,bounds.Height,NoteTabIds.Where(PinnedTabIds.Contains).ToArray());
    }
    internal async Task RestoreSavedWorkspace(SavedWorkspace saved)
    {
        PinnedTabIds.Clear();PinnedTabIds.UnionWith((saved.PinnedTabs??[]).Where(saved.Tabs.Contains));
        NoteTabIds.Clear();NoteTabIds.AddRange(saved.Tabs.Where(PinnedTabIds.Contains).Concat(saved.Tabs.Where(id=>!PinnedTabIds.Contains(id))));SettingsTabOpen=saved.SettingsOpen;
        NoteId=NoteTabIds.Contains(saved.ActiveId)?saved.ActiveId:null;IsSettingsView=saved.ActiveId=="settings"&&SettingsTabOpen;
        RestoreNoteLayout(new(saved.Tabs.FirstOrDefault()??Guid.Empty.ToString(),saved.X,saved.Y,saved.Width,saved.Height));
        var wasInitialized=initialized;Reveal();await WaitForWorkspace();
        if(wasInitialized)await InstallWorkspaceView(NoteId is null?null:await store.LoadNote(NoteId));
    }
    internal async Task DetachNoteTab(string id)
    {
        if(Mode=="note")
        {
            if(NoteId!=id)throw new InvalidDataException("Note not found in this window.");
            try{await FlushEditor(true);}
            catch{Post(new{@event="resume-editing"});throw;}
            // The destination already exists; this transfer is not a last-window close.
            ClosePermanently();return;
        }
        await CloseNoteTab(id,saveExternal:false,rememberClosed:false);
    }
    internal async Task DeleteNoteTab(string id)
    {
        if(Mode=="note")
        {
            if(NoteId!=id)throw new InvalidDataException("Delete a note from its own window.");
            try{await Flush(true);await store.Delete(id);}
            catch{Post(new{@event="resume-editing"});throw;}
            ClosePermanently();
            try{await session.Changed(false);}catch(Exception error){session.Log.Error("delete-notification",error,Mode);}
            try{await session.QuitIfNoOpenWindows();}
            catch(Exception error){session.Log.Error("close-deleted-note",error);session.Home().ShowWarning("The note was deleted, but Jot could not finish saving its session. Please try Quit again.");}
            return;
        }
        await QueueTabOperation(async()=>{
            if(!NoteTabIds.Contains(id))throw new InvalidDataException("Note not found in Home.");
            await FlushEditor(true);
            var active=id==NoteId;
            var nextId=active?NoteTabIds.FirstOrDefault(n=>n!=id):null;
            var next=nextId is not null?await store.LoadNote(nextId):null;
            try{await store.Delete(id);}catch{Post(new{@event="resume-editing"});throw;}
            NoteTabIds.Remove(id);PinnedTabIds.Remove(id);closedNoteTabs.RemoveAll(item=>item.Id==id);
            if(active)
            {
                // A deleted editor must never be flushed back into storage.
                NoteId=null;
                if(next is not null)await InstallTab(next.Value);
                else{IsSettingsView=false;ApplyWorkspacePin(await store.LoadPreferences());await InstallWorkspaceView();}
            }
            else{await RefreshTabHeaders();Post(new{@event="resume-editing"});}
            return true;
        });
        try{await session.Changed(false);}catch(Exception error){session.Log.Error("delete-notification",error,Mode);}
    }
}
