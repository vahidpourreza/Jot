using System.IO;
using System.Text.Json;
using PixelBounds=System.Drawing.Rectangle;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyNoteFullscreen(List<object> checks)
    {
        var s=new JotSession(true,Path.Combine(testOutput,"note-fullscreen")){ExerciseLifecycle=true};
        var frames=new List<object>();
        void Check(string name,bool passed)=>checks.Add(new{name="note-fullscreen-"+name,passed});
        try
        {
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var note=await s.NewNote();await note.WaitFor("window.jotReady===true");
            note.Left=-31500;note.Top=-31400;note.Width=452;note.Height=338;note.UpdateLayout();await Task.Delay(50);
            var normal=note.ReadWindowPixels();var html=await note.Script("editor.innerHTML");
            var growing=note.SetNoteFullscreen(true);
            Check("enter-is-immediate",growing.IsCompletedSuccessfully&&!note.IsFullscreenTransitioning);
            await growing;
            Check("enter-keeps-normal-session-bounds",note.CaptureNoteLayout().Width==normal.Width&&note.CaptureNoteLayout().X==normal.X);
            var full=note.ReadWindowPixels();var entering=note.FullscreenFrameSamples.ToArray();frames.Add(new{phase="enter",bounds=entering});
            Check("maximize-has-only-final-work-area-rectangle",entering.Length==1&&entering[0]==full&&full==new PixelBounds(-32000,-32000,1200,760));
            Check("enter-does-not-enable-topmost",!note.Topmost&&!note.ShowActivated&&note.Opacity==0);
            await note.Script("noteFullscreen=true;updateFullscreenButton()");
            Check("active-button-has-no-heavy-background",await note.Script("getComputedStyle(document.getElementById('fullscreenButton')).backgroundColor==='rgba(0, 0, 0, 0)'")=="true");
            var shrinking=note.SetNoteFullscreen(false);
            Check("exit-is-immediate",shrinking.IsCompletedSuccessfully&&!note.IsFullscreenTransitioning);
            await shrinking;var exiting=note.FullscreenFrameSamples.ToArray();frames.Add(new{phase="exit",bounds=exiting});
            Check("exit-has-only-final-native-rectangle",exiting.Length==1&&exiting[0]==normal);
            Check("exit-restores-exact-native-small-rectangle",note.ReadWindowPixels()==normal&&!note.IsFullscreenTransitioning&&!note.IsWindowFullscreen);
            Check("fullscreen-preserves-note-content",html==await note.Script("editor.innerHTML"));
            for(int cycle=0;cycle<3;cycle++){await note.SetNoteFullscreen(true);await note.SetNoteFullscreen(false);Check("repeat-restores-exact-rectangle-"+cycle,note.ReadWindowPixels()==normal);}
            var first=note.SetNoteFullscreen(true);var second=note.SetNoteFullscreen(false);await Task.WhenAll(first,second);
            Check("rapid-opposite-requests-serialize-without-losing-small-bounds",note.ReadWindowPixels()==normal&&!note.IsWindowFullscreen);
            await note.Script("request('note-fullscreen',{enabled:true,animate:true}).then(()=>window.legacyFullscreen=true)");await note.WaitFor("window.legacyFullscreen===true");
            Check("old-animation-payload-cannot-reenable-tween",note.FullscreenFrameSamples.Count==1&&note.IsWindowFullscreen);
            await note.SetNoteFullscreen(false);
            await VerifyFullscreenHeaderDrag(note,normal,checks);
            var restored=note.ReadWindowPixels();
            await note.SetNoteFullscreen(true);await s.Quit();
            var saved=(await s.Store.LoadWindowSession()).Single();
            Check("quit-fullscreen-saves-latest-small-bounds-and-closes-safely",s.Windows.Count==0&&saved.Width==restored.Width&&saved.Height==restored.Height&&saved.X==restored.X&&saved.Y==restored.Y&&note.RuntimeErrors.Count==0);
            await VerifyHomeMaximizeDrag(checks);
        }
        finally{foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
        await File.WriteAllTextAsync(Path.Combine(testOutput,"fullscreen-frames.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
    }

    private async Task VerifyHomeMaximizeDrag(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="home-maximize-"+name,passed});
        var session=new JotSession(true,Path.Combine(testOutput,"home-maximize-drag")){ExerciseLifecycle=true};
        try
        {
            var home=session.Home();await home.WaitFor("window.jotReady===true");
            home.Left=-31500;home.Top=-31400;home.Width=520;home.Height=540;home.UpdateLayout();
            await Task.Delay(50);
            var normal=home.ReadWindowPixels();
            await home.SetWindowFullscreen(true);
            Check("uses-monitor-work-area-and-keeps-taskbar-space",home.ReadWindowPixels()==new PixelBounds(-32000,-32000,1200,760)&&!home.Topmost);
            home.TestDragPoint=new(-31300,-31400);
            await home.DragHeader(JsonSerializer.SerializeToElement(new{restore=false,anchorX=.5,anchorY=15}));
            Check("ordinary-click-does-not-restore",home.IsWindowFullscreen);
            var expected=PlaceDraggedNote(normal,home.TestDragPoint.Value,.5,15,System.Windows.Media.VisualTreeHelper.GetDpi(home).DpiScaleX);
            await home.DragHeader(JsonSerializer.SerializeToElement(new{restore=true,anchorX=.5,anchorY=15}));
            Check("header-drag-restores-home-under-pointer",!home.IsWindowFullscreen&&home.ReadWindowPixels()==expected&&home.TestHostActions.Count(a=>a=="native-drag-handoff")==1);
            await home.SetWindowFullscreen(true);await home.SetWindowFullscreen(false);
            Check("restore-retains-new-dragged-position",home.ReadWindowPixels()==expected);
            await home.SetWindowFullscreen(true);await home.WaitFor("fullscreen&&innerWidth===1200&&innerHeight===760");
            await home.Script("""
                window.workspaceDragEvent=(type,target='workspaceHandle',x=600,y=2,buttons=1,button=0)=>document.getElementById(target).dispatchEvent(new PointerEvent(type,{bubbles:true,pointerId:9,isPrimary:true,pointerType:'mouse',clientX:x,clientY:y,buttons,button}));
                workspaceDragEvent('pointerdown');workspaceDragEvent('pointerup','workspaceHandle',600,2,0);
                """);await Task.Delay(40);
            Check("shared-header-click-does-not-restore",home.IsWindowFullscreen);
            await home.Script("workspaceDragEvent('pointerdown');workspaceDragEvent('pointermove','workspaceHandle',600,6);workspaceDragEvent('pointerup','workspaceHandle',600,6,0)");await Task.Delay(40);
            Check("shared-header-four-pixel-move-stays-maximized",home.IsWindowFullscreen);
            await home.Script("workspaceDragEvent('pointerdown','workspaceClose');workspaceDragEvent('pointermove','workspaceHandle',600,24);workspaceDragEvent('pointerup','workspaceHandle',600,24,0)");await Task.Delay(40);
            Check("shared-header-window-buttons-do-not-start-drag",home.IsWindowFullscreen);
            await home.Script("workspaceDragEvent('pointerdown','workspaceHandle',600,2,2,2);workspaceDragEvent('pointermove','workspaceHandle',600,24,2,2);workspaceDragEvent('pointerup','workspaceHandle',600,24,0,2)");await Task.Delay(40);
            Check("shared-header-right-button-does-not-restore",home.IsWindowFullscreen);
            foreach(var cancel in new[]{"pointercancel","lostpointercapture","pointerup"})
            {
                await home.Script("workspaceDragEvent('pointerdown');workspaceDragEvent('"+cancel+"');workspaceDragEvent('pointermove','workspaceHandle',600,24)");await Task.Delay(30);
                Check("shared-header-cancel-clears-gesture-"+cancel,home.IsWindowFullscreen);
            }
            var handoffs=home.TestHostActions.Count(a=>a=="native-drag-handoff");
            await home.Script("window.workspacePointerId=null;document.getElementById('workspaceHandle').addEventListener('pointerdown',event=>window.workspacePointerId=event.pointerId,{once:true})");
            async Task Mouse(string type,int y,int buttons)=>await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=600,y,button=type=="mouseMoved"?"none":"left",buttons,clickCount=type=="mouseMoved"?0:1}));
            await Mouse("mouseMoved",2,0);await Mouse("mousePressed",2,1);
            Check("shared-header-trusted-pointer-captures-blank-header",await home.Script("document.getElementById('workspaceHandle').hasPointerCapture(workspacePointerId)")=="true");
            try{await Mouse("mouseMoved",24,1);await home.WaitFor("!fullscreen");}
            finally{await Mouse("mouseReleased",24,0);}
            var pointerExpected=PlaceDraggedNote(expected,home.TestDragPoint.Value,.5,2,System.Windows.Media.VisualTreeHelper.GetDpi(home).DpiScaleX);
            Check("shared-header-trusted-drag-restores-under-pointer",home.ReadWindowPixels()==pointerExpected&&home.TestHostActions.Count(a=>a=="native-drag-handoff")==handoffs+1);
            Check("shared-header-releases-pointer-and-restores-button-icon",await home.Script("!document.getElementById('workspaceHandle').hasPointerCapture(workspacePointerId)&&document.getElementById('workspaceMaximize').title==='Maximize'")=="true");
            await home.WaitFor("Math.abs(innerWidth-"+JsonSerializer.Serialize(home.Browser.ActualWidth)+")<1&&Math.abs(innerHeight-"+JsonSerializer.Serialize(home.Browser.ActualHeight)+")<1");
            await home.DoubleClickControl("#workspaceHandle");await home.WaitFor("fullscreen&&innerWidth===1200&&innerHeight===760");
            Check("blank-header-double-click-maximizes-with-taskbar-space",home.ReadWindowPixels()==new PixelBounds(-32000,-32000,1200,760));
            await home.DoubleClickControl("#workspaceHandle");await home.WaitFor("!fullscreen");
            Check("blank-header-double-click-restores-previous-size",home.ReadWindowPixels()==pointerExpected);
            home.TestDragPoint=null;
            Check("tests-never-interact-with-desktop",home.Opacity==0&&!home.ShowActivated&&!home.ShowInTaskbar&&!home.Topmost&&home.Left< -10000&&home.Top< -10000);
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }

    private async Task VerifyFullscreenHeaderDrag(MainWindow note,PixelBounds normal,List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="fullscreen-drag-"+name,passed});
        foreach(double dpi in new[]{1,1.25,1.5,2})foreach(double anchor in new[]{0,.25,.5,.9,1})
        {
            var pointer=new System.Drawing.Point(-30000,-31000);
            var placed=PlaceDraggedNote(normal,pointer,anchor,17,dpi);
            int inset=(int)Math.Round(9*dpi);
            Check($"anchor-{anchor}-scale-{dpi}",placed.Width==normal.Width&&placed.Height==normal.Height&&Math.Abs(placed.X+inset+anchor*(normal.Width-2*inset)-pointer.X)<=.5&&Math.Abs(placed.Y+inset+17*dpi-pointer.Y)<=.5);
        }
        await note.SetNoteFullscreen(true);await note.WaitFor("noteFullscreen");
        note.TestDragPoint=new(-31400,-31600);
        await note.Script("""
            window.dragEvent=(type,target='handle',x=300,y=15,buttons=1,button=0)=>document.getElementById(target).dispatchEvent(new PointerEvent(type,{bubbles:true,pointerId:7,isPrimary:true,pointerType:'mouse',clientX:x,clientY:y,buttons,button}));
            dragEvent('pointerdown');dragEvent('pointerup','handle',300,15,0);
            """);await Task.Delay(40);
        Check("click-does-not-exit",note.IsNoteFullscreen);
        await note.Script("dragEvent('pointerdown');dragEvent('pointermove','handle',300,19);dragEvent('pointermove','handle',400,15);dragEvent('pointermove','handle',400,1);dragEvent('pointerup','handle',400,1,0)");await Task.Delay(40);
        Check("small-horizontal-upward-movements-do-not-exit",note.IsNoteFullscreen);
        await note.Script("dragEvent('pointerdown','pinButton');dragEvent('pointermove','handle',300,60);dragEvent('pointerup','handle',300,60,0)");await Task.Delay(40);
        Check("header-button-is-not-drag-handle",note.IsNoteFullscreen);
        await note.Script("dragEvent('pointerdown','handle',300,15,2,2);dragEvent('pointermove','handle',300,60,2,2)");await Task.Delay(40);
        Check("right-click-does-not-exit",note.IsNoteFullscreen);
        foreach(var cancel in new[]{"pointercancel","lostpointercapture","pointerup"})
        {
            await note.Script($"dragEvent('pointerdown');dragEvent('{cancel}');dragEvent('pointermove','handle',300,60)");await Task.Delay(30);
            Check("cancel-clears-gesture-"+cancel,note.IsNoteFullscreen);
        }
        await note.Script("dragEvent('pointerdown');dragEvent('pointermove','handle',300,60,0)");await Task.Delay(30);
        Check("released-button-does-not-restore",note.IsNoteFullscreen);
        var expected=PlaceDraggedNote(normal,note.TestDragPoint.Value,.25,15,1);
        await note.Script("dragEvent('pointerdown');dragEvent('pointermove','handle',300,30)");await note.WaitFor("!noteFullscreen");
        Check("downward-drag-restores-small-window-under-pointer",note.ReadWindowPixels()==expected&&!note.IsFullscreenTransitioning);
        Check("drag-dispatches-native-handoff-without-desktop-input",note.TestHostActions.Count(a=>a=="native-drag-handoff")==1&&!note.Topmost&&note.Opacity==0&&!note.ShowActivated);
        Check("renderer-exit-button-resynchronizes",await note.Script("document.getElementById('fullscreenButton').ariaPressed==='false'&&document.getElementById('fullscreenButton').title==='Maximize'&&document.querySelector('#fullscreenButton svg').innerHTML===JotDesign.icon('window-maximize').innerHTML")=="true");
        Check("session-follows-dragged-position",note.CaptureNoteLayout().X==expected.X&&note.CaptureNoteLayout().Y==expected.Y);
        await note.SetNoteFullscreen(true);await note.SetNoteFullscreen(false);
        Check("next-button-exit-restores-new-dragged-position",note.ReadWindowPixels()==expected);
        await note.SetNoteFullscreen(true);await note.WaitFor("noteFullscreen");
        await note.WaitFor("innerWidth===1200&&innerHeight===760");
        await note.Script("window.headerPointerTrace=[];for(const type of ['pointerdown','pointermove','pointerup','gotpointercapture','lostpointercapture'])document.addEventListener(type,e=>headerPointerTrace.push({type,target:e.target.id,trusted:e.isTrusted,x:e.clientX,y:e.clientY,buttons:e.buttons}),true)");
        async Task Mouse(string type,int y,int buttons)=>await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=600,y,button=type=="mouseMoved"?"none":"left",buttons,clickCount=type=="mouseMoved"?0:1}));
        await Mouse("mouseMoved",15,0);await Mouse("mousePressed",15,1);
        Check("trusted-pointer-captures-header",await note.Script("!!headerDrag&&document.getElementById('handle').hasPointerCapture(headerDrag.id)")=="true");
        await Mouse("mouseMoved",55,1);
        try{await note.WaitFor("!noteFullscreen");}
        finally{await File.WriteAllTextAsync(Path.Combine(testOutput,"fullscreen-pointer-trace.json"),await note.Script("({events:headerPointerTrace,drag:headerDrag,width:innerWidth,height:innerHeight})"));await Mouse("mouseReleased",55,0);}
        var trustedExpected=PlaceDraggedNote(normal,note.TestDragPoint.Value,.5,15,1);
        Check("trusted-downward-drag-restores-and-releases-capture",note.ReadWindowPixels()==trustedExpected&&await note.Script("headerDrag===null")=="true");
        Check("trusted-drag-only-starts-one-native-handoff",note.TestHostActions.Count(a=>a=="native-drag-handoff")==2);
        note.TestDragPoint=null;
    }
}
