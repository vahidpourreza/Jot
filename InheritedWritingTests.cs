using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyInheritedWriting(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="writing-defaults-"+name,passed});
        await VerifyLegacyWritingDefaults(checks);
        var session=new JotSession(true,Path.Combine(testOutput,"writing-defaults")){ExerciseLifecycle=true};
        try
        {
            var inheritedId=await session.Store.Create();var existingId=await session.Store.Create();
            await session.Store.SaveNotePreferences(existingId,JsonSerializer.SerializeToElement(new{fontSize=16,lineHeight=1.95,toolbarVisible=false,pinned=true}));
            var inheritedView=(await session.Store.LoadNote(inheritedId))!.Value.GetProperty("view");
            Check("new-note-stores-only-local-toolbar-and-pin",!inheritedView.TryGetProperty("fontSize",out _)&&!inheritedView.TryGetProperty("lineHeight",out _)&&inheritedView.GetProperty("toolbarVisible").GetBoolean()&&!inheritedView.GetProperty("pinned").GetBoolean());
            await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=20,lineHeight=2.2}));
            var inherited=new MainWindow(session,"note",inheritedId);var existing=new MainWindow(session,"note",existingId);inherited.Reveal();existing.Reveal();
            await inherited.WaitFor("window.jotReady===true");await existing.WaitFor("window.jotReady===true");
            Check("unconfigured-note-resolves-current-app-defaults",await inherited.Script("model.prefs.fontSize===20&&model.prefs.lineHeight===2.2&&document.getElementById('defaultFontSize').disabled&&document.getElementById('defaultLineHeight').disabled")=="true");
            Check("existing-equal-baseline-snapshots-stay-explicit",await existing.Script("model.prefs.fontSize===16&&model.prefs.lineHeight===1.95&&!keepFormatOpen&&pinned&&!document.getElementById('defaultFontSize').disabled")=="true");
            var original=await inherited.Script("editor.innerHTML");var oldExisting=(await session.Store.LoadNote(existingId))!.Value.GetProperty("view").GetRawText();
            var home=session.Settings();await home.WaitFor("window.jotReady===true&&JotWorkspace.view==='settings'");
            await home.ClickControl("#settingsFontLarger");await inherited.WaitFor("model.prefs.fontSize===21");
            Check("app-font-stepper-updates-only-inheriting-notes",await existing.Script("model.prefs.fontSize===16")=="true"&&(await session.Store.LoadPreferences()).GetProperty("fontSize").GetInt32()==21);
            await home.Script("document.getElementById('settingsLineHeight').value='1.5';document.getElementById('settingsLineHeight').dispatchEvent(new Event('change',{bubbles:true}))");await inherited.WaitFor("model.prefs.lineHeight===1.5");
            Check("app-line-choice-preserves-existing-note-spacing",await existing.Script("model.prefs.lineHeight===1.95")=="true"&&(await session.Store.LoadNote(existingId))!.Value.GetProperty("view").GetRawText()==oldExisting);
            await inherited.Script("setPreference({fontSize:17}).then(()=>window.localFontSet=true)");await inherited.WaitFor("window.localFontSet===true");
            await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=22,lineHeight=1.75}));await session.Changed();await inherited.WaitFor("model.prefs.lineHeight===1.75");
            Check("overrides-are-independent-per-writing-setting",await inherited.Script("model.prefs.fontSize===17&&model.prefs.lineHeight===1.75&&!document.getElementById('defaultFontSize').disabled&&document.getElementById('defaultLineHeight').disabled")=="true");
            await inherited.ClickControl("#menuButton");await inherited.ClickControl("#defaultFontSize");await inherited.WaitFor("!('fontSize' in activeNote().view)&&model.prefs.fontSize===22&&!document.getElementById('defaultFontSize').hasAttribute('aria-busy')");
            Check("use-default-removes-the-stored-font-override",!(await session.Store.LoadNote(inheritedId))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));await inherited.Script("closePanels()");
            await existing.ClickControl("#menuButton");await existing.ClickControl("#defaultLineHeight");await existing.WaitFor("!('lineHeight' in activeNote().view)&&model.prefs.lineHeight===1.75&&!document.getElementById('defaultLineHeight').hasAttribute('aria-busy')");
            Check("resetting-line-height-preserves-font-toolbar-and-pin",await existing.Script("model.prefs.fontSize===16&&!keepFormatOpen&&pinned&&activeNote().view.fontSize===16")=="true");await existing.Script("closePanels()");
            await inherited.Script("setPreference({fontSize:22}).then(()=>window.sameAsDefaultSet=true)");await inherited.WaitFor("window.sameAsDefaultSet===true");
            await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=23}));await session.Changed();await home.WaitFor("prefs.fontSize===23");
            Check("explicitly-choosing-the-current-default-still-creates-override",await inherited.Script("model.prefs.fontSize===22&&activeNote().view.fontSize===22")=="true");
            var reopened=new NoteStore(session.Store.Root);Check("sparse-and-legacy-settings-persist-across-reopen",(await reopened.LoadNote(inheritedId))!.Value.GetProperty("view").GetProperty("fontSize").GetInt32()==22&&!(await reopened.LoadNote(existingId))!.Value.GetProperty("view").TryGetProperty("lineHeight",out _));
            Check("writing-default-changes-do-not-rewrite-rich-content",await inherited.Script("editor.innerHTML")==original);
            foreach(var patch in new object[]{new{fontSize=12},new{fontSize=25},new{fontSize=16.5},new{fontSize="18"},new{lineHeight=0.5},new{lineHeight=3}})
            {
                var before=(await session.Store.LoadPreferences()).GetRawText();bool rejected=false;try{await session.Store.SavePreferences(JsonSerializer.SerializeToElement(patch));}catch(InvalidDataException){rejected=true;}
                Check("invalid-global-default-is-atomic-"+JsonSerializer.Serialize(patch),rejected&&(await session.Store.LoadPreferences()).GetRawText()==before);
            }
            bool badReset=false;try{await session.Store.SaveNotePreferences(existingId,JsonSerializer.SerializeToElement(new{toolbarVisible=(bool?)null}));}catch(InvalidDataException){badReset=true;}
            Check("toolbar-and-pin-cannot-become-global-inheritance",badReset);
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,900})
            {
                await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await session.Changed();home.Width=width;home.Height=540;await Task.Delay(100);
                Check("compact-settings-fit-"+theme+"-"+width,await home.Script("(()=>{const content=document.querySelector('#workspaceSettings .appearance'),mode=document.getElementById('newNoteTarget').getBoundingClientRect();return content.scrollWidth<=content.clientWidth&&mode.width<230&&mode.left>=0&&mode.right<=innerWidth&&document.getElementById('newNoteTargetLabel').textContent==='Open mode'&&document.querySelectorAll('#settingsLineHeight option').length>=6})()")=="true");
                await home.Capture("writing-defaults-"+theme+"-"+width);
            }
            Check("all-tests-are-isolated",session.Windows.All(w=>w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Left< -10000&&w.Top< -10000));
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}
    }

    private async Task VerifyLegacyWritingDefaults(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="writing-defaults-legacy-"+name,passed});
        foreach(var route in new[]{"json-migration","browser-import"})
        {
            var legacy=new NoteStore(Path.Combine(testOutput,"writing-defaults-legacy",route));
            var missing=Guid.NewGuid().ToString();var custom=Guid.NewGuid().ToString();var sparse=Guid.NewGuid().ToString();
            var data=JsonSerializer.SerializeToElement(new{
                version=2,activeId=missing,prefs=new{fontSize=19,lineHeight=2.2,toolbarVisible=false,theme="dark"},
                notes=new object[]{
                    new{id=missing,html="<p><b>Existing rich note</b></p>",plain="Existing rich note",updatedAt=1},
                    new{id=custom,html="<p>Custom typography</p>",plain="Custom typography",updatedAt=2,view=new{fontSize=14,lineHeight=1.5,toolbarVisible=true,pinned=false}},
                    new{id=sparse,html="<p>Explicit inheritance</p>",plain="Explicit inheritance",updatedAt=3,view=new{toolbarVisible=true,pinned=false}}
                }
            });
            if(route=="json-migration"){Directory.CreateDirectory(legacy.Root);await File.WriteAllTextAsync(legacy.LegacyFilePath,data.GetRawText());}
            else await legacy.Import(data);
            var migrated=(await legacy.LoadNote(missing))!.Value;var view=migrated.GetProperty("view");
            Check(route+"-missing-view-snapshots-previous-effective-typography",view.GetProperty("fontSize").GetInt32()==19&&view.GetProperty("lineHeight").GetDouble()==2.2&&!view.GetProperty("toolbarVisible").GetBoolean()&&migrated.GetProperty("html").GetString()=="<p><b>Existing rich note</b></p>");
            var explicitView=(await legacy.LoadNote(custom))!.Value.GetProperty("view");
            Check(route+"-keeps-explicit-and-sparse-views",explicitView.GetProperty("fontSize").GetInt32()==14&&explicitView.GetProperty("lineHeight").GetDouble()==1.5&&!(await legacy.LoadNote(sparse))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));
            await legacy.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=22,lineHeight=1.2}));
            var reopened=new NoteStore(legacy.Root);var preserved=(await reopened.LoadNote(missing))!.Value.GetProperty("view");
            Check(route+"-global-changes-do-not-reset-migrated-note",preserved.GetProperty("fontSize").GetInt32()==19&&preserved.GetProperty("lineHeight").GetDouble()==2.2);
            var newId=await reopened.Create();
            Check(route+"-new-notes-still-inherit",!(await reopened.LoadNote(newId))!.Value.GetProperty("view").TryGetProperty("fontSize",out _)&&!(await reopened.LoadNote(newId))!.Value.GetProperty("view").TryGetProperty("lineHeight",out _));
            if(route=="json-migration")Check("source-json-remains-byte-identical",await File.ReadAllTextAsync(legacy.LegacyFilePath)==data.GetRawText());
            foreach(var invalidView in new object[]{"invalid view",new[]{"fontSize", "lineHeight"}})
            {
                var shape=invalidView is string?"string":"array";
                var invalidStore=new NoteStore(Path.Combine(testOutput,"writing-defaults-legacy",route+"-invalid-"+shape));
                var invalidData=JsonSerializer.SerializeToElement(new{
                    version=2,activeId=missing,prefs=new{fontSize=19,lineHeight=2.2},
                    notes=new object[]{
                        new{id=missing,html="<p>First note must not be partially imported</p>",plain="First note must not be partially imported",updatedAt=1},
                        new{id=custom,html="<p>Invalid view</p>",plain="Invalid view",updatedAt=2,view=invalidView}
                    }
                });
                if(route=="json-migration"){Directory.CreateDirectory(invalidStore.Root);await File.WriteAllTextAsync(invalidStore.LegacyFilePath,invalidData.GetRawText());}
                bool rejected=false;
                try{if(route=="json-migration")await invalidStore.Load();else await invalidStore.Import(invalidData);}
                catch(InvalidDataException){rejected=true;}
                if(route=="json-migration")
                    Check(route+"-invalid-"+shape+"-view-rejected-without-publishing",rejected&&!File.Exists(invalidStore.FilePath)&&await File.ReadAllTextAsync(invalidStore.LegacyFilePath)==invalidData.GetRawText()&&Directory.GetFiles(invalidStore.Root,"jot-initializing-*").Length==0);
                else
                    Check(route+"-invalid-"+shape+"-view-rejected-without-partial-import",rejected&&await invalidStore.Load() is null&&(await invalidStore.LoadPreferences()).GetProperty("fontSize").GetInt32()==16);
            }
        }
    }
}
