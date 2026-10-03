using System.IO;
using System.Text.Json;

namespace Jot;

internal sealed partial class JotSession
{
    private readonly HashSet<Task<JsonElement>> preferenceChanges=[];
    internal bool ChangingOpeningMode=>preferenceChanges.Any(task=>!task.IsCompleted);

    internal async Task WaitForOpeningModeChange()
    {
        // Closing still works after a failed preference change. The originating
        // Settings request reports that error and the old editors remain open.
        foreach(var pending in preferenceChanges.ToArray())try{await pending;}catch{}
    }

    internal Task<MainWindow> OpenNoteDefault(string id)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        async Task<MainWindow> Open()
        {
            await noteOwnershipGate.WaitAsync();
            try
            {
                var prefs=await Store.LoadPreferences();
                return prefs.GetProperty("newNoteTarget").GetString()=="tab"
                    ?await OpenAsTabOwned(id):await OpenNoteWindowOwned(id);
            }
            finally{noteOwnershipGate.Release();}
        }
        var pending=Open();openingNotes.Add(pending);return ObserveCreation(pending);
    }

    internal Task<JsonElement> ApplyPreferences(JsonElement patch)
    {
        if(quitting)throw new InvalidOperationException("Jot is closing.");
        var pending=ApplyPreferencesCore(patch.Clone());preferenceChanges.Add(pending);
        async Task<JsonElement> Observe(){try{return await pending;}finally{preferenceChanges.Remove(pending);}}
        return Observe();
    }

    private async Task<JsonElement> ApplyPreferencesCore(JsonElement patch)
    {
        await noteOwnershipGate.WaitAsync();
        var held=new List<MainWindow>();
        var staged=new List<MainWindow>();
        MainWindow? workspace=null;
        string[] originalTabs=[];
        string[] originalPinnedTabs=[];
        string? originalActive=null;
        bool originalSettings=false,tabsStaged=false,committed=false;
        try
        {
            var previous=await Store.LoadPreferences();
            var changesMode=patch.TryGetProperty("newNoteTarget",out var choice)&&choice.ValueKind==JsonValueKind.String&&
                choice.GetString() is "tab" or "window"&&choice.GetString()!=previous.GetProperty("newNoteTarget").GetString();
            if(!changesMode)
            {
                var unchanged=(await SaveAppPreferences(patch)).GetProperty("prefs");
                await Changed();return unchanged;
            }

            workspace=Windows.FirstOrDefault(window=>window.Mode=="home"&&window.IsVisible&&!window.HideRequested);
            var sources=Windows.Where(window=>window.Mode=="note"&&window.IsVisible&&!window.HideRequested).ToArray();
            if(workspace is not null)
            {
                // Wait for an already accepted switch before taking its snapshot.
                held.Add(workspace);await workspace.Flush(true);
                originalTabs=workspace.NoteTabIds.ToArray();originalPinnedTabs=workspace.PinnedTabIds.ToArray();originalActive=workspace.NoteId;originalSettings=workspace.IsSettingsView;
            }
            foreach(var source in sources)
            {
                held.Add(source);await source.Flush(true);
            }
            // No preference or ownership changes have occurred until every
            // affected draft has been acknowledged by its original editor.
            if(choice.GetString()=="tab"&&sources.Length>0)
            {
                workspace??=Windows.FirstOrDefault(window=>window.Mode=="home")??new MainWindow(this,"home");
                if(!workspace.IsVisible)workspace.Reveal();
                var merged=originalTabs.Concat(sources.Select(window=>window.NoteId!)).Distinct().ToArray();
                if(merged.Length>200)throw new InvalidOperationException("Home supports 200 tabs. Close some notes before switching to Tabs.");
                tabsStaged=true;
                await workspace.SetTransferredTabs(merged,originalActive,originalSettings,originalPinnedTabs);
            }
            else if(choice.GetString()=="window"&&workspace is not null)
            {
                foreach(var id in originalTabs)
                {
                    var window=new MainWindow(this,"note",id){TransferPending=true};
                    staged.Add(window);window.Reveal();
                }
                // A failed destination leaves every source tab intact. Staged
                // windows remain transparent and unfocusable until commit.
                foreach(var window in staged)await window.WaitForTransferredEditor();
                tabsStaged=true;
                await workspace.SetTransferredTabs([],null,originalSettings);
            }

            var prefs=(await SaveAppPreferences(patch)).GetProperty("prefs");
            committed=true;
            if(choice.GetString()=="tab")foreach(var source in sources)source.ClosePermanently();
            foreach(var window in staged)window.CommitTransferredEditor();
            try{await Changed();}catch(Exception error){Log.Error("opening-mode-notification",error);}
            return prefs;
        }
        catch
        {
            if(!committed)
            {
                foreach(var window in staged)window.ClosePermanently();
                if(tabsStaged&&workspace is not null&&Windows.Contains(workspace))
                    try{await workspace.SetTransferredTabs(originalTabs,originalActive,originalSettings,originalPinnedTabs);}
                    catch(Exception error){Log.Error("opening-mode-restore",error);workspace.ShowWarning("Could not refresh Home. Your saved notes are safe; reopen Home to continue.");}
            }
            throw;
        }
        finally
        {
            foreach(var window in held)window.Post(new{@event="resume-editing"});
            noteOwnershipGate.Release();
        }
    }
}

public partial class MainWindow
{
    internal bool TransferPending {get;set;}
    internal async Task WaitForTransferredEditor()
    {
        if(!await editorReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(16),windowLifetime.Token))
            throw new IOException("A note window could not open. Your existing notes were kept in place.");
    }
    internal void CommitTransferredEditor()
    {TransferPending=false;RevealReadyContent();Post(new{@event="resume-editing"});}
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
