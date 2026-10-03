using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyUndoSelection(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="undo-selection-"+name,passed});
        var session=new JotSession(true,Path.Combine(testOutput,"undo-selection")){ExerciseLifecycle=true};
        MainWindow? note=null;
        try
        {
            note=new MainWindow(session,"note",await session.Store.Create());note.Reveal();await note.WaitFor("window.jotReady===true");
            await note.Script("""
                window.undoFixture=html=>{
                  flushTypingHistory();editor.innerHTML=sanitizeHtml(html);bookmark=null;normalizeDirection();updateEmpty();
                  editor.focus();getSelection().selectAllChildren(editor);getSelection().collapseToEnd();rememberSelection();
                  histories.set(model.activeId,{values:[editor.innerHTML],selections:[editorSelectionState()],index:0,time:0,kind:''});
                  onEdit('command');clearTimeout(saveTimer);document.getElementById('error').hidden=true;JotToast.dismiss();
                };
                window.undoSelect=(selector,backward=false)=>{
                  const target=editor.querySelector(selector);editor.focus();
                  getSelection().setBaseAndExtent(target,backward?target.childNodes.length:0,target,backward?0:target.childNodes.length);rememberSelection();
                  window.undoBeforeHtml=editor.innerHTML;window.undoBeforeSelection=JSON.stringify(editorSelectionState());window.undoBeforeText=getSelection().toString();
                };
                const canvas=document.createElement('canvas');canvas.width=2;canvas.height=2;
                window.undoRichFixture='<p>Before <b>selected <i>rich text</i></b> after.</p><p data-jot-direction="rtl">Currency حذف شد.</p><p><img src="'+canvas.toDataURL()+'"> inline image</p>';
                """);
            async Task Key(string key,int virtualKey,bool control=false)
            {
                foreach(var type in new[]{"rawKeyDown","keyUp"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key=key.Length==1?key.ToLowerInvariant():key,code=key.Length==1?"Key"+key:key,windowsVirtualKeyCode=virtualKey,modifiers=control?2:0}));
            }
            async Task Cut(string source)
            {
                if(source=="keyboard")await Key("X",88,true);
                else{await note.Script("window.undoMenuDone=false;JotEditorMenu.open(60,80,null,true);JotEditorMenu.run('cut').then(()=>window.undoMenuDone=true)");await note.WaitFor("window.undoMenuDone===true");}
                await note.WaitFor("!cutting");
            }
            foreach(var source in new[]{"keyboard","context"})foreach(bool backward in new[]{false,true})
            {
                await note.Script("undoFixture(undoRichFixture);undoSelect('b',"+(backward?"true":"false")+")");
                await Cut(source);await note.WaitFor("!editor.textContent.includes('selected rich text')");
                Check(source+"-cut-removes-only-selection-"+backward,await note.Script("editor.textContent.includes('Before')&&editor.textContent.includes('Currency حذف شد.')&&!!editor.querySelector('img')&&histories.get(model.activeId).values.length===2&&histories.get(model.activeId).index===1")=="true");
                await note.Script("window.undoAfterHtml=editor.innerHTML;window.undoAfterSelection=JSON.stringify(editorSelectionState())");
                await Key("Z",90,true);
                Check(source+"-undo-restores-rich-content-and-original-selection-"+backward,await note.Script("editor.innerHTML===undoBeforeHtml&&JSON.stringify(editorSelectionState())===undoBeforeSelection&&getSelection().toString()===undoBeforeText&&getComputedStyle(editor.children[1]).direction==='rtl'")=="true");
                await Key("Y",89,true);
                Check(source+"-redo-restores-cut-result-and-caret-"+backward,await note.Script("editor.innerHTML===undoAfterHtml&&JSON.stringify(editorSelectionState())===undoAfterSelection&&getSelection().isCollapsed")=="true");
                await Key("Z",90,true);
            }
            await note.Script("(()=>{undoFixture(undoRichFixture);const image=editor.querySelector('img'),range=document.createRange();range.selectNode(image);getSelection().removeAllRanges();getSelection().addRange(range);rememberSelection();window.undoBeforeHtml=editor.innerHTML;window.undoBeforeSelection=JSON.stringify(editorSelectionState())})()");
            await Cut("context");Check("image-cut-removes-only-selected-original",await note.Script("!editor.querySelector('img')&&editor.textContent.includes('selected rich text')")=="true");
            await Key("Z",90,true);Check("image-cut-undo-restores-original-and-image-selection",await note.Script("editor.innerHTML===undoBeforeHtml&&JSON.stringify(editorSelectionState())===undoBeforeSelection&&getSelection().getRangeAt(0).cloneContents().querySelector('img').src===editor.querySelector('img').src")=="true");

            await note.Script("(()=>{undoFixture('<p>abcdef</p>');const text=editor.firstElementChild.firstChild;getSelection().setBaseAndExtent(text,3,text,3);rememberSelection();window.typingBeforeSelection=JSON.stringify(editorSelectionState())})()");
            foreach(var text in new[]{"X","Y"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
            await note.Script("window.typingAfterSelection=JSON.stringify(editorSelectionState())");await Key("Z",90,true);
            Check("typing-undo-restores-caret-before-the-group",await note.Script("editor.textContent==='abcdef'&&JSON.stringify(editorSelectionState())===typingBeforeSelection")=="true");
            await Key("Y",89,true);Check("typing-redo-restores-caret-after-the-group",await note.Script("editor.textContent==='abcXYdef'&&JSON.stringify(editorSelectionState())===typingAfterSelection")=="true");
            await note.Script("(()=>{undoFixture('<p>abcdef</p>');const text=editor.firstElementChild.firstChild;getSelection().setBaseAndExtent(text,4,text,1);rememberSelection();window.replaceBeforeSelection=JSON.stringify(editorSelectionState())})()");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText","{\"text\":\"X\"}");await Key("Z",90,true);
            Check("typing-replacement-undo-restores-backward-selection",await note.Script("editor.textContent==='abcdef'&&JSON.stringify(editorSelectionState())===replaceBeforeSelection&&getSelection().toString()==='bcd'")=="true");

            foreach(var source in new[]{"keyboard","context"})
            {
                note.TestClipboardWriteAccepted=false;
                await note.Script("undoFixture(undoRichFixture);undoSelect('b',true);window.failedCutHistory=JSON.stringify(histories.get(model.activeId))");
                await Cut(source);await note.WaitFor("JotToast.has('note-error')");
                Check(source+"-failed-copy-keeps-content-selection-and-history",await note.Script("editor.innerHTML===undoBeforeHtml&&JSON.stringify(histories.get(model.activeId))===failedCutHistory&&JSON.stringify(editorSelectionState())===undoBeforeSelection")=="true");
                note.TestClipboardWriteAccepted=true;note.TestClipboardDelayMs=350;
                await note.Script("undoFixture(undoRichFixture);undoSelect('b',true);window.staleCutHistory=JSON.stringify(histories.get(model.activeId))");
                if(source=="keyboard")await Key("X",88,true);
                else await note.Script("window.staleMenuDone=false;JotEditorMenu.open(60,80,null,true);JotEditorMenu.run('cut').then(()=>window.staleMenuDone=true)");
                await note.WaitFor("cutting");
                await note.Script("editor.focus();getSelection().selectAllChildren(editor);getSelection().collapseToStart();rememberSelection();window.changedSelection=JSON.stringify(editorSelectionState())");
                await note.WaitFor("!cutting");
                if(source=="context")await note.WaitFor("window.staleMenuDone===true");
                Check(source+"-selection-change-during-copy-never-cuts",await note.Script("editor.innerHTML===undoBeforeHtml&&JSON.stringify(histories.get(model.activeId))===staleCutHistory&&JSON.stringify(editorSelectionState())===changedSelection")=="true");
                note.TestClipboardDelayMs=0;
            }
            note.TestClipboardDelayMs=350;
            await note.Script("undoFixture(undoRichFixture);undoSelect('b');window.cutOriginalId=model.activeId");await Key("X",88,true);await note.WaitFor("cutting");
            await note.Script("model.activeId=crypto.randomUUID();editor.innerHTML='<p>Other note stays intact.</p>';bookmark=null;clearTimeout(saveTimer)");await note.WaitFor("!cutting");
            Check("note-change-during-copy-never-deletes-the-new-editor",await note.Script("editor.textContent==='Other note stays intact.'")=="true");
            await note.Script("model.activeId=cutOriginalId;undoFixture(undoRichFixture)");note.TestClipboardDelayMs=0;
            Check("history-html-api-and-selection-count-stay-compatible",await note.Script("[...histories.values()].every(history=>history.values.every(value=>typeof value==='string')&&history.values.length===history.selections.length)")=="true");
            Check("tests-stay-offscreen-without-system-clipboard-access",note.Opacity==0&&!note.ShowActivated&&!note.ShowInTaskbar&&!note.Topmost&&note.Left< -10000&&note.Top< -10000&&note.TestClipboardData is not null);
        }
        finally
        {
            if(note is not null){note.TestClipboardWriteAccepted=true;note.TestClipboardDelayMs=0;}
            foreach(var window in session.Windows.ToArray())window.ClosePermanently();
        }
    }
}
