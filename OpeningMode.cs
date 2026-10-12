using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed partial class JotSession
{
    private readonly HashSet<Task<JsonElement>> preferenceChanges=[];
    internal bool ChangingOpeningMode=>preferenceChanges.Any(task=>!task.IsCompleted);

    internal async Task WaitForOpeningModeChange()
    {
        // Existing close/quit callers also wait for settings persistence.
        foreach(var pending in preferenceChanges.ToArray())try{await pending;}catch{}
    }

    internal Task<MainWindow> OpenNoteDefault(string id)=>OpenAsTab(id);

    internal Task<JsonElement> ApplyPreferences(JsonElement patch)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=ApplyPreferencesCore(patch.Clone());preferenceChanges.Add(pending);
        async Task<JsonElement> Observe(){try{return await pending;}finally{preferenceChanges.Remove(pending);}}
        return Observe();
    }

    private async Task<JsonElement> ApplyPreferencesCore(JsonElement patch)
    {
        // Tolerate stale settings messages from older renderers, but never let
        // the retired open mode move notes or change normal tab-first opening.
        var values=JsonNode.Parse(patch.GetRawText())!.AsObject();values.Remove("newNoteTarget");
        await noteOwnershipGate.WaitAsync();
        try
        {
            var prefs=(await SaveAppPreferences(JsonSerializer.SerializeToElement(values))).GetProperty("prefs");
            await Changed();return prefs;
        }
        finally{noteOwnershipGate.Release();}
    }
}

public partial class MainWindow
{
    // Kept for native reveal compatibility; settings no longer stage windows.
    internal bool TransferPending {get;set;}
    internal Task SetTransferredTabs(string[] ids,string? active,bool settings,string[]? pinnedTabs=null)=>QueueTabOperation(async()=>{
        var previous=NoteTabIds.ToArray();var previousPins=PinnedTabIds.ToArray();var previousActive=NoteId;var previousSettings=IsSettingsView;
        if(pinnedTabs is not null){PinnedTabIds.Clear();PinnedTabIds.UnionWith(pinnedTabs);}
        PinnedTabIds.IntersectWith(ids);
        NoteTabIds.Clear();NoteTabIds.AddRange(ids.Where(PinnedTabIds.Contains).Concat(ids.Where(id=>!PinnedTabIds.Contains(id))));
        NoteId=active;IsSettingsView=active is null&&settings;
        try
        {
            if(previousActive!=active||previousSettings!=IsSettingsView)
                await InstallWorkspaceView(active is null?null:await store.LoadNote(active));
            else await RefreshTabHeaders();
            return true;
        }
        catch
        {
            NoteTabIds.Clear();NoteTabIds.AddRange(previous);
            PinnedTabIds.Clear();PinnedTabIds.UnionWith(previousPins);
            await RestoreWorkspaceView(previousActive,previousSettings);throw;
        }
    });
}
