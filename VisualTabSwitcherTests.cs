using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyVisualTabSwitcher(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="visual-tab-switcher-"+name,passed,evidence});
        var directory=Path.Combine(testOutput,"visual-tab-switcher");
        var target=new JotSession(true,directory){ExerciseLifecycle=true};MainWindow? home=null;
        const string idle="!JotWorkspace.tabSwitcher.open&&!JotAppShortcuts.busy&&document.getElementById('workspaceTabs').ariaBusy==='false'";
        string IsActive(string id)=>"model.activeId==="+JsonSerializer.Serialize(id)+"&&JotWorkspace.view==='note'&&"+idle;
        async Task Key(string key,string code,int value,int modifiers=2,string type="rawKeyDown")=>await home!.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code,windowsVirtualKeyCode=value,modifiers}));
        async Task Cycle(bool reverse=false)
        {
            await Key("Tab","Tab",9,reverse?10:2);await Key("Tab","Tab",9,reverse?10:2,"keyUp");
            await home!.WaitFor("JotWorkspace.tabSwitcher.open");
        }
        async Task Release()=>await Key("Control","ControlLeft",17,0,"keyUp");
        async Task Escape(){await Key("Escape","Escape",27);await Release();await home!.WaitFor(idle);}
        try
        {
            var ids=new List<string>();
            foreach(var color in new[]{"blue","crimson","teal"})
            {
                var id=await target.Store.Create();ids.Add(id);
                await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title=ids.Count==1?"First pinned note":ids.Count==2?"یادداشت فارسی English":"Current draft",color,icon="emoji:📖"}));
            }
            home=target.Home();home.Width=900;home.Height=650;await home.WaitFor("window.jotReady===true&&!!JotWorkspace.tabSwitcher");
            foreach(var id in ids)await home.SwitchNoteTab(id);
            await home.SetTabPinned(ids[0],true);await home.SwitchHomeView(true);await home.SwitchHomeView();await home.SwitchNoteTab(ids[0]);await home.SwitchNoteTab(ids[2]);
            await home.Script("editor.innerHTML='<p><b>Unsaved chooser draft</b></p>';onEdit();clearTimeout(saveTimer);editor.focus();window.switcherBefore=editor.innerHTML");
            await Cycle();
            Check("hold-opens-recent-tab-preview-without-switching-or-flushing",home.NoteId==ids[2]&&await home.Script("JotWorkspace.tabSwitcher.selectedId==="+JsonSerializer.Serialize(ids[0])+"&&editor.innerHTML===switcherBefore&&document.getElementById('workspaceViews').inert&&document.activeElement.id==='tabSwitcherList'")=="true"&&(await target.Store.LoadNote(ids[2]))!.Value.GetProperty("plain").GetString()!="Unsaved chooser draft");
            Check("home-settings-emojis-color-and-pin-have-accessible-options",await home.Script("(()=>{const list=document.getElementById('tabSwitcherList'),options=[...list.children];return list.getAttribute('role')==='listbox'&&list.getAttribute('aria-activedescendant')===options[1].id&&options.length===5&&options.filter(option=>option.ariaSelected==='true').length===1&&options.some(option=>option.dataset.workspaceId==='home')&&options.some(option=>option.dataset.workspaceId==='settings')&&options.some(option=>option.querySelector('.tab-switcher-pin'))&&options.filter(option=>option.querySelector('.note-emoji')).length===3&&options.filter(option=>option.style.getPropertyValue('--switcher-accent')).length===3})()")=="true");
            await home.Capture("visual-tab-switcher-dark");
            await Cycle();Check("cycle-keeps-frozen-recent-order",await home.Script("JotWorkspace.tabSwitcher.selectedId==='home'")=="true"&&home.NoteId==ids[2]);
            await Cycle(true);await Key("Shift","ShiftLeft",16,2,"keyUp");
            Check("reverse-cycle-and-shift-release-do-not-commit",await home.Script("JotWorkspace.tabSwitcher.open&&JotWorkspace.tabSwitcher.selectedId==="+JsonSerializer.Serialize(ids[0]))=="true"&&home.NoteId==ids[2]);
            await Release();await home.WaitFor(IsActive(ids[0]));
            Check("control-release-commits-once-through-normal-draft-save",(await target.Store.LoadNote(ids[2]))!.Value.GetProperty("plain").GetString()=="Unsaved chooser draft"&&await home.Script("!document.getElementById('workspaceViews').inert")=="true");
            await Cycle();Check("next-gesture-returns-to-most-recent-note",await home.Script("JotWorkspace.tabSwitcher.selectedId==="+JsonSerializer.Serialize(ids[2]))=="true");
            await Release();await home.WaitFor(IsActive(ids[2]));
            await home.Script("editor.focus();const t=editor.querySelector('b').firstChild;getSelection().setBaseAndExtent(t,7,t,1);rememberSelection();window.switcherSelection=getSelection().toString()");
            await Cycle();await Escape();
            Check("escape-cancels-and-restores-backward-text-selection",home.NoteId==ids[2]&&await home.Script("document.activeElement===editor&&getSelection().toString()===switcherSelection&&getSelection().anchorOffset===7&&getSelection().focusOffset===1&&!document.getElementById('workspaceViews').inert")=="true");
            await Cycle();await home.ClickControl("#tabSwitcherList [data-workspace-id=settings]");await home.WaitFor("JotWorkspace.view==='settings'&&"+idle);
            Check("pointer-can-open-settings",home.IsSettingsView);
            await Cycle();await home.ClickControl("#tabSwitcherList [data-workspace-id=home]");await home.WaitFor("JotWorkspace.view==='home'&&"+idle);
            Check("pointer-can-open-permanent-home",home.NoteId is null&&!home.IsSettingsView);
            await home.SwitchNoteTab(ids[2]);await Cycle();await home.Script("window.dispatchEvent(new Event('blur'))");await Release();
            Check("lost-window-focus-cancels-preview",home.NoteId==ids[2]&&await home.Script("!JotWorkspace.tabSwitcher.open&&!document.getElementById('workspaceViews').inert")=="true");
            await home.Script("window.dispatchEvent(new Event('focus'))");
            Check("returning-to-window-restores-editor-focus",await home.Script("document.activeElement===editor")=="true");
            await Cycle();home.Post(new{@event="active-window",active=false});await home.WaitFor("!JotWorkspace.tabSwitcher.open");await Release();
            Check("native-deactivation-also-cancels-preview",home.NoteId==ids[2]);
            await Cycle();await home.Script("window.switcherModal=document.createElement('dialog');switcherModal.innerHTML='<button>Modal action</button>';document.body.append(switcherModal);switcherModal.showModal()");
            await home.WaitFor("!JotWorkspace.tabSwitcher.open");await Release();
            Check("new-modal-takes-keyboard-ownership",await home.Script("switcherModal.open&&switcherModal.contains(document.activeElement)&&!document.getElementById('workspaceViews').inert")=="true");
            await home.Script("switcherModal.close();switcherModal.remove()");
            await home.Script("window.switcherModal=document.createElement('dialog');document.body.append(switcherModal);switcherModal.showModal()");
            await Key("Tab","Tab",9);await Release();
            Check("modal-shortcut-does-not-open-chooser",await home.Script("!JotWorkspace.tabSwitcher.open")=="true");await home.Script("switcherModal.close();switcherModal.remove()");
            await home.SwitchNoteTab(ids[0]);await home.SwitchNoteTab(ids[2]);await Cycle();await home.CloseNoteTab(ids[0]);await Release();await home.WaitFor(idle);
            Check("closed-preview-cannot-reopen-itself-on-release",home.NoteId==ids[2]&&!home.NoteTabIds.Contains(ids[0]));
            await home.SwitchNoteTab(ids[1]);await home.SwitchNoteTab(ids[2]);
            await SetStoreTrigger(target.Store,"CREATE TRIGGER fail_switcher_save BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic chooser save failure');END;");
            try
            {
                await home.Script("editor.innerHTML='<p>Keep this draft after a failed switch</p>';onEdit();clearTimeout(saveTimer);editor.focus()");
                await Cycle();await Release();await home.WaitFor(idle+"&&!app.inert&&JotToast.has('workspace-error')");
                Check("failed-save-keeps-original-note-draft-and-focus",home.NoteId==ids[2]&&await home.Script("editor.textContent==='Keep this draft after a failed switch'&&editor.isContentEditable&&document.activeElement===editor&&!document.getElementById('workspaceViews').inert")=="true");
            }
            finally{await SetStoreTrigger(target.Store,"DROP TRIGGER fail_switcher_save;");}
            await Cycle();await Release();await home.WaitFor(IsActive(ids[1]));
            Check("save-retry-switches-and-preserves-failed-draft",(await target.Store.LoadNote(ids[2]))!.Value.GetProperty("plain").GetString()=="Keep this draft after a failed switch");
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>{{"next-tab","Ctrl+KeyJ"},{"previous-tab","Ctrl+Shift+KeyJ"}}}));
            await home.WaitFor("JotShortcutBindings.label('next-tab')==='Ctrl+J'");
            await Key("Tab","Tab",9);await Release();Check("remapped-default-is-not-intercepted",await home.Script("!JotWorkspace.tabSwitcher.open")=="true");
            await Key("j","KeyJ",74);await home.WaitFor("JotWorkspace.tabSwitcher.open");
            Check("custom-shortcut-uses-same-chooser",await home.Script("document.getElementById('tabSwitcherHint').textContent.startsWith('Release Ctrl')")=="true");await Escape();
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>{{"next-tab","F6"},{"previous-tab","F7"}}}));
            await home.WaitFor("JotShortcutBindings.label('next-tab')==='F6'");
            await Key("F6","F6",117,0);await Key("F6","F6",117,0,"keyUp");await home.WaitFor("JotWorkspace.tabSwitcher.open");
            var selected=JsonSerializer.Deserialize<string>(await home.Script("JotWorkspace.tabSwitcher.selectedId"));
            Check("modifierless-custom-shortcut-explains-enter",await home.Script("document.getElementById('tabSwitcherHint').textContent.startsWith('Press Enter')")=="true");
            await Key("Enter","Enter",13,0);await home.WaitFor(idle);Check("enter-commits-custom-shortcut-choice",home.ActiveWorkspaceTab==selected);
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{shortcutBindings=new Dictionary<string,string>()}));
            await home.WaitFor("JotShortcutBindings.label('next-tab')==='Ctrl+Tab'");
            for(int i=0;i<14;i++){var id=await target.Store.Create();await target.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id,title="Long note "+i+" – یادداشت فارسی with a long English title"}));await home.SwitchNoteTab(id);}
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,900})
            {
                home.Width=width;home.Height=520;await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{theme}));await home.WaitFor("document.documentElement.dataset.theme==="+JsonSerializer.Serialize(theme));
                await Cycle();await Key("End","End",35);await Task.Delay(140);
                Check("long-list-scrolls-selection-and-fits-"+theme+"-"+width,await home.Script("(()=>{const panel=document.querySelector('.tab-switcher-panel'),list=document.getElementById('tabSwitcherList'),a=panel.getBoundingClientRect(),b=list.getBoundingClientRect(),s=list.querySelector('[aria-selected=true]').getBoundingClientRect();return a.left>=0&&a.right<=innerWidth+1&&a.top>=0&&a.bottom<=innerHeight+1&&list.scrollTop>0&&s.top>=b.top-1&&s.bottom<=b.bottom+1&&panel.scrollWidth<=panel.clientWidth&&document.documentElement.scrollWidth<=innerWidth})()")=="true");
                await home.Capture("visual-tab-switcher-"+theme+"-"+width);await Escape();
            }
            await home.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await Cycle();Check("reduced-motion-has-no-entry-animation",await home.Script("getComputedStyle(document.querySelector('.tab-switcher-panel')).animationName==='none'")=="true");await Escape();
            Check("no-renderer-errors-and-no-visible-or-global-input",home.RuntimeErrors.Count==0&&target.Windows.All(window=>window.Left< -10000&&window.Top< -10000&&!window.Topmost&&!window.ShowActivated&&!window.ShowInTaskbar));
        }
        catch
        {
            try{await File.WriteAllTextAsync(Path.Combine(directory,"diagnostics.json"),JsonSerializer.Serialize(new{checks,errors=home?.RuntimeErrors,renderer=home?.Browser.CoreWebView2 is null?null:await home.Script("({view:JotWorkspace.view,active:model.activeId,switcher:{open:JotWorkspace.tabSwitcher.open,selected:JotWorkspace.tabSwitcher.selectedId,order:JotWorkspace.tabSwitcher.order},html:document.getElementById('tabSwitcher').outerHTML})")},new JsonSerializerOptions{WriteIndented=true}));}catch{}
            throw;
        }
        finally{foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
    }
}
