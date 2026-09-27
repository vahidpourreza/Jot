using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifySimplifiedShell(List<object> checks,MainWindow note)
    {
        var originalWidth=Width;var originalHeight=Height;var prefs=await store.LoadPreferences();
        try
        {
            checks.Add(new{name="index-header-has-only-new-settings-minimize-close",passed=await Script("[...document.querySelectorAll('#homeHandle button')].map(b=>b.id).join(',')==='homeNew,settingsButton,homeMinimize,homeClose'&&!document.querySelector('#homeSearch,#groupFilters,#noteCount,#homeQuit,.home-footer')")=="true"});
            foreach(var theme in new[]{"dark","light"})
            {
                await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await session.Changed();
                await WaitFor("document.documentElement.dataset.theme==='"+theme+"'");
                foreach(var size in new[]{(Width:360d,Height:280d),(Width:520d,Height:540d)})
                {
                    Width=size.Width;Height=size.Height;await Task.Delay(120);
                    checks.Add(new{name=$"compact-index-controls-fit-{theme}-{size.Width}",passed=await Script("[...document.querySelectorAll('#homeHandle button')].every(b=>{const r=b.getBoundingClientRect();return r.width>=26&&r.height>=26&&r.left>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;})&&document.documentElement.scrollWidth<=innerWidth")=="true"});
                }
                await Capture("simple-index-"+theme);
                await GoToSettings();Width=360;Height=280;await Task.Delay(120);
                checks.Add(new{name="compact-settings-fit-"+theme,passed=await Script("!document.querySelector('#trayVisibility,[data-weight],#homeFontSize,#homeLineHeight,#toolbarVisible')&&[...document.querySelectorAll('button')].every(b=>{const r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight;})")=="true"});
                await Capture("simple-settings-"+theme);await GoToIndex();
            }
            foreach(var selector in new[]{"#homeMinimize","#homeClose"})await ClickControl(selector);
            checks.Add(new{name="index-close-and-minimize-use-distinct-host-actions-without-quitting",passed=TestHostActions.Contains("minimize")&&TestHostActions.Contains("hide")&&!windowClosed&&session.Windows.Contains(note)});
            await GoToSettings();await ClickControl("#settingsMinimize");await ClickControl("#settingsClose");
            checks.Add(new{name="settings-close-and-minimize-keep-app-alive",passed=!windowClosed&&session.Windows.Contains(note)});await GoToIndex();
            var actions=TestHostActions.Count;var noteActions=note.TestHostActions.Count;var noteCount=session.Windows.Count;
            const string press="['KeyN','KeyK','KeyS','KeyF'].forEach(code=>document.dispatchEvent(new KeyboardEvent('keydown',{key:code.slice(3).toLowerCase(),code,ctrlKey:true,shiftKey:code==='KeyF',bubbles:true,cancelable:true})));document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}));";
            await note.Script("closePanels();window.toolsBeforeShortcut=keepFormatOpen;"+press);await Script(press);await Task.Delay(120);
            checks.Add(new{name="custom-note-and-index-shortcuts-do-nothing",passed=TestHostActions.Count==actions&&note.TestHostActions.Count==noteActions&&session.Windows.Count==noteCount&&await note.Script("keepFormatOpen===window.toolsBeforeShortcut")=="true"});
            await GoToSettings();var settingsActions=TestHostActions.Count;await Script(press);await Task.Delay(100);
            checks.Add(new{name="escape-does-not-navigate-settings",passed=IsSettingsView&&TestHostActions.Count==settingsActions});await GoToIndex();
            checks.Add(new{name="global-hotkey-registration-removed",passed=typeof(MainWindow).GetMethod("RegisterHotKey",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static) is null});
            var image=JsonSerializer.Deserialize<string>(await note.Script("(()=>{const c=document.createElement('canvas');c.width=10;c.height=10;return c.toDataURL();})()"))!;
            var viewer=session.Image(image);
            try
            {
                await viewer.WaitFor("window.jotReady===true");var viewerActions=viewer.TestHostActions.Count;
                await viewer.Script("window.zoomBeforeShortcut=zoom;['F11','Escape','+','-','0','1'].forEach(key=>document.dispatchEvent(new KeyboardEvent('keydown',{key,bubbles:true,cancelable:true})))");await Task.Delay(100);
                checks.Add(new{name="image-key-shortcuts-removed-with-buttons-retained",passed=viewer.TestHostActions.Count==viewerActions&&!viewer.windowClosed&&!viewer.IsImageFullscreen&&await viewer.Script("zoom===window.zoomBeforeShortcut&&!!document.getElementById('imageFullscreen')&&!!document.getElementById('zoomIn')")=="true"});
            }
            finally{viewer.ClosePermanently();}
        }
        finally{Width=originalWidth;Height=originalHeight;await store.SavePreferences(prefs);await session.Changed();}
    }
}
