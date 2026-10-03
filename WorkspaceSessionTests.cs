using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyWorkspaceSession(List<object> checks)
    {
        void Check(string name,bool passed,object? evidence=null)=>checks.Add(new{name="workspace-session-"+name,passed,evidence});
        static async Task ClickWorkspaceClose(MainWindow window)
        {
            var previous=window.hideTask;
            await window.ClickControl("#workspaceClose");
            // Mouse delivery can finish before the bridge even starts saving.
            // Observe the actual UI-accepted close; do not start another close.
            for(int attempt=0;attempt<200&&ReferenceEquals(previous,window.hideTask)&&!window.windowClosed;attempt++)await Task.Delay(30);
            if(ReferenceEquals(previous,window.hideTask)&&!window.windowClosed)throw new TimeoutException("The workspace X did not start its native close operation.");
            if(window.hideTask is {} closing)await closing.WaitAsync(TimeSpan.FromSeconds(20));
        }
        var sessions=new List<JotSession>();var folder=Path.Combine(testOutput,"workspace-session");
        JotSession Create(){var session=new JotSession(true,folder){ExerciseLifecycle=true};sessions.Add(session);return session;}
        const string settings="window.JotWorkspace?.view==='settings'&&document.getElementById('workspaceTabs').dataset.activeId==='settings'";
        const string editable="window.JotWorkspace?.view==='note'&&!editorLockedByHost&&!app.inert&&editor.isContentEditable";
        try
        {
            var session=Create();await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="tab"}));
            var ids=new List<string>();for(int i=0;i<3;i++)ids.Add(await session.Store.Create());
            var home=session.Home();home.Width=680;home.Height=510;await home.WaitFor("window.jotReady===true");
            foreach(var id in ids)await home.SwitchNoteTab(id);
            await home.SwitchHomeView(true);await home.WaitFor(settings);await home.SwitchHomeView(true);
            Check("settings-is-one-reusable-closable-tab",home.SettingsTabOpen&&home.NoteTabIds.SequenceEqual(ids)&&await home.Script("document.querySelectorAll('[role=tab][data-workspace-id=settings]').length===1&&!!document.querySelector('.workspace-tab[data-kind=settings] .tab-close')&&document.querySelector('.workspace-tabs [role=tab]').dataset.workspaceId==='home'")=="true");
            await home.SwitchNoteTab(ids[0]);await home.WaitFor(editable);
            Check("settings-tab-remains-open-while-editing-note",await home.Script("document.querySelector('[role=tab][data-workspace-id=settings]').ariaSelected==='false'")=="true");
            await home.CloseNoteTab("settings");
            Check("closing-inactive-settings-keeps-active-note",!home.SettingsTabOpen&&home.NoteId==ids[0]&&home.NoteTabIds.SequenceEqual(ids));
            await home.SwitchHomeView(true);await home.WaitFor(settings);
            await home.ClickControl(".workspace-tab[data-kind=settings] .tab-close");await home.WaitFor(editable+"&&!document.querySelector('[role=tab][data-workspace-id=settings]')");
            Check("closing-active-settings-selects-neighbor-without-deleting",!home.SettingsTabOpen&&home.NoteId==ids[2]&&home.NoteTabIds.SequenceEqual(ids)&&(await session.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==3);
            await home.CloseNoteTab(ids[1]);await home.SwitchHomeView(true);await home.SwitchNoteTab(ids[0]);await home.WaitFor(editable);
            var separateId=await session.Store.Create();var separate=await session.OpenNoteWindow(separateId);await separate.WaitFor("window.jotReady===true");
            await home.Script("editor.innerHTML='<p><b>Final workspace draft</b> survives closing the whole window.</p>';onEdit();clearTimeout(saveTimer)");
            await ClickWorkspaceClose(home);
            var closed=await session.Store.LoadWorkspaceSession();
            Check("workspace-x-saves-tabs-order-active-view-and-size-before-hiding",!home.IsVisible&&separate.IsVisible&&home.RememberWorkspaceOnQuit&&closed is not null&&closed.Tabs.SequenceEqual([ids[0],ids[2]])&&closed.ActiveId==ids[0]&&closed.SettingsOpen&&closed.Width==680&&closed.Height==510,
                new{homeVisible=home.IsVisible,separateVisible=separate.IsVisible,home.HideRequested,home.RememberWorkspaceOnQuit,snapshot=closed,expectedTabs=new[]{ids[0],ids[2]}});
            var persistedDraft=(await session.Store.LoadNote(ids[0]))!.Value.GetProperty("plain").GetString();var closedTabExists=await session.Store.Contains(ids[1]);
            Check("workspace-x-flushes-latest-draft-and-keeps-closed-tab-in-library",persistedDraft=="Final workspace draft survives closing the whole window."&&closedTabExists,new{persistedDraft,closedTabExists});
            await session.Quit();
            Check("later-quit-retains-hidden-workspace-and-other-windows",session.Windows.Count==0&&(await session.Store.LoadWorkspaceSession())!.Tabs.SequenceEqual([ids[0],ids[2]])&&(await session.Store.LoadWindowSession()).Single().NoteId==separateId);

            var restarted=Create();await restarted.StartWork();var restored=restarted.Windows.Single(window=>window.Mode=="home");await restored.WaitFor(editable);
            Check("tab-mode-restores-order-active-note-settings-and-geometry",restored.NoteTabIds.SequenceEqual([ids[0],ids[2],separateId])&&restored.NoteId==ids[0]&&restored.SettingsTabOpen&&restored.Width==680&&restored.Height==510&&restarted.Windows.Count(window=>window.ContainsNote(ids[0]))==1);
            Check("explicitly-closed-tab-is-not-restored-or-deleted",!restored.NoteTabIds.Contains(ids[1])&&await restarted.Store.Contains(ids[1]));
            await restored.SwitchHomeView(true);await restored.WaitFor(settings);await restarted.PrepareQuit();
            var beforeFailure=JsonSerializer.Serialize(await restarted.Store.LoadWorkspaceSession());
            await SetStoreTrigger(restarted.Store,"CREATE TRIGGER fail_workspace_close BEFORE UPDATE OF extra ON app_state BEGIN SELECT RAISE(ABORT,'synthetic workspace snapshot failure');END;");
            bool failed=false;try{await restored.HideAfterSaving(true);}catch(SqliteException){failed=true;}
            Check("snapshot-failure-keeps-workspace-open-and-old-session-intact",failed&&restored.IsVisible&&!restored.HideRequested&&beforeFailure==JsonSerializer.Serialize(await restarted.Store.LoadWorkspaceSession()));
            await SetStoreTrigger(restarted.Store,"DROP TRIGGER fail_workspace_close;");
            await ClickWorkspaceClose(restored);
            var lastClosed=await restarted.Store.LoadWorkspaceSession();
            Check("last-workspace-x-quits-and-retains-settings-as-active-tab",restarted.Windows.Count==0&&lastClosed?.ActiveId=="settings",new{windowCount=restarted.Windows.Count,restored.windowClosed,restored.HideRequested,snapshot=lastClosed});

            var settingsRestart=Create();await settingsRestart.StartWork();var settingsWindow=settingsRestart.Windows.Single(window=>window.Mode=="home");await settingsWindow.WaitFor(settings);
            Check("settings-active-workspace-reopens-with-all-notes",settingsWindow.IsSettingsView&&settingsWindow.SettingsTabOpen&&settingsWindow.NoteTabIds.SequenceEqual([ids[0],ids[2],separateId]));
            await settingsWindow.CloseNoteTab("settings");await settingsWindow.CloseNoteTab(ids[0]);await settingsWindow.CloseNoteTab(ids[2]);await settingsWindow.CloseNoteTab(separateId);
            await settingsWindow.SwitchHomeView(true);await settingsWindow.WaitFor(settings);
            var noteCountBeforeActivation=Convert.ToInt64(await StoreScalar(settingsRestart.Store,"SELECT COUNT(*) FROM notes;"));
            await settingsRestart.ActivateWork();await settingsWindow.WaitFor(settings);
            Check("second-launch-reuses-settings-only-workspace-without-new-note",settingsRestart.Windows.Count==1&&settingsWindow.NoteTabIds.Count==0&&settingsWindow.NoteId is null&&settingsWindow.IsSettingsView&&Convert.ToInt64(await StoreScalar(settingsRestart.Store,"SELECT COUNT(*) FROM notes;"))==noteCountBeforeActivation);
            await settingsWindow.CloseNoteTab("settings");
            Check("last-settings-tab-close-leaves-permanent-home",!settingsWindow.SettingsTabOpen&&settingsWindow.NoteId is null&&settingsWindow.NoteTabIds.Count==0&&await settingsWindow.Script("JotWorkspace.view==='home'&&document.querySelectorAll('.workspace-tabs [role=tab]').length===1")=="true");
            // Recreate a retained workspace and change only the persisted mode
            // to model a restart into separate windows, without a live transfer.
            await settingsWindow.SwitchNoteTab(ids[0]);await settingsWindow.SwitchNoteTab(ids[2]);await settingsWindow.SwitchHomeView(true);await settingsWindow.SwitchHomeView();await settingsWindow.HideAfterSaving(true);
            await settingsRestart.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var windowRestart=Create();await windowRestart.StartWork();foreach(var window in windowRestart.Windows.Where(window=>window.Mode=="note"))await window.WaitFor("window.jotReady===true");
            Check("retained-workspace-respects-separate-window-opening-mode",windowRestart.Windows.Count(window=>window.Mode=="note")==2&&new[]{ids[0],ids[2]}.All(id=>windowRestart.Windows.Count(window=>window.Mode=="note"&&window.NoteId==id)==1));
            var restoredHome=windowRestart.Windows.Single(window=>window.Mode=="home");await restoredHome.WaitFor("window.JotWorkspace?.view==='home'");
            Check("separate-window-restore-keeps-home-selected-and-settings-open",restoredHome.NoteId is null&&!restoredHome.IsSettingsView&&restoredHome.SettingsTabOpen&&restoredHome.NoteTabIds.Count==0&&await restoredHome.Script("document.querySelector('[role=tab][data-workspace-id=home]').ariaSelected==='true'&&document.querySelector('[role=tab][data-workspace-id=settings]').ariaSelected==='false'")=="true");
            // Exercise the restore-routing boundary with a dormant workspace
            // fixture: two real editors, rather than loading 200 note tabs.
            var fullSession=new JotSession(true,Path.Combine(testOutput,"workspace-restore-capacity")){ExerciseLifecycle=true};sessions.Add(fullSession);
            var fullWorkspace=new MainWindow(fullSession,"home");fullWorkspace.StartInTray();
            var reservedTabs=Enumerable.Range(0,200).Select(_=>Guid.NewGuid().ToString()).ToArray();fullWorkspace.NoteTabIds.AddRange(reservedTabs);
            var overflowIds=new[]{await fullSession.Store.Create(),await fullSession.Store.Create()};
            foreach(var id in overflowIds)
            {
                var overflow=await fullSession.RestoreNoteFromSession(new(id,10,20,440,350),true);await overflow.WaitFor("window.jotReady===true");
            }
            Check("full-workspace-restores-extra-notes-as-separate-windows",fullWorkspace.NoteTabIds.SequenceEqual(reservedTabs)&&fullWorkspace.Browser.CoreWebView2 is null&&!fullWorkspace.IsVisible&&overflowIds.All(id=>fullSession.Windows.Count(window=>window.Mode=="note"&&window.NoteId==id&&window.IsVisible&&window.RestoredLayout is {Width:440,Height:350})==1));
            Check("all-verification-windows-stay-offscreen",sessions.SelectMany(item=>item.Windows).All(window=>window.Left< -10000&&window.Top< -10000&&window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.RuntimeErrors.Count==0));
        }
        finally{foreach(var session in sessions)foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }
}
