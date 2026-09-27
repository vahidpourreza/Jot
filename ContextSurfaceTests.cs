using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task RightClickControl(string selector)
    {
        using var point=JsonDocument.Parse(await Script("(()=>{const r=document.querySelector("+JsonSerializer.Serialize(selector)+").getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()"));
        var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
        foreach(var type in new[]{"mousePressed","mouseReleased"})await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button="right",clickCount=1}));
    }
    private async Task VerifyContextSurfaces(List<object> checks,MainWindow note)
    {
        checks.Add(new{name="browser-right-click-disabled-in-index-and-note",passed=!Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled&&!note.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled});
        await RightClickControl("#homeHandle");
        checks.Add(new{name="no-context-menu-on-index-header",passed=await Script("document.getElementById('contentContextMenu').hidden")=="true"});
        await note.RightClickControl("#handle");
        checks.Add(new{name="no-context-menu-on-note-header",passed=await note.Script("document.getElementById('contentContextMenu').hidden&&document.getElementById('editorMenu').hidden")=="true"});
        await ClickControl("[data-note-id='"+note.NoteId+"'] .card-edit");
        await Script("document.getElementById('noteTitleInput').value='abc فارسی';document.getElementById('noteTitleInput').focus();document.getElementById('noteTitleInput').setSelectionRange(4,9,'backward')");
        var reads=TestHostActions.Count(a=>a=="clipboard-read-text");
        await Script("document.getElementById('noteTitleInput').dispatchEvent(new KeyboardEvent('keydown',{key:'F10',shiftKey:true,bubbles:true}))");
        checks.Add(new{name="title-field-custom-menu-does-not-read-clipboard-on-open",passed=await Script("!document.getElementById('contentContextMenu').hidden&&document.querySelectorAll('#contentContextMenu [role=menuitem]').length===4")=="true"&&TestHostActions.Count(a=>a=="clipboard-read-text")==reads});
        await Capture("input-context-menu");await ClickControl("#contentContextMenu [data-action=copy]");await WaitFor("document.getElementById('contentContextMenu').hidden");
        checks.Add(new{name="title-field-context-copy-preserves-persian-selection",passed=(string?)TestClipboardData?.GetData(DataFormats.UnicodeText)=="فارسی"&&await Script("document.getElementById('noteTitleInput').value==='abc فارسی'")=="true"});
        TestClipboardContent=new ClipboardContent("English متن","<b>ignored</b>",null);
        await Script("document.getElementById('noteTitleInput').focus();document.getElementById('noteTitleInput').select();document.getElementById('noteTitleInput').dispatchEvent(new KeyboardEvent('keydown',{key:'ContextMenu',bubbles:true}))");
        await ClickControl("#contentContextMenu [data-action=paste]");await WaitFor("document.getElementById('contentContextMenu').hidden");
        checks.Add(new{name="title-field-context-paste-is-plain-and-updates-field",passed=await Script("document.getElementById('noteTitleInput').value==='English متن'")=="true"});
        TestClipboardWriteAccepted=false;
        await Script("document.getElementById('noteTitleInput').focus();document.getElementById('noteTitleInput').select();document.getElementById('noteTitleInput').dispatchEvent(new KeyboardEvent('keydown',{key:'ContextMenu',bubbles:true}))");
        await ClickControl("#contentContextMenu [data-action=cut]");await WaitFor("document.getElementById('contentContextMenu').hidden");
        checks.Add(new{name="input-context-failed-cut-keeps-text",passed=await Script("document.getElementById('noteTitleInput').value==='English متن'")=="true"});TestClipboardWriteAccepted=true;
        await Script("document.getElementById('homeError').hidden=true");
        await Script("document.getElementById('noteTitleInput').focus();document.getElementById('noteTitleInput').select();document.getElementById('noteTitleInput').dispatchEvent(new KeyboardEvent('keydown',{key:'ContextMenu',bubbles:true}))");
        checks.Add(new{name="input-context-is-visible-in-modal-top-layer",passed=await Script("(()=>{const m=document.getElementById('contentContextMenu'),r=m.getBoundingClientRect();return m.parentElement===document.getElementById('metadataDialog')&&!!document.elementFromPoint(r.left+20,r.top+20)?.closest('#contentContextMenu');})()")=="true"});
        await Script("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
        checks.Add(new{name="input-menu-escape-does-not-close-dialog",passed=await Script("document.getElementById('metadataDialog').open&&document.getElementById('contentContextMenu').hidden")=="true"});await ClickControl("#metadataClose");
        await GoToSettings();await RightClickControl("#settingsHandle");
        checks.Add(new{name="settings-chrome-has-no-context-menu",passed=!Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled&&await Script("document.getElementById('contentContextMenu').hidden")=="true"});await GoToIndex();
        TestClipboardContent=new("","",null);
    }
}
