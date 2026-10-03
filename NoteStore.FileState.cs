using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

internal sealed partial class NoteStore
{
    internal static string FileTitle(string path){var value=Path.GetFileNameWithoutExtension(path);return value[..Math.Min(140,value.Length)];}
    internal static JsonElement FileDocument(JsonElement note,JsonElement defaults,string path,string format,bool legacyFilenameTitle=false)
    {
        var document=JsonNode.Parse(note.GetRawText())!.AsObject();
        // Version 2 bindings exported the filename as title. Keep that exact
        // comparison only for their old baseline; new files own their title.
        if(legacyFilenameTitle)document["title"]=FileTitle(path);
        if(format=="jot"){
            var view=document["view"] as JsonObject??new JsonObject();
            foreach(var key in new[]{"fontSize","lineHeight"})if(view[key] is null)view[key]=JsonNode.Parse(defaults.GetProperty(key).GetRawText());
            if(document["view"] is null)document["view"]=view;
        }
        return JsonSerializer.SerializeToElement(document);
    }
    private static void RefreshFileDirty(SqliteConnection connection,SqliteTransaction tx,string id)
    {
        var raw=Scalar(connection,tx,"SELECT extra FROM notes WHERE id=$id;",("$id",id)) as string;
        if(raw is null)return;
        var extra=JsonNode.Parse(raw)!.AsObject();if(extra["noteFile"] is not JsonObject file)return;
        if(file["path"] is null||file["format"] is null||file["contentDigest"] is null)return;
        var format=file["format"]!.GetValue<string>();JsonElement comparable;
        if(format=="txt")
            comparable=JsonSerializer.SerializeToElement(new{plain=Scalar(connection,tx,"SELECT plain FROM notes WHERE id=$id;",("$id",id)) as string??""});
        else
        {
            var note=JsonSerializer.SerializeToElement(ReadNote(connection,tx,id)!);
            var stateVersion=file["stateVersion"]?.GetValue<int>()??1;
            comparable=stateVersion>=2?FileDocument(note,JsonSerializer.SerializeToElement(ReadPreferences(connection,tx)),file["path"]!.GetValue<string>(),format,legacyFilenameTitle:stateVersion==2):note;
        }
        var dirty=NoteFileFormat.ContentDigest(comparable,format)!=file["contentDigest"]!.GetValue<string>();
        if(dirty&&format=="jot"&&(file["stateVersion"]?.GetValue<int>()??1)<=2&&comparable.TryGetProperty("icon",out var icon)&&icon.GetString()=="emoji:📝")
        {
            // An absent old icon now displays the new default emoji. Old
            // explicit 📝 exports included it; old default exports omitted it.
            // Accept either historical baseline without changing it on disk.
            var legacy=JsonNode.Parse(comparable.GetRawText())!.AsObject();legacy["icon"]="icon:notepad-text";
            dirty=NoteFileFormat.ContentDigest(JsonSerializer.SerializeToElement(legacy),format)!=file["contentDigest"]!.GetValue<string>();
        }
        // A title edit must not disappear behind v2's filename normalization.
        // Do not rewrite its baseline until a successful explicit/automatic save.
        if(format=="jot"&&file["stateVersion"]?.GetValue<int>()==2)
            dirty|=(Scalar(connection,tx,"SELECT title FROM notes WHERE id=$id;",("$id",id)) as string??"")!=FileTitle(file["path"]!.GetValue<string>());
        if(extra["fileDirty"] is JsonValue existing&&existing.TryGetValue<bool>(out var previous)&&previous==dirty)return;
        extra["fileDirty"]=dirty;
        Execute(connection,tx,"UPDATE notes SET extra=$extra WHERE id=$id;",("$extra",extra.ToJsonString()),("$id",id));
    }
    private static void RefreshAllFileDirty(SqliteConnection connection,SqliteTransaction tx)
    {
        var ids=new List<string>();using(var command=Command(connection,tx,"SELECT id FROM notes WHERE json_extract(extra,'$.noteFile.path') IS NOT NULL;"))using(var reader=command.ExecuteReader())while(reader.Read())ids.Add(reader.GetString(0));
        foreach(var id in ids)RefreshFileDirty(connection,tx,id);
    }
    private static void InitializeMissingFileDirty(SqliteConnection connection,string? onlyId=null)
    {
        // Older releases kept the last exported content digest, but no cached
        // dirty flag. Compare with that legacy baseline before showing a note
        // as clean. Do not canonicalize its title or upgrade the digest here:
        // old baselines intentionally used the raw library note, not the file's
        // name or a snapshot of inherited writing defaults.
        using var transaction=connection.BeginTransaction();
        var ids=new List<string>();
        using(var command=Command(connection,transaction,"""
            SELECT id FROM notes
            WHERE ($id IS NULL OR id=$id)
              AND json_type(extra,'$.noteFile.path')='text'
              AND json_extract(extra,'$.noteFile.format') IN ('jot','txt')
              AND json_type(extra,'$.noteFile.contentDigest')='text'
              AND (json_type(extra,'$.fileDirty') IS NULL OR json_type(extra,'$.fileDirty')='null');
            """,("$id",onlyId)))
        using(var reader=command.ExecuteReader())while(reader.Read())ids.Add(reader.GetString(0));
        foreach(var id in ids)RefreshFileDirty(connection,transaction,id);
        transaction.Commit();
    }
    internal Task InitializeFileDirtyState()=>Run(connection=>{InitializeMissingFileDirty(connection);return true;},true);
    internal Task<JsonElement?> LoadFileState(string id)=>Run(connection=>{
        string title;JsonObject extra;
        using(var command=Command(connection,null,"SELECT title,extra FROM notes WHERE id=$id;",("$id",id)))
        using(var reader=command.ExecuteReader()){
            if(!reader.Read())return (JsonElement?)null;
            title=reader.GetString(0);extra=JsonNode.Parse(reader.GetString(1))!.AsObject();
        }
        if(extra["noteFile"] is JsonObject&&extra["fileDirty"] is null){
            InitializeMissingFileDirty(connection,id);
            extra=JsonNode.Parse(Scalar(connection,null,"SELECT extra FROM notes WHERE id=$id;",("$id",id))!.ToString()!)!.AsObject();
        }
        var file=extra["noteFile"] as JsonObject;var path=file?["path"]?.GetValue<string>();
        return JsonSerializer.SerializeToElement(new{id,title,path,name=path is null?null:Path.GetFileName(path),format=file?["format"]?.GetValue<string>(),dirty=extra["fileDirty"]?.GetValue<bool>()==true});
    },true);
    internal Task<string[]> FileNoteIds()=>Run(connection=>{
        var ids=new List<string>();using var command=Command(connection,null,"SELECT id FROM notes WHERE json_extract(extra,'$.noteFile.path') IS NOT NULL;");using var reader=command.ExecuteReader();while(reader.Read())ids.Add(reader.GetString(0));return ids.ToArray();
    });
}
