using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifySimplifiedShell(List<object> checks,MainWindow note)
    {
        var originalWidth=Width;var originalHeight=Height;var prefs=await store.LoadPreferences();
        async Task CompleteWindowAction(string selector,string action)
        {
            var before=TestHostActions.Count(item=>item==action);
            await ClickControl(selector);
            // Mouse dispatch only begins the bridge operation. Home X now saves
            // its retained workspace before the completed hide is acknowledged.
            for(int i=0;i<200&&TestHostActions.Count(item=>item==action)==before&&!windowClosed;i++)await Task.Delay(50);
            if(TestHostActions.Count(item=>item==action)!=before+1)throw new TimeoutException("The "+action+" window action did not complete exactly once.");
        }
        try
        {
            checks.Add(new{name="index-separates-window-controls-from-appbar",passed=await Script("[...document.querySelectorAll('#workspaceHandle .index-window-actions button')].map(b=>b.id).join(',')==='workspacePin,workspaceMinimize,workspaceMaximize,workspaceClose'&&!!document.querySelector('.library-appbar #homeNew')&&!!document.querySelector('.library-appbar #settingsButton')")=="true"});
            checks.Add(new{name="home-uses-familiar-window-icons",passed=await Script("[['workspaceMinimize','window-minimize'],['workspaceMaximize','window-maximize'],['workspaceClose','window-close']].every(([id,icon])=>document.getElementById(id).querySelector('svg').innerHTML===JotDesign.icon(icon).innerHTML)")=="true"});
            checks.Add(new{name="normal-note-window-controls-and-tabs-boundary",passed=await note.Script("[...document.querySelectorAll('#handle button')].map(b=>b.id).join(',')==='newButton,menuButton,pinButton,minimizeButton,fullscreenButton,hideButton'&&!document.querySelector('#noteTabs,.workspace-tabs,#newTabButton,#newWindowButton')&&document.getElementById('newButton').ariaLabel==='New note'")=="true"});
            foreach(var theme in new[]{"dark","light"})
            {
                await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await session.Changed();
                await WaitFor("document.documentElement.dataset.theme==='"+theme+"'");
                foreach(var size in new[]{(Width:360d,Height:280d),(Width:520d,Height:540d)})
                {
                    Width=size.Width;Height=size.Height;await Task.Delay(120);
                    using var geometry=JsonDocument.Parse(await Script("(()=>{const controls=[...document.querySelectorAll('#workspaceHandle button')].filter(b=>b.getClientRects().length).map(b=>{const r=b.getBoundingClientRect(),minimumHeight=b.classList.contains('tab-close')?24:26;return {id:b.id||b.className,width:r.width,height:r.height,left:r.left,right:r.right,top:r.top,bottom:r.bottom,minimumHeight,valid:r.width>=24&&r.height>=minimumHeight&&r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight};});return {passed:controls.every(b=>b.valid)&&document.documentElement.scrollWidth<=innerWidth,controls,width:innerWidth,height:innerHeight};})()"));
                    checks.Add(new{name=$"compact-index-controls-fit-{theme}-{size.Width}",passed=geometry.RootElement.GetProperty("passed").GetBoolean(),geometry=geometry.RootElement.Clone()});
                }
                await Capture("simple-index-"+theme);
                await GoToSettings();Width=360;Height=280;await Task.Delay(120);
                checks.Add(new{name="compact-settings-fit-"+theme,passed=await Script("(()=>{const area=document.querySelector('#workspaceSettings .appearance'),r=area.getBoundingClientRect();return !document.querySelector('#trayVisibility,[data-weight],#homeFontSize,#homeLineHeight,#toolbarVisible')&&[...area.querySelectorAll('button')].every(b=>{const c=b.getBoundingClientRect();return c.left>=0&&c.right<=innerWidth&&c.top>=r.top-area.scrollTop&&c.bottom<=r.top-area.scrollTop+area.scrollHeight;})&&['settingsBack','settingsQuit','workspaceClose'].every(id=>{const c=document.getElementById(id).getBoundingClientRect();return c.left>=0&&c.right<=innerWidth&&c.top>=0&&c.bottom<=innerHeight;});})()")=="true"});
                await Capture("simple-settings-"+theme);await GoToIndex();
            }
            await CompleteWindowAction("#workspaceMinimize","minimize");await CompleteWindowAction("#workspaceClose","hide");
            checks.Add(new{name="index-close-and-minimize-use-distinct-host-actions-without-quitting",passed=TestHostActions.Contains("minimize")&&TestHostActions.Contains("hide")&&!windowClosed&&session.Windows.Contains(note)});
            await GoToSettings();await CompleteWindowAction("#workspaceMinimize","minimize");await CompleteWindowAction("#workspaceClose","hide");
            checks.Add(new{name="settings-close-and-minimize-keep-app-alive",passed=!windowClosed&&session.Windows.Contains(note)});await GoToIndex();
            var actions=TestHostActions.Count;var noteActions=note.TestHostActions.Count;var noteCount=session.Windows.Count;
            const string press="['KeyK','KeyF'].forEach(code=>document.dispatchEvent(new KeyboardEvent('keydown',{key:code.slice(3).toLowerCase(),code,ctrlKey:true,shiftKey:code==='KeyF',bubbles:true,cancelable:true})));document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}));";
            await note.Script("closePanels();window.toolsBeforeShortcut=keepFormatOpen;"+press);await Script(press);await Task.Delay(120);
            checks.Add(new{name="unassigned-note-and-index-shortcuts-do-nothing",passed=TestHostActions.Count==actions&&note.TestHostActions.Count==noteActions&&session.Windows.Count==noteCount&&await note.Script("keepFormatOpen===window.toolsBeforeShortcut")=="true"});
            await GoToSettings();var settingsActions=TestHostActions.Count;await Script(press);await Task.Delay(100);
            checks.Add(new{name="escape-does-not-navigate-settings",passed=IsSettingsView&&TestHostActions.Count==settingsActions});await GoToIndex();
            checks.Add(new{name="global-hotkeys-are-never-registered-by-offscreen-tests",passed=!JsonSerializer.SerializeToElement(session.ShortcutStatus).GetProperty("active").GetBoolean()});
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
