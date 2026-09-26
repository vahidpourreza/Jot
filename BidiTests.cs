using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyBidiWriting(System.Collections.Generic.List<object> checks,MainWindow note)
    {
        await note.Script(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"tests","bidi-tests.js")));
        await note.WaitFor("window.bidiFinished===true",500);
        using(var results=JsonDocument.Parse(await note.Script("window.bidiResults")))foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
        var width=note.Width;var height=note.Height;
        await note.Script("window.beforeBidiPreview=editor.innerHTML;flushTypingHistory();editor.innerHTML=plainToHtml(window.bidiExampleText);bookmark=null;onEdit('command')");
        try{note.Width=840;note.Height=850;await note.Capture("bidi-user-examples");}
        finally{note.Width=width;note.Height=height;await note.Script("flushTypingHistory();editor.innerHTML=window.beforeBidiPreview;bookmark=null;onEdit('command');saveNow().then(()=>window.bidiPreviewRestored=true)");await note.WaitFor("window.bidiPreviewRestored===true");}
        var original=await note.Script("editor.innerHTML");
        try
        {
            await note.Script("flushTypingHistory();editor.innerHTML='<p><br></p>';bookmark=null;normalizeDirection();editor.focus();const r=document.createRange();r.selectNodeContents(editor.firstElementChild);r.collapse(true);getSelection().removeAllRanges();getSelection().addRange(r);rememberSelection()");
            foreach(var text in new[]{"- Currency enum"," حذف شد."," و Money.Zero"," ثبت شد."})
                await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
            checks.Add(new{name="bidi-real-input-keeps-caret-at-logical-end",passed=await note.Script("editor.textContent==='- Currency enum حذف شد. و Money.Zero ثبت شد.'&&editor.firstElementChild.dir==='rtl'&&JotBidi.pointOffset(editor,getSelection().anchorNode,getSelection().anchorOffset)===editor.textContent.length")=="true"});
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"rawKeyDown\",\"key\":\"Backspace\",\"code\":\"Backspace\",\"windowsVirtualKeyCode\":8}");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyUp\",\"key\":\"Backspace\",\"code\":\"Backspace\",\"windowsVirtualKeyCode\":8}");
            checks.Add(new{name="bidi-backspace-after-mixed-input-removes-only-last-character",passed=await note.Script("editor.textContent==='- Currency enum حذف شد. و Money.Zero ثبت شد'")=="true"});
            await note.Script("rememberSelection()");await note.ClickControl("#menuButton");await note.ClickControl("[data-paragraph-direction='ltr']");
            await note.Script("saveNow().then(()=>window.savedBidiOverride=true)");await note.WaitFor("window.savedBidiOverride===true");
            await note.Reload();
            checks.Add(new{name="bidi-explicit-direction-survives-real-reload",passed=await note.Script("editor.firstElementChild.dir==='ltr'&&editor.firstElementChild.dataset.jotDirection==='ltr'&&editor.textContent==='- Currency enum حذف شد. و Money.Zero ثبت شد'")=="true"});
            var fragment=JsonSerializer.Deserialize<string>(await note.Script("sanitizeHtml(editor.innerHTML)"))!;
            var exported=ExportDocument(fragment);
            checks.Add(new{name="bidi-export-retains-explicit-direction-and-inline-isolation",passed=exported.Contains("unicode-bidi:isolate")&&exported.Contains("data-jot-direction=\"ltr\"")&&exported.Contains("<bdi")&&!exported.Contains("unicode-bidi:plaintext")});
        }
        finally{await note.Script("flushTypingHistory();editor.innerHTML="+original+";bookmark=null;onEdit('command');saveNow().then(()=>window.bidiNativeRestored=true)");await note.WaitFor("window.bidiNativeRestored===true");}
    }
}
