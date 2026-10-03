using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyShortcutsSettings(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="shortcuts-"+name,passed});
        var active=new HashSet<int>();var unregistered=new List<int>();var fail=true;var attempts=0;
        using(var registration=new ShortcutRegistration((id,key)=>{attempts++;if(fail&&id==ShortcutRegistration.ShowJotId)return false;active.Add(id);return true;},id=>{unregistered.Add(id);active.Remove(id);}))
        {
            bool conflict=false;try{registration.SetEnabled(true);}catch(InvalidOperationException error){conflict=error.Message.Contains("Ctrl+Alt+J")&&error.Message.Contains("still off");}
            Check("windows-conflict-rolls-back-all-registered-keys",conflict&&!registration.Enabled&&active.Count==0&&unregistered.SequenceEqual([ShortcutRegistration.NewNoteId]));
            fail=false;registration.SetEnabled(true);var afterEnable=attempts;registration.SetEnabled(true);
            Check("windows-registration-is-idempotent",registration.Enabled&&active.Count==2&&attempts==afterEnable);
            registration.SetEnabled(false);registration.Dispose();
            Check("windows-disable-and-dispose-release-every-key",active.Count==0&&!registration.Enabled&&unregistered.Count==3);
        }
        // The production close path deliberately leaves generic test windows
        // visible; this fixture explicitly exercises real close/hide behavior.
        var testSession=new JotSession(true,Path.Combine(testOutput,"shortcuts-settings")){ExerciseLifecycle=true};
        try
        {
            var initial=await testSession.Store.LoadPreferences();
            Check("new-profile-defaults-to-tabs-and-opt-in-file-save-and-global-keys",initial.GetProperty("newNoteTarget").GetString()=="tab"&&!initial.GetProperty("autoSaveFiles").GetBoolean()&&!initial.GetProperty("globalShortcuts").GetBoolean());
            var home=testSession.Home();await home.WaitFor("window.jotReady===true&&!!window.JotAppShortcuts");
            Check("layout-independent-command-map",await home.Script("(()=>{const map=JotAppShortcuts.commandFor;return map({ctrlKey:true,code:'KeyN',key:'ن'})==='new-note'&&map({ctrlKey:true,shiftKey:true,code:'Tab'})==='previous-tab'&&map({ctrlKey:true,code:'Digit9'})==='tab-9'&&map({ctrlKey:true,altKey:true,code:'KeyN'})===null&&map({ctrlKey:true,isComposing:true,code:'KeyN'})===null&&map({ctrlKey:true,code:'KeyB'})===null&&map({ctrlKey:true,code:'KeyS'})===null})()")=="true");
            await home.ShortcutKey("KeyN");await home.WaitFor("JotWorkspace.view==='note'&&JotWorkspace.tabs.length===1&&!JotAppShortcuts.busy");var first=home.NoteId!;
            await home.ShortcutKey("KeyT");await home.WaitFor("JotWorkspace.tabs.length===2&&!JotAppShortcuts.busy");var second=home.NoteId!;
            Check("new-note-and-new-tab-open-in-same-workspace",first!=second&&testSession.Windows.Count==1);
            await home.ShortcutKey("Digit1");await home.WaitFor("JotWorkspace.view==='home'&&!JotAppShortcuts.busy");
            Check("home-is-first-tab",home.NoteId is null&&!home.IsSettingsView);
            await home.ShortcutKey("Digit9");await home.WaitFor("JotWorkspace.view==='note'&&!JotAppShortcuts.busy");
            Check("nine-activates-last-tab",home.NoteId==second);
            await home.ShortcutKey("Tab",true);await home.WaitFor("!JotAppShortcuts.busy");Check("previous-tab",home.NoteId==first);
            await home.ShortcutKey("Tab");await home.WaitFor("!JotAppShortcuts.busy");Check("next-tab",home.NoteId==second);
            await home.ShortcutKey("KeyW");await home.WaitFor("JotWorkspace.tabs.length===1&&!JotAppShortcuts.busy");
            Check("close-tab-retains-library-note",!home.NoteTabIds.Contains(second)&&await testSession.Store.Contains(second)&&home.NoteId==first);
            await home.ShortcutKey("Comma");await home.WaitFor("JotWorkspace.view==='settings'&&!JotAppShortcuts.busy");
            Check("settings-shortcut-opens-settings-tab",home.SettingsTabOpen&&home.IsSettingsView);
            await home.ShortcutKey("KeyP",true);await home.WaitFor("JotSettings.page==='shortcuts'&&!JotAppShortcuts.busy");
            Check("dedicated-searchable-page-with-context-and-back",await home.Script("document.getElementById('appearance').hidden&&document.getElementById('settingsPageTitle').textContent==='Keyboard shortcuts'&&document.getElementById('settingsBack').ariaLabel==='Back to Settings'&&document.querySelectorAll('#shortcutGroups .shortcut-row').length>=25")=="true");
            await home.Script("document.getElementById('shortcutSearch').value='save';document.getElementById('shortcutSearch').dispatchEvent(new Event('input'))");
            Check("search-filters-shortcuts-without-hiding-windows-preference",await home.Script("document.querySelectorAll('#shortcutGroups .shortcut-row:not([hidden])').length>=2&&document.querySelectorAll('#shortcutGroups .shortcut-row:not([hidden])').length<10&&!document.getElementById('globalShortcuts').hidden")=="true");
            await home.Script("document.getElementById('shortcutSearch').value='';document.getElementById('shortcutSearch').dispatchEvent(new Event('input'))");
            await home.ClickControl("#globalShortcuts");await home.WaitFor("prefs.globalShortcuts===true&&!document.getElementById('globalShortcuts').disabled");
            var hotkeyStatus=JsonSerializer.SerializeToElement(testSession.ShortcutStatus);
            Check("global-keys-test-never-registers-with-windows",hotkeyStatus.GetProperty("enabled").GetBoolean()&&!hotkeyStatus.GetProperty("active").GetBoolean()&&hotkeyStatus.GetProperty("testing").GetBoolean());
            await home.ClickControl("#globalShortcuts");await home.WaitFor("prefs.globalShortcuts===false&&!document.getElementById('globalShortcuts').disabled");
            foreach(var theme in new[]{"dark","light"})foreach(var width in new[]{360,900})
            {
                await testSession.Store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await testSession.Changed();home.Width=width;home.Height=600;await Task.Delay(100);
                Check("page-fits-"+theme+"-"+width,await home.Script("(()=>{const page=document.getElementById('shortcutsPage');return page.scrollWidth<=page.clientWidth&&document.querySelectorAll('#shortcutGroups kbd').length>30&&document.getElementById('settingsBack').getBoundingClientRect().left>=0})()")=="true");
                await home.Capture("shortcuts-"+theme+"-"+width);
            }
            await home.ClickControl("#settingsBack");Check("back-returns-to-settings-without-changing-tab",home.IsSettingsView&&await home.Script("JotSettings.page==='settings'&&!document.getElementById('appearance').hidden")=="true");
            await home.ClickControl("#autoSaveFiles");await home.WaitFor("prefs.autoSaveFiles===true&&!document.getElementById('autoSaveFiles').disabled");
            Check("auto-save-toggle-persists-with-clear-label",(await testSession.Store.LoadPreferences()).GetProperty("autoSaveFiles").GetBoolean()&&await home.Script("document.getElementById('autoSaveFiles').getAttribute('aria-checked')==='true'&&document.getElementById('autoSaveFilesHint').textContent.includes('1 second')")=="true");
            await home.ClickControl("#autoSaveFiles");await home.WaitFor("prefs.autoSaveFiles===false&&!document.getElementById('autoSaveFiles').disabled");
            await home.ShortcutKey("KeyH",true);await home.WaitFor("JotWorkspace.view==='home'&&!JotAppShortcuts.busy");
            await home.ShortcutKey("KeyW");await home.WaitFor("!JotAppShortcuts.busy");Check("close-home-keeps-permanent-home-tab",home.IsVisible&&home.NoteId is null&&!home.IsSettingsView);
            var count=home.NoteTabIds.Count;await home.Script("window.shortcutModal=document.createElement('dialog');document.body.append(shortcutModal);shortcutModal.showModal()");
            await home.ShortcutKey("KeyN");Check("modal-keeps-keyboard-ownership",home.NoteTabIds.Count==count);await home.Script("shortcutModal.close();shortcutModal.remove()");
            var separate=await testSession.OpenNoteWindow(second);await separate.WaitFor("window.jotReady===true&&!!window.JotAppShortcuts");
            await separate.ShortcutKey("KeyT");await separate.WaitFor("!JotAppShortcuts.busy");await home.WaitFor("JotWorkspace.tabs.length===2");
            Check("new-tab-from-separate-note-opens-home-workspace",separate.IsVisible&&separate.NoteId==second&&home.NoteTabIds.Count==2&&!home.NoteTabIds.Contains(second));
            await separate.ShortcutKey("KeyW");for(var attempt=0;attempt<100&&separate.IsVisible;attempt++)await Task.Delay(50);
            Check("close-shortcut-hides-only-separate-note",!separate.IsVisible&&home.IsVisible&&await testSession.Store.Contains(second));
            Check("verification-remains-offscreen",testSession.Windows.All(w=>!w.Topmost&&!w.ShowActivated&&!w.ShowInTaskbar&&w.Opacity==0&&w.Left< -10000&&w.Top< -10000));
        }
        finally{testSession.DisposeGlobalShortcuts();foreach(var window in testSession.Windows.ToArray())window.ClosePermanently();}
    }
    private async Task ShortcutKey(string code,bool shift=false)
    {
        var key=code=="Tab"?"Tab":code=="Comma"?",":code.StartsWith("Key")?code[3..].ToLowerInvariant():code.Replace("Digit","");
        var virtualKey=code=="Tab"?9:code=="Comma"?188:code.StartsWith("Key")?(int)code[3]:(int)code[^1];
        // WebView-only input verifies real Chromium shortcut delivery without
        // synthesizing desktop input or touching another app's keyboard focus.
        foreach(var type in new[]{"rawKeyDown","keyUp"})
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type,key,code,windowsVirtualKeyCode=virtualKey,modifiers=shift?10:2}));
    }
}
