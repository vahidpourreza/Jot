using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed record SavedNoteWindow(string NoteId,int X,int Y,int Width,int Height,string[]? Tabs=null)
{
    internal bool IsValid=>Guid.TryParse(NoteId,out _)&&X is >= -2000000 and <= 2000000&&Y is >= -2000000 and <= 2000000&&Width is >= 200 and <= 20000&&Height is >= 120 and <= 20000&&(Tabs is null||Tabs.Length is >0 and <=200&&Tabs.All(id=>Guid.TryParse(id,out _))&&Tabs.Contains(NoteId));
}
internal sealed record SavedWorkspace(string[] Tabs,string ActiveId,bool SettingsOpen,int X,int Y,int Width,int Height,string[]? PinnedTabs=null)
{
    internal bool IsValid=>Tabs is not null&&Tabs.Length<=200&&Tabs.Distinct().Count()==Tabs.Length&&Tabs.All(id=>Guid.TryParse(id,out _))&&
        (PinnedTabs is null||PinnedTabs.Length<=Tabs.Length&&PinnedTabs.Distinct().Count()==PinnedTabs.Length&&PinnedTabs.All(Tabs.Contains))&&
        (ActiveId=="home"||ActiveId=="settings"&&SettingsOpen||Tabs.Contains(ActiveId))&&X is >= -2000000 and <=2000000&&Y is >= -2000000 and <=2000000&&Width is >=200 and <=20000&&Height is >=120 and <=20000;
}

internal sealed partial class NoteStore
{
    internal Task<SavedNoteWindow[]> LoadWindowSession()=>Run(connection=>{
        var extra=JsonNode.Parse(Scalar(connection,null,"SELECT extra FROM app_state WHERE singleton=1;") as string??"{}")!.AsObject();
        if(extra["desktopSession"] is not { } saved)return Array.Empty<SavedNoteWindow>();
        if(saved is not JsonObject session||session["version"]?.GetValue<int>()!=1||session["windows"] is not JsonArray)
            throw new InvalidDataException("Jot could not read the previous window session. Your notes have not changed.");
        var windows=session["windows"]!.Deserialize<SavedNoteWindow[]>()??[];
        if(windows.Any(w=>w is null||!w.IsValid))throw new InvalidDataException("The saved window session is invalid. Your notes have not changed.");
        var restored=new List<SavedNoteWindow>();var seen=new HashSet<string>();
        foreach(var window in windows)
        {
            var ids=(window.Tabs??[window.NoteId]).Distinct().Where(id=>!seen.Contains(id)&&Scalar(connection,null,"SELECT 1 FROM notes WHERE id=$id;",("$id",id)) is not null).ToArray();
            // Older sessions grouped floating notes into tabs. Restore every
            // surviving note as its own window; Home never opens on startup.
            for(int i=0;i<ids.Length;i++)
            {
                seen.Add(ids[i]);
                restored.Add(window with{NoteId=ids[i],Tabs=null,X=Math.Min(2000000,window.X+i*24),Y=Math.Min(2000000,window.Y+i*24)});
            }
        }
        return restored.ToArray();
    });
    internal Task<SavedWorkspace?> LoadWorkspaceSession()=>Run(connection=>{
        var extra=JsonNode.Parse(Scalar(connection,null,"SELECT extra FROM app_state WHERE singleton=1;") as string??"{}")!.AsObject();
        if(extra["desktopSession"]?["workspace"] is not {} json)return (SavedWorkspace?)null;
        var saved=json.Deserialize<SavedWorkspace>();
        if(saved is null||!saved.IsValid)throw new InvalidDataException("The saved workspace is invalid. Your notes have not changed.");
        var tabs=saved.Tabs.Where(id=>Scalar(connection,null,"SELECT 1 FROM notes WHERE id=$id;",("$id",id)) is not null).ToArray();
        return saved with{Tabs=tabs,PinnedTabs=(saved.PinnedTabs??[]).Where(tabs.Contains).ToArray(),ActiveId=saved.ActiveId is "home" or "settings"||tabs.Contains(saved.ActiveId)?saved.ActiveId:"home"};
    });
    internal Task SaveWindowSession(IEnumerable<SavedNoteWindow> windows,SavedWorkspace? workspace=null)
    {
        var snapshot=windows.ToArray();
        if(snapshot.Any(w=>w is null||!w.IsValid))throw new InvalidDataException("Invalid note window layout.");
        if(workspace is not null&&!workspace.IsValid)throw new InvalidDataException("Invalid workspace layout.");
        return Run(connection=>{
            using var tx=connection.BeginTransaction();
            var extra=JsonNode.Parse(Scalar(connection,tx,"SELECT extra FROM app_state WHERE singleton=1;") as string??"{}")!.AsObject();
            var surviving=snapshot.DistinctBy(w=>w.NoteId).Where(w=>Scalar(connection,tx,"SELECT 1 FROM notes WHERE id=$id;",("$id",w.NoteId)) is not null).ToArray();
            extra["desktopSession"]=JsonSerializer.SerializeToNode(new{version=1,windows=surviving,workspace});
            Execute(connection,tx,"UPDATE app_state SET extra=$extra WHERE singleton=1;",("$extra",extra.ToJsonString()));tx.Commit();return true;
        },true);
    }
}
