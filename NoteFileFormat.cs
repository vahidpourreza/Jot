using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed record NoteFileData(JsonObject Note,string Format,string Digest);
internal static class NoteFileFormat
{
    internal const int CurrentVersion=2;
    internal const int MaxBytes=32*1024*1024;
    internal static string Format(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{
        ".jot"=>"jot",".txt"=>"txt",_=>throw new InvalidDataException("Choose a .jot or .txt file.")};
    internal static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static async Task<NoteFileData> Read(string path)
    {
        var format=Format(path);
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
        if(input.Length>MaxBytes)throw new InvalidDataException("This file is larger than 32 MB.");
        using var memory=new MemoryStream();var buffer=new byte[81920];int read;
        while((read=await input.ReadAsync(buffer))>0){if(memory.Length+read>MaxBytes)throw new InvalidDataException("This file is larger than 32 MB.");await memory.WriteAsync(buffer.AsMemory(0,read));}
        var bytes=memory.ToArray();JsonObject note;
        if(format=="txt")
        {
            using var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),true);
            var plain=(await reader.ReadToEndAsync()).Replace("\r\n","\n").Replace('\r','\n');
            if(plain.Contains('\0'))throw new InvalidDataException("This does not appear to be a text file.");
            // Literal newlines in one pre-wrapped text block round-trip exactly.
            // Empty <div><br></div> paragraphs would add extra innerText breaks.
            var html="<div dir=\"auto\">"+WebUtility.HtmlEncode(plain)+"</div>";
            var title=Path.GetFileNameWithoutExtension(path);if(title.Length>140)title=title[..140];
            note=new(){["title"]=title,["html"]=html,["plain"]=plain,["color"]="crimson"};
        }
        else
        {
            JsonObject document;
            try{document=JsonNode.Parse(bytes,new JsonNodeOptions(),new JsonDocumentOptions{MaxDepth=64})?.AsObject()??throw new JsonException();}
            catch(Exception error) when(error is JsonException or InvalidOperationException){throw new InvalidDataException("This is not a valid Jot note file.",error);}
            if(document["format"]?.ToString()!="jot-note"||document["version"] is not JsonValue version||!version.TryGetValue<int>(out var number)||number is <1 or >CurrentVersion||document["note"] is not JsonObject source)
                throw new InvalidDataException("This Jot file format is not supported.");
            note=new();
            foreach(var key in new[]{"title","html","plain","color","view","icon"})if(source[key] is {} value)note[key]=value.DeepClone();
            foreach(var key in new[]{"title","color"})if(note[key] is {} metadata&&(metadata is not JsonValue textValue||!textValue.TryGetValue<string>(out _)))throw new InvalidDataException("This Jot note has invalid metadata.");
            if(note["icon"] is {} icon){if(icon is not JsonValue choice||!choice.TryGetValue<string>(out var chosen))throw new InvalidDataException("Invalid note icon.");NoteStore.ValidateNoteIcon(chosen);}
            if(note["view"] is not null and not JsonObject)throw new InvalidDataException("This Jot note has invalid writing settings.");
            if(note["html"] is not JsonValue html||!html.TryGetValue<string>(out _)||note["plain"] is not JsonValue text||!text.TryGetValue<string>(out _))
                throw new InvalidDataException("This Jot note is missing its text.");
        }
        return new(note,format,Digest(bytes));
    }
    internal static byte[] Encode(JsonElement note,string format)=>EncodeVersion(note,format,CurrentVersion);
    private static byte[] EncodeVersion(JsonElement note,string format,int version)
    {
        if(format is not ("txt" or "jot"))throw new InvalidDataException("Unsupported note file format.");
        if(format=="txt")return new UTF8Encoding(false).GetBytes(note.GetProperty("plain").GetString()??"");
        var data=new JsonObject();
        foreach(var key in new[]{"title","html","plain","color","view","icon"})if(note.TryGetProperty(key,out var value)){
            // Legacy files omitted this icon. Keep their byte/content digest
            // stable even though new notes now use the notepad emoji.
            if(key=="icon"&&(value.ValueKind!=JsonValueKind.String||value.GetString()=="icon:notepad-text"))continue;
            data[key]=JsonNode.Parse(value.GetRawText());
        }
        return JsonSerializer.SerializeToUtf8Bytes(new JsonObject{["format"]="jot-note",["version"]=version,["note"]=data},new JsonSerializerOptions{WriteIndented=true});
    }
    // A content fingerprint is independent of the file envelope version. Keep
    // its historical encoding so opening/upgrading a clean v1 file does not
    // mark it dirty or silently trigger an automatic file save. Actual saved
    // bytes use v2 above, which older Jot readers already reject safely.
    internal static string ContentDigest(JsonElement note,string format)=>Digest(EncodeVersion(note,format,1));
    internal static async Task<string> Write(string path,byte[] bytes,string? expectedDigest,Func<Task>? testBeforeCommit=null)
    {
        if(bytes.Length>MaxBytes)throw new InvalidDataException("This note is too large for a standalone file (32 MB maximum).");
        path=Path.GetFullPath(path);
        var directory=Path.GetDirectoryName(path)!;
        var temporary=Path.Combine(directory,".jot-save-"+Guid.NewGuid().ToString("N")+".tmp");
        Exception? saveFailure=null;
        try
        {
            await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {await stream.WriteAsync(bytes);await stream.FlushAsync();stream.Flush(true);}
            if(expectedDigest is null)File.Move(temporary,path,true); // Explicit Save as / first save.
            else
            {
                // Deny in-place writers from the comparison through commit.
                // Delete sharing is necessary for the atomic replacement; it
                // also permits another editor to swap the path, so retain and
                // verify the file Replace actually displaced before discarding it.
                await using var original=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
                if(original.Length>MaxBytes||Convert.ToHexString(await SHA256.HashDataAsync(original))!=expectedDigest)
                    throw new IOException("This file changed outside Jot. Use Save as to keep both versions.");
                if(testBeforeCommit is not null)await testBeforeCommit();
                var recovery=Path.Combine(directory,".jot-recovery-"+Guid.NewGuid().ToString("N")+Path.GetExtension(path));
                bool verified=false;
                try
                {
                    // Never fall back to an unguarded overwrite if Replace is
                    // unsupported or rejected by permissions on this volume.
                    File.Replace(temporary,path,recovery);
                    await using(var displaced=new FileStream(recovery,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,81920,FileOptions.Asynchronous|FileOptions.SequentialScan))
                    {
                        if(displaced.Length>MaxBytes||Convert.ToHexString(await SHA256.HashDataAsync(displaced))!=expectedDigest)
                        {
                            var conflict=new IOException("This file changed during saving. The external version is preserved at \""+recovery+"\". Jot's version is at the original location. Use Save as before continuing.");
                            conflict.Data["recoveryPath"]=recovery;
                            throw conflict;
                        }
                    }
                    verified=true;
                    File.Delete(recovery);
                }
                catch(Exception error) when(File.Exists(recovery)&&!error.Data.Contains("recoveryPath"))
                {
                    var failure=new IOException((verified?"The file was saved, but its recovery copy could not be removed. ":"The save could not be verified. ")+"A recovery copy is preserved at \""+recovery+"\". Use Save as before continuing.",error);
                    failure.Data["recoveryPath"]=recovery;
                    throw failure;
                }
            }
            return Digest(bytes);
        }
        catch(Exception error){saveFailure=error;throw;}
        finally
        {
            if(File.Exists(temporary))try{File.Delete(temporary);}
            catch(Exception) when(saveFailure is not null){saveFailure.Data["temporaryPath"]=temporary;}
        }
    }
}
