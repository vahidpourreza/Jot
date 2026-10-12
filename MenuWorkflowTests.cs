using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyMenuWorkflow(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="menu-workflow-"+name,passed,evidence});
        static void Cleanup(JotSession target){foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        static async Task Until(Func<bool> predicate){for(int i=0;i<200&&!predicate();i++)await Task.Delay(40);if(!predicate())throw new TimeoutException("The note menu operation did not settle.");}
        const string ready="window.jotReady===true&&!!window.JotRichEditor?.ready&&editor.isContentEditable&&!!window.JotNoteFiles&&!!window.JotEditorMenu&&!!window.JotAppShortcuts";
        const string contextItems="[...document.querySelectorAll('#contentContextMenu button')].map(button=>button.dataset.action)";
        var directory=Path.Combine(testOutput,"menu-workflow");
        var target=new JotSession(true,directory){ExerciseLifecycle=true};
        string? tabId=null,windowId=null;
        try
        {
            // A stored preference from v1.7 must not silently restore the removed
            // app-wide window mode. Only an explicit note action creates one.
            await target.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var home=await target.NewNote();await home.WaitFor(ready);tabId=home.NoteId!;
            Check("legacy-window-preference-new-note-is-tab",home.Mode=="home"&&home.NoteTabIds.SequenceEqual([tabId])&&target.Windows.Count==1);
            var second=await target.Store.Create();await target.Changed(false);
            Check("ordinary-open-is-tab",ReferenceEquals(await target.OpenNoteDefault(second),home)&&home.NoteTabIds.Contains(second));
            await home.WaitFor(ready);
            await home.Script("JotRichEditor.setContent('<p>Keep my draft and selection.</p>');JotRichEditor.editor.commands.focus('end');clearTimeout(saveTimer)");
            await home.RightClickControl("[role=tab][data-workspace-id='"+second+"']");
            Check("tab-menu-is-purposeful",await home.Script(contextItems+".join(',')==='options,tab-pin,open-window,copy,export,delete'")=="true");
            await home.ClickControl("#contentContextMenu [data-action=tab-pin]");
            await home.WaitFor("JotWorkspace.tabs.some(note=>note.id==='"+second+"'&&note.tabPinned)");
            Check("tab-pin-still-works",home.PinnedTabIds.Contains(second)&&!home.Topmost);
            await home.Script("document.getElementById('workspaceTabs').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:20,clientY:20}))");
            Check("blank-tab-bar-no-longer-opens-reopen-menu",await home.Script("document.getElementById('contentContextMenu').hidden")=="true");
            // Pinned tabs intentionally hide their X. Exercise the real visible
            // close button after unpinning, then restore the pin for later work.
            await home.SetTabPinned(second,false);
            await home.WaitFor("!JotWorkspace.tabs.find(note=>note.id==='"+second+"').tabPinned&&document.getElementById('workspaceTabs').ariaBusy==='false'");
            await home.ClickControl(".tab-close[data-workspace-id='"+second+"']");
            await home.WaitFor("!JotWorkspace.tabs.some(note=>note.id==='"+second+"')&&JotWorkspace.canReopenTab");
            await home.Script("document.dispatchEvent(new KeyboardEvent('keydown',{key:'T',code:'KeyT',ctrlKey:true,shiftKey:true,bubbles:true,cancelable:true}))");
            await home.WaitFor("JotWorkspace.view==='note'&&activeNote().id==='"+second+"'&&!JotAppShortcuts.busy");
            Check("close-button-and-reopen-shortcut-still-work",home.NoteId==second&&home.NoteTabIds.Contains(second));
            await home.SetTabPinned(second,true);
            await home.Script("JotRichEditor.editor.commands.focus('end');JotEditorMenu.open(40,80,null,true)");
            Check("note-menu-removes-navigation-and-typing-settings",await home.Script("[...document.querySelectorAll('#editorMenu button')].map(button=>button.dataset.action).join(',')==='cut,copy,paste,file-save,file-save-as,open-window'")=="true");
            await home.Script("JotEditorMenu.close(true)");
            await home.SwitchHomeView(true);await home.WaitFor("JotWorkspace.view==='settings'");
            Check("open-mode-control-is-absent",await home.Script("!document.querySelector('#newNoteTarget,[data-new-note-target]')&&!document.querySelector('#workspaceSettings').textContent.includes('Open mode')")=="true");
            var noteCount=target.Windows.Count;var openTabs=home.NoteTabIds.ToArray();
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="tab",theme="light"}));
            Check("stale-mode-patch-does-not-move-notes",target.Windows.Count==noteCount&&home.NoteTabIds.SequenceEqual(openTabs)&&home.IsSettingsView);
            await home.RightClickControl(".workspace-tab[data-home=true] [role=tab]");
            Check("app-logo-menu-has-light-dark-modes",await home.Script("['theme-light','theme-dark'].every(action=>document.querySelector('#contentContextMenu [data-action='+action+']'))&&document.querySelector('#contentContextMenu [data-action=theme-light]').disabled")=="true");
            await home.ClickControl("#contentContextMenu [data-action=theme-dark]");
            await home.WaitFor("document.documentElement.dataset.theme==='dark'&&document.getElementById('contentContextMenu').hidden");
            Check("app-logo-theme-updates-app-preference",(await target.Store.LoadPreferences()).GetProperty("theme").GetString()=="dark");

            await home.SwitchNoteTab(second);await home.WaitFor(ready);
            await home.RightClickControl("[role=tab][data-workspace-id='"+second+"']");
            await home.ClickControl("#contentContextMenu [data-action=open-window]");
            await Until(()=>target.Windows.Any(window=>window.Mode=="note"&&window.NoteId==second));
            var standalone=target.Windows.Single(window=>window.Mode=="note"&&window.NoteId==second);await standalone.WaitFor(ready);windowId=second;
            Check("explicit-tab-menu-detaches-after-flush",!home.NoteTabIds.Contains(second)&&target.Windows.Count(window=>window.ContainsNote(second))==1&&await standalone.Script("JotRichEditor.getText()==='Keep my draft and selection.'")=="true");
            await standalone.Script("JotRichEditor.editor.commands.focus('end');JotEditorMenu.open(40,80,null,true)");
            var standaloneItems=await standalone.Script("[...document.querySelectorAll('#editorMenu button')].map(button=>button.dataset.action).join(',')");
            Check("standalone-menu-does-not-reopen-another-window",standaloneItems==JsonSerializer.Serialize("cut,copy,paste,file-save,file-save-as"),JsonSerializer.Deserialize<string>(standaloneItems));
            await standalone.Script("JotEditorMenu.close(true)");
            await standalone.RightClickControl("#noteAppLogo");
            await standalone.ClickControl("#contentContextMenu [data-action=theme-light]");
            await standalone.WaitFor("document.documentElement.dataset.theme==='light'");
            Check("standalone-logo-theme-is-app-wide",(await target.Store.LoadPreferences()).GetProperty("theme").GetString()=="light"&&await home.Script("document.documentElement.dataset.theme==='light'")=="true");
            await target.ApplyPreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window",fontSize=17}));
            Check("ordinary-settings-keep-explicit-window-owner",target.Windows.Contains(standalone)&&standalone.IsVisible&&!home.NoteTabIds.Contains(second));
            var created=await target.NewNote();await home.WaitFor(ready);
            Check("new-note-leaves-explicit-window-alone",ReferenceEquals(created,home)&&target.Windows.Contains(standalone)&&standalone.NoteId==second&&home.NoteId!=second);

            await home.SwitchHomeView();await home.WaitFor("JotWorkspace.view==='home'&&homeData.notes.some(note=>note.id==='"+second+"')");
            await home.RightClickControl("[data-note-id='"+second+"'] .card-open");
            Check("library-context-has-explicit-window-action",await home.Script(contextItems+".includes('open-window')&&!("+contextItems+").includes('tab')")=="true");
            await home.Script("JotMenus.close()");
            await home.ClickControl("[data-note-id='"+second+"'] .card-open");
            await home.WaitFor("JotWorkspace.view==='note'&&activeNote().id==='"+second+"'");
            Check("ordinary-library-open-is-tab-with-single-owner",home.NoteTabIds.Contains(second)&&!target.Windows.Contains(standalone)&&target.Windows.Count(window=>window.ContainsNote(second))==1);
            var fileId=await target.Store.Create();await target.OpenAcceptedNoteFile(fileId);
            Check("opening-files-uses-tabs-even-with-legacy-preference",home.NoteId==fileId&&home.NoteTabIds.Contains(fileId)&&!target.Windows.Any(window=>window.Mode=="note"));

            await home.SwitchNoteTab(second);await home.WaitFor(ready);
            await home.Script("JotRichEditor.setContent('<p>Failed detach keeps this live draft.</p>');clearTimeout(saveTimer)");
            await SetStoreTrigger(target.Store,"CREATE TRIGGER fail_menu_detach BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic detach failure');END;");
            bool refused=false;try{await target.OpenNoteWindow(second);}catch(IOException){refused=true;}
            await home.WaitFor("!app.inert&&!editorLockedByHost");
            Check("failed-explicit-detach-keeps-editable-tab",refused&&home.NoteTabIds.Contains(second)&&target.Windows.Count(window=>window.ContainsNote(second))==1&&await home.Script("editor.isContentEditable&&JotRichEditor.getText()==='Failed detach keeps this live draft.'")=="true");
            await SetStoreTrigger(target.Store,"DROP TRIGGER fail_menu_detach;");
            standalone=await target.OpenNoteWindow(second);await standalone.WaitFor(ready);
            await home.SwitchNoteTab(tabId);await target.PrepareQuit();
            Check("snapshot-keeps-workspace-and-explicit-window",(await target.Store.LoadWorkspaceSession())!.Tabs.Contains(tabId)&&(await target.Store.LoadWindowSession()).Any(note=>note.NoteId==second));
            Check("all-fixtures-remain-offscreen-and-error-free",target.Windows.All(window=>window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.Topmost&&!window.ShowInTaskbar&&window.RuntimeErrors.Count==0));
        }
        finally{Cleanup(target);}

        var restored=new JotSession(true,directory){ExerciseLifecycle=true};
        try
        {
            await restored.StartWork();
            var home=restored.Windows.Single(window=>window.Mode=="home");await home.WaitFor(ready);
            var standalone=restored.Windows.Single(window=>window.Mode=="note");await standalone.WaitFor(ready);
            Check("restart-retains-workspace-despite-legacy-window-preference",home.NoteTabIds.Contains(tabId!)&&home.NoteId==tabId);
            Check("restart-retains-explicit-standalone-without-duplicating",standalone.NoteId==windowId&&!home.NoteTabIds.Contains(windowId!)&&restored.Windows.Count(window=>window.ContainsNote(windowId!))==1);
        }
        finally{Cleanup(restored);}
    }
}
