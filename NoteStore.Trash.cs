using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Jot;

internal sealed partial class NoteStore
{
    private string TrashDirectory=>Path.Combine(Root,"trash");
    private const long TrashArchiveLimit=64L*1024*1024;
    private static bool IsTrashKey(string value)
    {
        // Early imported notes could use N/B/P-form GUIDs. Accept those safe
        // historical basenames; new archives always use canonical D-form IDs.
        var prefix=value.Length-38;
        return prefix is 32 or 36 or 38&&value[prefix]=='-'&&value.EndsWith(".json",StringComparison.Ordinal)
            &&Guid.TryParse(value[..prefix],out _)&&Guid.TryParseExact(value.Substring(prefix+1,32),"N",out _);
    }
    private void WriteTrashArchive(JsonObject note)
    {
        CheckTrashDirectory();var archive=JsonSerializer.SerializeToUtf8Bytes(new{deletedAt=DateTimeOffset.UtcNow,note});
        if(archive.LongLength>TrashArchiveLimit)throw new InvalidDataException("This note is too large to move to Trash safely. It has been kept in your library.");
        Directory.CreateDirectory(TrashDirectory);
        var path=Path.Combine(TrashDirectory,$"{Guid.Parse(note["id"]!.GetValue<string>()):D}-{Guid.NewGuid():N}.json");
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough);
        stream.Write(archive);stream.Flush(true);
    }
    internal static string[] TrashKeys(IEnumerable<string> values)
    {
        var keys=values.Distinct(StringComparer.Ordinal).ToArray();
        if(keys.Length is 0 or >1000||keys.Any(key=>key is null||!IsTrashKey(key)))throw new InvalidDataException("Select between 1 and 1,000 items from Trash.");
        return keys;
    }
    private void CheckTrashDirectory()
    {
        if(File.Exists(TrashDirectory))throw new IOException("Jot could not open Trash. Its local folder is unavailable.");
        if(Directory.Exists(TrashDirectory)&&(File.GetAttributes(TrashDirectory)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Jot will not follow a redirected Trash folder.");
    }
    private string TrashPath(string key)
    {
        if(!IsTrashKey(key))throw new InvalidDataException("Invalid Trash item.");
        CheckTrashDirectory();var path=Path.GetFullPath(Path.Combine(TrashDirectory,key));
        if(!string.Equals(Path.GetDirectoryName(path),TrashDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid Trash path.");
        if(!File.Exists(path))throw new FileNotFoundException("A selected Trash item no longer exists. Refresh and try again.");
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Jot will not follow a redirected Trash file.");
        return path;
    }
    private static HashSet<string> RestoredTrashKeys(SqliteConnection connection)
    {
        var keys=new HashSet<string>(StringComparer.Ordinal);
        if(Scalar(connection,null,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='trash_restores';") is null)return keys;
        using var command=Command(connection,null,"SELECT archive_id FROM trash_restores;");using var reader=command.ExecuteReader();
        while(reader.Read())keys.Add(reader.GetString(0));return keys;
    }
    private string[] AvailableTrashKeys(SqliteConnection connection)
    {
        CheckTrashDirectory();if(!Directory.Exists(TrashDirectory))return [];
        var restored=RestoredTrashKeys(connection);
        return Directory.EnumerateFiles(TrashDirectory,"*.json",SearchOption.TopDirectoryOnly).Select(Path.GetFileName)
            .Where(key=>key is not null&&IsTrashKey(key)&&!restored.Contains(key)).Select(key=>key!).ToArray();
    }
    private (JsonObject Note,long DeletedAt) ReadTrashNote(string key)
    {
        var path=TrashPath(key);
        if(new FileInfo(path).Length>TrashArchiveLimit)throw new InvalidDataException("This recovery copy is too large to restore automatically.");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var document=JsonDocument.Parse(stream);
        var archive=document.RootElement;
        if(archive.ValueKind!=JsonValueKind.Object||!archive.TryGetProperty("deletedAt",out var date)||!date.TryGetDateTimeOffset(out var deleted)
            ||!archive.TryGetProperty("note",out var value))throw new InvalidDataException("This recovery copy is unreadable.");
        ValidateNote(value);var note=JsonNode.Parse(value.GetRawText())!.AsObject();
        if(Guid.Parse(note["id"]!.GetValue<string>())!=Guid.Parse(key[..(key.Length-38)]))throw new InvalidDataException("This recovery copy has an invalid note identifier.");
        foreach(var (field,limit) in new[]{("title",140),("group",64)})
            if((note[field]?.GetValue<string>()??"").Length>limit)throw new InvalidDataException("This recovery copy has invalid note metadata.");
        if(note["color"] is not null&&!NoteColors.Contains(note["color"]!.GetValue<string>()))throw new InvalidDataException("This recovery copy has an invalid note color.");
        if(note["icon"] is not null)ValidateNoteIcon(note["icon"]!.GetValue<string>());
        if(note["libraryPinned"] is JsonValue pin&&!pin.TryGetValue<bool>(out _))throw new InvalidDataException("This recovery copy has invalid note settings.");
        if(note["view"] is JsonObject view)ApplyNotePreferencePatch(new JsonObject(),JsonSerializer.SerializeToElement(ReadNotePreferences(view.ToJsonString())));
        return(note,deleted.ToUnixTimeMilliseconds());
    }
    internal Task<int> CountTrash()=>Run(connection=>AvailableTrashKeys(connection).Length);
    internal Task<JsonElement> LoadTrash()=>Run(connection=>{
        var items=new List<JsonObject>();
        foreach(var key in AvailableTrashKeys(connection))
        {
            try
            {
                var (note,deletedAt)=ReadTrashNote(key);
                items.Add(new JsonObject{["key"]=key,["title"]=note["title"]?.DeepClone(),["legacyTitle"]=note["legacyTitle"]?.DeepClone(),
                    ["plain"]=(note["plain"]?.GetValue<string>()??"")[..Math.Min(320,(note["plain"]?.GetValue<string>()??"").Length)],
                    ["group"]=note["group"]?.DeepClone(),["icon"]=NormalizeNoteIcon(note["icon"]?.GetValue<string>()),["color"]=note["color"]?.DeepClone(),
                    ["deletedAt"]=deletedAt,["canRestore"]=true,["hadFile"]=note["noteFile"] is JsonObject});
            }
            catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or ArgumentException)
            {
                // Keep damaged archives visible and explicitly removable. Never
                // guess their contents or import them into the live database.
                items.Add(new JsonObject{["key"]=key,["title"]="Unreadable recovery copy",["plain"]="This item cannot be restored. The recovery file has been kept.",["deletedAt"]=0L,["canRestore"]=false});
            }
        }
        return JsonSerializer.SerializeToElement(items.OrderByDescending(item=>item["deletedAt"]!.GetValue<long>()));
    });
    internal Task<int> RestoreTrash(IEnumerable<string> values)=>Run(connection=>{
        var keys=TrashKeys(values);var restored=RestoredTrashKeys(connection);
        // Validate every selected archive before inserting any of them.
        var notes=keys.Where(key=>!restored.Contains(key)).Select(key=>(Key:key,Note:ReadTrashNote(key).Note)).ToArray();
        using(var tx=connection.BeginTransaction())
        {
            Execute(connection,tx,"CREATE TABLE IF NOT EXISTS trash_restores(archive_id TEXT PRIMARY KEY NOT NULL,note_id TEXT NOT NULL);");
            var defaults=ReadPreferences(connection,tx);var position=Convert.ToInt64(Scalar(connection,tx,"SELECT COALESCE(MIN(position),0)-1 FROM notes;"));
            foreach(var (key,note) in notes)
            {
                var id=note["id"]!.GetValue<string>();
                if(Scalar(connection,tx,"SELECT 1 FROM notes WHERE id=$id;",("$id",id)) is not null)id=Guid.NewGuid().ToString();
                note["id"]=id;
                // Restoring a library note is not permission to overwrite an
                // external file. Save As can reconnect it deliberately later.
                note.Remove("noteFile");note.Remove("fileDirty");note.Remove("tabPinned");
                InsertNote(connection,tx,note,position--,"neutral",defaults);
                Execute(connection,tx,"INSERT INTO trash_restores(archive_id,note_id) VALUES($key,$id);",("$key",key),("$id",id));
            }
            Execute(connection,tx,"UPDATE app_state SET initialized=1 WHERE singleton=1;");tx.Commit();
        }
        // A durable SQL receipt prevents duplicate restores if the process exits
        // between COMMIT and archive removal, or Windows temporarily locks it.
        foreach(var key in keys)try{File.Delete(TrashPath(key));}catch(IOException){}catch(UnauthorizedAccessException){}
        return notes.Length;
    },true);
    internal Task<int> DeleteTrash(IEnumerable<string> values)=>Run(connection=>{
        var keys=TrashKeys(values);var restored=RestoredTrashKeys(connection);
        if(keys.Any(restored.Contains))throw new InvalidDataException("A selected item was already restored. Refresh Trash and try again.");
        var paths=keys.Select(TrashPath).ToArray();var count=0;
        foreach(var path in paths)
        {
            try{File.Delete(path);count++;}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {throw new IOException($"Deleted {count} of {keys.Length} Trash items. The remaining recovery copies were kept. Close apps using them and try again.",error);}
        }
        return count;
    },true);
}
