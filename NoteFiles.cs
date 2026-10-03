using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;
public partial class MainWindow
{
    internal string? TestOpenNoteFilePath,TestSaveNoteFilePath;
    internal Task<string> OpenNoteFile(string path)=>session.RunNoteFileOperation(async()=>
    {
            path=Path.GetFullPath(path);var file=await NoteFileFormat.Read(path);var existing=await store.FindNoteFile(path);string id;
            if(existing is {} cached)
            {
                id=cached.GetProperty("id").GetString()!;var latest=await session.ReadLatestNote(id);var binding=latest.GetProperty("noteFile");
                if(binding.GetProperty("digest").GetString()!=file.Digest)
                {
                    if(session.Windows.Any(window=>window.ContainsNote(id))||(await store.LoadFileState(id))?.GetProperty("dirty").GetBoolean()==true)
                        throw new IOException("This file changed outside Jot and its note is open or has unsaved file edits. Use Save as to keep your edits first.");
                    await store.ImportNoteFile(file,path,id);
                }
            }
            else id=await store.ImportNoteFile(file,path);
            await session.Changed(false,id);await session.OpenAcceptedNoteFile(id);await session.NotifyFileState(id);return id;
    });
    private async Task<string?> ChooseNoteFile()
    {
        var path=TestOpenNoteFilePath;
        if(!testing){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Jot and text notes (*.jot;*.txt)|*.jot;*.txt|Jot note (*.jot)|*.jot|Plain text (*.txt)|*.txt",CheckFileExists=true,Multiselect=false,Title="Open note"};if(dialog.ShowDialog(this)!=true)return null;path=dialog.FileName;}
        return path is null?null:await OpenNoteFile(path);
    }
    internal Task<string?> SaveNoteFile(string id,bool saveAs=false,bool automatic=false)=>session.RunNoteFileOperation<string?>(async()=>
    {
        if(!automatic&&Mode!="home"&&NoteId!=id)throw new InvalidDataException("Save a note from its own editor.");
            // The idle timer follows the library-save acknowledgement. Export
            // that immutable snapshot without locking/focusing a live editor.
            // Close/Quit explicitly flush editors before calling this path.
            var note=automatic?(await store.LoadNote(id)??throw new InvalidDataException("Note not found.")):await session.ReadLatestNote(id);
            JsonElement? binding=note.TryGetProperty("noteFile",out var source)?source:null;
            var path=!saveAs&&binding is {} file?file.GetProperty("path").GetString():null;
            var expectedDigest=!saveAs&&binding is {} linked?linked.GetProperty("digest").GetString():null;
            if(path is null)
            {
                if(automatic)return null; // Never open a save dialog from a timer or shutdown.
                if(testing)path=TestSaveNoteFilePath;
                else
                {
                    var title=note.GetProperty("title").GetString();if(string.IsNullOrWhiteSpace(title))title="Note";
                    foreach(var c in Path.GetInvalidFileNameChars())title=title.Replace(c,'_');
                    var suggested=binding is {} previous&&previous.TryGetProperty("path",out var priorPath)?Path.GetFileName(priorPath.GetString()):title[..Math.Min(60,title.Length)]+".jot";
                    var textFile=string.Equals(Path.GetExtension(suggested),".txt",StringComparison.OrdinalIgnoreCase);
                    var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Jot note (*.jot)|*.jot|Plain text (*.txt)|*.txt",FilterIndex=textFile?2:1,DefaultExt=textFile?".txt":".jot",AddExtension=true,OverwritePrompt=true,FileName=suggested,Title="Save note as"};
                    if(dialog.ShowDialog(this)!=true)return null;path=dialog.FileName;
                }
                if(path is null)return null;
            }
            path=Path.GetFullPath(path);var format=NoteFileFormat.Format(path);
            var other=await store.FindNoteFile(path);
            if(other is {} linkedNote&&linkedNote.GetProperty("id").GetString()!=id)throw new IOException("This file is already linked to another note in Jot. Choose a different name.");
            var exportNote=NoteStore.FileDocument(note,await store.LoadPreferences(),path,format);
            var bytes=NoteFileFormat.Encode(exportNote,format);var digest=await NoteFileFormat.Write(path,bytes,expectedDigest);
            try{await store.BindNoteFile(id,path,format,digest,NoteFileFormat.ContentDigest(exportNote,format));}
            catch(Exception error){throw new IOException("The file was saved, but Jot could not remember its location. Open it again or use Save as.",error);}
            session.ClearFileSaveError(id);await session.Changed(false,id);await session.NotifyFileState(id);return path;
    },allowClosing:automatic);
}
