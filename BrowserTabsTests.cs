using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyBrowserTabs(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="browser-tabs-"+name,passed,evidence});
        var sessions=new List<JotSession>();
        var directory=Path.Combine(testOutput,"browser-tabs");
        var session=new JotSession(true,directory){ExerciseLifecycle=true};sessions.Add(session);
        MainWindow? home=null;
        static string Selector(string id)=>"[role=tab][data-workspace-id='"+id+"']";
        const string idle="window.JotWorkspace&&document.getElementById('workspaceTabs').ariaBusy==='false'";
        try
        {
            await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="tab"}));
            var ids=new List<string>();
            foreach(var color in new[]{"crimson","teal","blue","orange","pink"})
            {var id=await session.Store.Create();ids.Add(id);await session.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Tab "+ids.Count,color}));}
            home=session.Home();home.Width=1000;home.Height=540;await home.WaitFor("window.jotReady===true");
            foreach(var id in ids)await home.SwitchNoteTab(id);
            await home.RightClickControl(Selector(ids[1]));
            Check("context-distinguishes-tab-pin-from-window-pin",await home.Script("['tab-pin','workspace-pin','tab-reopen'].every(id=>document.querySelector('#contentContextMenu [data-action='+id+']'))&&document.querySelector('#contentContextMenu [data-action=tab-reopen]').disabled")=="true");
            await home.ClickControl("#contentContextMenu [data-action=tab-pin]");
            await home.WaitFor(idle+"&&JotWorkspace.tabs.find(note=>note.id==="+JsonSerializer.Serialize(ids[1])+").tabPinned");
            Check("pin-moves-tab-after-home-without-selecting-or-window-pin",home.NoteTabIds[0]==ids[1]&&home.PinnedTabIds.SetEquals([ids[1]])&&home.NoteId==ids[^1]&&!home.Topmost&&
                !(await session.Store.LoadPreferences()).GetProperty("workspacePinned").GetBoolean()&&!(await session.Store.LoadNote(ids[1]))!.Value.GetProperty("libraryPinned").GetBoolean());
            await home.SetTabPinned(ids[3],true);
            Check("pinned-tab-keeps-visible-title-and-pin-with-home-fixed",await home.Script("(()=>{const p=document.querySelector('.workspace-pinned-tabs [role=tab]'),r=p.getBoundingClientRect(),label=p.querySelector('.tab-title'),pin=p.querySelector('.tab-pin-marker');return document.querySelector('.workspace-tabs [role=tab]').dataset.workspaceId==='home'&&p.ariaLabel.includes('pinned tab')&&!label.hidden&&label.getBoundingClientRect().width>24&&pin.getClientRects().length>0&&r.width>=100&&!document.querySelector('.workspace-tab[data-home=true] .tab-close')})()")=="true");
            await home.WaitFor("document.querySelectorAll('.workspace-pinned-tabs>.workspace-tab').length===2");
            await home.Script("window.browserTabDragEvents=[];for(const type of ['pointerdown','pointermove','pointerup','pointercancel','gotpointercapture','lostpointercapture'])document.addEventListener(type,event=>{browserTabDragEvents.push({type,target:event.target.id,id:event.target.closest?.('[data-workspace-id]')?.dataset.workspaceId,pointerId:event.pointerId,buttons:event.buttons,x:event.clientX,y:event.clientY,focused:document.hasFocus(),dragging:document.getElementById('workspaceTabs').dataset.dragging,order:[...document.querySelectorAll('.workspace-tabs [role=tab]')].map(button=>button.dataset.workspaceId)});if(browserTabDragEvents.length>80)browserTabDragEvents.shift()},true);for(const type of ['blur','focus'])window.addEventListener(type,()=>browserTabDragEvents.push({type,focused:document.hasFocus()}))");
            await home.Script("window.dragCleanupSnapshot=()=>({dragging:document.getElementById('workspaceTabs').dataset.dragging||null,tabs:[...document.querySelectorAll('.workspace-tabs .workspace-tab')].map(tab=>({id:tab.querySelector('[role=tab]').dataset.workspaceId,transform:tab.style.transform,willChange:tab.style.willChange,computedTransform:getComputedStyle(tab).transform,animations:tab.getAnimations().map(animation=>({state:animation.playState,pending:animation.pending,currentTime:animation.currentTime,startTime:animation.startTime,transition:animation.transitionProperty||null,properties:animation.effect.getKeyframes().map(frame=>Object.keys(frame))}))}))})");
            var sendMouse=typeof(WebView2CompositionControl).GetMethod("SendMouseInput",BindingFlags.Instance|BindingFlags.NonPublic)!;
            async Task Mouse(string type,double x,double y,int buttons)
            {
                // CDP loses held-button capture in this composition runtime,
                // before any tab moves. Match the standard-editing fixture by
                // sending real WebView controller input, not OS desktop input.
                var dpi=VisualTreeHelper.GetDpi(home.Browser);
                var kind=type=="mouseMoved"?CoreWebView2MouseEventKind.Move:type=="mousePressed"?CoreWebView2MouseEventKind.LeftButtonDown:CoreWebView2MouseEventKind.LeftButtonUp;
                sendMouse.Invoke(home.Browser,[kind,buttons==1?CoreWebView2MouseEventVirtualKeys.LeftButton:CoreWebView2MouseEventVirtualKeys.None,0u,new System.Drawing.Point((int)Math.Round(x*dpi.DpiScaleX),(int)Math.Round(y*dpi.DpiScaleY))]);
                await Task.Delay(20);
            }
            async Task Drag(string from,string to,bool escape=false,bool after=false)
            {
                await Task.Delay(600); // A separate gesture, not a title double-click.
                await home.Script("window.dragStartOrder=[...document.querySelectorAll('.workspace-tabs [role=tab]')].map(button=>button.dataset.workspaceId).join();window.dragMutations=[];window.dragObserver=new MutationObserver(records=>dragMutations.push(...records.filter(record=>record.type==='childList'&&record.target.matches('.workspace-note-tabs,.workspace-pinned-tabs'))));dragObserver.observe(document.getElementById('workspaceTabs'),{subtree:true,childList:true});window.dragSource=document.querySelector("+JsonSerializer.Serialize(Selector(from))+").closest('.workspace-tab');window.dragLeft=dragSource.getBoundingClientRect().left");
                using var point=JsonDocument.Parse(await home.Script("(()=>{const a=document.querySelector("+JsonSerializer.Serialize(Selector(from))+").getBoundingClientRect(),b=document.querySelector("+JsonSerializer.Serialize(Selector(to))+").getBoundingClientRect();return {x:a.x+a.width/2,y:a.y+a.height/2,to:"+(after?"b.right-4":"b.x+4")+"}})()"));
                var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();var target=point.RootElement.GetProperty("to").GetDouble();
                await Mouse("mouseMoved",x,y,0);await Mouse("mousePressed",x,y,1);
                for(int step=1;step<=5;step++)
                {
                    await Mouse("mouseMoved",x+(target-x)*step/5,y,1);
                    if(step==3)
                    {
                        Check("drag-keeps-dom-stable-"+from+"-"+escape,await home.Script("dragMutations.length===0&&dragStartOrder===[...document.querySelectorAll('.workspace-tabs [role=tab]')].map(button=>button.dataset.workspaceId).join()")=="true");
                        Check("dragged-tab-follows-pointer-"+from+"-"+escape,await home.Script("Math.abs(dragSource.getBoundingClientRect().left-dragLeft)>10&&dragSource.style.transform.startsWith('translateX(')&&getComputedStyle(dragSource).transitionDuration==='0s'")=="true");
                    }
                }
                await home.Script("dragObserver.disconnect()");
                if(escape)foreach(var type in new[]{"rawKeyDown","keyUp"})await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key="Escape",code="Escape",windowsVirtualKeyCode=27}));
                if(escape)Check("escape-removes-drag-visuals-immediately",await home.Script("!document.getElementById('workspaceTabs').dataset.dragging&&![...document.querySelectorAll('.workspace-tabs .workspace-tab')].some(tab=>tab.style.transform||tab.style.willChange||tab.getAnimations().some(animation=>animation.playState==='running'&&animation.effect.getKeyframes().some(frame=>'transform' in frame)))")=="true",JsonSerializer.Deserialize<JsonElement>(await home.Script("dragCleanupSnapshot()")));
                await Mouse("mouseReleased",target,y,0);
                await home.WaitFor("!document.getElementById('workspaceTabs').dataset.dragging");
                await Task.Delay(180);
                // A drop may move a different tab under the pointer and start
                // its normal color/border hover transitions. Those are not
                // leaked drag motion: require all transform state to be gone.
                Check("drag-cleans-transforms-and-animation-"+from+"-"+escape,await home.Script("!document.querySelector('.workspace-tabs [data-dragging=true]')&&![...document.querySelectorAll('.workspace-tabs .workspace-tab')].some(tab=>tab.style.transform||tab.style.willChange||getComputedStyle(tab).transform!=='none'||tab.getAnimations().some(animation=>(animation.playState==='running'||animation.pending)&&animation.effect.getKeyframes().some(frame=>'transform' in frame)))")=="true",JsonSerializer.Deserialize<JsonElement>(await home.Script("dragCleanupSnapshot()")));
            }
            await Drag(ids[4],ids[0]);
            var normalOrder=new[]{ids[1],ids[3],ids[4],ids[0],ids[2]};
            await home.WaitFor(idle+"&&JotWorkspace.tabs.map(note=>note.id).join()==="+JsonSerializer.Serialize(string.Join(',',normalOrder)));
            Check("real-pointer-drag-reorders-normal-tabs-without-switching",home.NoteTabIds.SequenceEqual(normalOrder)&&home.NoteId==ids[4]);
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await Drag(ids[4],ids[2],after:true);
            await home.WaitFor(idle+"&&JotWorkspace.tabs.map(note=>note.id).join()==="+JsonSerializer.Serialize(string.Join(',',new[]{ids[1],ids[3],ids[0],ids[2],ids[4]})));
            Check("reduced-motion-drag-can-reach-last-slot",home.NoteTabIds[^1]==ids[4]&&home.NoteId==ids[4]);
            await Drag(ids[4],ids[0]);
            await home.WaitFor(idle+"&&JotWorkspace.tabs.map(note=>note.id).join()==="+JsonSerializer.Serialize(string.Join(',',normalOrder)));
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");
            await Drag(ids[3],ids[1]);
            var pinnedOrder=new[]{ids[3],ids[1],ids[4],ids[0],ids[2]};
            await home.WaitFor(idle+"&&JotWorkspace.tabs.map(note=>note.id).join()==="+JsonSerializer.Serialize(string.Join(',',pinnedOrder)));
            Check("real-pointer-drag-reorders-pinned-section",home.NoteTabIds.SequenceEqual(pinnedOrder)&&home.PinnedTabIds.SetEquals([ids[1],ids[3]]));
            await Drag(ids[2],ids[4],escape:true);
            Check("escape-cancels-drag-and-releases-pointer-capture",home.NoteTabIds.SequenceEqual(pinnedOrder)&&await home.Script("!document.querySelector('[data-dragging=true]')&&![...document.querySelectorAll('.workspace-tabs,.workspace-tabs [role=tab]')].some(button=>button.hasPointerCapture(1))")=="true");
            await home.ReorderNoteTabs([ids[4],ids[1],ids[0],ids[3],ids[2]]);
            Check("reorder-cannot-cross-pin-boundary",home.NoteTabIds.SequenceEqual([ids[1],ids[3],ids[4],ids[0],ids[2]]));
            var beforeInvalid=home.NoteTabIds.ToArray();bool invalidRejected=false;
            try{await home.ReorderNoteTabs([ids[1],ids[1]]);}catch(InvalidDataException){invalidRejected=true;}
            Check("stale-or-duplicate-reorder-does-not-drop-tabs",invalidRejected&&home.NoteTabIds.SequenceEqual(beforeInvalid));
            await home.SetTabPinned(ids[1],false);
            Check("unpin-enters-start-of-regular-section",home.NoteTabIds.SequenceEqual([ids[3],ids[1],ids[4],ids[0],ids[2]])&&!home.PinnedTabIds.Contains(ids[1]));
            await home.SwitchNoteTab(ids[3]);
            await home.Script("editor.innerHTML='<p><b>Final pinned draft</b></p>';onEdit();clearTimeout(saveTimer)");
            await home.CloseNoteTab(ids[3]);
            Check("closing-pin-saves-draft-and-retains-library-note",home.CanReopenClosedTab&&!home.PinnedTabIds.Contains(ids[3])&&(await session.Store.LoadNote(ids[3]))!.Value.GetProperty("plain").GetString()=="Final pinned draft");
            await home.Script("editor.focus()");
            foreach(var type in new[]{"rawKeyDown","keyUp"})await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key="T",code="KeyT",windowsVirtualKeyCode=84,modifiers=10}));
            await home.WaitFor(idle+"&&JotWorkspace.tabs.some(note=>note.id==="+JsonSerializer.Serialize(ids[3])+"&&note.tabPinned)&&model.activeId==="+JsonSerializer.Serialize(ids[3]));
            Check("ctrl-shift-t-reopens-original-position-pin-and-content",home.NoteTabIds[0]==ids[3]&&home.PinnedTabIds.Contains(ids[3])&&home.NoteId==ids[3]&&await home.Script("editor.textContent==='Final pinned draft'")=="true");
            await home.CloseNoteTab(ids[4]);await home.CloseNoteTab(ids[0]);
            await session.Store.Delete(ids[0]);await session.ReopenClosedTab(home);
            Check("reopen-skips-deleted-and-restores-next-most-recent",home.NoteId==ids[4]&&home.NoteTabIds.Contains(ids[4])&&!home.NoteTabIds.Contains(ids[0])&&!await session.Store.Contains(ids[0]));
            await home.CloseNoteTab(ids[2]);await home.SwitchNoteTab(ids[2]);
            var reopenedOrder=home.NoteTabIds.ToArray();await session.ReopenClosedTab(home);
            Check("reopen-skips-already-open-notes-without-duplication",home.NoteTabIds.SequenceEqual(reopenedOrder)&&!home.CanReopenClosedTab);
            await home.SwitchHomeView(true);await home.CloseNoteTab("settings");
            Check("settings-close-is-not-note-reopen-history",!home.CanReopenClosedTab&&!home.SettingsTabOpen);
            await session.OpenNoteWindow(ids[2]);
            Check("opening-in-window-is-not-a-closed-tab",!home.CanReopenClosedTab&&session.Windows.Count(window=>window.ContainsNote(ids[2]))==1);
            var latest=(await session.OpenAsTab(ids[2]));
            Check("transfer-back-preserves-other-tab-pins",ReferenceEquals(home,latest)&&home.PinnedTabIds.SetEquals([ids[3]]));
            // Bound the history without creating more editors or touching real notes.
            for(int i=0;i<24;i++)home.closedNoteTabs.Add(new(Guid.NewGuid().ToString(),0,false));
            await home.CloseNoteTab(ids[2]);
            Check("closed-history-is-bounded",home.closedNoteTabs.Count<=20,home.closedNoteTabs.Count);
            // Discard synthetic missing IDs through the production reopen path.
            await session.ReopenClosedTab(home);await session.ReopenClosedTab(home);
            Check("missing-history-is-drained-safely",!home.CanReopenClosedTab&&home.NoteTabIds.Distinct().Count()==home.NoteTabIds.Count);
            await home.SwitchHomeView(true);var savedOrder=home.NoteTabIds.ToArray();await session.PrepareQuit();
            var snapshot=(await session.Store.LoadWorkspaceSession())!;
            Check("snapshot-preserves-pins-order-settings",snapshot.Tabs.SequenceEqual(savedOrder)&&snapshot.PinnedTabs!.SequenceEqual([ids[3]])&&snapshot.ActiveId=="settings"&&snapshot.SettingsOpen);
            var oldSnapshot=new SavedWorkspace(savedOrder,"home",false,0,0,600,500);
            Check("legacy-workspace-without-pins-remains-valid",oldSnapshot.IsValid);
            Check("invalid-pinned-membership-is-rejected",!(oldSnapshot with{PinnedTabs=[Guid.NewGuid().ToString()]}).IsValid);
            await home.SetTabPinned(ids[1],true);await home.SetTabPinned(ids[2],true);
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,900})
            {
                home.Width=width;await session.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));await home.SwitchNoteTab(ids[4]);
                await home.WaitFor("document.documentElement.dataset.theme==="+JsonSerializer.Serialize(theme));
                Check("pinned-and-fixed-controls-fit-"+theme+"-"+width,await home.Script("(()=>{const p=document.querySelector('.workspace-pinned-tabs').getBoundingClientRect(),n=document.querySelector('.workspace-note-tabs').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth&&p.width>0&&p.right<=innerWidth&&n.width>=72&&[...document.querySelectorAll('.index-window-actions button')].filter(b=>b.getClientRects().length).every(b=>{const r=b.getBoundingClientRect();return r.right<=innerWidth&&r.left>=0})})()")=="true");
                if(width==360){
                    Check("compact-pins-keep-picker-instead-of-crowded-arrows-"+theme,await home.Script("!document.querySelector('.workspace-tab-all').hidden&&document.querySelector('.workspace-tab-all').getClientRects().length>0&&!document.querySelector('.workspace-tab-previous').getClientRects().length&&!document.querySelector('.workspace-tab-next').getClientRects().length")=="true");
                    await home.ClickControl(".workspace-tab-all");
                    Check("picker-lists-every-overflowing-pin-"+theme,await home.Script("document.querySelectorAll('.workspace-tabs-menu [data-pinned=true]').length===3")=="true");
                    await home.ClickControl(Selector(ids[4]));
                }
                await home.Capture("browser-tabs-"+theme+"-"+width);
            }
            await home.SetTabPinned(ids[1],false);await home.SetTabPinned(ids[2],false);await home.ReorderNoteTabs(savedOrder);
            await session.PrepareQuit();foreach(var window in session.Windows.ToArray())window.ClosePermanently();
            var restoredSession=new JotSession(true,directory){ExerciseLifecycle=true};sessions.Add(restoredSession);await restoredSession.StartWork();
            var restored=restoredSession.Windows.Single(window=>window.Mode=="home");await restored.WaitFor("window.jotReady===true");
            Check("restart-restores-custom-order-and-pinned-membership",restored.NoteTabIds.SequenceEqual(savedOrder)&&restored.PinnedTabIds.SetEquals([ids[3]])&&await restored.Script("document.querySelectorAll('.workspace-pinned-tabs>.workspace-tab').length===1")=="true");
            Check("all-tests-offscreen-and-no-renderer-errors",sessions.SelectMany(s=>s.Windows).All(window=>window.RuntimeErrors.Count==0&&window.Left< -10000&&window.Top< -10000&&!window.Topmost&&!window.ShowActivated&&!window.ShowInTaskbar));
        }
        catch
        {
            try{await File.WriteAllTextAsync(Path.Combine(directory,"browser-tabs-diagnostics.json"),JsonSerializer.Serialize(new{checks,nativeTabs=home?.NoteTabIds,pinned=home?.PinnedTabIds,errors=home?.RuntimeErrors,renderer=home?.Browser.CoreWebView2 is null?null:await home.Script("({view:JotWorkspace.view,tabs:JotWorkspace.tabs,busy:document.getElementById('workspaceTabs').ariaBusy,dragEvents:window.browserTabDragEvents,html:document.getElementById('workspaceTabs').outerHTML})")},new JsonSerializerOptions{WriteIndented=true}));}catch{}
            throw;
        }
        finally{foreach(var s in sessions)foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
    }
}
