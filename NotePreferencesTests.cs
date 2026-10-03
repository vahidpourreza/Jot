using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyNotePreferences(List<object> checks,MainWindow a,MainWindow b)
    {
        var oldA=await a.Script("JSON.stringify(activeNote().view)");var oldB=await b.Script("JSON.stringify(activeNote().view)");
        var defaults=(await store.LoadPreferences()).GetRawText();var oldHtml=await a.Script("editor.innerHTML");
        try
        {
            await a.Script("setPreference({fontSize:23,lineHeight:2.5,toolbarVisible:false,pinned:true}).then(()=>window.ownViewSaved=true)");await a.WaitFor("window.ownViewSaved===true");
            checks.Add(new{name="note-settings-change-only-the-current-note",passed=await a.Script("model.prefs.fontSize===23&&model.prefs.lineHeight===2.5&&document.getElementById('formatBar').hidden&&pinned")=="true"&&await b.Script("JSON.stringify(activeNote().view)")==oldB&&(await store.LoadPreferences()).GetRawText()==defaults&&!a.Topmost});
            await a.Reload();checks.Add(new{name="note-settings-spacing-toolbar-pin-persist-after-reload",passed=await a.Script("model.prefs.fontSize===23&&model.prefs.lineHeight===2.5&&document.getElementById('formatBar').hidden&&pinned")=="true"&&!a.Topmost&&await a.Script("editor.innerHTML")==oldHtml});
            await a.Script("Promise.all([setPreference({fontSize:13}),setPreference({fontSize:18}),setPreference({lineHeight:1.2}),setPreference({toolbarVisible:true})]).then(()=>window.rapidViewSaved=true)");await a.WaitFor("window.rapidViewSaved===true");
            var stored=(await store.LoadNote(a.NoteId!))!.Value.GetProperty("view");
            checks.Add(new{name="rapid-note-settings-keep-latest-values",passed=stored.GetProperty("fontSize").GetInt32()==18&&stored.GetProperty("lineHeight").GetDouble()==1.2&&stored.GetProperty("toolbarVisible").GetBoolean()});
            await a.Script("request('note-preferences',{id:"+JsonSerializer.Serialize(b.NoteId)+",settings:{fontSize:24}}).then(()=>window.crossViewRejected=false,()=>window.crossViewRejected=true)");await a.WaitFor("window.crossViewRejected!==undefined");
            checks.Add(new{name="note-window-cannot-configure-another-note",passed=await a.Script("window.crossViewRejected")=="true"&&await b.Script("JSON.stringify(activeNote().view)")==oldB});
            await a.Script("request('preferences',{fontSize:24}).then(()=>window.globalViewRejected=false,()=>window.globalViewRejected=true)");await a.WaitFor("window.globalViewRejected!==undefined");
            checks.Add(new{name="note-window-cannot-change-global-defaults",passed=await a.Script("window.globalViewRejected")=="true"&&(await store.LoadPreferences()).GetRawText()==defaults});
            await VerifyGlobalTheme(checks,a,b);
            await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme="dark",fontSize=24,lineHeight=2.2,toolbarVisible=false}));await session.Changed();
            await a.WaitFor("model.prefs.theme==='dark'");await b.WaitFor("model.prefs.theme==='dark'");
            checks.Add(new{name="global-theme-updates-existing-notes-without-changing-writing-settings",passed=await a.Script("model.prefs.theme==='dark'&&model.prefs.fontSize===18&&model.prefs.lineHeight===1.2&&model.prefs.toolbarVisible")=="true"&&await b.Script("JSON.stringify(activeNote().view)")==oldB});
            var id=await store.Create();var created=(await store.LoadNote(id))!.Value;
            checks.Add(new{name="new-note-inherits-writing-defaults-and-keeps-toolbar-pin-local",passed=!created.GetProperty("view").TryGetProperty("theme",out _)&&!created.GetProperty("view").TryGetProperty("fontSize",out _)&&!created.GetProperty("view").TryGetProperty("lineHeight",out _)&&!created.GetProperty("view").GetProperty("toolbarVisible").GetBoolean()&&!created.GetProperty("view").GetProperty("pinned").GetBoolean()});await store.Delete(id);
        }
        finally
        {
            using var prefs=JsonDocument.Parse(defaults);await store.SavePreferences(prefs.RootElement);await session.Changed();
            await a.Script("setPreference({fontSize:null,lineHeight:null,...JSON.parse("+oldA+")}).then(()=>window.viewRestored=true)");await a.WaitFor("window.viewRestored===true");
        }
        await VerifyNotePreferenceUpgrade(checks);
    }
    private async Task VerifyGlobalTheme(List<object> checks,MainWindow a,MainWindow b)
    {
        var viewA=await a.Script("JSON.stringify(activeNote().view)");var viewB=await b.Script("JSON.stringify(activeNote().view)");
        var contentA=await a.Script("JSON.stringify([editor.innerHTML,activeNote().color])");var contentB=await b.Script("JSON.stringify([editor.innerHTML,activeNote().color])");
        var image=JsonSerializer.Deserialize<string>(await a.Script("(()=>{const c=document.createElement('canvas');c.width=32;c.height=16;return c.toDataURL();})()"))!;
        var viewer=session.Image(image);
        try
        {
            await viewer.WaitFor("window.jotReady===true");await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
            foreach(var theme in new[]{"light","dark"})
            {
                await a.Script("setAppTheme('"+theme+"').then(()=>window.sharedThemeSaved='"+theme+"')");await a.WaitFor("window.sharedThemeSaved==='"+theme+"'");
                var predicate="document.documentElement.dataset.theme==='"+theme+"'";
                foreach(var window in new[]{this,a,b,viewer})await window.WaitFor(predicate);
                checks.Add(new{name="theme-from-note-updates-index-sibling-image-and-tray-"+theme,passed=session.TrayMenu!.Theme==theme&&(await store.LoadPreferences()).GetProperty("theme").GetString()==theme&&await a.Script("JSON.stringify(activeNote().view)")==viewA&&await b.Script("JSON.stringify(activeNote().view)")==viewB});
            }
            await GoToSettings();await ClickControl("[data-theme-choice=light]");
            foreach(var window in new[]{this,a,b,viewer})await window.WaitFor("document.documentElement.dataset.theme==='light'");
            await a.Reload();await b.Reload();
            checks.Add(new{name="theme-from-settings-persists-across-note-reload",passed=await a.Script("model.prefs.theme==='light'")=="true"&&await b.Script("model.prefs.theme==='light'")=="true"&&session.TrayMenu!.Theme=="light"});
            await a.Script("activeNote().view.theme='dark';applyPrefs()");
            checks.Add(new{name="stale-renderer-note-theme-cannot-override-global-theme",passed=await a.Script("model.prefs.theme==='light'&&document.documentElement.dataset.theme==='light'")=="true"});
            await a.Script("delete activeNote().view.theme;document.getElementById('menuButton').click()");await a.ClickControl("#themeButton");
            foreach(var window in new[]{this,a,b,viewer})await window.WaitFor("document.documentElement.dataset.theme==='dark'");
            checks.Add(new{name="more-theme-control-updates-settings-and-describes-global-scope",passed=await Script("document.querySelector('[data-theme-choice=dark]').getAttribute('aria-pressed')==='true'")=="true"&&await a.Script("document.getElementById('themeButton').title==='Switch all of Jot to light mode'")=="true"});
            await a.Script("closePanels();Promise.all([setAppTheme('light'),setAppTheme('dark'),setAppTheme('light')]).then(()=>window.rapidThemeSaved=true)");await a.WaitFor("window.rapidThemeSaved===true");
            foreach(var window in new[]{this,a,b,viewer})await window.WaitFor("document.documentElement.dataset.theme==='light'");
            checks.Add(new{name="rapid-global-theme-changes-persist-the-last-choice",passed=(await store.LoadPreferences()).GetProperty("theme").GetString()=="light"});
            var before=(await store.LoadPreferences()).GetRawText();
            await a.Script("Promise.all(['system',null,{theme:'dark',fontSize:24}].map(value=>request('app-theme',value).then(()=>false,()=>true))).then(result=>window.invalidThemesRejected=result.every(Boolean))");await a.WaitFor("window.invalidThemesRejected!==undefined");
            checks.Add(new{name="theme-only-note-endpoint-rejects-invalid-values-and-extra-settings",passed=await a.Script("window.invalidThemesRejected")=="true"&&(await store.LoadPreferences()).GetRawText()==before});
            checks.Add(new{name="global-theme-preserves-note-content-colors-and-local-views",passed=await a.Script("JSON.stringify([editor.innerHTML,activeNote().color])")==contentA&&await b.Script("JSON.stringify([editor.innerHTML,activeNote().color])")==contentB&&await a.Script("JSON.stringify(activeNote().view)")==viewA&&await b.Script("JSON.stringify(activeNote().view)")==viewB&&!a.Topmost&&!b.Topmost&&!viewer.Topmost});
        }
        finally
        {
            await a.Script("delete activeNote().view.theme;closePanels()");
            viewer.ClosePermanently();session.TrayMenu?.Hide();
            if(await Script("window.JotWorkspace?.view==='settings'")=="true")await GoToIndex();
        }
    }
    private async Task VerifyNotePreferenceUpgrade(List<object> checks)
    {
        var fixture=new NoteStore(Path.Combine(testOutput,"note-view-upgrade"));var id=await fixture.Create();var other=await fixture.Create();
        await fixture.SavePreferences(JsonSerializer.SerializeToElement(new{theme="light",fontSize=19,lineHeight=2.2,toolbarVisible=false}));
        await SetStoreTrigger(fixture,"DROP TABLE note_preferences;PRAGMA user_version=1;");
        var upgraded=new NoteStore(fixture.Root);var model=(await upgraded.Load())!.Value;
        checks.Add(new{name="schema-upgrade-seeds-writing-settings-and-keeps-theme-global",passed=model.GetProperty("prefs").GetProperty("theme").GetString()=="light"&&model.GetProperty("notes").EnumerateArray().All(n=>{var v=n.GetProperty("view");return !v.TryGetProperty("theme",out _)&&v.GetProperty("fontSize").GetInt32()==19&&v.GetProperty("lineHeight").GetDouble()==2.2&&!v.GetProperty("toolbarVisible").GetBoolean();})&&Convert.ToInt64(await StoreScalar(upgraded,"PRAGMA user_version;"))==2});
        using(var backup=upgraded.Connect(upgraded.BackupPath)){using var cmd=backup.CreateCommand();cmd.CommandText="PRAGMA user_version;";checks.Add(new{name="schema-upgrade-keeps-v1-recovery-snapshot",passed=Convert.ToInt64(cmd.ExecuteScalar())==1});}
        await upgraded.Backup();
        using(var backup=upgraded.Connect(upgraded.FilePath+".before-v2.bak")){using var cmd=backup.CreateCommand();cmd.CommandText="PRAGMA user_version;";checks.Add(new{name="pre-upgrade-snapshot-survives-normal-backups",passed=Convert.ToInt64(cmd.ExecuteScalar())==1});}
        var otherView=(await upgraded.LoadNote(other))!.Value.GetProperty("view").GetRawText();
        await upgraded.SaveNotePreferences(id,JsonSerializer.SerializeToElement(new{fontSize=14,pinned=true}));
        checks.Add(new{name="database-note-settings-do-not-change-siblings",passed=(await upgraded.LoadNote(other))!.Value.GetProperty("view").GetRawText()==otherView});
        foreach(var patch in new object[]{new{fontSize=12},new{fontSize=25},new{lineHeight=0.1},new{theme="system"},new{theme="light"},new{toolbarVisible="true"},new{unknown=true}}){bool rejected=false;try{await upgraded.SaveNotePreferences(id,JsonSerializer.SerializeToElement(patch));}catch(InvalidDataException){rejected=true;}checks.Add(new{name="invalid-note-setting-rejected-"+JsonSerializer.Serialize(patch),passed=rejected});}
        await SetStoreTrigger(upgraded,"UPDATE note_preferences SET value=json_set(value,'$.theme','dark');");
        checks.Add(new{name="old-per-note-theme-is-ignored-on-read",passed=!(await upgraded.LoadNote(id))!.Value.GetProperty("view").TryGetProperty("theme",out _)&&(await upgraded.LoadPreferences()).GetProperty("theme").GetString()=="light"});
        await upgraded.SaveNotePreferences(id,JsonSerializer.SerializeToElement(new{fontSize=14}));
        checks.Add(new{name="old-per-note-theme-is-dropped-on-next-settings-save",passed=Convert.ToInt64(await StoreScalar(upgraded,"SELECT COUNT(*) FROM note_preferences WHERE json_type(value,'$.theme') IS NOT NULL;"))==1});
        var imported=System.Text.Json.Nodes.JsonNode.Parse((await upgraded.Load())!.Value.GetRawText())!;
        imported["notes"]![0]!["view"]!["theme"]="dark";await upgraded.Save(JsonSerializer.SerializeToElement(imported));
        checks.Add(new{name="legacy-note-theme-import-preserves-global-theme-and-writing-settings",passed=(await upgraded.LoadPreferences()).GetProperty("theme").GetString()=="light"&&!(await upgraded.LoadNote(id))!.Value.GetProperty("view").TryGetProperty("theme",out _)&&(await upgraded.LoadNote(id))!.Value.GetProperty("view").GetProperty("fontSize").GetInt32()==14});
        var note=(await upgraded.LoadNote(id))!.Value;await upgraded.SaveNote(JsonSerializer.SerializeToElement(new{id,html="<p>still mine</p>",plain="still mine",updatedAt=1,view=new{fontSize=24}}));
        checks.Add(new{name="content-autosave-does-not-overwrite-note-settings",passed=(await upgraded.LoadNote(id))!.Value.GetProperty("view").GetRawText()==note.GetProperty("view").GetRawText()});
        await upgraded.Delete(id);checks.Add(new{name="deleting-note-cleans-up-own-settings",passed=Convert.ToInt64(await StoreScalar(upgraded,"SELECT COUNT(*) FROM note_preferences;"))==1});
        var blocked=new NoteStore(Path.Combine(testOutput,"blocked-note-view-upgrade"));await blocked.Create();await SetStoreTrigger(blocked,"DROP TABLE note_preferences;PRAGMA user_version=1;");Directory.CreateDirectory(blocked.BackupPath+".tmp");
        bool failed=false;try{await new NoteStore(blocked.Root).Load();}catch(SqliteException){failed=true;}
        checks.Add(new{name="failed-upgrade-backup-leaves-schema-and-notes-intact",passed=failed&&Convert.ToInt64(await StoreScalar(blocked,"PRAGMA user_version;"))==1&&Convert.ToInt64(await StoreScalar(blocked,"SELECT COUNT(*) FROM notes;"))==1});
    }
}
