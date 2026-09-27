using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed record SavedNoteWindow(string NoteId,int X,int Y,int Width,int Height)
{
    internal bool IsValid=>Guid.TryParse(NoteId,out _)&&X is >= -2000000 and <= 2000000&&Y is >= -2000000 and <= 2000000&&Width is >= 200 and <= 20000&&Height is >= 120 and <= 20000;
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
        return windows.DistinctBy(w=>w.NoteId).Where(w=>Scalar(connection,null,"SELECT 1 FROM notes WHERE id=$id;",("$id",w.NoteId)) is not null).ToArray();
    });
    internal Task SaveWindowSession(IEnumerable<SavedNoteWindow> windows)
    {
        var snapshot=windows.ToArray();
        if(snapshot.Any(w=>w is null||!w.IsValid))throw new InvalidDataException("Invalid note window layout.");
        return Run(connection=>{
            using var tx=connection.BeginTransaction();
            var extra=JsonNode.Parse(Scalar(connection,tx,"SELECT extra FROM app_state WHERE singleton=1;") as string??"{}")!.AsObject();
            var surviving=snapshot.DistinctBy(w=>w.NoteId).Where(w=>Scalar(connection,tx,"SELECT 1 FROM notes WHERE id=$id;",("$id",w.NoteId)) is not null).ToArray();
            extra["desktopSession"]=JsonSerializer.SerializeToNode(new{version=1,windows=surviving});
            Execute(connection,tx,"UPDATE app_state SET extra=$extra WHERE singleton=1;",("$extra",extra.ToJsonString()));tx.Commit();return true;
        },true);
    }
}
