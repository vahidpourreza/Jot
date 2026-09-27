using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyInactivePinnedChrome(List<object> checks,MainWindow note)
    {
        var originalTheme=JsonSerializer.Deserialize<string>(await note.Script("model.prefs.theme"));
        var originalActive=JsonSerializer.Deserialize<string>(await note.Script("app.dataset.activeWindow"));
        var originalPin=JsonSerializer.Deserialize<bool>(await note.Script("pinned"));
        try
        {
            await note.Script("closePanels();if(!pinned)document.getElementById('pinButton').click()");
            await note.WaitFor("pinned===true");
            foreach(var theme in new[]{"dark","light"})
            {
                await note.Script("setAppTheme('"+theme+"').then(()=>window.pinnedTheme='"+theme+"')");
                await note.WaitFor("window.pinnedTheme==='"+theme+"'");
                await note.Script("app.dataset.activeWindow='false';editor.focus()");
                await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mouseMoved\",\"x\":100,\"y\":100}");
                await Task.Delay(200);
                checks.Add(new{name="inactive-pin-hides-with-other-header-icons-"+theme,passed=await note.Script("(()=>{const p=document.getElementById('pinButton');return [...document.querySelectorAll('#handle button')].every(button=>getComputedStyle(button).opacity==='0')&&!!p.querySelector('[fill=currentColor]')&&getComputedStyle(p).backgroundColor==='rgba(0, 0, 0, 0)';})()")=="true"&&!note.Topmost});
                await note.Capture("pinned-inactive-"+theme);
                using var point=JsonDocument.Parse(await note.Script("(()=>{const r=document.getElementById('menuButton').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()"));
                await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseMoved",x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble()}));
                await Task.Delay(180);
                checks.Add(new{name="inactive-header-hover-stays-below-color-strip-"+theme,passed=await note.Script("(()=>{const h=document.getElementById('handle').getBoundingClientRect();return [...document.querySelectorAll('#handle button')].every(button=>{const r=button.getBoundingClientRect();return r.top>=h.top+8&&r.bottom<=h.bottom&&getComputedStyle(button).opacity==='1';});})()")=="true"});
                await note.Capture("pinned-inactive-hover-"+theme);
            }
        }
        finally
        {
            await note.Script("setAppTheme("+JsonSerializer.Serialize(originalTheme)+").then(()=>window.pinnedThemeRestored=true);app.dataset.activeWindow="+JsonSerializer.Serialize(originalActive)+";if(pinned!=="+JsonSerializer.Serialize(originalPin)+")document.getElementById('pinButton').click()");
            await note.WaitFor("window.pinnedThemeRestored===true&&pinned==="+JsonSerializer.Serialize(originalPin));
        }
    }
    private async Task CaptureMotionFrame(MainWindow note,string name)
    {
        await Task.Delay(50); // The animation is paused at a deterministic progress value.
        await using var stream=File.Create(Path.Combine(testOutput,name+".png"));
        await note.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
    }
    private async Task VerifyNoteMotion(List<object> checks, MainWindow note)
    {
        var previousActive=JsonSerializer.Deserialize<string>(await note.Script("app.dataset.activeWindow"));
        var previousToolbar=JsonSerializer.Deserialize<bool>(await note.Script("model.prefs.toolbarVisible!==false"));
        try
        {
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"no-preference\"}]}");
            await note.WaitFor("!reducedMotion.matches");
            await note.Script("closePanels();app.dataset.activeWindow='false'");
            await note.WaitFor("getComputedStyle(document.getElementById('handle'),'::before').height==='8px'");
            var expanded=await note.Script("(()=>{const header=document.getElementById('handle'),top=document.getElementById('writingArea').getBoundingClientRect().top;getComputedStyle(header,'::before').height;app.dataset.activeWindow='true';getComputedStyle(header,'::before').height;const animation=header.getAnimations({subtree:true}).find(a=>a.transitionProperty==='height');if(!animation)return false;animation.pause();animation.currentTime=80;const middle=parseFloat(getComputedStyle(header,'::before').height);animation.finish();return middle>8&&middle<34&&getComputedStyle(header,'::before').height==='34px'&&document.getElementById('writingArea').getBoundingClientRect().top===top;})()");
            checks.Add(new{name="header-expands-smoothly-without-moving-editor",passed=expanded=="true"});
            var collapsed=await note.Script("(()=>{const header=document.getElementById('handle'),top=document.getElementById('writingArea').getBoundingClientRect().top;app.dataset.activeWindow='false';getComputedStyle(header,'::before').height;const animation=header.getAnimations({subtree:true}).find(a=>a.transitionProperty==='height');if(!animation)return false;animation.finish();return getComputedStyle(header,'::before').height==='8px'&&getComputedStyle(header,'::before').transitionProperty.includes('background-color')&&document.getElementById('writingArea').getBoundingClientRect().top===top;})()");
            checks.Add(new{name="header-collapses-to-colored-strip-without-layout-shift",passed=collapsed=="true"});
            var opening=await note.Script("setNoteMenuVisible(true);!!noteMenuAnimation&&!document.getElementById('menu').hidden&&!document.getElementById('menu').inert");
            checks.Add(new{name="more-menu-animates-open",passed=opening=="true"});
            await note.Script("window.menuOpening=noteMenuAnimation;menuOpening.pause();menuOpening.currentTime=0");
            checks.Add(new{name="more-opens-from-fully-collapsed-height",passed=await note.Script("getComputedStyle(document.getElementById('menu')).clipPath.includes('100%')&&menuOpening.effect.getTiming().duration===260")=="true"});
            await CaptureMotionFrame(note,"more-opening-start");
            await note.Script("menuOpening.currentTime=70");
            checks.Add(new{name="more-has-visible-intermediate-reveal",passed=await note.Script("menuOpening.effect.getComputedTiming().progress>0&&menuOpening.effect.getComputedTiming().progress<1&&!getComputedStyle(document.getElementById('menu')).clipPath.includes('100%')&&getComputedStyle(document.getElementById('menu')).clipPath!=='inset(0%)'")=="true"});
            await CaptureMotionFrame(note,"more-opening-middle");
            await note.Script("menuOpening.currentTime=260");await CaptureMotionFrame(note,"more-opening-end");
            await note.Script("menuOpening.finish()");await note.WaitFor("noteMenuAnimation===null");
            var closing=await note.Script("setNoteMenuVisible(false);!!noteMenuAnimation&&document.getElementById('menu').hidden&&document.getElementById('menu').inert&&document.getElementById('menu').classList.contains('is-exiting')");
            checks.Add(new{name="closing-menu-animation-is-non-interactive",passed=closing=="true"});
            checks.Add(new{name="interrupted-menu-reverses-from-current-frame",passed=await note.Script("(()=>{noteMenuAnimation.pause();noteMenuAnimation.currentTime=80;const clip=getComputedStyle(document.getElementById('menu')).clipPath;setNoteMenuVisible(true);noteMenuAnimation.pause();noteMenuAnimation.currentTime=0;const matches=getComputedStyle(document.getElementById('menu')).clipPath===clip;noteMenuAnimation.play();return matches;})()")=="true"});
            await note.WaitFor("noteMenuAnimation===null");
            checks.Add(new{name="rapid-reopen-is-not-hidden-by-stale-close-animation",passed=await note.Script("!document.getElementById('menu').hidden&&!document.getElementById('menu').inert&&!document.getElementById('menu').classList.contains('is-exiting')")=="true"});
            await note.Script("setNoteMenuVisible(false)");await note.WaitFor("noteMenuAnimation===null");
            checks.Add(new{name="closed-menu-exit-animation-cleans-up",passed=await note.Script("getComputedStyle(document.getElementById('menu')).display==='none'&&noteMenuAnimation===null")=="true"});
            await note.Script("setToolbarVisible(true)");await note.WaitFor("toolbarAnimation===null");
            checks.Add(new{name="toolbar-has-visible-close-motion-without-editor-shift",passed=await note.Script("(()=>{const before=document.getElementById('writingArea').getBoundingClientRect().bottom;setToolbarVisible(false);if(!toolbarAnimation)return false;toolbarAnimation.pause();toolbarAnimation.currentTime=90;const style=getComputedStyle(document.getElementById('formatBar'));return Number(style.opacity)>0&&Number(style.opacity)<1&&style.transform!=='none'&&document.getElementById('formatBar').inert&&document.getElementById('writingArea').getBoundingClientRect().bottom===before;})()")=="true"});
            await CaptureMotionFrame(note,"toolbar-closing-middle");
            await note.Script("setToolbarVisible(true)");
            checks.Add(new{name="toolbar-animates-show-and-recovers-from-rapid-toggle",passed=await note.Script("!!toolbarAnimation&&!document.getElementById('formatBar').hidden&&!document.getElementById('formatBar').inert")=="true"});
            await note.WaitFor("toolbarAnimation===null");
            await note.Script("syncColorChoices();setColorMenuVisible(true);positionFormatMenu('colorMenuButton','colorMenu')");
            checks.Add(new{name="color-picker-animates-open",passed=await note.Script("!!colorMenuAnimation&&!document.getElementById('colorMenu').hidden")=="true"});
            await note.WaitFor("colorMenuAnimation===null");await note.Capture("new-color-picker");
            await note.Script("setColorMenuVisible(false)");await note.WaitFor("colorMenuAnimation===null");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await note.WaitFor("reducedMotion.matches");
            checks.Add(new{name="reduced-motion-disables-header-menu-toolbar-and-picker-animation",passed=await note.Script("(()=>{app.dataset.activeWindow='true';setNoteMenuVisible(true);const open=noteMenuAnimation===null;setNoteMenuVisible(false);setToolbarVisible(false);const hidden=toolbarAnimation===null;setToolbarVisible(true);setColorMenuVisible(true);const color=colorMenuAnimation===null;setColorMenuVisible(false);return open&&hidden&&color&&visibilityAnimations.size===0&&!document.getElementById('menu').classList.contains('is-exiting')&&getComputedStyle(document.getElementById('handle'),'::before').transitionDuration==='0s';})()")=="true"});
        }
        finally
        {
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");
            await note.Script("app.dataset.activeWindow="+JsonSerializer.Serialize(previousActive)+";closePanels();setToolbarVisible("+JsonSerializer.Serialize(previousToolbar)+")");
        }
    }
}
