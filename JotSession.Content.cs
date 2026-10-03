using System.Text.Json;

namespace Jot;
internal sealed partial class JotSession
{
    internal async Task NoteContentChanged(string id)
    {
        if(await Store.LoadNoteSummary(id) is not {} note)return;
        foreach(var window in Windows.ToArray())
            if(window.Mode=="home")window.Post(new{@event="note-content",note});
        if(note.TryGetProperty("noteFile",out var file)&&file.ValueKind==JsonValueKind.Object&&file.TryGetProperty("path",out var path)&&path.ValueKind==JsonValueKind.String)
            await FileNoteChanged(id,JsonSerializer.SerializeToElement(new{id,title=note.GetProperty("title").GetString(),path=path.GetString(),name=System.IO.Path.GetFileName(path.GetString()),format=file.TryGetProperty("format",out var format)?format.GetString():null,dirty=note.TryGetProperty("fileDirty",out var dirty)&&dirty.ValueKind==JsonValueKind.True}));
        else
        {
            // Unlinked library notes never need file-status queries, file hash
            // work, or auto-save timers. Still keep native taskbar captions fresh.
            var caption=JsonSerializer.SerializeToElement(new{id,title=note.GetProperty("title").GetString(),name=(string?)null,dirty=false});
            foreach(var window in Windows.Where(window=>window.NoteId==id).ToArray())window.SetNoteFileCaption(caption);
        }
    }
}
