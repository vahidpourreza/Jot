using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyCustomShortcuts(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="custom-shortcuts-"+name,passed});
        JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        bool Reject(string json){try{ShortcutBindings.ValidateOverrides(JsonDocument.Parse(json).RootElement);return false;}catch(InvalidDataException){return true;}}
        Check("unknown-action-rejected",Reject("{\"unknown\":\"Ctrl+KeyK\"}"));
        Check("bare-letter-does-not-intercept-typing",Reject("{\"new-note\":\"KeyK\"}"));
        Check("native-editing-and-system-keys-protected",new[]{"Ctrl+KeyC","Ctrl+KeyX","Ctrl+KeyV","Ctrl+KeyZ","Ctrl+Shift+KeyZ","Ctrl+KeyA","Alt+F4","Alt+Tab","Win+ArrowUp","Ctrl+ArrowLeft","Ctrl+Backspace","F12"}.All(key=>Reject(Json(new Dictionary<string,string>{{"new-note",key}}).GetRawText())));
        Check("altgr-chord-not-allowed-in-app",Reject("{\"new-note\":\"Ctrl+Alt+KeyK\"}"));
        Check("duplicate-with-default-app-binding-rejected",Reject("{\"new-note\":\"Ctrl+KeyT\"}"));
        Check("duplicate-across-app-and-windows-rejected",Reject("{\"global-new-note\":\"Ctrl+KeyN\"}"));
        var cleared=ShortcutBindings.Resolve(ShortcutBindings.ValidateOverrides(Json(new Dictionary<string,string?>{{"new-note",null}})));
        Check("explicit-clear-retains-null-not-default",cleared["new-note"] is null&&ShortcutBindings.Resolve(new())["new-note"]=="Ctrl+KeyN");
        var legacyPreferences=new JsonObject{["globalShortcuts"]=true};ShortcutBindings.NormalizeStored(legacyPreferences);
        Check("legacy-missing-bindings-preserve-defaults-and-opt-in",legacyPreferences["shortcutBindings"] is JsonObject legacy&&legacy.Count==0&&legacyPreferences["globalShortcuts"]!.GetValue<bool>());
        foreach(var malformed in new[]{"null","[]","{\"new-note\":\"Ctrl+KeyC\"}","{\"unknown-action\":\"Ctrl+KeyK\"}","{\"new-note\":\"Ctrl+KeyT\"}"})
        {
            var prefs=new JsonObject{["shortcutBindings"]=JsonNode.Parse(malformed),["globalShortcuts"]=true};ShortcutBindings.NormalizeStored(prefs);
            Check("malformed-persisted-binding-safe-fallback-"+checks.Count,prefs["shortcutBindings"] is JsonObject map&&map.Count==0&&!prefs["globalShortcuts"]!.GetValue<bool>());
        }

        var active=new Dictionary<int,(uint Modifiers,uint Key)>();var rejectKey=0u;var calls=0;
        using(var registration=new ShortcutRegistration((id,modifiers,key)=>{calls++;if(key==rejectKey)return false;active[id]=(modifiers,key);return true;},id=>active.Remove(id)))
        {
            registration.SetEnabled(true);var before=calls;registration.SetEnabled(true);
            Check("registration-idempotent-with-defaults",calls==before&&registration.Enabled&&active.Count==2);
            var custom=new JsonObject{["global-new-note"]="Ctrl+Shift+KeyK",["global-show-jot"]="Alt+Shift+KeyJ"};registration.Configure(true,custom);
            Check("native-map-applies-recorded-modifiers-and-key",active[ShortcutRegistration.NewNoteId]==(0x4006u,0x4Bu)&&active[ShortcutRegistration.ShowJotId]==(0x4005u,0x4Au));
            rejectKey=0x51;var refused=false;try{registration.Configure(true,new JsonObject{["global-new-note"]="Ctrl+Shift+KeyL",["global-show-jot"]="Ctrl+Shift+KeyQ"});}catch(InvalidOperationException error){refused=error.Message.Contains("previous shortcuts are still active");}
            Check("conflict-rolls-back-entire-previous-native-map",refused&&registration.Enabled&&active.Count==2&&active[ShortcutRegistration.NewNoteId]==(0x4006u,0x4Bu)&&active[ShortcutRegistration.ShowJotId]==(0x4005u,0x4Au));
            rejectKey=0;registration.Configure(true,new JsonObject{["global-new-note"]=null,["global-show-jot"]=null});Check("cleared-global-bindings-reserve-no-keys",registration.Enabled&&active.Count==0);
            registration.SetEnabled(false);Check("disabled-registration-reserves-no-keys",!registration.Enabled&&active.Count==0);
        }
        var testSession=new JotSession(true,Path.Combine(testOutput,"custom-shortcuts"));
        try
        {
            var original=await testSession.Store.LoadPreferences();
            Check("old-defaults-remain-empty-overrides",original.GetProperty("shortcutBindings").EnumerateObject().Count()==0);
            await testSession.ApplyPreferences(Json(new{globalShortcuts=true}));
            var failed=false;try{await testSession.ApplyPreferences(Json(new{globalShortcuts=false,fontSize=100}));}catch(InvalidDataException){failed=true;}
            Check("failed-preference-commit-restores-global-enabled-state",failed&&(await testSession.Store.LoadPreferences()).GetProperty("globalShortcuts").GetBoolean()&&Json(testSession.ShortcutStatus).GetProperty("enabled").GetBoolean());
            await testSession.ApplyPreferences(Json(new{globalShortcuts=false}));
            var home=testSession.Home();await home.WaitFor("window.jotReady===true&&!!window.JotShortcutBindings?.config");
            Check("renderer-defaults-come-from-native-catalog",await home.Script("JotShortcutBindings.config.actions.every(action=>JotShortcutBindings.config.bindings[action.id]===action.defaultChord&&JotShortcutBindings.validate(action.id,action.defaultChord)==='')")=="true");
            await home.ShowShortcutsPage();await home.WaitFor("JotSettings.page==='shortcuts'&&JotShortcutsPage.mounted");
            Check("all-app-file-and-global-actions-editable",await home.Script("document.querySelectorAll('[data-shortcut-id] .shortcut-binding').length===JotShortcutBindings.config.actions.length&&document.querySelectorAll('#windowsShortcutList .shortcut-binding').length===2")=="true");
            await home.ClickControl("[data-shortcut-id='new-note'] .shortcut-binding");
            await home.ShortcutKey("KeyC");
            Check("recording-reserved-key-shows-specific-feedback",await home.Script("document.getElementById('shortcutRecordSave').disabled&&document.querySelector('#shortcutRecordDialog .dialog-error').textContent.includes('reserved')")=="true");
            await home.ShortcutKey("KeyT");
            Check("recording-duplicate-shows-owning-action",await home.Script("document.getElementById('shortcutRecordSave').disabled&&document.querySelector('#shortcutRecordDialog .dialog-error').textContent.includes('New tab')")=="true");
            await home.ShortcutKey("KeyK",true);await home.ClickControl("#shortcutRecordSave");
            await home.WaitFor("!document.getElementById('shortcutRecordDialog').open&&!JotShortcutsPage.busy");
            Check("recording-custom-key-persists-and-updates-label",(await testSession.Store.LoadPreferences()).GetProperty("shortcutBindings").GetProperty("new-note").GetString()=="Ctrl+Shift+KeyK"&&await home.Script("JotShortcutBindings.label('new-note')==='Ctrl+Shift+K'")=="true");
            Check("old-shortcut-and-native-typing-not-intercepted",await home.Script("(()=>{const f=JotAppShortcuts.commandFor;return f({ctrlKey:true,code:'KeyN'})===null&&f({code:'KeyK'})===null&&f({ctrlKey:true,altKey:true,code:'KeyK'})===null&&f({ctrlKey:true,shiftKey:true,code:'KeyK',isComposing:true})===null&&f({ctrlKey:true,shiftKey:true,code:'KeyK',getModifierState:()=>true})===null&&f({ctrlKey:true,code:'KeyC'})===null})()")=="true");
            var noteCount=home.NoteTabIds.Count;await home.ShortcutKey("KeyK",true);await home.WaitFor("JotWorkspace.view==='note'&&!JotAppShortcuts.busy");
            Check("new-shortcut-invokes-action",home.NoteTabIds.Count==noteCount+1);
            await home.Reload();await home.WaitFor("!!window.JotShortcutBindings?.config");Check("binding-survives-document-reload",await home.Script("JotShortcutBindings.label('new-note')==='Ctrl+Shift+K'")=="true");
            await home.ShowShortcutsPage();await home.WaitFor("JotSettings.page==='shortcuts'");
            await home.ClickControl("[data-shortcut-id='new-note'] .shortcut-clear");
            Check("clear-requires-confirmation",await home.Script("document.getElementById('shortcutConfirmDialog').open&&JotShortcutBindings.label('new-note')==='Ctrl+Shift+K'")=="true");
            await home.ClickControl("#shortcutConfirmCancel");Check("cancel-clear-leaves-binding-unchanged",await home.Script("JotShortcutBindings.label('new-note')==='Ctrl+Shift+K'")=="true");
            await home.ClickControl("[data-shortcut-id='new-note'] .shortcut-clear");await home.ClickControl("#shortcutConfirmApply");await home.WaitFor("!document.getElementById('shortcutConfirmDialog').open&&!JotShortcutsPage.busy");
            Check("cleared-binding-stays-unassigned",await home.Script("JotShortcutBindings.config.bindings['new-note']===null&&document.querySelector('[data-shortcut-id=\"new-note\"] .shortcut-binding').textContent==='Not set'")=="true");
            await home.Reload();await home.ShowShortcutsPage();await home.WaitFor("JotSettings.page==='shortcuts'");
            Check("cleared-binding-survives-reload",await home.Script("JotShortcutBindings.config.bindings['new-note']===null")=="true");
            await home.ClickControl("[data-shortcut-id='new-note'] .shortcut-reset");await home.ClickControl("#shortcutConfirmApply");await home.WaitFor("!document.getElementById('shortcutConfirmDialog').open&&!JotShortcutsPage.busy");
            Check("single-reset-restores-default-and-removes-override",await home.Script("JotShortcutBindings.label('new-note')==='Ctrl+N'&&!Object.hasOwn(JotShortcutBindings.config.overrides,'new-note')")=="true");
            await testSession.ApplyPreferences(Json(new{shortcutBindings=new Dictionary<string,string?>{{"new-note","Ctrl+Shift+KeyK"},{"file-save","Ctrl+Shift+KeyD"},{"global-show-jot","Ctrl+Alt+KeyG"}}}));
            await home.WaitFor("JotShortcutBindings.label('file-save')==='Ctrl+Shift+D'");
            Check("file-commands-share-custom-catalog",await home.Script("JotShortcutBindings.match({ctrlKey:true,shiftKey:true,code:'KeyD'},'file')==='file-save'&&JotNoteFiles.editorMenuItems().find(item=>item.id==='file-save').shortcut==='Ctrl+Shift+D'")=="true");
            // The real workspace minimum is 360; also stress 320 explicitly.
            home.MinWidth=320;
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{320,360,900})
            {
                await testSession.ApplyPreferences(Json(new{theme}));home.Width=width;home.Height=620;await Task.Delay(100);
                Check("page-fits-"+theme+"-"+width,await home.Script("(()=>{const page=document.getElementById('shortcutsPage');return page.scrollWidth<=page.clientWidth&&document.documentElement.scrollWidth<=innerWidth})()")=="true");
                await home.Script("document.getElementById('shortcutsPage').scrollTop=0");await home.Capture("custom-shortcuts-"+theme+"-"+width);
                if(width<=360)
                {
                    await home.ClickControl("[data-shortcut-id='new-note'] .shortcut-binding");await home.ShortcutKey("KeyC");
                    Check("record-dialog-opaque-contained-and-error-visible-"+theme+"-"+width,await home.Script("(()=>{const d=document.getElementById('shortcutRecordDialog'),r=d.getBoundingClientRect(),e=d.querySelector('.dialog-error'),c=document.createElement('canvas');c.width=c.height=1;const ctx=c.getContext('2d');ctx.fillStyle=getComputedStyle(d).backgroundColor;ctx.fillRect(0,0,1,1);return d.open&&ctx.getImageData(0,0,1,1).data[3]>=250&&r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1&&d.scrollWidth<=d.clientWidth&&!e.hidden&&getComputedStyle(e).color!==getComputedStyle(d.querySelector('p')).color})()")=="true");
                    await home.Capture("custom-shortcut-record-dialog-"+theme+"-"+width);await home.ClickControl("#shortcutRecordCancel");
                }
            }
            await home.ClickControl("#shortcutsResetAll");await home.ClickControl("#shortcutConfirmApply");await home.WaitFor("!document.getElementById('shortcutConfirmDialog').open&&!JotShortcutsPage.busy");
            Check("reset-all-restores-all-scopes-without-enabling-global-keys",await home.Script("Object.keys(JotShortcutBindings.config.overrides).length===0&&JotShortcutBindings.label('file-save')==='Ctrl+S'&&JotShortcutBindings.label('global-show-jot')==='Ctrl+Alt+J'")=="true"&&!(await testSession.Store.LoadPreferences()).GetProperty("globalShortcuts").GetBoolean());
            Check("no-real-windows-keys-or-visible-test-windows",!Json(testSession.ShortcutStatus).GetProperty("active").GetBoolean()&&testSession.Windows.All(window=>!window.Topmost&&!window.ShowActivated&&!window.ShowInTaskbar&&window.Opacity==0&&window.Left< -10000));
        }
        finally{testSession.DisposeGlobalShortcuts();foreach(var window in testSession.Windows.ToArray())window.ClosePermanently();}
    }
}
