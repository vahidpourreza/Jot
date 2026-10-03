using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyToastDesign(List<object> checks)
    {
        void Check(string name, bool passed) => checks.Add(new { name = "toast-design-" + name, passed });
        var root = Path.Combine(testOutput, "toast-design");
        Directory.CreateDirectory(root);
        var toastSession = new JotSession(true, root) { ExerciseLifecycle = true };
        try
        {
            await toastSession.Store.SavePreferences(JsonSerializer.SerializeToElement(new { newNoteTarget = "window" }));
            var note = await toastSession.NewNote();
            await note.WaitFor("window.jotReady===true&&!!window.JotToast");
            async Task Escape()
            {
                foreach (var type in new[] { "rawKeyDown", "keyUp" })
                    await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new { type, key = "Escape", code = "Escape", windowsVirtualKeyCode = 27 }));
            }
            note.Width = 360; note.Height = 520;
            await note.Script("editor.focus();window.toastSelectionBefore=getSelection().toString();JotToast.error('Could not save this file.',{id:'design-error',description:'Your note is safe in Jot. Check the file location and try again.'})");
            Check("show-does-not-steal-writing-focus", await note.Script("document.activeElement===editor&&getSelection().toString()===toastSelectionBefore") == "true");
            Check("explicit-id-update-does-not-duplicate", await note.Script("JotToast.error('Could not save this file.',{id:'design-error',description:'Your note is safe in Jot. Check the file location and try again.'});document.querySelectorAll('.jot-toast[data-toast-id=design-error]').length===1&&JotToast.has('design-error')&&JotToast.get('design-error').type==='error'") == "true");
            await note.Script("JotToast.success('Image copied.',{id:'design-success',duration:0});JotToast.info('Auto-save is off.',{id:'design-info',description:'Use Ctrl+S to save this file.',duration:0})");
            await note.WaitFor("document.querySelectorAll('.jot-toast:not([hidden])').length===3");
            Check("neutral-popover-surface-and-muted-type", await note.Script("(()=>{const t=document.querySelector('.jot-toast'),s=getComputedStyle(t),title=getComputedStyle(t.querySelector('.jot-toast-title')),desc=getComputedStyle(t.querySelector('.jot-toast-description')),probe=document.createElement('span');probe.style.cssText='background:var(--card);color:var(--muted-foreground)';document.body.append(probe);const p=getComputedStyle(probe),pass=s.backgroundColor===p.backgroundColor&&title.color===p.color&&title.fontSize==='12px'&&title.lineHeight==='18px'&&desc.fontSize==='11.2px'&&desc.lineHeight==='16px'&&parseFloat(s.borderRadius)>6;probe.remove();return pass})()") == "true");
            Check("error-uses-red-icon-not-red-card-or-text", await note.Script("(()=>{const e=document.querySelector('[data-toast-id=design-error]'),s=document.querySelector('[data-toast-id=design-success]');return getComputedStyle(e).backgroundColor===getComputedStyle(s).backgroundColor&&getComputedStyle(e.querySelector('.jot-toast-title')).color===getComputedStyle(s.querySelector('.jot-toast-title')).color&&getComputedStyle(e.querySelector('.jot-toast-icon')).color!==getComputedStyle(s.querySelector('.jot-toast-icon')).color})()") == "true");
            Check("accessible-live-regions-and-focusable-cards-without-close-icons", await note.Script("!!document.querySelector('#jotToaster [role=status][aria-live=polite][aria-atomic=true]')&&!!document.querySelector('#jotToaster [role=alert][aria-live=assertive][aria-atomic=true]')&&!document.querySelector('.jot-toast-close')&&[...document.querySelectorAll('.jot-toast')].every(card=>card.tabIndex===0&&card.getAttribute('aria-label').endsWith('Press Escape to dismiss.'))") == "true");
            await note.Script("JotToast.warning('Queued fourth notice.',{id:'queued',duration:0})");
            Check("at-most-three-visible-and-queued-state-inspectable", await note.Script("document.querySelectorAll('.jot-toast:not([hidden])').length===3&&JotToast.has('queued')&&!JotToast.get('queued').visible") == "true");
            await note.Script("JotToast.dismiss('design-info')");
            Check("dismiss-promotes-queued-notice", await note.Script("JotToast.get('queued').visible&&document.querySelectorAll('.jot-toast:not([hidden])').length===3") == "true");
            await note.Script("JotToast.dismiss('queued');JotToast.info('<img src=x onerror=alert(1)>',{id:'safe-text',description:'<script>unsafe()</script>',duration:0})");
            Check("message-and-description-render-as-text-only", await note.Script("(()=>{const t=document.querySelector('[data-toast-id=safe-text]');return !t.querySelector('img,script')&&t.querySelector('.jot-toast-title').textContent.includes('<img')&&t.querySelector('.jot-toast-description').textContent.includes('<script>')})()") == "true");
            await note.Script("JotToast.dismiss('safe-text')");
            foreach (var theme in new[] { "dark", "light" })
            {
                await note.Script("document.documentElement.dataset.theme=" + JsonSerializer.Serialize(theme));
                foreach (var width in new[] { 360, 520 })
                {
                    note.Width = width;
                    await Task.Delay(100);
                    Check(theme + "-" + width + "-bottom-right-and-clear-of-controls", await note.Script("(()=>{const t=document.getElementById('jotToaster').getBoundingClientRect(),f=document.querySelector('.quiet-footer'),r=f.getBoundingClientRect(),clear=f.classList.contains('document-toolbar')?t.bottom<=innerHeight-14&&t.top>=r.bottom+8:t.bottom<=r.top-8;return t.left>=13&&Math.abs(t.right-(innerWidth-14))<1&&t.top>=34&&clear&&t.width<=356.5&&[...document.querySelectorAll('.jot-toast:not([hidden])')].every(card=>{const r=card.getBoundingClientRect();return r.left>=8&&r.right<=innerWidth-8})})()") == "true");
                    await note.Capture("toast-" + theme + "-" + width);
                }
            }
            await note.Script("JotToast.dismiss();editor.focus();for(let i=1;i<=3;i++)JotToast.error('Earlier failure '+i,{id:'priority-'+i,duration:0});JotToast.error('Latest save failed.',{id:'priority-latest',duration:0})");
            Check("new-error-visible-despite-three-sticky-errors", await note.Script("JotToast.get('priority-latest').visible&&!JotToast.get('priority-1').visible&&JotToast.has('priority-1')&&document.querySelectorAll('.jot-toast:not([hidden])').length===3&&document.activeElement===editor") == "true");
            await note.Script("document.querySelector('[data-toast-id=priority-2]').focus();JotToast.error('Another save failed.',{id:'priority-next',duration:0})");
            Check("priority-change-never-hides-focused-notice-or-steals-focus", await note.Script("JotToast.get('priority-2').visible&&JotToast.get('priority-next').visible&&document.activeElement.closest('[data-toast-id]').dataset.toastId==='priority-2'&&!JotToast.get('priority-3').visible") == "true");
            // Exercise the real browser focus boundary in the offscreen view.
            // The first paragraph is above the toast stack; the editor's center
            // can instead hit a notification in this compact window.
            await note.ClickControl("#editor p");
            Check("priority-fixture-focus-returns-to-editor", await note.Script("document.activeElement===editor") == "true");
            try { await note.WaitFor("!JotToast.get('priority-2').visible"); }
            catch (TimeoutException error)
            {
                var detail = await note.Script("({activeId:document.activeElement.id,activeClass:document.activeElement.className,hasFocus:document.hasFocus(),editorEditable:editor.contentEditable,notices:['priority-1','priority-2','priority-3','priority-latest','priority-next'].map(id=>JotToast.get(id))})");
                throw new InvalidOperationException("Notification focus reconciliation: " + detail, error);
            }
            Check("focus-leaving-releases-protected-queue-slot", await note.Script("JotToast.get('priority-next').visible&&JotToast.get('priority-latest').visible&&JotToast.get('priority-3').visible&&document.activeElement===editor") == "true");
            await note.Script("JotToast.dismiss();JotToast.info('Operation in progress.',{id:'priority-action',duration:0,action:{label:'Apply',onClick:()=>new Promise(resolve=>window.finishPriorityAction=resolve)}});document.querySelector('[data-toast-id=priority-action] .jot-toast-action').click();for(let i=1;i<=3;i++)JotToast.error('Concurrent failure '+i,{id:'during-action-'+i})");
            Check("priority-change-preserves-running-action", await note.Script("JotToast.get('priority-action').visible&&JotToast.get('during-action-3').visible&&!JotToast.get('during-action-1').visible&&document.querySelector('[data-toast-id=priority-action] .jot-toast-action').disabled") == "true");
            await note.Script("finishPriorityAction()");
            await note.WaitFor("!JotToast.has('priority-action')&&JotToast.get('during-action-1').visible");
            Check("completing-action-promotes-queued-notices", true);
            await note.Script("JotToast.dismiss();editor.focus();JotToast.info('Keyboard accessible.',{id:'keyboard',duration:0});document.querySelector('[data-toast-id=keyboard]').focus()");
            await Escape();
            Check("escape-dismisses-focused-toast-and-restores-editor-focus", await note.Script("!JotToast.has('keyboard')&&document.activeElement===editor") == "true");
            await note.Script("JotToast.info('Focus pauses expiry.',{id:'focus-timer',duration:250});document.querySelector('[data-toast-id=focus-timer]').focus()");
            await Task.Delay(400);
            Check("keyboard-focus-pauses-expiry", await note.Script("JotToast.has('focus-timer')") == "true");
            await note.Script("editor.focus()");
            await note.WaitFor("!JotToast.has('focus-timer')");
            Check("expiry-resumes-after-focus-leaves", true);
            await note.Script("JotToast.info('Hover pauses expiry.',{id:'hover-timer',duration:250});document.querySelector('[data-toast-id=hover-timer]').dispatchEvent(new PointerEvent('pointerenter'))");
            await Task.Delay(400);
            Check("pointer-hover-pauses-expiry", await note.Script("JotToast.has('hover-timer')") == "true");
            await note.Script("document.querySelector('[data-toast-id=hover-timer]').dispatchEvent(new PointerEvent('pointerleave'))");
            await note.WaitFor("!JotToast.has('hover-timer')");
            Check("expiry-resumes-after-hover-leaves", true);
            await note.Script("window.toastActionDone=false;JotToast.error('Try the operation again.',{id:'action',action:{label:'Retry',pendingLabel:'Retrying…',onClick:()=>new Promise(resolve=>window.finishToastAction=()=>{window.toastActionDone=true;resolve()})}});document.querySelector('[data-toast-id=action] .jot-toast-action').click()");
            Check("async-action-shows-spinner-pending-label-and-disabled-state", await note.Script("(()=>{const a=document.querySelector('[data-toast-id=action] .jot-toast-action');return a.disabled&&a.getAttribute('aria-busy')==='true'&&a.textContent==='Retrying…'&&getComputedStyle(a,'::before').content!=='none'})()") == "true");
            await note.Script("finishToastAction()");
            await note.WaitFor("toastActionDone&&!JotToast.has('action')");
            Check("successful-action-dismisses-notice", true);
            await note.Script("JotToast.info('Completing an old action.',{id:'replace-action',duration:250,action:{label:'Apply',onClick:()=>new Promise(resolve=>window.finishOldToastAction=resolve)}});document.querySelector('[data-toast-id=replace-action] .jot-toast-action').click();JotToast.success('Newer success stays visible.',{id:'replace-action',duration:0});finishOldToastAction()");
            await Task.Delay(400);
            Check("old-action-cannot-expire-replacement-notice", await note.Script("JotToast.get('replace-action')?.message==='Newer success stays visible.'") == "true");
            await note.Script("JotToast.dismiss('replace-action')");
            await note.Script("JotToast.error('Try again.',{id:'failed-action',action:{label:'Retry',onClick:()=>Promise.reject(new Error('Still unavailable.'))}});document.querySelector('[data-toast-id=failed-action] .jot-toast-action').click()");
            await note.WaitFor("JotToast.get('failed-action')?.message==='Still unavailable.'");
            Check("failed-action-stays-as-clear-error", await note.Script("JotToast.get('failed-action').type==='error'") == "true");
            await note.Script("JotToast.dismiss();window.toastDismissCount=0;JotToast.info('Callback test.',{id:'callback',onDismiss:()=>window.toastDismissCount++});JotToast.dismiss('callback');JotToast.dismiss('callback')");
            Check("dismiss-callback-runs-once", await note.Script("toastDismissCount===1") == "true");
            await note.Script("document.getElementById('deleteDialog').showModal();JotToast.error('A modal notice.',{id:'modal'})");
            await note.WaitFor("document.querySelector('#deleteDialog #jotToaster')?.dataset.modal==='true'");
            Check("modal-notification-is-in-accessible-top-layer", await note.Script("!document.getElementById('jotToaster').showPopover||document.getElementById('jotToaster').matches(':popover-open')") == "true");
            await note.Script("document.querySelector('[data-toast-id=modal]').focus()");
            await Escape();
            Check("modal-toast-escape-dismisses-only-notice-not-dialog", await note.Script("!JotToast.has('modal')&&document.getElementById('deleteDialog').open") == "true");
            await note.Script("JotToast.error('Still available after closing the dialog.',{id:'modal-survivor'});document.getElementById('deleteDialog').close()");
            await note.WaitFor("document.getElementById('jotToaster').parentElement===document.body&&document.getElementById('jotToaster').dataset.modal==='false'");
            Check("closing-dialog-keeps-toast-visible-and-accessible", await note.Script("JotToast.has('modal-survivor')&&(!document.getElementById('jotToaster').showPopover||document.getElementById('jotToaster').matches(':popover-open'))") == "true");
            await note.Script("document.querySelector('[data-toast-id=modal-survivor]').focus()");
            await Escape();
            Check("surviving-toast-can-still-be-dismissed-with-escape", await note.Script("!JotToast.has('modal-survivor')") == "true");
            await note.Script("editor.focus();JotToast.error('Longer-lived error.',{id:'timed-error'});JotToast.success('Normal notice.',{id:'timed-normal'})");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            Check("reduced-motion-disables-entry-animation", await note.Script("getComputedStyle(document.querySelector('[data-toast-id=timed-error]')).animationName==='none'") == "true");
            await Task.Delay(5100);
            await note.WaitFor("!JotToast.has('timed-normal')",15);
            Check("normal-notices-expire-without-needing-a-close-button", await note.Script("!JotToast.has('timed-normal')&&!document.querySelector('.jot-toast-close')") == "true");
            Check("errors-have-more-reading-time-than-normal-notices", await note.Script("JotToast.has('timed-error')") == "true");
            await note.WaitFor("!JotToast.has('timed-error')",45);
            Check("errors-also-expire-without-leaving-an-undismissable-stack", await note.Script("document.getElementById('jotToaster').hidden") == "true");
            await note.Script("JotToast.error('Explicitly persistent error.',{id:'sticky',duration:0})");
            await note.Script("JotToast.dismissType('error')");
            Check("dismiss-by-type-hides-empty-viewport", await note.Script("!JotToast.has('sticky')&&document.getElementById('jotToaster').hidden") == "true");
            Check("isolated-offscreen-no-live-user-data", toastSession.Windows.All(window => window.Left < -10000 && window.Opacity == 0 && !window.ShowActivated && !window.ShowInTaskbar && !window.Topmost));
        }
        finally { foreach (var window in toastSession.Windows.ToArray()) window.ClosePermanently(); }
    }
}
