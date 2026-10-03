using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed partial class NoteStore
{
    internal static string[] LibraryIds(IEnumerable<string> values)
    {
        var ids=values.Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length is 0 or >1000||ids.Any(id=>!Guid.TryParse(id,out _)))
            throw new InvalidDataException("Select between 1 and 1,000 notes.");
        return ids;
    }
    internal Task<int> UpdateLibraryNotes(string[] ids,string action,bool? pinned=null,string? folder=null)=>Run(c=>{
        ids=LibraryIds(ids);
        if(action is not ("pin" or "move" or "delete"))throw new InvalidDataException("Choose a valid note action.");
        if(action=="pin"&&pinned is null)throw new InvalidDataException("Choose Pin or Unpin.");
        if(action=="move")folder=string.IsNullOrWhiteSpace(folder)?"":FolderName(folder);
        using var tx=c.BeginTransaction();
        var notes=ids.Select(id=>ReadNote(c,tx,id)??throw new InvalidDataException("A selected note no longer exists. Refresh your library and try again.")).ToArray();
        if(action=="move"&&folder!.Length>0&&Scalar(c,tx,"SELECT 1 FROM groups WHERE name=$name;",("$name",folder)) is null)
            throw new InvalidDataException("The selected folder no longer exists.");
        if(action=="delete")
        {
            // All recovery copies must reach disk before any DELETE executes.
            // A failed archive or SQL command leaves the entire selection intact.
            var trash=Path.Combine(Root,"trash");Directory.CreateDirectory(trash);
            foreach(var note in notes)
            {
                var id=note["id"]!.GetValue<string>();
                var archive=JsonSerializer.SerializeToUtf8Bytes(new{deletedAt=DateTimeOffset.UtcNow,note});
                using var stream=new FileStream(Path.Combine(trash,$"{id}-{Guid.NewGuid():N}.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough);
                stream.Write(archive);stream.Flush(true);
            }
        }
        foreach(var note in notes)
        {
            var id=note["id"]!.GetValue<string>();
            if(action=="pin")
            {
                var extra=JsonNode.Parse(Scalar(c,tx,"SELECT extra FROM notes WHERE id=$id;",("$id",id)) as string??"{}")!.AsObject();
                extra["libraryPinned"]=pinned!.Value;
                Execute(c,tx,"UPDATE notes SET extra=$extra WHERE id=$id;",("$extra",extra.ToJsonString()),("$id",id));
            }
            else if(action=="move")Execute(c,tx,"UPDATE notes SET group_id=$group WHERE id=$id;",("$group",GroupId(c,tx,folder!)),("$id",id));
            else Execute(c,tx,"DELETE FROM notes WHERE id=$id;",("$id",id));
        }
        if(action=="delete")Execute(c,tx,"UPDATE app_state SET active_id=(SELECT id FROM notes ORDER BY position,id LIMIT 1) WHERE singleton=1 AND active_id NOT IN (SELECT id FROM notes);");
        tx.Commit();return ids.Length;
    },true);
}
