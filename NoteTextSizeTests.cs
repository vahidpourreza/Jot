using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyNoteTextSize(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="note-text-size-"+name,passed,evidence});
        var migrated=new JsonObject{["shortcutBindings"]=new JsonObject{["new-note"]="Ctrl+Equal"},["globalShortcuts"]=false};
        ShortcutBindings.NormalizeStored(migrated);
        Check("existing-custom-key-retained",migrated["shortcutBindings"]!["new-note"]!.GetValue<string>()=="Ctrl+Equal"&&migrated["shortcutBindings"]!["note-text-larger"] is null);
        foreach(var mode in new[]{"tab","window"})
        {
            var target=new JotSession(true,Path.Combine(testOutput,"text-size-"+mode)){ExerciseLifecycle=true};
            try
            {
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=mode,fontSize=16}));
                var note=await target.NewNote();note.Width=720;note.Height=520;
                await note.WaitFor("window.jotReady===true&&!!window.JotTextSize&&editor.isContentEditable");var id=note.NoteId!;
                var peer=await target.Store.Create();
                await note.Script("editor.innerHTML='<p>Alpha <b>bold</b> · متن فارسی</p>';onEdit('command');editor.focus();getSelection().setBaseAndExtent(editor.firstChild.firstChild,0,editor.firstChild.firstChild,5);rememberSelection();window.beforeSizing=editor.innerHTML");
                async Task Key(string key,string code,int vk,int modifiers=2)
                {
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="rawKeyDown",key,code,windowsVirtualKeyCode=vk,modifiers}));
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code,windowsVirtualKeyCode=vk,modifiers}));
                }
                await Key("=","Equal",187);await note.WaitFor("model.prefs.fontSize===17");await note.Flush();
                Check("ctrl-plus-saves-note-only-"+mode,(await target.Store.LoadNote(id))!.Value.GetProperty("view").GetProperty("fontSize").GetInt32()==17&&!((await target.Store.LoadNote(peer))!.Value.GetProperty("view").TryGetProperty("fontSize",out _))&&(await target.Store.LoadPreferences()).GetProperty("fontSize").GetInt32()==16);
                Check("selection-and-content-stay-intact-"+mode,await note.Script("editor.innerHTML===beforeSizing&&getSelection().toString()==='Alpha'")=="true"&&note.Browser.ZoomFactor==1);
                await Key("+","Equal",187,10);await note.WaitFor("model.prefs.fontSize===18");
                await Key("-","Minus",189);await note.WaitFor("model.prefs.fontSize===17");
                Check("shift-plus-and-minus-work-"+mode,await note.Script("model.prefs.fontSize===17&&editor.innerHTML===beforeSizing")=="true");
                await note.Script("document.getElementById('writingArea').dispatchEvent(new WheelEvent('wheel',{deltaY:-120,ctrlKey:true,bubbles:true,cancelable:true}))");
                await note.WaitFor("model.prefs.fontSize===18");
                Check("ctrl-wheel-changes-font-not-page-"+mode,note.Browser.ZoomFactor==1&&await note.Script("JotToast.get('note-text-size').message==='Text size: 18 px'")=="true");
                using(var point=JsonDocument.Parse(await note.Script("(()=>{const r=editor.getBoundingClientRect();return {x:r.left+40,y:r.top+20}})()")))
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseWheel",x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),deltaX=0,deltaY=120,modifiers=2}));
                await note.WaitFor("model.prefs.fontSize===17");
                Check("native-wheel-keeps-browser-scale-"+mode,note.Browser.ZoomFactor==1);
                await note.Script("document.getElementById('writingArea').dispatchEvent(new WheelEvent('wheel',{deltaY:-120,ctrlKey:true,bubbles:true,cancelable:true}))");
                await note.Script("document.getElementById('writingArea').dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true}))");
                Check("ordinary-scroll-does-not-resize-"+mode,await note.Script("model.prefs.fontSize===18")=="true");
                await note.Script("document.getElementById('documentTitle').focus()");await Key("=","Equal",187);
                Check("title-field-keeps-keyboard-ownership-"+mode,await note.Script("model.prefs.fontSize===18")=="true");
                await note.Script("editor.focus();composing=true");await Key("=","Equal",187);
                Check("composition-not-intercepted-"+mode,await note.Script("model.prefs.fontSize===18")=="true");await note.Script("composing=false");
                await note.Script("for(let i=0;i<30;i++)JotTextSize.change(1)");await note.Flush();
                Check("upper-limit-"+mode,await note.Script("model.prefs.fontSize===24")=="true");
                await note.Script("for(let i=0;i<30;i++)JotTextSize.change(-1)");await note.Flush();
                Check("lower-limit-"+mode,await note.Script("model.prefs.fontSize===13")=="true");
                await Key("0","Digit0",48);await note.WaitFor("model.prefs.fontSize===16");await note.Flush();
                Check("ctrl-zero-restores-inheritance-"+mode,!(await target.Store.LoadNote(id))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=19}));await target.Changed();await note.WaitFor("model.prefs.fontSize===19");
                Check("reset-follows-future-default-"+mode,await note.Script("model.prefs.fontSize===19&&editor.innerHTML===beforeSizing")=="true");
                await note.Script("JotTextSize.change(1)");await note.Flush();await note.Reload();
                Check("resized-font-survives-reload-"+mode,await note.Script("model.prefs.fontSize===20")=="true");
                await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>{{"note-text-larger","Alt+KeyF"}}}));await target.Changed();await note.WaitFor("JotShortcutBindings.config.bindings['note-text-larger']==='Alt+KeyF'");
                await Key("=","Equal",187);
                Check("old-binding-released-after-remap-"+mode,await note.Script("model.prefs.fontSize===20")=="true");
                await Key("f","KeyF",70,1);await note.WaitFor("model.prefs.fontSize===21");
                Check("custom-font-binding-"+mode,await note.Script("model.prefs.fontSize===21")=="true");
                await note.Script("JotTextSize.change(-1)");await note.Flush();
                if(mode=="tab")
                {
                    await note.SwitchNoteTab(peer);await note.WaitFor("activeNote()?.id==="+JsonSerializer.Serialize(peer)+"&&ready");
                    Check("other-tab-keeps-own-size",await note.Script("model.prefs.fontSize===19")=="true");
                    await note.SwitchNoteTab(id);await note.WaitFor("activeNote()?.id==="+JsonSerializer.Serialize(id)+"&&ready");
                    Check("return-tab-retains-size",await note.Script("model.prefs.fontSize===20")=="true");
                }
                Check("no-runtime-errors-"+mode,note.RuntimeErrors.Count==0,note.RuntimeErrors);
            }
            finally{target.StopFileAutoSave();await target.WaitForNoteFiles();foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        }
    }
}
