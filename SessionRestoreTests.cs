using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifySessionRestore(List<object> checks)
    {
        var output=Path.Combine(testOutput,"work-session");var sessions=new List<JotSession>();
        JotSession NewSession(){var s=new JotSession(true,output);sessions.Add(s);new MainWindow(s,"home").StartInTray();return s;}
        void CloseSession(JotSession s){foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
        void Check(string name,bool passed)=>checks.Add(new{name="work-session-"+name,passed});
        try
        {
            var first=NewSession();await first.StartWork();
            var a=first.Windows.Single(w=>w.Mode=="note");await a.WaitFor("window.jotReady===true");
            Check("fresh-launch-opens-empty-note-with-home-dormant",first.Windows.Single(w=>w.Mode=="home") is {IsVisible:false} home&&home.Browser.CoreWebView2 is null&&await a.Script("editor.textContent.trim()===''")=="true");
            await a.Script("editor.innerHTML='<p>Restore فارسی English</p><p><b>Keep formatting</b></p>';onEdit();setPreference({fontSize:21,lineHeight:2.2,toolbarVisible:false,pinned:true}).then(()=>setNoteColor('blue')).then(()=>saveNow()).then(()=>window.resumeSaved=true)");await a.WaitFor("window.resumeSaved===true");
            var savedHtml=await a.Script("editor.innerHTML");a.Width=480;a.Height=340;
            var b=await first.NewNote();await b.WaitFor("window.jotReady===true");b.Width=540;b.Height=380;
            var hidden=await first.NewNote();await hidden.WaitFor("window.jotReady===true");hidden.Hide();
            var image=first.Image(JsonSerializer.Deserialize<string>(await a.Script("(()=>{const c=document.createElement('canvas');return c.toDataURL();})()"))!);await image.WaitFor("window.jotReady===true");
            await first.PrepareQuit();
            var snapshot=await first.Store.LoadWindowSession();var expected=new[]{a.NoteId,b.NoteId}.ToHashSet();
            Check("quit-snapshots-only-visible-notes-not-home-hidden-or-images",snapshot.Length==2&&snapshot.All(s=>expected.Contains(s.NoteId))&&File.Exists(first.Store.BackupPath));
            Check("saved-window-size-kept",snapshot.Single(s=>s.NoteId==a.NoteId).Width==480&&snapshot.Single(s=>s.NoteId==b.NoteId).Height==380);
            CloseSession(first);
            var restarted=NewSession();await Task.WhenAll(restarted.StartWork(),restarted.StartWork());
            var restored=restarted.Windows.Where(w=>w.Mode=="note").ToArray();foreach(var w in restored)await w.WaitFor("window.jotReady===true");
            Check("restart-restores-same-notes-once-without-home",restored.Length==2&&restored.All(w=>expected.Contains(w.NoteId)&&w.IsVisible)&&restarted.Windows.Single(w=>w.Mode=="home").Browser.CoreWebView2 is null);
            var restoredA=restored.Single(w=>w.NoteId==a.NoteId);
            Check("restore-preserves-rich-bilingual-content-and-own-settings",await restoredA.Script("editor.innerHTML")==savedHtml&&await restoredA.Script("model.prefs.fontSize===21&&model.prefs.lineHeight===2.2&&!keepFormatOpen&&pinned&&activeNote().color==='blue'")=="true");
            Check("restore-keeps-test-windows-offscreen-and-non-topmost",restored.All(w=>w.Left< -10000&&w.Top< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.RestoredLayout is not null));
            await restarted.ActivateWork();Check("second-launch-reuses-visible-work-notes",restarted.Windows.Count(w=>w.Mode=="note")==2&&!restarted.Windows.Single(w=>w.Mode=="home").IsVisible);
            // Failed persistence must neither close windows nor replace the last snapshot.
            var previous=JsonSerializer.Serialize(await restarted.Store.LoadWindowSession());
            await SetStoreTrigger(restarted.Store,"CREATE TRIGGER fail_window_session BEFORE UPDATE OF extra ON app_state BEGIN SELECT RAISE(ABORT,'synthetic session failure');END;");
            bool failed=false;try{await restarted.PrepareQuit();}catch(SqliteException){failed=true;}
            Check("failed-session-save-leaves-windows-and-old-session-intact",failed&&restored.All(w=>w.IsVisible)&&JsonSerializer.Serialize(await restarted.Store.LoadWindowSession())==previous);
            await SetStoreTrigger(restarted.Store,"DROP TRIGGER fail_window_session;");
            foreach(var w in restored)w.Hide();await restarted.PrepareQuit();Check("quit-with-no-on-screen-notes-remembers-empty-session",(await restarted.Store.LoadWindowSession()).Length==0);CloseSession(restarted);
            var emptyRestart=NewSession();await emptyRestart.StartWork();var empty=emptyRestart.Windows.Single(w=>w.Mode=="note");await empty.WaitFor("window.jotReady===true");
            Check("empty-session-starts-one-new-blank-not-an-old-note",!expected.Contains(empty.NoteId)&&empty.NoteId!=hidden.NoteId&&await empty.Script("editor.textContent.trim()===''")=="true"&&(await emptyRestart.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==4);
            var openedHome=emptyRestart.Home();await openedHome.WaitFor("window.jotReady===true");
            Check("home-remains-explicitly-accessible-with-gear-icon",await openedHome.Script("document.querySelector('#homeHandle .index-brand span').textContent==='Home'&&document.getElementById('settingsButton').dataset.icon==='settings'&&!!document.querySelector('#settingsButton circle')")=="true");
            empty.Hide();await emptyRestart.ActivateWork();Check("relaunch-with-all-work-hidden-creates-one-blank",emptyRestart.Windows.Count(w=>w.Mode=="note"&&w.IsVisible)==1&&emptyRestart.Windows.Any(w=>w.Mode=="note"&&w.IsVisible&&w.NoteId!=empty.NoteId));
            foreach(var w in emptyRestart.Windows.Where(w=>w.Mode=="note"))await w.WaitFor("window.jotReady===true");
            CloseSession(emptyRestart);
            var storeOnly=new NoteStore(Path.Combine(testOutput,"session-records"));var id=await storeOnly.Create();var other=await storeOnly.Create();
            await SetStoreTrigger(storeOnly,"UPDATE app_state SET extra='{\"unrelatedLegacyValue\":17}';");
            await storeOnly.SaveWindowSession([new(id,20,30,440,500),new(id,20,30,440,500),new(other,40,60,540,600)]);
            Check("snapshot-deduplicates-without-overwriting-other-app-state",(await storeOnly.LoadWindowSession()).Length==2&&(await storeOnly.Load())!.Value.GetProperty("unrelatedLegacyValue").GetInt32()==17);
            await storeOnly.Delete(other);Check("deleted-note-is-not-resurrected",(await storeOnly.LoadWindowSession()).Select(s=>s.NoteId).SequenceEqual([id]));
            var beforeInvalid=JsonSerializer.Serialize(await storeOnly.LoadWindowSession());bool rejected=false;
            try{await storeOnly.SaveWindowSession([new(id,0,0,-1,500)]);}catch(InvalidDataException){rejected=true;}
            Check("invalid-layout-cannot-overwrite-session",rejected&&JsonSerializer.Serialize(await storeOnly.LoadWindowSession())==beforeInvalid);
            foreach(var area in new[]{new System.Drawing.Rectangle(0,0,1920,1040),new System.Drawing.Rectangle(-1600,-900,1600,860),new System.Drawing.Rectangle(1920,100,1280,720)})
            {
                var fit=FitNoteLayout(new(id,-32000,32000,12000,9000),area);
                Check("disconnected-monitor-layout-clamps-to-work-area-"+area.X,fit.X>=area.Left&&fit.Y>=area.Top&&fit.X+fit.Width<=area.Right&&fit.Y+fit.Height<=area.Bottom);
            }
            var brokenOutput=Path.Combine(testOutput,"broken-session");var broken=new JotSession(true,brokenOutput);sessions.Add(broken);var original=await broken.Store.Create();
            await SetStoreTrigger(broken.Store,"UPDATE app_state SET extra='{\"desktopSession\":{\"version\":999,\"windows\":[]}}';");
            rejected=false;try{await broken.StartWork();}catch(InvalidDataException){rejected=true;}
            Check("invalid-session-fails-without-creating-or-overwriting-notes",rejected&&!broken.Windows.Any()&&(await broken.Store.LoadIndex())!.Value.GetProperty("notes").GetArrayLength()==1&&await broken.Store.Contains(original));
        }
        finally{foreach(var s in sessions)CloseSession(s);}
    }
}
