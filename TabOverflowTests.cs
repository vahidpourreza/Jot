using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyTabOverflow(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="tab-overflow-"+name,passed});
        var session=new JotSession(true,Path.Combine(testOutput,"tab-overflow")){ExerciseLifecycle=true};
        MainWindow? home=null;
        try
        {
            var notes=new List<string>();
            for(int i=0;i<32;i++)
            {
                var id=await session.Store.Create();notes.Add(id);
                await session.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title=$"Project note {i+1:00}",color=i%2==0?"teal":"crimson"}));
            }
            home=session.Home();home.Width=520;home.Height=560;
            await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
            Check("controls-hidden-when-home-is-only-tab",await home.Script("['previous','next','all'].every(name=>document.querySelector('.workspace-tab-'+name).hidden)")=="true");
            foreach(var id in notes)await home.SwitchNoteTab(id);
            const string idle="window.JotWorkspace?.view==='note'&&!editorLockedByHost&&!app.inert&&document.getElementById('workspaceTabs').ariaBusy==='false'";
            const string visibleItem="function visibleItem(id){const item=document.querySelector('.workspace-note-tabs [data-workspace-id=\"'+id+'\"]').closest('.workspace-tab').getBoundingClientRect(),list=document.querySelector('.workspace-note-tabs').getBoundingClientRect();return item.left>=list.left-1&&item.right<=list.right+1}";
            await home.Script(visibleItem+";window.overflowNodes={home:document.querySelector('.workspace-tab[data-home=true]'),list:document.querySelector('.workspace-note-tabs'),tabs:[...document.querySelectorAll('.workspace-note-tabs>.workspace-tab')],add:document.querySelector('.workspace-add-tab')};window.overflowKeyEvents=[];for(const type of ['keydown','keypress','keyup','click'])document.addEventListener(type,event=>{overflowKeyEvents.push({type,key:event.key,id:event.target.dataset.workspaceId,prevented:event.defaultPrevented});if(overflowKeyEvents.length>24)overflowKeyEvents.shift()})");
            async Task Key(string key,int code)
            {
                // Enter must include its character event: rawKeyDown alone
                // deliberately omits keypress and does not activate buttons.
                if(key=="Enter")await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyDown",key,code=key,windowsVirtualKeyCode=code,text="\r",unmodifiedText="\r"}));
                else await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="rawKeyDown",key,code=key,windowsVirtualKeyCode=code}));
                await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type="keyUp",key,code=key,windowsVirtualKeyCode=code}));
            }
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,520,900})
            {
                var suffix=theme+"-"+width;
                await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await session.Changed();
                home.Width=width;home.Height=560;await Task.Delay(120);await home.SwitchNoteTab(notes[^1]);
                await home.WaitFor(idle+"&&document.documentElement.dataset.theme==="+JsonSerializer.Serialize(theme)+"&&document.getElementById('workspaceTabs').dataset.overflow==='true'");
                Check("fixed-controls-fit-"+suffix,await home.Script("(()=>{const controls=[document.querySelector('.workspace-tab[data-home=true]'),document.querySelector('.workspace-add-tab'),...document.querySelectorAll('.workspace-tab-previous,.workspace-tab-next,.workspace-tab-all,.index-window-actions button')].filter(node=>node.id!=='workspacePin'||node.getClientRects().length);return document.documentElement.scrollWidth<=innerWidth&&controls.every(node=>{const r=node.getBoundingClientRect();return !node.hidden&&r.width>0&&r.left>=0&&r.right<=innerWidth+1&&node.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2))})})()")=="true");
                Check("selected-last-tab-is-visible-"+suffix,await home.Script("visibleItem("+JsonSerializer.Serialize(notes[^1])+")")=="true");
                await home.ClickControl(".workspace-tab-previous");
                await home.Script("for(let i=0;i<100&&document.querySelector('.workspace-tab-previous').ariaDisabled!=='true';i++)document.querySelector('.workspace-tab-previous').click()");
                await home.WaitFor("!document.querySelector('.workspace-note-tabs').dataset.scrolling");
                Check("scroll-left-reaches-first-without-closing-or-switching-"+suffix,home.NoteId==notes[^1]&&home.NoteTabIds.Count==32&&await home.Script("visibleItem("+JsonSerializer.Serialize(notes[0])+")&&document.querySelector('.workspace-tab-previous').ariaDisabled==='true'&&document.querySelectorAll('.workspace-tab[data-active=true]').length===1")=="true");
                await home.ClickControl(".workspace-note-tabs [role=tab][data-workspace-id='"+notes[0]+"']");await home.WaitFor(idle+"&&model.activeId==="+JsonSerializer.Serialize(notes[0]));
                await home.ClickControl(".workspace-tab-next");
                await home.Script("for(let i=0;i<100&&document.querySelector('.workspace-tab-next').ariaDisabled!=='true';i++)document.querySelector('.workspace-tab-next').click()");
                await home.WaitFor("!document.querySelector('.workspace-note-tabs').dataset.scrolling");
                Check("scroll-right-reaches-last-without-closing-or-switching-"+suffix,home.NoteId==notes[0]&&home.NoteTabIds.Count==32&&await home.Script("visibleItem("+JsonSerializer.Serialize(notes[^1])+")&&document.querySelector('.workspace-tab-next').ariaDisabled==='true'")=="true");
                await home.ClickControl(".workspace-tab-all");
                Check("all-tabs-menu-is-scrollable-and-marks-selection-"+suffix,await home.Script("(()=>{const menu=document.querySelector('.workspace-tabs-menu'),r=menu.getBoundingClientRect();return !menu.hidden&&menu.children.length===33&&menu.scrollHeight>menu.clientHeight&&r.left>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&menu.querySelectorAll('[aria-checked=true]').length===1&&menu.querySelector('[aria-checked=true]').dataset.workspaceId==="+JsonSerializer.Serialize(notes[0])+"&&document.querySelector('.workspace-tab-all').ariaExpanded==='true'})()")=="true");
                await Key("End",35);
                Check("menu-keyboard-reaches-last-"+suffix,await home.Script("document.activeElement.dataset.workspaceId==="+JsonSerializer.Serialize(notes[^1])+"&&document.activeElement.getBoundingClientRect().bottom<=document.querySelector('.workspace-tabs-menu').getBoundingClientRect().bottom")=="true");
                await Key("Home",36);await Key("ArrowDown",40);
                Check("menu-keyboard-reaches-first-"+suffix,await home.Script("document.activeElement.dataset.workspaceId==="+JsonSerializer.Serialize(notes[0]))=="true");
                for(int i=0;i<15;i++)await Key("ArrowDown",40);
                await home.Capture("tabs-overflow-menu-"+suffix);
                await Key("Enter",13);await home.WaitFor(idle+"&&model.activeId==="+JsonSerializer.Serialize(notes[15]));
                Check("menu-opens-middle-tab-without-closing-"+suffix,home.NoteId==notes[15]&&home.NoteTabIds.Count==32&&await home.Script("document.querySelector('.workspace-tabs-menu').hidden&&visibleItem("+JsonSerializer.Serialize(notes[15])+")")=="true");
                await home.Capture("tabs-overflow-strip-"+suffix);
                await home.ClickControl(".workspace-tab-all");await Key("Escape",27);
                Check("escape-returns-focus-to-all-tabs-"+suffix,await home.Script("document.querySelector('.workspace-tabs-menu').hidden&&document.activeElement===document.querySelector('.workspace-tab-all')&&document.activeElement.ariaExpanded==='false'")=="true");
            }
            home.Width=520;await Task.Delay(120);await home.SwitchNoteTab(notes[^1]);
            await home.Script("overflowNodes.list.scrollLeft=0;overflowNodes.tabs[0].querySelector('[role=tab]').focus({preventScroll:true});overflowNodes.focus=document.activeElement");
            await session.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=notes[4],title="Renamed while browsing earlier tabs"}));await home.RefreshTabHeaders();
            await home.WaitFor("document.querySelector('.workspace-note-tabs [data-workspace-id=\""+notes[4]+"\"]').title==='Renamed while browsing earlier tabs'");
            Check("metadata-keeps-nodes-focus-and-earlier-scroll",await home.Script("overflowNodes.home===document.querySelector('.workspace-tab[data-home=true]')&&overflowNodes.list===document.querySelector('.workspace-note-tabs')&&overflowNodes.add===document.querySelector('.workspace-add-tab')&&overflowNodes.tabs.every((node,i)=>node===document.querySelectorAll('.workspace-note-tabs>.workspace-tab')[i])&&document.activeElement===overflowNodes.focus&&overflowNodes.list.scrollLeft===0")=="true");
            home.TestSaveDelayMs=1500;
            await home.Script("editor.innerHTML='<p>Keep the selected note and all tabs while saving.</p>';onEdit();clearTimeout(saveTimer);JotWorkspace.runAction('tab-switch',"+JsonSerializer.Serialize(notes[15])+").then(()=>window.overflowSaveFinished=true)");
            await home.WaitFor("document.getElementById('workspaceTabs').ariaBusy==='true'");
            Check("pending-save-keeps-scroll-and-focused-tab",await home.Script("overflowNodes.list.scrollLeft===0&&document.activeElement===overflowNodes.focus")=="true");
            await home.WaitFor("window.overflowSaveFinished===true");home.TestSaveDelayMs=0;
            await home.Script("document.querySelector('.workspace-note-tabs [role=tab]').focus({preventScroll:true})");await Key("End",35);
            Check("tab-end-key-reveals-last-without-switching",home.NoteId==notes[15]&&await home.Script("document.activeElement.dataset.workspaceId==="+JsonSerializer.Serialize(notes[^1])+"&&visibleItem("+JsonSerializer.Serialize(notes[^1])+")")=="true");
            await Key("Home",36);await Key("ArrowRight",39);
            Check("tab-home-and-arrow-keys-reveal-first",home.NoteId==notes[15]&&await home.Script("document.activeElement.dataset.workspaceId==="+JsonSerializer.Serialize(notes[0])+"&&visibleItem("+JsonSerializer.Serialize(notes[0])+")")=="true");
            async Task Wheel(double deltaX,double deltaY)
            {
                using var point=JsonDocument.Parse(await home.Script("(()=>{const r=overflowNodes.list.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()"));
                await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseWheel",x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),deltaX,deltaY}));
            }
            await home.Script("overflowNodes.list.scrollLeft=0");await Wheel(0,180);await home.WaitFor("overflowNodes.list.scrollLeft>0");
            Check("mouse-wheel-scrolls-tabs",home.NoteId==notes[15]);
            await home.Script("overflowNodes.list.scrollLeft=0");await Wheel(180,0);await home.WaitFor("overflowNodes.list.scrollLeft>0");
            Check("horizontal-touchpad-scroll-remains-native",home.NoteId==notes[15]);
            await home.Script("overflowNodes.list.scrollLeft=0;document.querySelector('.workspace-tab-next').click();window.scrollStarted=overflowNodes.list.dataset.scrolling==='true';document.querySelector('.workspace-tab-next').click();document.querySelector('.workspace-tab-previous').click()");
            await home.WaitFor("!overflowNodes.list.dataset.scrolling");
            Check("arrow-scroll-eases-and-latest-request-wins",await home.Script("scrollStarted&&Math.abs(overflowNodes.list.scrollLeft-Math.max(96,overflowNodes.list.clientWidth-40))<2")=="true");
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            Check("reduced-motion-arrow-scroll-is-immediate",await home.Script("(()=>{const before=overflowNodes.list.scrollLeft;document.querySelector('.workspace-tab-next').click();return overflowNodes.list.scrollLeft>before&&!overflowNodes.list.dataset.scrolling})()")=="true");
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");
            await home.ClickControl(".workspace-tab-all");await Key("Home",36);await Key("Enter",13);await home.WaitFor("JotWorkspace.view==='home'");
            Check("all-tabs-menu-includes-permanent-home",home.NoteId is null&&home.NoteTabIds.Count==32);
            // Named pins remain individually reachable at the minimum supported
            // width; do not trade their labels for icon-only overflow controls.
            foreach(var id in notes.Take(12))await home.SetTabPinned(id,true);
            home.Width=360;await home.SwitchNoteTab(notes[11]);await Task.Delay(120);
            Check("many-pins-retain-title-and-small-pin",await home.Script("document.querySelectorAll('.workspace-pinned-tabs>.workspace-tab').length===12&&[...document.querySelectorAll('.workspace-pinned-tabs [role=tab]')].every(button=>!button.querySelector('.tab-title').hidden&&button.querySelector('.tab-title').getBoundingClientRect().width>16&&button.querySelector('.tab-pin-marker').getClientRects().length>0)")=="true");
            const string visiblePin="function visiblePin(id){const item=document.querySelector('.workspace-pinned-tabs [data-workspace-id=\"'+id+'\"]').closest('.workspace-tab').getBoundingClientRect(),list=document.querySelector('.workspace-pinned-tabs').getBoundingClientRect();return item.left>=list.left-1&&item.right<=list.right+1}";
            await home.Script(visiblePin);
            Check("last-pin-revealed-without-hiding-home-or-tools",await home.Script("visiblePin("+JsonSerializer.Serialize(notes[11])+")&&document.documentElement.scrollWidth<=innerWidth&&!document.querySelector('.workspace-tab-all').hidden")=="true");
            await home.Script("document.querySelector('.workspace-pinned-tabs [role=tab]').focus({preventScroll:true})");await Key("Home",36);await Key("ArrowRight",39);
            Check("keyboard-reaches-first-pin-without-changing-note",home.NoteId==notes[11]&&await home.Script("visiblePin("+JsonSerializer.Serialize(notes[0])+")&&document.activeElement.dataset.workspaceId==="+JsonSerializer.Serialize(notes[0]))=="true");
            await home.ClickControl(".workspace-tab-all");
            Check("picker-includes-all-twelve-named-pins",await home.Script("document.querySelectorAll('.workspace-tabs-menu [data-pinned=true]').length===12&&[...document.querySelectorAll('.workspace-tabs-menu [data-pinned=true] .tab-title')].every(label=>label.textContent.trim().length>0)")=="true");
            await Key("Home",36);for(int i=0;i<12;i++)await Key("ArrowDown",40);await Key("Enter",13);
            await home.WaitFor(idle+"&&model.activeId==="+JsonSerializer.Serialize(notes[11]));
            Check("picker-opens-last-pin-and-reveals-its-title",await home.Script("visiblePin("+JsonSerializer.Serialize(notes[11])+")")=="true");
            foreach(var id in notes.Take(12))await home.SetTabPinned(id,false);
            foreach(var id in notes.Skip(2))await home.CloseNoteTab(id);
            home.Width=900;await home.WaitFor("document.getElementById('workspaceTabs').dataset.overflow==='false'");
            Check("overflow-controls-disappear-when-tabs-fit",await home.Script("['previous','next','all'].every(name=>document.querySelector('.workspace-tab-'+name).hidden)&&document.querySelectorAll('.workspace-note-tabs>.workspace-tab').length===2")=="true");
            home.Width=360;await home.WaitFor("document.getElementById('workspaceTabs').dataset.overflow==='true'");
            home.Width=900;await home.WaitFor("document.getElementById('workspaceTabs').dataset.overflow==='false'");
            Check("resize-recalculates-overflow-in-both-directions",true);
            Check("no-renderer-errors-or-desktop-interaction",home.RuntimeErrors.Count==0&&session.Windows.All(window=>!window.IsVisible||window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost));
        }
        catch
        {
            try
            {
                var state=home?.Browser.CoreWebView2 is null?null:await home.Script("JSON.stringify({view:window.JotWorkspace?.view,nativeActive:document.getElementById('workspaceTabs')?.dataset.activeId,modelActive:window.model?.activeId,busy:document.getElementById('workspaceTabs')?.ariaBusy,locked:typeof editorLockedByHost!=='undefined'&&editorLockedByHost,inert:document.getElementById('app')?.inert,focused:document.activeElement?.outerHTML.slice(0,700),menuHidden:document.querySelector('.workspace-tabs-menu')?.hidden,menuSelection:document.querySelector('.workspace-tabs-menu [aria-checked=true]')?.dataset.workspaceId,scroll:document.querySelector('.workspace-note-tabs')?.scrollLeft,events:window.overflowKeyEvents,error:document.getElementById('workspaceError')?.textContent})");
                await File.WriteAllTextAsync(Path.Combine(testOutput,"tab-overflow-diagnostics.json"),JsonSerializer.Serialize(new{checks,nativeNote=home?.NoteId,renderer=state,errors=home?.RuntimeErrors},new JsonSerializerOptions{WriteIndented=true}));
            }
            catch{}
            throw;
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}
