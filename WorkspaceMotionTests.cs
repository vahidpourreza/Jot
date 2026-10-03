using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

public partial class MainWindow
{
    private async Task DoubleClickControl(string selector)
    {
        await Script("document.querySelector("+JsonSerializer.Serialize(selector)+").scrollIntoView({block:'nearest',inline:'nearest'})");
        using var point=JsonDocument.Parse(await Script("(()=>{const r=document.querySelector("+JsonSerializer.Serialize(selector)+").getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()"));
        var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
        await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseMoved",x,y,button="none"}));
        foreach(int clickCount in new[]{1,2})foreach(var type in new[]{"mousePressed","mouseReleased"})
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button="left",clickCount}));
    }
    private async Task VerifyWorkspaceMotion(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="workspace-motion-"+name,passed});
        const string homeReady="window.jotReady===true&&window.JotWorkspace?.view==='home'";
        const string noteReady="window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost";
        const string settingsReady="window.jotReady===true&&window.JotWorkspace?.view==='settings'";
        var s=new JotSession(true,Path.Combine(testOutput,"workspace-motion")){ExerciseLifecycle=true};
        MainWindow? home=null;int navigations=0;
        void Navigating(object? sender,CoreWebView2NavigationStartingEventArgs args)=>navigations++;
        try
        {
            var notes=new List<string>();
            var titles=new[]{"Product design review","Release checklist","Ideas for next week","Meeting notes","Weekend reading"};
            var colors=new[]{"blue","amber","teal","violet","crimson"};
            for(int i=0;i<titles.Length;i++)
            {
                var id=await s.Store.Create();notes.Add(id);
                var html="<p><b>"+titles[i]+"</b></p><p>Keep the important details close at hand.</p><p>Review the plan, make a few changes, and continue where you left off.</p>";
                await s.Store.SaveNote(JsonSerializer.SerializeToElement(new{id,html,plain=titles[i]+"\nKeep the important details close at hand.",updatedAt=1000+i}));
                await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title=titles[i],color=colors[i]}));
            }
            home=s.Home();home.Width=520;home.Height=560;await home.WaitFor(homeReady);
            var nativeHandle=home.source!.Handle;var browserUri=home.Browser.Source;var navigationId=home.activeNavigationId;
            home.Browser.CoreWebView2.NavigationStarting+=Navigating;
            var token=Guid.NewGuid().ToString("N");
            await home.Script("window.workspaceDocumentToken="+JsonSerializer.Serialize(token)+";window.workspaceProbe={document:document,root:document.documentElement,header:document.getElementById('workspaceHandle'),strip:document.getElementById('workspaceTabs'),editor:document.getElementById('editor'),cards:document.getElementById('cards'),card:document.querySelector('[data-note-id=\""+notes[0]+"\"]')}");
            foreach(var id in notes)await home.SwitchNoteTab(id);
            await home.SwitchNoteTab(notes[0]);await home.WaitFor(noteReady);
            await home.Script("Object.assign(workspaceProbe,{list:document.querySelector('.workspace-note-tabs'),home:document.querySelector('.workspace-tab[data-home=true]'),add:document.querySelector('.workspace-add-tab'),items:[...document.querySelectorAll('.workspace-note-tabs>.workspace-tab')]});renderNoteTabs()");
            var persistentDocument="workspaceProbe.document===document&&workspaceProbe.root===document.documentElement&&workspaceProbe.header===document.getElementById('workspaceHandle')&&workspaceProbe.strip===document.getElementById('workspaceTabs')&&workspaceProbe.editor===document.getElementById('editor')&&workspaceProbe.cards===document.getElementById('cards')&&workspaceProbe.card===document.querySelector('[data-note-id=\""+notes[0]+"\"]')&&workspaceDocumentToken==="+JsonSerializer.Serialize(token);
            const string sameTabs="workspaceProbe.list===document.querySelector('.workspace-note-tabs')&&workspaceProbe.home===document.querySelector('.workspace-tab[data-home=true]')&&workspaceProbe.add===document.querySelector('.workspace-add-tab')&&workspaceProbe.items.every((node,i)=>node===document.querySelectorAll('.workspace-note-tabs>.workspace-tab')[i])";
            Check("opening-tabs-keeps-document-home-card-and-editor",await home.Script(persistentDocument+"&&"+sameTabs)=="true");
            Check("one-permanent-shared-header-and-tab-strip",await home.Script("document.querySelectorAll('.workspace-header').length===1&&document.querySelectorAll('.workspace-tabs').length===1&&document.querySelectorAll('#editor').length===1")=="true");
            Check("home-is-an-accessible-icon-only-first-tab",await home.Script("(()=>{const tab=document.querySelector('.workspace-tabs [role=tab]'),label=tab.querySelector('.tab-title');return tab.dataset.workspaceId==='home'&&tab.ariaLabel==='Home'&&tab.title==='Home'&&!!tab.querySelector('img[alt=Jot]')&&label.hidden&&!label.textContent.trim()&&!tab.closest('.workspace-tab').querySelector('.tab-close')})()")=="true");
            Check("inactive-tabs-have-visible-surfaces-and-borders",await home.Script("[...document.querySelectorAll('.workspace-note-tabs>.workspace-tab[data-active=false]')].every(tab=>{const style=getComputedStyle(tab),rect=tab.getBoundingClientRect();return style.backgroundColor!=='rgba(0, 0, 0, 0)'&&style.backgroundColor!=='transparent'&&parseFloat(style.borderTopWidth)>=1&&parseFloat(style.borderLeftWidth)>=1&&parseFloat(style.borderTopLeftRadius)>0&&rect.height>=30})")=="true");
            Check("all-tabs-align-at-top-within-the-header",await home.Script("(()=>{const h=document.getElementById('workspaceHandle').getBoundingClientRect(),tabs=[...document.querySelectorAll('.workspace-tab')].map(n=>n.getBoundingClientRect());return tabs.length===6&&tabs.every(r=>Math.abs(r.top-tabs[0].top)<1&&r.top>=h.top&&r.bottom<=h.bottom+1)})()")=="true");
            Check("workspace-note-has-no-inner-new-or-more-buttons",await home.Script("getComputedStyle(document.getElementById('handle')).display==='none'&&document.getElementById('newButton').getBoundingClientRect().height===0&&document.getElementById('menuButton').getBoundingClientRect().height===0&&document.getElementById('writingArea').getBoundingClientRect().top>=document.getElementById('workspaceHandle').getBoundingClientRect().bottom")=="true");
            Check("no-native-screenshot-cover-or-extra-render-layer",home.FindName("WorkspaceTransitionImage") is null&&home.FindName("WorkspaceTransitionCover") is null&&await home.Script("!document.querySelector('.workspace-editor-cover')")=="true");

            home.TestSaveDelayMs=1500;
            await home.Script("workspaceProbe.focus=workspaceProbe.items[1].querySelector('[role=tab]');workspaceProbe.focus.focus({preventScroll:true});workspaceProbe.list.scrollLeft=30;workspaceProbe.scroll=workspaceProbe.list.scrollLeft;editor.innerHTML+='<p>Pending save fixture.</p>';onEdit();clearTimeout(saveTimer);JotWorkspace.runAction('tab-switch',"+JsonSerializer.Serialize(notes[1])+").then(()=>window.workspacePendingDone=true)");
            await home.WaitFor("document.getElementById('workspaceTabs').getAttribute('aria-busy')==='true'");
            Check("pending-operation-keeps-tab-elements-focus-and-scroll",await home.Script(sameTabs+"&&document.activeElement===workspaceProbe.focus&&Math.abs(workspaceProbe.list.scrollLeft-workspaceProbe.scroll)<1")=="true");
            Check("pending-operation-does-not-dim-or-disable-all-tabs",await home.Script("[...document.querySelectorAll('.workspace-tabs button')].every(button=>!button.disabled)&&[...document.querySelectorAll('.workspace-tabs [role=tab],.workspace-add-tab')].every(button=>Number(getComputedStyle(button).opacity)===1)")=="true");
            await home.WaitFor("window.workspacePendingDone===true");home.TestSaveDelayMs=0;await home.SwitchNoteTab(notes[0]);await home.WaitFor(noteReady);
            await home.Script("workspaceProbe.focus.focus({preventScroll:true});workspaceProbe.list.scrollLeft=30;workspaceProbe.scroll=workspaceProbe.list.scrollLeft");
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=notes[1],title="Release checklist · September"}));
            await home.RefreshTabHeaders();await home.WaitFor("[...document.querySelectorAll('.workspace-tabs [role=tab]')].some(button=>button.title==='Release checklist · September')");
            Check("rename-keeps-elements-focus-and-overflow-position",await home.Script(sameTabs+"&&document.activeElement===workspaceProbe.focus&&Math.abs(workspaceProbe.list.scrollLeft-workspaceProbe.scroll)<1")=="true");

            const string chromeRectangles="JSON.stringify([document.getElementById('workspaceHandle'),document.querySelector('.workspace-tab[data-home=true]'),...document.querySelectorAll('#workspaceHandle .index-window-actions button')].map(node=>{const r=node.getBoundingClientRect();return [r.x,r.y,r.width,r.height].map(value=>Math.round(value*100)/100)}))";
            var pageTimings=new List<object>();
            async Task MeasurePageSwitch(string destination,int width,Func<Task> navigate,string ready)
            {
                var elapsed=Stopwatch.StartNew();await navigate();await home.WaitFor(ready);elapsed.Stop();
                pageTimings.Add(new{destination,width,elapsedMs=elapsed.ElapsedMilliseconds});
                Check("persistent-document-and-tabs-after-"+destination+"-"+width,await home.Script(persistentDocument+"&&"+sameTabs)=="true");
                Check("no-content-fade-or-slide-after-"+destination+"-"+width,await home.Script("[document.getElementById('workspaceHandle'),document.getElementById('writingArea')].every(node=>{const s=getComputedStyle(node);return Number(s.opacity)===1&&s.transform==='none'&&!node.getAnimations().some(a=>a.playState==='running')})")=="true");
            }
            foreach(var width in new[]{360,520,900})
            {
                home.Width=width;await Task.Delay(100);var noteChrome=await home.Script(chromeRectangles);
                await MeasurePageSwitch("home",width,()=>home.SwitchNoteTab("home"),homeReady);var homeChrome=await home.Script(chromeRectangles);
                await MeasurePageSwitch("settings",width,()=>home.SwitchHomeView(true),settingsReady);var settingsChrome=await home.Script(chromeRectangles);
                Check("window-controls-stay-fixed-between-views-"+width,noteChrome==homeChrome&&homeChrome==settingsChrome&&home.source!.Handle==nativeHandle);
                await MeasurePageSwitch("note",width,()=>home.SwitchNoteTab(notes[0]),noteReady);
            }
            checks.Add(new{name="workspace-motion-switches-never-navigate-or-cover-the-browser",passed=navigations==0&&home.activeNavigationId==navigationId&&home.Browser.Source==browserUri&&home.FindName("WorkspaceTransitionImage") is null,navigations,measurement="Offscreen elapsed time includes save, bridge acknowledgement and readiness polling.",pageTimings});

            var secondTab="[role=tab][data-workspace-id='"+notes[1]+"']";
            await home.RightClickControl(secondTab);
            Check("right-click-inactive-tab-keeps-current-note-and-opens-actions",home.NoteId==notes[0]&&await home.Script("!document.getElementById('contentContextMenu').hidden&&['rename','options','open-window','copy','export','close','delete'].every(action=>document.querySelector('#contentContextMenu [data-action='+action+']'))")=="true");
            await home.ClickControl("#contentContextMenu [data-action=rename]");await home.WaitFor("document.getElementById('metadataDialog').open");
            await home.Script("document.getElementById('noteTitleInput').value='Release plan'");await home.ClickControl("#metadataSave");await home.WaitFor("!document.getElementById('metadataDialog').open&&document.querySelector("+JsonSerializer.Serialize(secondTab)+").title==='Release plan'");
            Check("context-rename-updates-inactive-note-without-changing-content",home.NoteId==notes[0]&&(await s.Store.LoadNote(notes[1]))!.Value.GetProperty("title").GetString()=="Release plan"&&(await s.Store.LoadNote(notes[1]))!.Value.GetProperty("plain").GetString()!.StartsWith("Release checklist"));
            await home.DoubleClickControl(secondTab+" .tab-title");await home.WaitFor("document.getElementById('metadataDialog').open");
            await home.Script("document.getElementById('noteTitleInput').value='Release plan revised'");await home.ClickControl("#metadataSave");await home.WaitFor("!document.getElementById('metadataDialog').open&&document.querySelector("+JsonSerializer.Serialize(secondTab)+").title==='Release plan revised'");
            Check("double-click-tab-title-opens-working-rename-dialog",(await s.Store.LoadNote(notes[1]))!.Value.GetProperty("title").GetString()=="Release plan revised");
            await home.RightClickControl("[role=tab][data-workspace-id='"+notes[0]+"']");await home.ClickControl("#contentContextMenu [data-action=options]");await home.WaitFor(noteReady+"&&model.activeId==="+JsonSerializer.Serialize(notes[0])+"&&!document.getElementById('menu').hidden");
            Check("tab-options-opens-note-preferences-without-inner-toolbar",await home.Script("!document.getElementById('menu').hidden&&getComputedStyle(document.getElementById('handle')).display==='none'&&document.getElementById('menu').getBoundingClientRect().height>0")=="true");await home.Script("closePanels()");

            await home.Script("editor.innerHTML='<p>Draft remains editable if saving fails.</p>';onEdit();clearTimeout(saveTimer)");
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_workspace_motion_save BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic workspace save failure');END;");
            foreach(var destination in new[]{"note","home","settings"})
            {
                bool failed=false;try{if(destination=="settings")await home.SwitchHomeView(true);else await home.SwitchNoteTab(destination=="home"?"home":notes[1]);}catch(IOException){failed=true;}
                await home.WaitFor(noteReady);
                Check("failed-save-keeps-current-draft-editable-before-"+destination,failed&&home.NoteId==notes[0]&&await home.Script("editor.isContentEditable&&editor.textContent==='Draft remains editable if saving fails.'&&"+persistentDocument)=="true");
            }
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_workspace_motion_save;");
            await Task.WhenAll(home.SwitchNoteTab("home"),home.SwitchHomeView(true),home.SwitchNoteTab(notes[1]),home.SwitchNoteTab(notes[0]));await home.WaitFor(noteReady);
            Check("rapid-home-settings-note-switches-settle-without-overlays",home.NoteId==notes[0]&&await home.Script("editor.isContentEditable&&editor.textContent==='Draft remains editable if saving fails.'&&document.querySelectorAll('.workspace-tab[data-active=true]').length===1&&"+persistentDocument+"&&"+sameTabs)=="true");

            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");await home.WaitFor("reducedMotion.matches");
            await home.SwitchNoteTab(notes[1]);await home.WaitFor(noteReady);
            Check("reduced-motion-has-no-content-animation",await home.Script("getComputedStyle(document.querySelector('.workspace-tab')).transitionDuration.split(',').every(value=>parseFloat(value)===0)&&!document.getElementById('writingArea').getAnimations().length&&editor.textContent.includes('Release checklist')")=="true");
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");

            await home.SwitchNoteTab(notes[0]);
            await home.Script("editor.innerHTML='<p><b>Product design review</b></p><p>A quiet space to think, capture ideas, and come back to them.</p><p>Keep the first version simple.</p><p>Opening a note should feel immediate, with the familiar controls staying in place.</p><p><b>Next steps</b></p><ul><li>Review the new workspace</li><li>Collect feedback from the team</li><li>Polish the small details</li></ul>';onEdit();clearTimeout(saveTimer)");await home.Flush();
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,520,900})
            {
                await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await s.Changed();home.Width=width;home.Height=560;
                await home.WaitFor("document.documentElement.dataset.theme==="+JsonSerializer.Serialize(theme));await Task.Delay(100);
                Check("editor-and-tabs-fit-"+theme+"-"+width,await home.Script("document.documentElement.scrollWidth<=innerWidth&&document.querySelector('.workspace-header').scrollWidth<=innerWidth&&document.querySelector('.workspace-note-tabs').scrollWidth>=document.querySelector('.workspace-note-tabs').clientWidth&&document.querySelector('.workspace-tab[data-home=true]').getBoundingClientRect().left>=0&&document.querySelector('.workspace-header .index-window-actions').getBoundingClientRect().right<=innerWidth")=="true");
                await home.Capture("workspace-editor-"+theme+"-"+width);
                await home.SwitchNoteTab("home");await home.WaitFor(homeReady);await home.Capture("workspace-library-tabs-"+theme+"-"+width);
                await home.SwitchNoteTab(notes[0]);await home.WaitFor(noteReady);
            }
            await home.RightClickControl("[role=tab][data-workspace-id='"+notes[4]+"']");await home.ClickControl("#contentContextMenu [data-action=close]");
            await home.WaitFor("!document.querySelector('[role=tab][data-workspace-id=\""+notes[4]+"\"]')");
            Check("context-close-removes-only-tab-and-keeps-note-in-library",home.NoteId==notes[0]&&await s.Store.Contains(notes[4])&&!home.NoteTabIds.Contains(notes[4]));
            Check("all-switches-and-tab-actions-use-one-document",navigations==0&&home.activeNavigationId==navigationId&&home.Browser.Source==browserUri&&await home.Script(persistentDocument)=="true");
            Check("no-renderer-errors",home.RuntimeErrors.Count==0);
            Check("all-test-windows-stay-offscreen",s.Windows.All(w=>!w.IsVisible||w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
        }
        finally
        {
            if(home?.Browser.CoreWebView2 is not null)
            {
                home.Browser.CoreWebView2.NavigationStarting-=Navigating;
                await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");
            }
            foreach(var w in s.Windows.ToArray())w.ClosePermanently();
        }
    }
}
