using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyDocumentFileIdentity(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="document-file-identity-"+name,passed});
        var directory=Path.Combine(testOutput,"document-file-identity");Directory.CreateDirectory(directory);
        var target=new NoteStore(Path.Combine(directory,"data"));
        async Task<JsonElement> Note(string id)=>(await target.LoadNote(id))!.Value;
        async Task<bool> Dirty(string id)=>(await target.LoadFileState(id))!.Value.GetProperty("dirty").GetBoolean();
        async Task<string> Import(string name,JsonObject note,string format="jot",string? existingId=null)
        {
            var path=Path.Combine(directory,name+"."+format);
            await File.WriteAllBytesAsync(path,NoteFileFormat.Encode(JsonSerializer.SerializeToElement(note),format));
            return await target.ImportNoteFile(await NoteFileFormat.Read(path),path,existingId);
        }
        var content=new JsonObject{["title"]="Document title · عنوان",["html"]="<p><b>Body</b></p>",["plain"]="Body",["color"]="teal"};
        var id=await Import("Different filename",content);
        Check("import-preserves-embedded-rich-title",(await Note(id)).GetProperty("title").GetString()=="Document title · عنوان"&&!await Dirty(id));
        var state=(await target.LoadFileState(id))!.Value;
        Check("state-exposes-title-and-filename-independently",state.GetProperty("title").GetString()=="Document title · عنوان"&&state.GetProperty("name").GetString()=="Different filename.jot");
        Check("new-binding-uses-title-preserving-version",(await Note(id)).GetProperty("noteFile").GetProperty("stateVersion").GetInt32()==3);
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="A new title"}));
        Check("linked-rich-title-edit-is-dirty",await Dirty(id));
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Document title · عنوان"}));
        Check("reverting-title-clears-dirty",!await Dirty(id));
        var savePath=Path.Combine(directory,"Another filename.jot");
        var export=NoteStore.FileDocument(await Note(id),await target.LoadPreferences(),savePath,"jot");
        var bytes=NoteFileFormat.Encode(export,"jot");await File.WriteAllBytesAsync(savePath,bytes);
        await target.BindNoteFile(id,savePath,"jot",NoteFileFormat.Digest(bytes),NoteFileFormat.ContentDigest(export,"jot"));
        Check("save-as-changes-path-not-title",(await Note(id)).GetProperty("title").GetString()=="Document title · عنوان"&&!await Dirty(id));
        var independent=new NoteStore(Path.Combine(directory,"independent"));
        var reopenedId=await independent.ImportNoteFile(await NoteFileFormat.Read(savePath),savePath);
        Check("portable-rich-file-roundtrips-title-into-new-library",(await independent.LoadNote(reopenedId))!.Value.GetProperty("title").GetString()=="Document title · عنوان");

        var blank=(JsonObject)content.DeepClone();blank["title"]="";
        var blankId=await Import("Intentional blank title",blank);
        Check("explicit-empty-title-is-not-invented-from-filename",(await Note(blankId)).GetProperty("title").GetString()=="");
        var missing=(JsonObject)content.DeepClone();missing.Remove("title");
        var missingId=await Import("Legacy missing title",missing);
        Check("missing-title-falls-back-to-filename",(await Note(missingId)).GetProperty("title").GetString()=="Legacy missing title");

        var textId=await Import("Plain file",content,"txt");
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=textId,title="Local plain-text title"}));
        Check("txt-title-is-library-only-and-does-not-mark-file-dirty",!await Dirty(textId));
        var external=(JsonObject)content.DeepClone();external["plain"]="External text";
        await Import("Plain file",external,"txt",textId);
        Check("txt-external-reload-retains-local-title",(await Note(textId)).GetProperty("title").GetString()=="Local plain-text title"&&(await Note(textId)).GetProperty("plain").GetString()=="External text");

        var legacyId=await target.Create();var legacyPath=Path.Combine(directory,"Version two filename.jot");
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=legacyId,title="Version two filename"}));
        await target.SaveNote(JsonSerializer.SerializeToElement(new{id=legacyId,html="<p>Old saved body</p>",plain="Old saved body"}));
        var legacyExport=NoteStore.FileDocument(await Note(legacyId),await target.LoadPreferences(),legacyPath,"jot",legacyFilenameTitle:true);
        await target.BindNoteFile(legacyId,legacyPath,"jot","legacy-external-digest",NoteFileFormat.ContentDigest(legacyExport,"jot"));
        using(var connection=target.Connect(target.FilePath))using(var command=connection.CreateCommand())
        {
            command.CommandText="UPDATE notes SET extra=json_set(extra,'$.noteFile.stateVersion',2) WHERE id=$id;";
            command.Parameters.AddWithValue("$id",legacyId);command.ExecuteNonQuery();
        }
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=legacyId,group="Unrelated folder"}));
        Check("version-two-baseline-stays-clean-with-unchanged-title",!await Dirty(legacyId));
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=legacyId,title="Independent document title"}));
        Check("version-two-title-edit-is-not-hidden-by-filename-normalization",await Dirty(legacyId));
        Check("version-two-baseline-not-upgraded-before-file-save",(await Note(legacyId)).GetProperty("noteFile").GetProperty("stateVersion").GetInt32()==2);
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=legacyId,title="Version two filename"}));
        Check("version-two-title-undo-clears-dirty",!await Dirty(legacyId));
        await target.SaveNote(JsonSerializer.SerializeToElement(new{id=legacyId,html="<p>Unsaved version-two body</p>",plain="Unsaved version-two body"}));
        Check("version-two-content-edit-remains-dirty",await Dirty(legacyId));
        await target.SaveNote(JsonSerializer.SerializeToElement(new{id=legacyId,html="<p>Old saved body</p>",plain="Old saved body"}));
        await target.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=22}));
        Check("version-two-inherited-style-edit-remains-dirty",await Dirty(legacyId));

        // A metadata change can arrive while a file snapshot is being written.
        // Binding that older snapshot must not mark the newer title clean.
        var earlier=NoteStore.FileDocument(await Note(id),await target.LoadPreferences(),savePath,"jot");
        await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Title edited during file write"}));
        await target.BindNoteFile(id,savePath,"jot","earlier-external-digest",NoteFileFormat.ContentDigest(earlier,"jot"));
        Check("late-binding-preserves-concurrent-title-and-dirty-state",(await Note(id)).GetProperty("title").GetString()=="Title edited during file write"&&await Dirty(id));

        foreach(var version in new[]{1,2})
        {
            var oldId=await target.Create();var oldPath=Path.Combine(directory,"Legacy icon "+version+".jot");
            await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=oldId,title=NoteStore.FileTitle(oldPath)}));
            var oldNote=await Note(oldId);
            var oldComparable=version==2?NoteStore.FileDocument(oldNote,await target.LoadPreferences(),oldPath,"jot",true):oldNote;
            var oldDefault=JsonNode.Parse(oldComparable.GetRawText())!.AsObject();oldDefault["icon"]="icon:notepad-text";
            await target.BindNoteFile(oldId,oldPath,"jot","old-byte-digest",NoteFileFormat.ContentDigest(JsonSerializer.SerializeToElement(oldDefault),"jot"));
            using(var connection=target.Connect(target.FilePath))using(var command=connection.CreateCommand())
            {
                command.CommandText="UPDATE notes SET extra=json_remove(json_set(extra,'$.noteFile.stateVersion',$version),'$.icon','$.fileDirty') WHERE id=$id;";
                command.Parameters.AddWithValue("$id",oldId);command.Parameters.AddWithValue("$version",version);command.ExecuteNonQuery();
            }
            Check("legacy-omitted-icon-stays-clean-version-"+version,!await Dirty(oldId));
            await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=oldId,icon="emoji:🔥"}));
            Check("legacy-new-emoji-is-not-falsely-clean-version-"+version,await Dirty(oldId));
            await target.SaveMetadata(JsonSerializer.SerializeToElement(new{id=oldId,icon="emoji:📝"}));
            Check("legacy-default-emoji-revert-is-clean-version-"+version,!await Dirty(oldId));
        }
    }
}
