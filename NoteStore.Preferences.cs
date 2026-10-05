using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;
internal sealed partial class NoteStore
{
    private const string NotePreferencesSchema="CREATE TABLE note_preferences(note_id TEXT PRIMARY KEY NOT NULL REFERENCES notes(id) ON DELETE CASCADE,value TEXT NOT NULL CHECK(json_valid(value)));";
    private static JsonObject DefaultNotePreferences(JsonObject defaults)=>new(){
        ["toolbarVisible"]=defaults["toolbarVisible"]?.GetValue<bool>()??true,["pinned"]=false
    };
    private static JsonElement PreserveLegacyWritingDefaults(JsonElement source)
    {
        // An omitted view identifies pre-note-settings data. Snapshot its old
        // effective typography; an explicit (including sparse) view is retained.
        // This runs only for legacy migration/import, never Create or file open.
        var data=JsonNode.Parse(source.GetRawText())!.AsObject();
        var prefs=data["prefs"] as JsonObject??Defaults();
        foreach(var item in data["notes"]!.AsArray())
        {
            var note=item!.AsObject();if(note["view"] is not null)continue;
            var view=DefaultNotePreferences(prefs);
            view["fontSize"]=Math.Clamp(prefs["fontSize"]?.GetValue<int>()??16,13,24);
            view["lineHeight"]=Math.Clamp(prefs["lineHeight"]?.GetValue<double>()??1.95,1.2,2.5);
            note["view"]=view;
        }
        return JsonSerializer.SerializeToElement(data);
    }
    private void UpgradeNotePreferences(SqliteConnection connection)
    {
        // Snapshot v1 before any schema change. Existing notes retain their old
        // appearance; only newly created notes inherit writing defaults.
        BackupCore(connection);
        var beforeUpgrade=FilePath+".before-v2.bak";
        if(!File.Exists(beforeUpgrade))BackupCore(connection,beforeUpgrade);
        using var transaction=connection.BeginTransaction();
        Execute(connection,transaction,NotePreferencesSchema);
        var defaults=DefaultNotePreferences(ReadPreferences(connection,transaction));
        var writing=ReadPreferences(connection,transaction);
        defaults["fontSize"]=Math.Clamp(writing["fontSize"]?.GetValue<int>()??16,13,24);
        defaults["lineHeight"]=Math.Clamp(writing["lineHeight"]?.GetValue<double>()??1.95,1.2,2.5);
        Execute(connection,transaction,"INSERT INTO note_preferences(note_id,value) SELECT id,$value FROM notes;",("$value",defaults.ToJsonString()));
        Execute(connection,transaction,"PRAGMA user_version=2;");transaction.Commit();backedUpThisSession=true;
    }
    public Task<JsonElement> SaveNotePreferences(string id,JsonElement patch)=>Run(connection=>{
        using var transaction=connection.BeginTransaction();
        var json=Scalar(connection,transaction,"SELECT value FROM note_preferences WHERE note_id=$id;",("$id",id)) as string??throw new InvalidDataException("Note not found.");
        var view=ReadNotePreferences(json);ApplyNotePreferencePatch(view,patch);
        Execute(connection,transaction,"UPDATE note_preferences SET value=$value WHERE note_id=$id;",("$id",id),("$value",view.ToJsonString()));
        RefreshFileDirty(connection,transaction,id);
        transaction.Commit();return JsonSerializer.SerializeToElement(view);
    },true);
    private static JsonObject ReadNotePreferences(string json)
    {
        var view=JsonNode.Parse(json)!.AsObject();
        // Early v1.6 builds stored theme here. It is app-wide now; tolerate old
        // rows/imports without letting them override the application theme.
        view.Remove("theme");return view;
    }
    private static void ApplyNotePreferencePatch(JsonObject view,JsonElement patch)
    {
        if(patch.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid note settings.");
        foreach(var field in patch.EnumerateObject())
        {
            if((field.Name is "fontSize" or "lineHeight")&&field.Value.ValueKind==JsonValueKind.Null){view.Remove(field.Name);continue;}
            bool valid=field.Name switch{
                "fontSize"=>field.Value.ValueKind==JsonValueKind.Number&&field.Value.TryGetInt32(out var size)&&size>=13&&size<=24,
                "lineHeight"=>field.Value.ValueKind==JsonValueKind.Number&&field.Value.TryGetDouble(out var height)&&double.IsFinite(height)&&height>=1.2&&height<=2.5,
                "toolbarVisible" or "pinned"=>field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _=>false
            };
            if(!valid)throw new InvalidDataException("Choose valid settings for this note.");
            view[field.Name]=JsonNode.Parse(field.Value.GetRawText());
        }
    }
}
