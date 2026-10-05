using System.IO;
using System.Text;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyNoteFiles(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="note-files-"+name,passed});
        static void Cleanup(JotSession target){foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        const string ready="window.jotReady===true&&!app.inert&&!editorLockedByHost&&editor.isContentEditable";
        var fixtures=Path.Combine(testOutput,"note-file-format-fixtures");Directory.CreateDirectory(fixtures);
        bool invalidRejected=true;
        foreach(var item in new[]{
            ("unsupported.html","<p>Not a supported note file</p>"),
            ("invalid.jot","{broken"),
            ("future.jot","{\"format\":\"jot-note\",\"version\":3,\"note\":{\"html\":\"\",\"plain\":\"\"}}"),
            ("missing-text.jot","{\"format\":\"jot-note\",\"version\":1,\"note\":{\"html\":4,\"plain\":\"\"}}"),
            ("binary.txt","not\0text")})
        {
            var path=Path.Combine(fixtures,item.Item1);await File.WriteAllTextAsync(path,item.Item2,new UTF8Encoding(false));
            try{await NoteFileFormat.Read(path);invalidRejected=false;}catch(InvalidDataException){}
        }
        Check("unsupported-malformed-future-and-binary-files-rejected",invalidRejected);
        var oversized=Path.Combine(fixtures,"oversized.jot");
        await using(var stream=new FileStream(oversized,FileMode.Create,FileAccess.Write))stream.SetLength((long)NoteFileFormat.MaxBytes+1);
        bool oversizedRejected=false;try{await NoteFileFormat.Read(oversized);}catch(InvalidDataException){oversizedRejected=true;}
        Check("oversized-file-rejected-before-parsing",oversizedRejected);

        foreach(var mode in new[]{"tab","window"})
        {
            var root=Path.Combine(testOutput,"note-files-"+mode);var folder=Path.Combine(root,"saved notes");Directory.CreateDirectory(folder);
            var session=new JotSession(true,Path.Combine(root,"store")){ExerciseLifecycle=true};
            try
            {
                await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode}));
                var home=session.Home();await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
                var malformed=Path.Combine(folder,"invalid metadata.jot");
                await File.WriteAllTextAsync(malformed,"{\"format\":\"jot-note\",\"version\":1,\"note\":{\"title\":42,\"html\":\"<p>Invalid</p>\",\"plain\":\"Invalid\",\"color\":\"teal\"}}");
                bool malformedRejected=false;
                try{await home.OpenNoteFile(malformed);}catch(Exception error) when(error is InvalidDataException or InvalidOperationException){malformedRejected=error is InvalidDataException;}
                Check("malformed-metadata-is-rejected-without-importing-"+mode,malformedRejected&&Convert.ToInt64(await StoreScalar(session.Store,"SELECT COUNT(*) FROM notes;"))==0);
                var image=JsonSerializer.Deserialize<string>(await home.Script("(()=>{const canvas=document.createElement('canvas');canvas.width=12;canvas.height=6;canvas.getContext('2d').fillRect(0,0,12,6);return canvas.toDataURL()})()"))!;
                const string title="Project review · گزارش روزانه";
                var html="<p><b>Important</b> <span style=\"color:#e11d48\">review</span></p><p><img src=\""+image+"\"></p>";
                var source=Path.Combine(folder,"یادداشت پروژه original.jot");var sourceId=Guid.NewGuid().ToString();
                await File.WriteAllBytesAsync(source,JsonSerializer.SerializeToUtf8Bytes(new{format="jot-note",version=1,note=new{
                    id=sourceId,title,html,plain="Important review\n",color="teal",view=new{fontSize=21,lineHeight=2.2,toolbarVisible=false},
                    group="Not imported",noteFile=new{path=Path.Combine(root,"ignored destination.jot"),format="jot",digest="ignored"}}}));
                home.TestOpenNoteFilePath=source;var id=(await home.ChooseNoteFile())!;
                var editor=session.Windows.Single(window=>window.ContainsNote(id));await editor.WaitFor(ready);
                var imported=(await session.Store.LoadNote(id))!.Value;
                Check("rich-open-preserves-content-and-obeys-"+mode,
                    id!=sourceId&&imported.GetProperty("title").GetString()==title&&imported.GetProperty("html").GetString()!.Contains(image)&&
                    imported.GetProperty("group").GetString()==""&&imported.GetProperty("color").GetString()=="teal"&&
                    imported.GetProperty("view").GetProperty("fontSize").GetInt32()==21&&imported.GetProperty("noteFile").GetProperty("path").GetString()==source&&
                    editor.Mode==(mode=="tab"?"home":"note")&&session.Windows.Count(window=>window.ContainsNote(id))==1&&
                    await editor.Script("!!editor.querySelector('b')&&!!editor.querySelector('img')&&model.prefs.fontSize===21&&model.prefs.lineHeight===2.2&&!keepFormatOpen")=="true");
                var saved=Path.Combine(folder,"Review copy نسخه جدید.jot");editor.TestSaveNoteFilePath=saved;
                var savedPath=await editor.SaveNoteFile(id,true);var richFile=await NoteFileFormat.Read(saved);
                var linked=(await session.Store.LoadNote(id))!.Value;
                Check("rich-save-as-preserves-images-style-and-binding-"+mode,
                    savedPath==saved&&richFile.Note["title"]!.GetValue<string>()==title&&richFile.Note["html"]!.GetValue<string>().Contains(image)&&
                    richFile.Note["html"]!.GetValue<string>().Contains("<b>")&&richFile.Note["color"]!.GetValue<string>()=="teal"&&
                    richFile.Note["view"]!["fontSize"]!.GetValue<int>()==21&&!richFile.Note.ContainsKey("id")&&!richFile.Note.ContainsKey("noteFile")&&
                    linked.GetProperty("noteFile").GetProperty("path").GetString()==saved);
                var savedBytes=await File.ReadAllBytesAsync(saved);
                await editor.Script("editor.innerHTML='<p><b>Local draft</b> stays in the library until Save.</p>';onEdit();clearTimeout(saveTimer)");await editor.Flush();
                Check("library-autosave-does-not-overwrite-standalone-file-"+mode,
                    (await File.ReadAllBytesAsync(saved)).SequenceEqual(savedBytes)&&(await session.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="Local draft stays in the library until Save.");
                var opened=await Task.WhenAll(home.OpenNoteFile(saved),home.OpenNoteFile(saved));
                Check("repeated-file-open-retains-draft-and-one-owner-"+mode,
                    opened.All(value=>value==id)&&session.Windows.Count(window=>window.ContainsNote(id))==1&&
                    (await session.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==1&&await editor.Script("editor.textContent==='Local draft stays in the library until Save.'")=="true");

                var externalBytes=NoteFileFormat.Encode(JsonSerializer.SerializeToElement(new{title="External edit",html="<p>Edited outside Jot</p>",plain="Edited outside Jot",color="teal"}),"jot");
                await File.WriteAllBytesAsync(saved,externalBytes);bool conflict=false,reopenConflict=false;
                try{await editor.SaveNoteFile(id);}catch(IOException){conflict=true;}
                try{await home.OpenNoteFile(saved);}catch(IOException){reopenConflict=true;}
                var recovered=Path.Combine(folder,"Keep local version.jot");editor.TestSaveNoteFilePath=recovered;await editor.SaveNoteFile(id,true);
                Check("external-edit-is-protected-and-save-as-recovers-"+mode,
                    conflict&&reopenConflict&&(await File.ReadAllBytesAsync(saved)).SequenceEqual(externalBytes)&&
                    (await NoteFileFormat.Read(recovered)).Note["plain"]!.GetValue<string>()=="Local draft stays in the library until Save."&&
                    (await session.Store.LoadNote(id))!.Value.GetProperty("noteFile").GetProperty("path").GetString()==recovered);
                var beforeDenied=await File.ReadAllBytesAsync(recovered);bool denied=false;
                await editor.Script("editor.innerHTML='<p>A denied file save must keep this draft.</p>';onEdit();clearTimeout(saveTimer)");
                File.SetAttributes(recovered,FileAttributes.ReadOnly);
                try{await editor.SaveNoteFile(id);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){denied=true;}
                finally{File.SetAttributes(recovered,FileAttributes.Normal);}
                Check("failed-replacement-keeps-original-and-removes-temp-file-"+mode,
                    denied&&(await File.ReadAllBytesAsync(recovered)).SequenceEqual(beforeDenied)&&!Directory.EnumerateFiles(folder,".jot-save-*.tmp").Any()&&
                    (await session.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()=="A denied file save must keep this draft.");

                bool textPassed=true;
                const string plain="First line\n\n    Indented <text> & symbols\nیادداشت فارسی\nLast line\n";
                foreach(var encoding in new[]{("utf8",(Encoding)new UTF8Encoding(false)),("utf16",(Encoding)new UnicodeEncoding(false,true))})
                {
                    var path=Path.Combine(folder,"متن saved "+encoding.Item1+".txt");await File.WriteAllTextAsync(path,plain.Replace("\n","\r\n"),encoding.Item2);
                    var textId=await home.OpenNoteFile(path);var textEditor=session.Windows.Single(window=>window.ContainsNote(textId));await textEditor.WaitFor(ready);
                    textPassed&=await textEditor.Script("editor.innerText==="+JsonSerializer.Serialize(plain))=="true";
                    await textEditor.Flush();await textEditor.SaveNoteFile(textId);
                    textPassed&=(await File.ReadAllTextAsync(path,Encoding.UTF8))==plain&&
                        (await session.Store.LoadNote(textId))!.Value.GetProperty("plain").GetString()==plain&&
                        textEditor.Mode==(mode=="tab"?"home":"note");
                }
                Check("utf8-and-utf16-preserve-lines-through-editor-flush-and-save-"+mode,textPassed);
                Check("test-files-and-windows-stay-isolated-"+mode,
                    session.Windows.All(window=>window.RuntimeErrors.Count==0&&window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost)&&
                    !File.Exists(Path.Combine(root,"ignored destination.jot")));
                editor=await session.OpenAcceptedNoteFile(id);await editor.WaitFor(ready);
                await editor.Script("editor.innerHTML='<p>Saved with the keyboard.</p>';onEdit();clearTimeout(saveTimer);editor.focus()");
                var saves=editor.TestHostActions.Count(action=>action=="save-note-file");
                await editor.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"rawKeyDown\",\"key\":\"s\",\"code\":\"KeyS\",\"windowsVirtualKeyCode\":83,\"modifiers\":2}");
                await editor.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyUp\",\"key\":\"s\",\"code\":\"KeyS\",\"windowsVirtualKeyCode\":83,\"modifiers\":2}");
                for(int i=0;i<100&&editor.TestHostActions.Count(action=>action=="save-note-file")==saves;i++)await Task.Delay(20);
                Check("ctrl-s-saves-the-associated-file-"+mode,editor.TestHostActions.Count(action=>action=="save-note-file")==saves+1&&(await NoteFileFormat.Read(recovered)).Note["plain"]!.GetValue<string>()=="Saved with the keyboard.");
                var finalText="Last live draft is written before Quit "+mode;
                await editor.Script("editor.innerHTML="+JsonSerializer.Serialize("<p>"+finalText+"</p>")+";onEdit();clearTimeout(saveTimer)");editor.TestSaveDelayMs=300;
                var saving=editor.SaveNoteFile(id);var quitting=session.Quit();await saving;await quitting;
                Check("quit-waits-for-accepted-file-save-and-last-editor-draft-"+mode,
                    session.Windows.Count==0&&(await NoteFileFormat.Read(recovered)).Note["plain"]!.GetValue<string>()==finalText&&
                    (await session.Store.LoadNote(id))!.Value.GetProperty("plain").GetString()==finalText);
            }
            finally{Cleanup(session);}
        }
    }
}
