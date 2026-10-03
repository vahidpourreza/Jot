using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;
internal sealed partial class NoteStore
{
    internal Task<JsonElement?> FindNoteFile(string path)=>Run(connection=>{
        var id=Scalar(connection,null,"SELECT id FROM notes WHERE json_extract(extra,'$.noteFile.path')=$path COLLATE NOCASE LIMIT 1;",("$path",path)) as string;
        return id is null?(JsonElement?)null:JsonSerializer.SerializeToElement(ReadNote(connection,null,id));
    });
    internal Task<string> ImportNoteFile(NoteFileData file,string path,string? existingId=null)=>Run(connection=>{
        using var tx=connection.BeginTransaction();var note=(JsonObject)file.Note.DeepClone();
        var previous=existingId is null?null:ReadNote(connection,tx,existingId)??throw new InvalidDataException("Note not found.");
        var id=existingId??Guid.NewGuid().ToString();note["id"]=id;note["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        note["title"]??=FileTitle(path);note["group"]=previous?["group"]?.DeepClone()??JsonValue.Create("");
        // Plain text has no title metadata, so an external reload must not
        // replace the title the user chose in the library.
        if(file.Format=="txt"&&previous is not null){note["title"]=previous["title"]?.DeepClone();note["color"]=previous["color"]?.DeepClone();note["view"]=previous["view"]?.DeepClone();note["icon"]=previous["icon"]?.DeepClone();}
        note["color"]??="crimson";
        ValidateNote(JsonSerializer.SerializeToElement(note));
        if(note["title"]!.GetValue<string>().Length>140||!NoteColors.Contains(note["color"]!.GetValue<string>()))throw new InvalidDataException("This note has invalid title or color settings.");
        var digest=NoteFileFormat.ContentDigest(JsonSerializer.SerializeToElement(note),file.Format);
        note["noteFile"]=new JsonObject{["path"]=path,["format"]=file.Format,["digest"]=file.Digest,["contentDigest"]=digest};
        var position=previous is null?Convert.ToInt64(Scalar(connection,tx,"SELECT COALESCE(MIN(position),0)-1 FROM notes;")):Convert.ToInt64(Scalar(connection,tx,"SELECT position FROM notes WHERE id=$id;",("$id",id)));
        if(previous is not null)Execute(connection,tx,"DELETE FROM notes WHERE id=$id;",("$id",id));
        InsertNote(connection,tx,note,position,"crimson",ReadPreferences(connection,tx));
        var inserted=ReadNote(connection,tx,id)!;
        var extra=JsonNode.Parse(Scalar(connection,tx,"SELECT extra FROM notes WHERE id=$id;",("$id",id))!.ToString()!)!.AsObject();
        extra["noteFile"]!["contentDigest"]=NoteFileFormat.ContentDigest(FileDocument(JsonSerializer.SerializeToElement(inserted),JsonSerializer.SerializeToElement(ReadPreferences(connection,tx)),path,file.Format),file.Format);
        extra["noteFile"]!["stateVersion"]=3;extra["fileDirty"]=false;
        Execute(connection,tx,"UPDATE notes SET extra=$extra WHERE id=$id;",("$extra",extra.ToJsonString()),("$id",id));
        Execute(connection,tx,"UPDATE app_state SET initialized=1,active_id=$id WHERE singleton=1;",("$id",id));tx.Commit();return id;
    },true);
    internal Task BindNoteFile(string id,string path,string format,string digest,string contentDigest)=>Run(connection=>{
        using var tx=connection.BeginTransaction();var raw=Scalar(connection,tx,"SELECT extra FROM notes WHERE id=$id;",("$id",id)) as string??throw new InvalidDataException("Note not found.");
        var extra=JsonNode.Parse(raw)!.AsObject();extra["noteFile"]=new JsonObject{["path"]=path,["format"]=format,["digest"]=digest,["contentDigest"]=contentDigest,["stateVersion"]=3};
        Execute(connection,tx,"UPDATE notes SET extra=$extra WHERE id=$id;",("$extra",extra.ToJsonString()),("$id",id));
        RefreshFileDirty(connection,tx,id);tx.Commit();return true;
    },true);
}
