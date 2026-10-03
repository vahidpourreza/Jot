using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyNewNoteDestination(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="new-note-settings-"+name,passed});
        static void Cleanup(JotSession target){foreach(var window in target.Windows.ToArray())window.ClosePermanently();}
        static async Task Until(Func<bool> predicate){for(int i=0;i<200&&!predicate();i++)await Task.Delay(40);if(!predicate())throw new TimeoutException("New-note destination did not settle.");}
        const string normalShell="!document.querySelector('#noteTabs,.workspace-tabs,.workspace-header,#newTabButton,#newWindowButton')&&document.getElementById('newButton').ariaLabel==='New note'";
        const string unlockedEditor="window.jotReady===true&&window.JotWorkspace?.view==='note'&&!app.inert&&!editorLockedByHost&&editor.isContentEditable";
        var fixture=new NoteStore(Path.Combine(testOutput,"new-note-preference-store"));
        Check("fresh-preferences-default-to-tabs",(await fixture.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab");
        await fixture.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
        Check("explicit-window-choice-persists-across-store-reopen",(await new NoteStore(fixture.Root).LoadPreferences()).GetProperty("newNoteTarget").GetString()=="window");
        var beforeInvalid=(await fixture.LoadPreferences()).GetRawText();
        foreach(var invalid in new object?[]{null,true,1,"tabs","separate","invalid",new[]{"tab"}})
        {
            bool rejected=false;
            try{await fixture.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget=invalid,theme="light"}));}
            catch(InvalidDataException){rejected=true;}
            Check("rejects-invalid-destination-"+JsonSerializer.Serialize(invalid),rejected&&(await fixture.LoadPreferences()).GetRawText()==beforeInvalid);
        }
        await SetStoreTrigger(fixture,"DELETE FROM settings WHERE key='newNoteTarget';");
        Check("existing-database-without-setting-uses-tab-default",(await new NoteStore(fixture.Root).LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab");

        var s=new JotSession(true,Path.Combine(testOutput,"new-note-destination")){ExerciseLifecycle=true};
        try
        {
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var source=await s.NewNote();await source.WaitFor("window.jotReady===true");var sourceId=source.NoteId!;
            await source.Script("editor.innerHTML='<p>Existing separate note stays here.</p>';onEdit();clearTimeout(saveTimer);setPreference({fontSize:21,lineHeight:2.2,toolbarVisible:false}).then(()=>window.destinationViewReady=true)");
            await source.WaitFor("window.destinationViewReady===true");
            var sourceView=await source.Script("JSON.stringify(activeNote().view)");
            Check("explicit-window-choice-keeps-familiar-standalone-editor",source.Mode=="note"&&await source.Script(normalShell)=="true");
            var home=s.Settings();await home.WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
            Check("settings-choice-has-clear-label-and-existing-default",await home.Script("document.getElementById('newNoteTargetLabel').textContent==='Open mode'&&document.querySelector('[data-new-note-target=window]').textContent==='Separate windows'&&document.querySelector('[data-new-note-target=tab]').textContent==='Tabs'&&document.querySelector('[data-new-note-target=window]').ariaPressed==='true'")=="true");
            await home.ClickControl("[data-new-note-target=tab]");
            await home.WaitFor("prefs.newNoteTarget==='tab'&&document.querySelector('[data-new-note-target=tab]').ariaPressed==='true'&&!document.querySelector('[data-new-note-target=tab]').disabled");
            Check("changing-setting-moves-existing-note-without-modifying-content",source.windowClosed&&home.NoteTabIds.SequenceEqual([sourceId])&&(await s.Store.LoadNote(sourceId))!.Value.GetProperty("plain").GetString()=="Existing separate note stays here.");
            source=await s.OpenNoteWindow(sourceId);await source.WaitFor("window.jotReady===true");
            Check("explicit-window-override-keeps-original-note-settings",await source.Script("JSON.stringify(activeNote().view)")==sourceView&&home.NoteTabIds.Count==0&&await source.Script(normalShell)=="true");
            await home.Reload();await home.WaitFor("window.JotWorkspace?.view==='settings'");
            Check("settings-selection-survives-workspace-reload",await home.Script("prefs.newNoteTarget==='tab'&&document.querySelector('[data-new-note-target=tab]').ariaPressed==='true'")=="true");
            foreach(var theme in new[]{"dark","light"})
            {
                await home.ClickControl("[data-theme-choice="+theme+"]");await home.WaitFor("prefs.theme==='"+theme+"'&&!document.querySelector('[data-theme-choice="+theme+"]').disabled");
                foreach(var width in new[]{360d,520d})
                {
                    home.Width=width;home.Height=width==360?280:540;home.UpdateLayout();await Task.Delay(70);
                    await home.Script("document.getElementById('newNoteTarget').scrollIntoView({block:'nearest'})");
                    Check("settings-fit-"+theme+"-"+width,await home.Script("(()=>{const group=document.getElementById('newNoteTarget'),bounds=group.getBoundingClientRect();return bounds.left>=0&&bounds.right<=innerWidth&&[...group.querySelectorAll('button')].every(b=>b.scrollWidth<=b.clientWidth)&&document.documentElement.scrollWidth<=innerWidth;})()")=="true");
                    Check("settings-choices-reachable-with-fixed-window-controls-"+theme+"-"+width,await home.Script("(()=>{const area=document.querySelector('#workspaceSettings .appearance').getBoundingClientRect(),group=document.getElementById('newNoteTarget').getBoundingClientRect();return group.top>=area.top&&group.bottom<=area.bottom&&['settingsBack','settingsQuit','workspaceClose'].every(id=>{const r=document.getElementById(id).getBoundingClientRect();return r.width>0&&r.top>=0&&r.bottom<=innerHeight;});})()")=="true");
                    await home.Capture("new-note-settings-"+theme+"-"+(int)width);
                }
            }
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_new_note_preference BEFORE UPDATE OF value ON settings WHEN NEW.key='newNoteTarget' BEGIN SELECT RAISE(ABORT,'synthetic preference failure');END;");
            await home.ClickControl("[data-new-note-target=window]");
            await home.WaitFor("JotToast.has('settings-error')&&!document.querySelector('[data-new-note-target=window]').disabled");
            Check("failed-settings-save-retains-choice-and-restores-controls",(await s.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab"&&await home.Script("prefs.newNoteTarget==='tab'&&document.querySelector('[data-new-note-target=tab]').ariaPressed==='true'&&document.querySelector('[data-new-note-target=window]').ariaPressed==='false'&&document.getElementById('homeStatus').ariaBusy==='false'&&[...document.querySelectorAll('[data-theme-choice],[data-new-note-target]')].every(b=>!b.disabled)")=="true");
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_new_note_preference;");
            await home.GoToIndex();
            await s.Store.CreateFolder("Destination folder");await s.Changed(false);await home.WaitFor("homeData.folders.includes('Destination folder')");
            await home.Script("folder='Destination folder';renderFolders();renderCards()");
            var visibleWindows=s.Windows.Count;await home.ClickControl("#homeNew");await home.WaitFor(unlockedEditor);var first=home.NoteId!;
            Check("home-new-note-uses-tab-and-selected-folder",home.IsVisible&&home.NoteTabIds.SequenceEqual([first])&&s.Windows.Count==visibleWindows&&(await s.Store.LoadNote(first))!.Value.GetProperty("group").GetString()=="Destination folder");
            await home.Script("editor.innerHTML='<p>Current tab draft survives every new note.</p>';onEdit();clearTimeout(saveTimer)");
            await source.ClickControl("#newButton");await Until(()=>home.NoteId!=first);await home.WaitFor(unlockedEditor);var second=home.NoteId!;
            Check("standalone-plus-respects-choice-without-changing-its-shell",second!=sourceId&&home.NoteTabIds.Count==2&&source.NoteId==sourceId&&source.IsVisible&&await source.Script(normalShell)=="true"&&s.Windows.Count==visibleWindows);
            Check("creating-next-tab-saves-both-current-editors",(await s.Store.LoadNote(first))!.Value.GetProperty("plain").GetString()=="Current tab draft survives every new note."&&(await s.Store.LoadNote(sourceId))!.Value.GetProperty("plain").GetString()=="Existing separate note stays here.");
            await s.ExecuteTrayAction("new-note");await home.WaitFor(unlockedEditor);var trayId=home.NoteId!;
            Check("tray-new-note-respects-tabs",trayId!=second&&home.NoteTabIds.Count==3&&s.Windows.Count==visibleWindows);
            await s.ExecuteTaskbarAction(TaskbarAction.NewNote);await home.WaitFor(unlockedEditor);var taskbarId=home.NoteId!;
            Check("taskbar-new-note-respects-tabs",taskbarId!=trayId&&home.NoteTabIds.Count==4&&s.Windows.Count==visibleWindows);
            await home.RightClickControl("[role=tab][data-workspace-id='"+taskbarId+"']");
            Check("tab-menu-has-short-window-link-and-more-settings",await home.Script("document.querySelector('#contentContextMenu [data-action=options]').textContent.trim()==='More settings'&&document.querySelector('#contentContextMenu [data-action=open-window]').textContent.trim()==='Open in window'&&document.querySelector('#contentContextMenu [data-action=open-window] svg').innerHTML===JotDesign.icon('external-link').innerHTML")=="true");
            await home.Capture("new-note-tab-menu");await home.Script("JotMenus.close()");
            await home.SwitchHomeView(true);await home.WaitFor("window.JotWorkspace?.view==='settings'");var tabsBefore=home.NoteTabIds.ToArray();
            await home.ClickControl("[data-new-note-target=window]");await home.WaitFor("prefs.newNoteTarget==='window'&&!document.querySelector('[data-new-note-target=window]').disabled");
            Check("returning-to-window-mode-detaches-existing-tabs",home.NoteTabIds.Count==0&&source.IsVisible&&tabsBefore.All(id=>s.Windows.Count(w=>w.Mode=="note"&&w.NoteId==id&&w.IsVisible)==1));
            tabsBefore=home.NoteTabIds.ToArray();visibleWindows=s.Windows.Count;
            await home.GoToIndex();await home.Script("folder='Destination folder';renderFolders();renderCards()");
            var existingWindows=s.Windows.ToArray();await home.ClickControl("#homeNew");await Until(()=>s.Windows.Count>visibleWindows);var standalone=s.Windows.Single(w=>w.Mode=="note"&&!existingWindows.Contains(w));await standalone.WaitFor("window.jotReady===true");
            Check("home-window-choice-keeps-home-and-folder",standalone.Mode=="note"&&await standalone.Script(normalShell)=="true"&&(await s.Store.LoadNote(standalone.NoteId!))!.Value.GetProperty("group").GetString()=="Destination folder"&&home.NoteId is null&&home.NoteTabIds.SequenceEqual(tabsBefore));
            await home.ClickControl(".workspace-add-tab");await home.WaitFor(unlockedEditor);var explicitTab=home.NoteId!;
            Check("explicit-plus-still-creates-tab-with-window-default",home.NoteTabIds.Count==tabsBefore.Length+1&&home.NoteTabIds.Contains(explicitTab)&&s.Windows.Count==visibleWindows+1);
            await home.SwitchHomeView(true);await home.WaitFor("window.JotWorkspace?.view==='settings'");await home.ClickControl("[data-new-note-target=tab]");await home.WaitFor("prefs.newNoteTarget==='tab'&&!document.querySelector('[data-new-note-target=tab]').disabled");
            await home.SwitchNoteTab(explicitTab);await home.Script("editor.innerHTML='<p>Explicit window keeps my final draft.</p>';onEdit();clearTimeout(saveTimer)");
            var detached=await s.OpenNoteWindow(explicitTab);await detached.WaitFor("window.jotReady===true");
            Check("explicit-open-in-window-overrides-tab-default",detached.Mode=="note"&&detached.NoteId==explicitTab&&!home.NoteTabIds.Contains(explicitTab)&&s.Windows.Count(w=>w.ContainsNote(explicitTab))==1&&await detached.Script("editor.textContent==='Explicit window keeps my final draft.'&&"+normalShell)=="true");
            await home.SwitchHomeView();await home.HideAfterSaving();var reopened=await s.NewNote();await home.WaitFor(unlockedEditor);
            Check("new-tab-preference-reopens-existing-hidden-home",ReferenceEquals(reopened,home)&&home.IsVisible&&!home.HideRequested&&s.Windows.Count(w=>w.Mode=="home")==1&&home.NoteId is not null&&s.Windows.Count(w=>w.ContainsNote(home.NoteId))==1);
            await home.SwitchNoteTab(first);await home.Script("editor.innerHTML='<p>Unsaved tab stays editable after creation fails.</p>';onEdit();clearTimeout(saveTimer)");
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_destination_flush BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic destination flush failure');END;");
            bool failed=false;try{await s.NewNote();}catch(IOException){failed=true;}
            await home.WaitFor("!app.inert&&!editorLockedByHost");
            Check("failed-tab-switch-keeps-current-draft-and-one-editor",failed&&home.NoteId==first&&await home.Script("editor.textContent==='Unsaved tab stays editable after creation fails.'&&editor.isContentEditable")=="true"&&s.Windows.Count(w=>w.ContainsNote(first))==1);
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_destination_flush;");await home.Flush();
            Check("all-windows-remain-offscreen",s.Windows.All(w=>w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
            Check("no-renderer-exceptions",s.Windows.All(w=>w.RuntimeErrors.Count==0));
        }
        finally{Cleanup(s);}

        var cold=new JotSession(true,Path.Combine(testOutput,"new-note-tab-start")){ExerciseLifecycle=true};
        try
        {
            var dormant=new MainWindow(cold,"home");dormant.StartInTray();
            await SetStoreTrigger(cold.Store,"CREATE TRIGGER fail_destination_create BEFORE INSERT ON notes BEGIN SELECT RAISE(ABORT,'synthetic note creation failure');END;");
            bool rejected=false;try{await cold.NewNote();}catch(SqliteException){rejected=true;}
            Check("failed-create-does-not-reveal-empty-workspace",rejected&&cold.Windows.Count==1&&!dormant.IsVisible&&dormant.Browser.CoreWebView2 is null&&Convert.ToInt64(await StoreScalar(cold.Store,"SELECT COUNT(*) FROM notes;"))==0);
            await SetStoreTrigger(cold.Store,"DROP TRIGGER fail_destination_create;");
            await cold.StartWork();await dormant.WaitFor(unlockedEditor);var startedId=dormant.NoteId!;
            Check("blank-startup-uses-fresh-tab-default",dormant.IsVisible&&dormant.NoteTabIds.SequenceEqual([startedId])&&cold.Windows.Count==1&&await dormant.Script("!editor.textContent.trim()&&document.querySelector('[role=tab]').dataset.workspaceId==='home'")=="true");
            await dormant.Script("editor.innerHTML='<p>Final text survives creating a tab during Quit.</p>';onEdit();clearTimeout(saveTimer)");dormant.TestSaveDelayMs=160;
            var opening=cold.NewNote();var quitting=cold.Quit();await opening;await quitting;
            Check("quit-waits-for-accepted-tab-creation-and-last-edit",cold.Windows.Count==0&&Convert.ToInt64(await StoreScalar(cold.Store,"SELECT COUNT(*) FROM notes;"))==2&&(await cold.Store.LoadNote(startedId))!.Value.GetProperty("plain").GetString()=="Final text survives creating a tab during Quit.");
        }
        finally{Cleanup(cold);}
    }
}
