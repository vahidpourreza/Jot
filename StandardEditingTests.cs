using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyStandardEditing(List<object> checks)
    {
        var editingSession=new JotSession(true,Path.Combine(testOutput,"standard-editing")){ExerciseLifecycle=true};
        MainWindow? note=null;
        try
        {
            note=new MainWindow(editingSession,"note",await editingSession.Store.Create());note.Width=640;note.Height=560;
            note.Reveal();await note.WaitFor("window.jotReady===true");
            await note.Script("""
                window.standardFixture=html=>{
                  flushTypingHistory();closePanels();editor.innerHTML=sanitizeHtml(html);bookmark=null;bookmarkDirection=null;
                  normalizeDirection();updateEmpty();editor.focus();getSelection().removeAllRanges();
                  histories.set(model.activeId,{values:[editor.innerHTML],selections:[null],index:0,time:0,kind:''});
                  onEdit('command');clearTimeout(saveTimer);window.standardOriginal=editor.innerHTML;window.standardEvents=[];
                };
                window.standardPoint=(paragraph,offset)=>{
                  const p=editor.children[paragraph],start=JotBidi.pointAt(p,offset),end=JotBidi.pointAt(p,offset+1);
                  const r=document.createRange();r.setStart(...start);r.setEnd(...end);const box=r.getBoundingClientRect();
                  return {x:box.x+box.width/2,y:box.y+box.height/2};
                };
                window.standardSelection=()=>{const s=getSelection();return {text:s.toString(),collapsed:s.isCollapsed,
                  anchor:JotBidi.pointOffset(editor,s.anchorNode,s.anchorOffset),focus:JotBidi.pointOffset(editor,s.focusNode,s.focusOffset),
                  html:editor.innerHTML,events:standardEvents,active:document.activeElement?.id,
                  userSelect:getComputedStyle(editor).userSelect};};
                for(const type of ['mousedown','mouseup','mousemove','pointerdown','pointerup','pointermove','click','dblclick','selectstart','dragstart','dragover','drop'])
                  editor.addEventListener(type,e=>standardEvents.push({type,detail:e.detail,button:e.button,buttons:e.buttons,
                    x:e.clientX,y:e.clientY,pointerType:e.pointerType,trusted:e.isTrusted,prevented:e.defaultPrevented,selected:getSelection().toString()}));
                """);
            async Task Check(string name,string predicate)
            {
                bool passed=await note.Script(predicate)=="true";
                checks.Add(new{name="standard-editing-"+name,passed,evidence=JsonSerializer.Deserialize<JsonElement>(await note.Script("standardSelection()"))});
            }
            async Task<(double X,double Y)> Point(int paragraph,int offset)
            {
                using var point=JsonDocument.Parse(await note.Script($"standardPoint({paragraph},{offset})"));
                return(point.RootElement.GetProperty("x").GetDouble(),point.RootElement.GetProperty("y").GetDouble());
            }
            async Task Mouse(string type,(double X,double Y) point,int count=1,int buttons=0,int modifiers=0)
                =>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=point.X,y=point.Y,button=type=="mouseMoved"&&buttons==0?"none":"left",buttons,clickCount=count,modifiers}));
            async Task Click(int paragraph,int offset,int count=1,int modifiers=0)
            {
                var point=await Point(paragraph,offset);await Mouse("mouseMoved",point,0);
                for(int click=1;click<=count;click++) {await Mouse("mousePressed",point,click,1,modifiers);await Mouse("mouseReleased",point,click,0,modifiers);}
            }
            async Task Key(string key,int virtualKey,int modifiers=0)
            {
                foreach(var type in new[]{"rawKeyDown","keyUp"})
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key=key.Length==1?key.ToLowerInvariant():key,code=key.Length==1?"Key"+key.ToUpperInvariant():key,windowsVirtualKeyCode=virtualKey,modifiers}));
            }
            async Task Type(string text)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text}));
            var fixtures=new[]{
                (Name:"english",Html:"<p>Alpha bravo charlie delta.</p><p>Second paragraph stays separate.</p>",Word:"bravo",Offset:8),
                (Name:"persian",Html:"<p>این نوشته برای انتخاب کلمه است.</p><p>پاراگراف دوم جدا می‌ماند.</p>",Word:"انتخاب",Offset:18),
                (Name:"persian-zwnj",Html:"<p>من می‌توانم درست بنویسم.</p><p>پاراگراف دوم</p>",Word:"می‌توانم",Offset:7),
                (Name:"mixed-latin-run",Html:"<p>این متن با alpha bravo charlie ادامه دارد.</p><p>پاراگراف دوم</p>",Word:"bravo",Offset:20),
                (Name:"rich-formatting",Html:"<p>Alpha <b>bravo <i>charlie</i></b> delta.</p><p>Second paragraph</p>",Word:"bravo",Offset:8),
                (Name:"legacy-loose-text",Html:"Alpha bravo charlie delta.",Word:"bravo",Offset:8)
            };
            foreach(var fixture in fixtures)
            {
                await note.Script("standardFixture("+JsonSerializer.Serialize(fixture.Html)+")");
                await Click(0,fixture.Offset);await Check(fixture.Name+"-single-click-places-caret","getSelection().isCollapsed&&editor.contains(getSelection().anchorNode)");
                await Click(0,fixture.Offset,2);
                // Windows may include one trailing word separator. It must
                // never include another word or the rest of the paragraph.
                await Check(fixture.Name+"-double-click-selects-word","["+JsonSerializer.Serialize(fixture.Word)+","+JsonSerializer.Serialize(fixture.Word+" ")+"].includes(getSelection().toString())");
                await Check(fixture.Name+"-selection-does-not-rewrite-content","editor.innerHTML===standardOriginal");
                if(fixture.Name is "english" or "persian" or "mixed-latin-run")await note.Capture("native-double-click-"+fixture.Name);
                await Click(0,fixture.Offset,3);
                await Check(fixture.Name+"-triple-click-selects-paragraph","getSelection().toString().trim()===editor.firstElementChild.textContent");
            }
            // CDP bypasses WPF's mouse adapter. Exercise its real routed event
            // class handlers too, without moving the system pointer or clicking
            // any visible window. The only reflected fields supply synthetic
            // offscreen coordinates and suppress SDK Focus() during this test.
            var sdk=typeof(WebView2CompositionControl);
            var location=sdk.GetField("_mouselocation",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var hasFocus=sdk.GetField("_webviewHasFocus",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var countSetter=typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(true)!;
            async Task NativePress(int count)
            {
                foreach(var routedEvent in new[]{System.Windows.Input.Mouse.MouseDownEvent,System.Windows.Input.Mouse.MouseUpEvent})
                {
                    var args=new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=routedEvent,Source=note.Browser};
                    countSetter.Invoke(args,[count]);note.Browser.RaiseEvent(args);
                    await Task.Delay(40);
                }
            }
            var previousLocation=location.GetValue(note.Browser);var previousFocus=hasFocus.GetValue(note.Browser);
            try
            {
                foreach(var fixture in fixtures.Where(item=>item.Name is "english" or "persian-zwnj" or "mixed-latin-run"))
                {
                    await note.Script("standardFixture("+JsonSerializer.Serialize(fixture.Html)+")");
                    var point=await Point(0,fixture.Offset);var dpi=VisualTreeHelper.GetDpi(note.Browser);
                    location.SetValue(note.Browser,new System.Drawing.Point((int)Math.Round(point.X*dpi.DpiScaleX),(int)Math.Round(point.Y*dpi.DpiScaleY)));
                    hasFocus.SetValue(note.Browser,true);
                    await Task.Delay(600);await NativePress(1);
                    await Check("wpf-"+fixture.Name+"-single-click","getSelection().isCollapsed&&standardEvents.filter(e=>e.type==='mousedown').length===1");
                    await NativePress(2);
                    await Check("wpf-"+fixture.Name+"-double-click-is-one-second-press","standardEvents.filter(e=>e.type==='mousedown').length===2&&standardEvents.some(e=>e.type==='dblclick')&&["+JsonSerializer.Serialize(fixture.Word)+","+JsonSerializer.Serialize(fixture.Word+" ")+"].includes(getSelection().toString())");
                    await NativePress(3);
                    await Check("wpf-"+fixture.Name+"-triple-click-is-one-third-press","standardEvents.filter(e=>e.type==='mousedown').length===3&&getSelection().toString().trim()===editor.firstElementChild.textContent");
                    await Check("wpf-"+fixture.Name+"-content-unchanged","editor.innerHTML===standardOriginal");
                }
            }
            finally{location.SetValue(note.Browser,previousLocation);hasFocus.SetValue(note.Browser,previousFocus);}
            await note.Script("standardFixture('<p>Alpha bravo charlie delta.</p><p>Second paragraph stays separate.</p>')");
            await Click(0,8);await Key("Home",36);
            await Check("home-moves-to-line-start","getSelection().isCollapsed&&standardSelection().focus===0");
            await Key("ArrowRight",39,2);
            await Check("ctrl-right-moves-by-word","getSelection().isCollapsed&&standardSelection().focus===6");
            await Key("ArrowRight",39,10);
            await Check("ctrl-shift-right-selects-next-word","getSelection().toString().trim()==='bravo'");
            await Type("replacement ");
            await Check("typing-replaces-selection-only","editor.firstElementChild.textContent==='Alpha replacement charlie delta.'&&getSelection().isCollapsed");
            await Key("Z",90,2);
            await Check("undo-replacement-restores-selected-word","editor.firstElementChild.textContent==='Alpha bravo charlie delta.'&&getSelection().toString().trim()==='bravo'");
            await Key("ArrowLeft",37);await Key("Backspace",8,2);
            await Check("ctrl-backspace-deletes-previous-word","editor.firstElementChild.textContent==='bravo charlie delta.'");
            await Key("Z",90,2);await Key("Home",36);await Key("Delete",46,2);
            await Check("ctrl-delete-deletes-next-word","editor.firstElementChild.textContent==='bravo charlie delta.'");
            await Key("Z",90,2);await Key("End",35);
            await Check("end-moves-to-line-end","getSelection().isCollapsed&&standardSelection().focus===editor.firstElementChild.textContent.length");
            await Key("Home",36,8);
            await Check("shift-home-selects-to-line-start","getSelection().toString()===editor.firstElementChild.textContent");
            await Key("End",35,2);
            await Check("ctrl-end-moves-to-document-end","getSelection().isCollapsed&&standardSelection().focus===editor.textContent.length");
            await Key("Home",36,10);
            await Check("ctrl-shift-home-selects-across-paragraphs","getSelection().toString().includes('Alpha bravo')&&getSelection().toString().includes('Second paragraph')");
            await Key("Home",36,2);await Key("ArrowRight",39,8);
            await Check("shift-arrow-selects-character","getSelection().toString()==='A'");
            await Click(0,8,2);await note.Script("window.standardCutHtml=editor.innerHTML;window.standardCutSelection=JSON.stringify(editorSelectionState())");
            await Key("X",88,2);await note.WaitFor("!cutting");await Key("Z",90,2);
            await Check("cut-undo-restores-native-word-selection","editor.innerHTML===standardCutHtml&&JSON.stringify(editorSelectionState())===standardCutSelection&&['bravo','bravo '].includes(getSelection().toString())");
            await Click(0,1);await note.Script("window.standardAnchor=standardSelection().anchor");await Click(0,15,1,8);
            await Check("shift-click-extends-existing-anchor","!getSelection().isCollapsed&&standardSelection().anchor===standardAnchor&&getSelection().toString().includes('bravo')");
            await note.Script("standardFixture('<p>Alpha bravo charlie.</p><p>Second paragraph.</p>')");
            // In this WebView runtime CDP move events cannot extend a native
            // selection, even inside a plain iframe with no app listeners and
            // with focus emulation enabled. Send real composition-controller
            // input with the held-left-button flag instead. It stays offscreen
            // and never sends OS input, changes focus or moves the user cursor.
            var sendMouse=typeof(WebView2CompositionControl).GetMethod("SendMouseInput",BindingFlags.Instance|BindingFlags.NonPublic)!;
            async Task DragMouse(CoreWebView2MouseEventKind kind,(double X,double Y) at,bool held)
            {
                var dpi=VisualTreeHelper.GetDpi(note.Browser);
                sendMouse.Invoke(note.Browser,[kind,held?CoreWebView2MouseEventVirtualKeys.LeftButton:CoreWebView2MouseEventVirtualKeys.None,0u,new System.Drawing.Point((int)Math.Round(at.X*dpi.DpiScaleX),(int)Math.Round(at.Y*dpi.DpiScaleY))]);
                await Task.Delay(20);
            }
            await Task.Delay(600);var start=await Point(0,2);var end=await Point(1,9);
            await DragMouse(CoreWebView2MouseEventKind.Move,start,false);await DragMouse(CoreWebView2MouseEventKind.LeftButtonDown,start,true);
            for(int step=1;step<=8;step++)await DragMouse(CoreWebView2MouseEventKind.Move,(start.X+(end.X-start.X)*step/8,start.Y+(end.Y-start.Y)*step/8),true);
            await DragMouse(CoreWebView2MouseEventKind.LeftButtonUp,end,false);
            const string dragPredicate="!getSelection().isCollapsed&&getSelection().toString().includes('bravo charlie.')&&getSelection().toString().includes('Second')";
            await Check("pointer-drag-selects-across-paragraphs",dragPredicate);
            if(await note.Script(dragPredicate)!="true")await DiagnoseStandardDrag(note,Point,Mouse);
            await Check("pointer-drag-keeps-note-content-intact","editor.innerHTML===standardOriginal");
            await note.Script("standardFixture('<p>متن آغاز</p>')");await Key("End",35,2);
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.imeSetComposition",JsonSerializer.Serialize(new{text=" می‌توانم",selectionStart=8,selectionEnd=8}));
            await Check("ime-keeps-composition-live","composing&&editor.textContent.includes('می‌توانم')");
            await Type(" می‌توانم");
            await Check("ime-commit-preserves-zwnj-and-single-insertion","!composing&&editor.textContent==='متن آغاز می‌توانم'");
            await Key("Z",90,2);
            await Check("ime-undo-removes-one-composition","editor.textContent==='متن آغاز'");
            checks.Add(new{name="standard-editing-tests-stay-offscreen",passed=note.Opacity==0&&!note.ShowActivated&&!note.ShowInTaskbar&&!note.Topmost&&note.Left< -10000&&note.Top< -10000});
        }
        finally{foreach(var window in editingSession.Windows.ToArray())window.ClosePermanently();}
    }

    private async Task DiagnoseStandardDrag(MainWindow note,Func<int,int,Task<(double X,double Y)>> point,Func<string,(double X,double Y),int,int,int,Task> mouse)
    {
        var evidence=new List<object>();
        var sendNative=typeof(WebView2CompositionControl).GetMethod("SendMouseInput",BindingFlags.Instance|BindingFlags.NonPublic)!;
        try
        {
            // Isolated in-memory control sample only; production keeps frame-src none.
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.setBypassCSP","{\"enabled\":true}");
            foreach(var variant in new[]{"editor-cdp-count1","editor-cdp-focused","frame-cdp","frame-cdp-focused","editor-native","editor-native-focused"})
            {
                bool frame=variant.StartsWith("frame"),focused=variant.EndsWith("focused"),native=variant.Contains("native");
                await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled",JsonSerializer.Serialize(new{enabled=focused}));
                await note.Script("standardFixture('<p>Alpha bravo charlie.</p><p>Second paragraph.</p>')");
                if(frame)
                {
                    await note.Script("""
                        window.standardFrame=document.createElement('iframe');standardFrame.style.cssText='position:fixed;left:10px;top:45px;width:580px;height:300px;z-index:99999;background:white;border:0';
                        standardFrame.srcdoc='<html><body><div id="plain" contenteditable="true" style="font:18px/2 sans-serif"><p>Alpha bravo charlie.</p><p>Second paragraph.</p></div></body></html>';document.body.append(standardFrame);
                        """);
                    await note.WaitFor("standardFrame.contentDocument?.getElementById('plain')?.children.length===2");
                    await note.Script("""
                        (()=>{window.standardFrameEvents=[];const d=standardFrame.contentDocument;d.getElementById('plain').focus();d.getSelection().removeAllRanges();
                        for(const type of ['mousedown','mouseup','mousemove','pointerdown','pointerup','pointermove','click','selectstart'])d.addEventListener(type,e=>standardFrameEvents.push({type,detail:e.detail,buttons:e.buttons,x:e.clientX,y:e.clientY,selected:d.getSelection().toString()}));
                        })();
                        """);
                }
                async Task<(double X,double Y)> At(int paragraph,int offset)
                {
                    if(!frame)return await point(paragraph,offset);
                    using var value=JsonDocument.Parse(await note.Script("(()=>{const d=standardFrame.contentDocument,n=d.getElementById('plain').children["+paragraph+"].firstChild,r=d.createRange();r.setStart(n,"+offset+");r.setEnd(n,"+(offset+1)+");const b=r.getBoundingClientRect(),f=standardFrame.getBoundingClientRect();return{x:f.x+b.x+b.width/2,y:f.y+b.y+b.height/2}})()"));
                    return(value.RootElement.GetProperty("x").GetDouble(),value.RootElement.GetProperty("y").GetDouble());
                }
                async Task Send(string kind,(double X,double Y) at,int buttons)
                {
                    if(!native){await mouse(kind,at,kind=="mouseMoved"?(variant.EndsWith("count1")?1:0):1,buttons,0);return;}
                    var dpi=VisualTreeHelper.GetDpi(note.Browser);
                    var type=kind=="mouseMoved"?CoreWebView2MouseEventKind.Move:kind=="mousePressed"?CoreWebView2MouseEventKind.LeftButtonDown:CoreWebView2MouseEventKind.LeftButtonUp;
                    sendNative.Invoke(note.Browser,[type,buttons==1?CoreWebView2MouseEventVirtualKeys.LeftButton:CoreWebView2MouseEventVirtualKeys.None,0u,new System.Drawing.Point((int)Math.Round(at.X*dpi.DpiScaleX),(int)Math.Round(at.Y*dpi.DpiScaleY))]);
                    await Task.Delay(20);
                }
                await Task.Delay(600);var from=await At(0,2);var to=await At(1,9);
                await Send("mouseMoved",from,0);await Send("mousePressed",from,1);
                for(int step=1;step<=8;step++){await Send("mouseMoved",(from.X+(to.X-from.X)*step/8,from.Y+(to.Y-from.Y)*step/8),1);await Task.Delay(20);}
                await Send("mouseReleased",to,0);
                var state=await note.Script(frame?"({focused:standardFrame.contentDocument.hasFocus(),text:standardFrame.contentWindow.getSelection().toString(),events:standardFrameEvents})":"({...standardSelection(),focused:document.hasFocus()})");
                evidence.Add(new{variant,state=JsonSerializer.Deserialize<JsonElement>(state)});
                if(frame)await note.Script("standardFrame.remove();delete window.standardFrame");
            }
        }
        finally
        {
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled","{\"enabled\":false}");
            await note.Script("window.standardFrame?.remove();delete window.standardFrame");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.setBypassCSP","{\"enabled\":false}");
            await File.WriteAllTextAsync(Path.Combine(testOutput,"standard-editing-drag-diagnostics.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
