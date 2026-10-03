using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyOpeningMode(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="opening-mode-"+name,passed});
        static JsonElement Mode(string mode)=>JsonSerializer.SerializeToElement(new{newNoteTarget=mode});
        static void Cleanup(JotSession session){foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
        const string ready="window.jotReady===true";
        var s=new JotSession(true,Path.Combine(testOutput,"opening-mode")){ExerciseLifecycle=true};
        try
        {
            await s.Store.SavePreferences(Mode("window"));
            var a=await s.NewNote();var b=await s.NewNote();var hidden=await s.NewNote();
            await a.WaitFor(ready);await b.WaitFor(ready);await hidden.WaitFor(ready);
            var aId=a.NoteId!;var bId=b.NoteId!;var hiddenId=hidden.NoteId!;
            await s.Store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=aId,title="Keep my title",group="Mode tests",color="teal"}));
            await a.Script("editor.innerHTML='<p><strong>Keep my rich draft.</strong></p>';onEdit();clearTimeout(saveTimer)");
            await b.Script("editor.innerHTML='<p>Minimized draft.</p>';onEdit();clearTimeout(saveTimer)");
            b.WindowState=System.Windows.WindowState.Minimized;
            await hidden.HideAfterSaving();
            var home=s.Settings();await home.WaitFor("window.JotWorkspace?.view==='settings'");

            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_mode_draft BEFORE UPDATE OF html ON notes BEGIN SELECT RAISE(ABORT,'synthetic mode draft failure');END;");
            var failed=false;try{await s.ApplyPreferences(Mode("tab"));}catch(IOException){failed=true;}
            await a.WaitFor("!app.inert&&!editorLockedByHost");
            Check("failed-draft-keeps-original-mode-and-editors",failed&&(await s.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="window"&&home.NoteTabIds.Count==0&&a.IsVisible&&b.IsVisible&&await a.Script("editor.textContent==='Keep my rich draft.'&&editor.isContentEditable")=="true");
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_mode_draft;");

            // Fresh stores resolve defaults without inserting preference rows.
            // WritePreferences uses INSERT ... ON CONFLICT UPDATE, so an INSERT
            // trigger exercises failed persistence for both fresh and saved keys.
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_mode_preference BEFORE INSERT ON settings WHEN NEW.key='newNoteTarget' BEGIN SELECT RAISE(ABORT,'synthetic mode preference failure');END;");
            failed=false;try{await s.ApplyPreferences(Mode("tab"));}catch(Microsoft.Data.Sqlite.SqliteException){failed=true;}
            var rollbackMode=(await s.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString();
            Check("failed-preference-rolls-back-staged-tabs",failed&&rollbackMode=="window"&&home.NoteTabIds.Count==0&&a.IsVisible&&b.IsVisible&&s.Windows.Count(w=>w.ContainsNote(aId))==1);
            if(!failed||rollbackMode!="window"||home.NoteTabIds.Count!=0||!a.IsVisible||!b.IsVisible)
                throw new InvalidOperationException(JsonSerializer.Serialize(new{failureInjected=failed,rollbackMode,tabs=home.NoteTabIds.Count,firstVisible=a.IsVisible,secondVisible=b.IsVisible,firstOwners=s.Windows.Count(w=>w.ContainsNote(aId))}));
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_mode_preference;");

            await s.ApplyPreferences(Mode("tab"));
            Check("all-visible-including-minimized-windows-move-to-home",home.NoteTabIds.SequenceEqual([aId,bId])&&!s.Windows.Contains(a)&&!s.Windows.Contains(b)&&home.IsSettingsView);
            Check("hidden-cached-notes-stay-hidden",!hidden.IsVisible&&!home.NoteTabIds.Contains(hiddenId));
            var saved=await s.Store.LoadNote(aId);
            Check("mode-change-preserves-body-title-folder-and-formatting",saved!.Value.GetProperty("html").GetString()!.Contains("<strong>Keep my rich draft.</strong>")&&saved.Value.GetProperty("title").GetString()=="Keep my title"&&saved.Value.GetProperty("group").GetString()=="Mode tests");

            await home.GoToIndex();var libraryId=await s.Store.Create();await s.Changed(false);await home.WaitFor("homeData.notes.some(n=>n.id==='"+libraryId+"')");
            await home.ClickControl("[data-note-id='"+libraryId+"'] .card-open");
            await home.WaitFor("window.JotWorkspace.view==='note'&&activeNote().id==='"+libraryId+"'");
            Check("ordinary-library-open-respects-tabs",home.NoteId==libraryId&&home.NoteTabIds.Contains(libraryId)&&!s.Windows.Any(w=>w.Mode=="note"&&w.NoteId==libraryId));
            await home.Script("editor.innerHTML='<p>Last active tab draft.</p>';onEdit();clearTimeout(saveTimer)");
            await home.SwitchHomeView(true);
            await SetStoreTrigger(s.Store,"CREATE TRIGGER fail_mode_windows BEFORE UPDATE OF value ON settings WHEN NEW.key='newNoteTarget' BEGIN SELECT RAISE(ABORT,'synthetic mode preference failure');END;");
            failed=false;try{await s.ApplyPreferences(Mode("window"));}catch(Microsoft.Data.Sqlite.SqliteException){failed=true;}
            Check("failed-preference-removes-staged-windows-and-retains-tabs",failed&&home.NoteTabIds.SequenceEqual([aId,bId,libraryId])&&s.Windows.Count==2&&(await s.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="tab");
            await SetStoreTrigger(s.Store,"DROP TRIGGER fail_mode_windows;");

            await s.ApplyPreferences(Mode("window"));
            Check("changing-to-windows-moves-all-tabs-and-keeps-home",home.IsVisible&&home.NoteTabIds.Count==0&&home.NoteId is null&&new[]{aId,bId,libraryId}.All(id=>s.Windows.Count(w=>w.Mode=="note"&&w.NoteId==id&&w.IsVisible)==1));
            await home.Script("request('tab-switch',"+JsonSerializer.Serialize(libraryId)+").then(()=>window.staleTabAccepted=true,()=>window.staleTabRejected=true)");
            await home.WaitFor("window.staleTabRejected===true");
            Check("obsolete-tab-selection-cannot-duplicate-a-migrated-window",home.NoteTabIds.Count==0&&home.NoteId is null&&s.Windows.Count(w=>w.ContainsNote(libraryId))==1);
            var opened=await s.OpenNoteDefault(libraryId);await opened.WaitFor(ready);
            Check("default-open-reuses-one-window-owner",opened.Mode=="note"&&s.Windows.Count(w=>w.ContainsNote(libraryId))==1&&await opened.Script("editor.textContent==='Last active tab draft.'")=="true");
            await s.OpenAsTab(libraryId);
            Check("explicit-tab-overrides-window-mode",home.NoteId==libraryId&&(await s.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="window");
            var explicitWindow=await s.OpenNoteWindow(libraryId);await explicitWindow.WaitFor(ready);
            await explicitWindow.Script("editor.innerHTML='<p>Saved before mode/create race.</p>';onEdit();clearTimeout(saveTimer)");explicitWindow.TestSaveDelayMs=150;
            var change=s.ApplyPreferences(Mode("tab"));var create=s.NewNote();await change;var created=await create;
            Check("concurrent-new-note-uses-committed-mode",ReferenceEquals(created,home)&&home.NoteTabIds.Count==4&&s.Windows.Count(w=>w.Mode=="note"&&w.IsVisible)==0);
            Check("mode-create-race-preserves-final-draft",(await s.Store.LoadNote(libraryId))!.Value.GetProperty("plain").GetString()=="Saved before mode/create race.");
            await home.SwitchHomeView();await s.PrepareQuit();var restoreIds=(await s.Store.LoadWindowSession()).Select(item=>item.NoteId).Concat((await s.Store.LoadWorkspaceSession())?.Tabs??[]).ToHashSet();
            Check("quit-snapshot-includes-tabs-while-home-is-selected",restoreIds.SetEquals(home.NoteTabIds)&&!restoreIds.Contains(hiddenId));
            Check("one-owner-per-note",home.NoteTabIds.All(id=>s.Windows.Count(window=>window.ContainsNote(id))==1));
            Check("all-test-windows-offscreen",s.Windows.All(w=>w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowInTaskbar&&!w.Topmost));
            Check("no-renderer-errors",s.Windows.All(w=>w.RuntimeErrors.Count==0));
        }
        finally{Cleanup(s);}

        var startup=new JotSession(true,Path.Combine(testOutput,"opening-mode-startup")){ExerciseLifecycle=true};
        try
        {
            var first=await startup.Store.Create();var second=await startup.Store.Create();
            await startup.Store.SavePreferences(Mode("tab"));
            await startup.Store.SaveWindowSession([new(first,10,20,420,480),new(second,30,40,420,480)]);
            await startup.StartWork();
            var home=startup.Windows.Single();await home.WaitFor(ready);
            Check("restored-notes-use-persisted-opening-mode",home.Mode=="home"&&home.NoteTabIds.SequenceEqual([first,second])&&home.IsVisible);
            await home.Script("editor.innerHTML='<p>Saved before preference and quit.</p>';onEdit();clearTimeout(saveTimer)");home.TestSaveDelayMs=150;
            var change=startup.ApplyPreferences(Mode("window"));var quit=startup.Quit();await change;await quit;
            Check("quit-waits-for-mode-transition",startup.Windows.Count==0&&(await startup.Store.LoadPreferences()).GetProperty("newNoteTarget").GetString()=="window"&&(await startup.Store.LoadNote(second))!.Value.GetProperty("plain").GetString()=="Saved before preference and quit."&&(await startup.Store.LoadWindowSession()).Length==2);
        }
        finally{Cleanup(startup);}
    }
}
