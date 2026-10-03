using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyPasteSpacing(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="paste-spacing-"+name,passed});
        var session=new JotSession(true,Path.Combine(testOutput,"paste-spacing")){ExerciseLifecycle=true};
        try
        {
            var note=new MainWindow(session,"note",await session.Store.Create());note.Reveal();await note.WaitFor("window.jotReady===true");
            await note.Script("""
                window.spacingFixture=html=>{
                  flushTypingHistory();editor.innerHTML=sanitizeHtml(html);bookmark=null;normalizeDirection();onEdit('command');clearTimeout(saveTimer);
                  editor.focus();getSelection().selectAllChildren(editor);rememberSelection();
                };
                """);
            var canvas=JsonSerializer.Deserialize<string>(await note.Script("(()=>{const c=document.createElement('canvas');c.width=3;c.height=2;return c.toDataURL()})()"))!;
            var fixtures=new[]{
                (Name:"paragraphs",Html:"<p><b>First</b> line</p><p>سطر دوم</p>",Plain:"First line\nسطر دوم",Lines:2),
                (Name:"intentional-blank-and-trailing-lines",Html:"<p>First line</p><p><br></p><p>سطر دوم</p><p><br></p>",Plain:"First line\n\nسطر دوم\n",Lines:4),
                (Name:"inline-image",Html:"<p>Before <img src=\""+canvas+"\"> after</p><p><br></p><p>Last line</p>",Plain:"Before  after\n\nLast line",Lines:3)
            };
            foreach(var fixture in fixtures)
            {
                await note.Script("spacingFixture("+JsonSerializer.Serialize(fixture.Html)+");window.spacingOriginalHtml=editor.innerHTML");
                Check(fixture.Name+"-copy-uses-logical-line-breaks",await note.Script("copyPayload(true).text==="+JsonSerializer.Serialize(fixture.Plain))=="true");
                if(fixture.Name=="paragraphs")Check("outgoing-copy-still-preserves-rich-formatting",await note.Script("copyPayload(true).html.includes('<b>First</b>')")=="true");
                for(int cycle=0;cycle<4;cycle++)
                {
                    await note.Script("window.spacingCutDone=false;editor.focus();getSelection().selectAllChildren(editor);rememberSelection();cutSelection().then(()=>window.spacingCutDone=true)");await note.WaitFor("window.spacingCutDone===true");
                    note.TestClipboardContent=ClipboardReader.Read(note.TestClipboardData);
                    Check(fixture.Name+"-native-clipboard-keeps-breaks-"+cycle,note.TestClipboardContent.text==fixture.Plain);
                    await note.Script("window.spacingPasteDone=false;JotEditorMenu.open(40,55,null,true);JotEditorMenu.run('paste').then(()=>window.spacingPasteDone=true)");await note.WaitFor("window.spacingPasteDone===true");
                    Check(fixture.Name+"-repeat-keeps-exact-lines-"+cycle,await note.Script("editorPlainText()==="+JsonSerializer.Serialize(fixture.Plain)+"&&editor.querySelectorAll('p').length==="+fixture.Lines+"&&[...editor.children].every(p=>p.dataset.jotPlainLine==='true'&&getComputedStyle(p).marginBottom==='0px'&&getComputedStyle(p).marginTop==='0px')")=="true");
                    if(fixture.Name=="inline-image")Check("repeated-paste-retains-original-inline-image-"+cycle,await note.Script("editor.querySelectorAll('img').length===1&&editor.querySelector('img').src==="+JsonSerializer.Serialize(canvas))=="true");
                    else Check(fixture.Name+"-paste-uses-note-writing-defaults-"+cycle,await note.Script("!editor.querySelector('b,i,font,[style]')&&getComputedStyle(editor.firstElementChild).fontSize===getComputedStyle(editor).fontSize&&getComputedStyle(editor.firstElementChild).lineHeight===getComputedStyle(editor).lineHeight")=="true");
                    if(cycle==0&&fixture.Name=="paragraphs")
                    {
                        await note.Script("undo();undo()");Check("cut-paste-undo-preserves-original-rich-note-and-selection",await note.Script("editor.innerHTML===spacingOriginalHtml&&getSelection().toString().includes('First line')&&getSelection().toString().includes('سطر دوم')")=="true");await note.Script("undo(true);undo(true)");
                    }
                }
                await note.Flush();Check(fixture.Name+"-saved-plain-text-is-stable",(await session.Store.LoadNote(note.NoteId!))!.Value.GetProperty("plain").GetString()==fixture.Plain);
            }
            await note.Script("spacingFixture('<p><br></p>');window.spacingExternalDone=false;const external=new DataTransfer();external.setData('text/plain',"+JsonSerializer.Serialize("First\r\n\r\nSecond\n")+");paste(new ClipboardEvent('paste',{clipboardData:external})).then(()=>window.spacingExternalDone=true)");await note.WaitFor("window.spacingExternalDone===true");
            Check("external-plain-text-keeps-intentional-empty-and-trailing-lines",await note.Script("editorPlainText()==="+JsonSerializer.Serialize("First\n\nSecond\n")+"&&editor.querySelectorAll('p').length===4")=="true");
            await note.Script("window.spacingFallback=document.createElement('div');spacingFallback.innerHTML='<p>One</p><p><br></p><p>Two</p>';window.spacingHtmlFallback=unformattedClipboardHtml(spacingFallback);spacingFallback.innerHTML=spacingHtmlFallback");
            Check("html-only-fallback-does-not-add-placeholder-blank-lines",await note.Script("editorPlainText(spacingFallback)==="+JsonSerializer.Serialize("One\n\nTwo")+"&&spacingFallback.querySelectorAll('p').length===3")=="true");
            Check("existing-rich-note-margin-stays-unchanged",await note.Script("(()=>{spacingFixture('<p>Existing rich paragraph</p><p>Next paragraph</p>');return getComputedStyle(editor.firstElementChild).marginBottom!=='0px'&&!editor.firstElementChild.hasAttribute('data-jot-plain-line')})()")=="true");
            Check("all-tests-are-isolated",session.Windows.All(w=>w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Left< -10000&&w.Top< -10000));
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}
