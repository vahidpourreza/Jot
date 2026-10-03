using System.Text.Json;

namespace Jot;
internal sealed partial class NoteStore
{
    // A content save changes one library preview, not the entire index. Keep
    // rich HTML/images out of the notification while retaining full plain text
    // for search and the exact committed file-dirty state.
    internal Task<JsonElement?> LoadNoteSummary(string id)=>Run(connection=>{
        using var command=Command(connection,null,"SELECT "+NoteColumns+" FROM notes n LEFT JOIN groups g ON g.id=n.group_id WHERE n.id=$id;",("$id",id));
        using var reader=command.ExecuteReader();
        return reader.Read()?JsonSerializer.SerializeToElement(ReadRow(reader,true)):(JsonElement?)null;
    });
}
