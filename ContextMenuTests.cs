using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyEditorContextMenu(List<object> checks,MainWindow note)
    {
        note.TestClipboardContent=new("Money.Currency حذف شد.","<pre style='color:red'><b>Money.Currency حذف شد.</b></pre>",null);
        await note.Script(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"tests","context-tests.js")));
        await note.WaitFor("window.contextFinished===true",500);
        using(var results=JsonDocument.Parse(await note.Script("window.contextResults")))foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
        checks.Add(new{name="context-native-browser-menu-disabled",passed=!note.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled});
        var imageClipboard=note.TestClipboardData;
        checks.Add(new{name="context-copy-image-keeps-original-pixels",passed=imageClipboard?.GetData(DataFormats.Bitmap) is BitmapSource bitmap&&bitmap.PixelWidth==600&&bitmap.PixelHeight==200});
        var before=await note.Script("editor.innerHTML");var width=note.Width;var height=note.Height;
        try
        {
            await note.Script("flushTypingHistory();editor.innerHTML='<p>Selected text فارسی</p>';onEdit('command');editor.focus();getSelection().selectAllChildren(editor);rememberSelection()");
            using(var point=JsonDocument.Parse(await note.Script("(()=>{const r=editor.firstElementChild.getBoundingClientRect();return {x:r.x+8,y:r.y+8};})()")))
            {
                double x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble();
                foreach(var type in new[]{"mousePressed","mouseReleased"})await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button="right",clickCount=1}));
            }
            await note.WaitFor("!document.getElementById('editorMenu').hidden");checks.Add(new{name="context-real-right-click-opens-custom-menu",passed=true});
            await note.Capture("editor-context-dark");await note.Script("JotEditorMenu.close(true)");
            checks.Add(new{name="context-right-click-retains-selected-text",passed=await note.Script("getSelection().toString()==='Selected text فارسی'")=="true"});
            note.Width=320;note.Height=250;await Task.Delay(100);
            await note.Script("JotEditorMenu.open(innerWidth-1,innerHeight-1,null,true)");
            checks.Add(new{name="context-small-window-menu-scrolls-inside-frame",passed=await note.Script("(()=>{const m=document.getElementById('editorMenu'),r=m.getBoundingClientRect();return r.right<=innerWidth&&r.bottom<=innerHeight&&m.scrollHeight>m.clientHeight;})()")=="true"});
            await note.Capture("editor-context-small");await note.Script("JotEditorMenu.close(true)");note.Width=width;note.Height=height;
            note.TestClipboardWriteAccepted=false;
            await note.Script("editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('cut').then(()=>window.failedCutDone=true)");await note.WaitFor("window.failedCutDone===true");
            checks.Add(new{name="context-rejected-copy-never-deletes-cut-text",passed=await note.Script("editor.textContent==='Selected text فارسی'")=="true"});
            note.TestClipboardWriteAccepted=true;note.TestClipboardDelayMs=300;
            await note.Script("editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('cut').then(()=>window.staleCutDone=true)");
            checks.Add(new{name="context-async-action-has-spinner-and-pending-label",passed=await note.Script("document.querySelector('#editorMenu [data-action=cut]').classList.contains('pending')&&document.getElementById('editorMenu').ariaBusy==='true'&&document.getElementById('editorMenu').textContent.includes('Copying')")=="true"});
            await note.Script("editor.append(document.createTextNode(' changed'));onEdit('command')");await note.WaitFor("window.staleCutDone===true");
            checks.Add(new{name="context-edit-during-copy-prevents-stale-cut",passed=await note.Script("editor.textContent==='Selected text فارسی changed'")=="true"});
            await note.Script("editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('cut').then(()=>window.imeCutDone=true);composing=true");await note.WaitFor("window.imeCutDone===true");
            checks.Add(new{name="context-ime-start-during-copy-never-cuts",passed=await note.Script("editor.textContent==='Selected text فارسی changed'")=="true"});await note.Script("composing=false");
            await note.Script("editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('cut').then(()=>window.cancelledCutDone=true);document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");await note.WaitFor("window.cancelledCutDone===true");
            checks.Add(new{name="context-escape-cancels-delayed-cut",passed=await note.Script("editor.textContent==='Selected text فارسی changed'")=="true"});note.TestClipboardDelayMs=0;
            await note.Script("document.getElementById('error').hidden=true;JotDesign.apply({...model.prefs,theme:'light'});editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(90,80,null,true)");await note.Capture("editor-context-light");
        }
        finally
        {
            note.TestClipboardDelayMs=0;note.TestClipboardWriteAccepted=true;note.Width=width;note.Height=height;
            await note.Script("JotEditorMenu.close();flushTypingHistory();editor.innerHTML="+before+";bookmark=null;onEdit('command');JotDesign.apply(model.prefs);document.getElementById('error').hidden=true;saveNow().then(()=>window.contextRestored=true)");await note.WaitFor("window.contextRestored===true");
        }
        var text="فارسی English";var html="<p>فارسی <b>English</b></p>";var data=new DataObject();data.SetData(DataFormats.UnicodeText,text);data.SetData(DataFormats.Html,RichClipboard.HtmlFormat(html));var read=ClipboardReader.Read(data);
        checks.Add(new{name="context-native-reader-decodes-persian-utf8-cfhtml",passed=read.text==text&&read.html==html&&read.image is null});
        data.SetData(DataFormats.Html,"Version:1.0\r\nStartFragment:100000\r\nEndFragment:200000\r\ninvalid");checks.Add(new{name="context-malformed-html-falls-back-to-plain-text",passed=ClipboardReader.Read(data).text==text&&ClipboardReader.Read(data).html==""});
        checks.Add(new{name="context-empty-clipboard-safe",passed=ClipboardReader.Read(null)==new ClipboardContent("","",null)});
        await VerifyContextImages(checks,note);
    }

    private async Task VerifyContextImages(List<object> checks,MainWindow note)
    {
        var before=await note.Script("editor.innerHTML");
        var image=JsonSerializer.Deserialize<string>(await note.Script("editor.querySelector('img').src"))!;
        try
        {
            note.TestClipboardContent=new("before after","<p><b>before </b><img src='"+image+"'><em> after</em></p>",image);
            await note.Script("editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('paste').then(()=>window.mixedContextPaste=true)");await note.WaitFor("window.mixedContextPaste===true");
            checks.Add(new{name="context-mixed-paste-order-plain-text-small-original-image",passed=await note.Script("editor.textContent==='before  after'&&!editor.querySelector('b,em')&&editor.querySelector('img').naturalWidth===600&&editor.querySelector('img').getBoundingClientRect().width<=72")=="true"});
            await note.Script("JotEditorMenu.open(30,60,editor.querySelector('img'));JotEditorMenu.run('open-image').then(()=>window.contextImageOpen=true)");await note.WaitFor("window.contextImageOpen===true");
            var opened=session.Windows.Last(w=>w.Mode=="image");await opened.WaitFor("window.jotReady===true");
            checks.Add(new{name="context-open-image-opens-original-viewer",passed=opened.ImageSource==image&&await opened.Script("fullImage.naturalWidth===600")=="true"});opened.ClosePermanently();
            note.TestClipboardContent=new("","",null);
            await note.Script("window.beforeEmptyContextPaste=editor.innerHTML;editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(30,60,null,true);JotEditorMenu.run('paste').then(()=>window.emptyContextPaste=true)");await note.WaitFor("window.emptyContextPaste===true");
            checks.Add(new{name="context-empty-paste-does-not-delete-selection",passed=await note.Script("editor.innerHTML===window.beforeEmptyContextPaste")=="true"});
            // Slow import must not replace text typed after paste began.
            await note.Script("window.originalImport=importHtml;importHtml=async(...args)=>{await new Promise(r=>setTimeout(r,120));return window.originalImport(...args)};window.delayedPaste=null;const transfer=new DataTransfer();transfer.setData('text/html','<p>late image <img src=\"'+editor.querySelector('img').src+'\"></p>');paste({preventDefault(){},clipboardData:transfer}).then(()=>window.delayedPaste='inserted',()=>window.delayedPaste='cancelled');editor.append(document.createTextNode(' keep typing'));onEdit('command')");
            await note.WaitFor("window.delayedPaste!==null");
            checks.Add(new{name="context-slow-image-paste-cannot-overwrite-new-typing",passed=await note.Script("window.delayedPaste==='cancelled'&&editor.textContent.endsWith(' keep typing')")=="true"});
            await note.Script("importHtml=window.originalImport");
        }
        finally
        {
            note.TestClipboardContent=new("","",null);
            await note.Script("if(window.originalImport)importHtml=window.originalImport;JotEditorMenu.close();flushTypingHistory();editor.innerHTML="+before+";bookmark=null;onEdit('command');saveNow().then(()=>window.contextImagesRestored=true)");await note.WaitFor("window.contextImagesRestored===true");
        }
    }
}
