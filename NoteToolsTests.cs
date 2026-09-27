using System.IO;
using System.Text.Json;
using System.Windows;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyNoteTools(List<object> checks)
    {
        var s=new JotSession(true,Path.Combine(testOutput,"note-tools")){ExerciseLifecycle=true};
        try
        {
            var a=await s.NewNote();var b=await s.NewNote();await a.WaitFor("window.jotReady===true");await b.WaitFor("window.jotReady===true");
            a.Width=480;a.Height=360;await Task.Delay(80);
            var bounds=new Rect(a.Left,a.Top,a.Width,a.Height);var pixelBounds=a.CaptureNoteLayout();
            var peerBounds=new Rect(b.Left,b.Top,b.Width,b.Height);var peerHtml=await b.Script("editor.innerHTML");
            await a.Script("editor.innerHTML='<p>Currency enum حذف شد.</p><p>English body</p>';bookmark=null;onEdit('command');saveNow().then(()=>window.fixtureSaved=true)");await a.WaitFor("window.fixtureSaved===true");
            var html=await a.Script("editor.innerHTML");var view=(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("view").GetRawText();
            await a.ClickControl("#fullscreenButton");await a.WaitFor("noteFullscreen&&!fullscreenBusy");
            checks.Add(new{name="note-fullscreen-fills-isolated-monitor-without-topmost",passed=a.IsNoteFullscreen&&!a.IsImageFullscreen&&a.Width==1200&&a.Height==800&&a.Surface.Margin.Left==0&&a.Browser.Margin.Left==0&&a.Browser.Clip is null&&a.ResizeMode==ResizeMode.NoResize&&!a.Topmost&&a.Opacity==0&&a.Left< -10000});
            checks.Add(new{name="note-fullscreen-has-visible-exit-and-unchanged-content",passed=await a.Script("document.getElementById('fullscreenButton').getAttribute('aria-pressed')==='true'&&document.getElementById('fullscreenButton').title==='Exit fullscreen'")=="true"&&await a.Script("editor.innerHTML")==html});
            checks.Add(new{name="note-fullscreen-does-not-change-other-notes-or-view-preferences",passed=!b.IsWindowFullscreen&&peerBounds==new Rect(b.Left,b.Top,b.Width,b.Height)&&peerHtml==await b.Script("editor.innerHTML")&&(await s.Store.LoadNote(a.NoteId!))!.Value.GetProperty("view").GetRawText()==view});
            await s.PrepareQuit();
            checks.Add(new{name="fullscreen-session-keeps-original-small-note-bounds",passed=(await s.Store.LoadWindowSession()).Single(w=>w.NoteId==a.NoteId)==pixelBounds});
            await a.Reload();checks.Add(new{name="note-fullscreen-button-resynchronizes-after-renderer-reload",passed=await a.Script("noteFullscreen&&document.getElementById('fullscreenButton').title==='Exit fullscreen'")=="true"});
            await a.Capture("note-fullscreen");
            await a.ClickControl("#hideButton");for(int i=0;i<100&&a.IsVisible;i++)await Task.Delay(20);
            checks.Add(new{name="fullscreen-note-can-hide-without-quitting-peer",passed=!a.IsVisible&&b.IsVisible&&!a.windowClosed});s.OpenNote(a.NoteId!);await a.WaitFor("!app.inert");
            await a.ClickControl("#fullscreenButton");await a.WaitFor("!noteFullscreen&&!fullscreenBusy");
            checks.Add(new{name="note-exit-fullscreen-restores-original-size-position-and-chrome",passed=!a.IsWindowFullscreen&&bounds==new Rect(a.Left,a.Top,a.Width,a.Height)&&a.Surface.Margin.Left==8&&a.Surface.CornerRadius.TopLeft==6&&a.Browser.Clip is not null&&a.ResizeMode==ResizeMode.CanResize});
            for(int i=0;i<3;i++){await a.ClickControl("#fullscreenButton");await a.WaitFor("noteFullscreen&&!fullscreenBusy");await a.ClickControl("#fullscreenButton");await a.WaitFor("!noteFullscreen&&!fullscreenBusy");}
            checks.Add(new{name="repeated-note-fullscreen-toggles-preserve-content",passed=html==await a.Script("editor.innerHTML")&&bounds==new Rect(a.Left,a.Top,a.Width,a.Height)});
            await a.Script("request('image-fullscreen',true).then(()=>window.imageApiRejected=false,()=>window.imageApiRejected=true)");await a.WaitFor("window.imageApiRejected!==undefined");
            checks.Add(new{name="note-does-not-use-image-fullscreen-endpoint",passed=await a.Script("window.imageApiRejected")=="true"});
            await a.Script("""
                window.directionChecks=[];
                (async()=>{
                  const check=(name,passed)=>directionChecks.push({name:'right-click-direction-'+name,passed:!!passed});
                  const select=(first,last=first)=>{editor.focus();const r=document.createRange();r.setStartBefore(first);r.setEndAfter(last);getSelection().removeAllRanges();getSelection().addRange(r);rememberSelection();};
                  const open=()=>JotEditorMenu.open(30,55,null,true);
                  editor.innerHTML='<p>Currency enum حذف شد.</p><p>English body</p><p>سلام English</p><pre><code>const x = 1;</code></pre>';bookmark=null;onEdit('command');
                  const plain=editor.textContent;let ps=editor.querySelectorAll('p');
                  select(ps[0]);open();check('three-explicit-choices',document.querySelectorAll('#editorMenu [role=menuitemradio]').length===3);
                  check('auto-initially-checked',document.querySelector('#editorMenu [data-action=direction-auto]').ariaChecked==='true');
                  await JotEditorMenu.run('direction-ltr');
                  check('ltr-applies-only-to-selected-paragraph',editor.querySelectorAll('p')[0].dir==='ltr'&&editor.querySelectorAll('p')[0].dataset.jotDirection==='ltr'&&!editor.querySelectorAll('p')[1].dataset.jotDirection&&editor.textContent===plain);
                  undo();check('undo-restores-automatic-bidi',editor.querySelector('p').dir==='rtl'&&!editor.querySelector('p').dataset.jotDirection);
                  undo(true);check('redo-restores-explicit-direction',editor.querySelector('p').dataset.jotDirection==='ltr');
                  ps=editor.querySelectorAll('p');select(ps[0],ps[1]);open();check('mixed-selection-has-no-false-check',![...document.querySelectorAll('#editorMenu [role=menuitemradio]')].some(b=>b.ariaChecked==='true'));
                  await JotEditorMenu.run('direction-rtl');check('multi-paragraph-rtl',editor.querySelectorAll('p')[0].dir==='rtl'&&editor.querySelectorAll('p')[1].dir==='rtl'&&!editor.querySelectorAll('p')[2].dataset.jotDirection);
                  ps=editor.querySelectorAll('p');select(ps[0],ps[1]);open();await JotEditorMenu.run('direction-auto');check('auto-restores-language-specific-flow',editor.querySelectorAll('p')[0].dir==='rtl'&&editor.querySelectorAll('p')[1].dir==='ltr'&&!editor.querySelector('p').dataset.jotDirection&&editor.textContent===plain);
                  const code=editor.querySelector('code');editor.focus();getSelection().selectAllChildren(code);getSelection().collapseToEnd();rememberSelection();open();check('code-direction-not-modified',[...document.querySelectorAll('#editorMenu [role=menuitemradio]')].every(b=>b.disabled));JotEditorMenu.close();
                  const p=editor.querySelectorAll('p')[2];editor.focus();getSelection().selectAllChildren(p);getSelection().collapseToEnd();rememberSelection();open();await JotEditorMenu.run('direction-ltr');check('caret-only-changes-current-paragraph',p.dataset.jotDirection==='ltr'&&!editor.querySelector('p').dataset.jotDirection);
                  open();document.querySelector('#editorMenu [data-action=direction-auto]').focus();document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));check('choices-keyboard-navigation',document.activeElement.dataset.action==='direction-ltr');JotEditorMenu.close();
                  await saveNow();window.directionChecksDone=true;
                })().catch(error=>{directionChecks.push({name:'right-click-direction-error',passed:false,detail:error.message});window.directionChecksDone=true;});
                """);
            await a.WaitFor("window.directionChecksDone===true");using(var report=JsonDocument.Parse(await a.Script("window.directionChecks")))foreach(var result in report.RootElement.EnumerateArray())checks.Add(result.Clone());
            await a.Reload();checks.Add(new{name="right-click-direction-persists-on-reload-without-changing-peer",passed=await a.Script("editor.querySelectorAll('p')[2].dataset.jotDirection==='ltr'&&getComputedStyle(editor.querySelectorAll('p')[2]).direction==='ltr'")=="true"&&peerHtml==await b.Script("editor.innerHTML")});
            foreach(var theme in new[]{"dark","light"})
            {
                await a.Script("setAppTheme('"+theme+"').then(()=>window.noteToolsTheme='"+theme+"')");await a.WaitFor("window.noteToolsTheme==='"+theme+"'");
                a.Width=360;a.Height=280;await Task.Delay(100);
                await a.Script("editor.focus();getSelection().selectAllChildren(editor.querySelector('p'));rememberSelection();JotEditorMenu.open(innerWidth-3,innerHeight-3,null,true);document.querySelector('#editorMenu .context-direction').scrollIntoView({block:'nearest'})");
                checks.Add(new{name="right-click-direction-compact-layout-"+theme,passed=await a.Script("(()=>{const m=document.getElementById('editorMenu').getBoundingClientRect();return m.left>=0&&m.right<=innerWidth&&m.top>=0&&m.bottom<=innerHeight&&[...document.querySelectorAll('#editorMenu [role=menuitemradio]')].every(b=>{const r=b.getBoundingClientRect();return r.left>=m.left&&r.right<=m.right&&r.top>=m.top&&r.bottom<=m.bottom;});})()")=="true"});
                checks.Add(new{name="direction-label-and-option-text-centered-"+theme,passed=await a.Script("(()=>{const textRect=e=>{const r=document.createRange();r.selectNodeContents(e);return r.getBoundingClientRect();},label=textRect(document.querySelector('.context-direction>span'));return [...document.querySelectorAll('#editorMenu [role=menuitemradio]')].every(b=>{const box=b.getBoundingClientRect(),text=textRect(b.querySelector('span'));return Math.abs(text.left+text.width/2-box.left-box.width/2)<.6&&Math.abs(text.top+text.height/2-label.top-label.height/2)<.6;});})()")=="true"});
                checks.Add(new{name="direction-selection-is-soft-without-box-border-"+theme,passed=await a.Script("(()=>{const b=document.querySelector('#editorMenu [aria-checked=true]');return !!b&&getComputedStyle(b).borderTopColor==='rgba(0, 0, 0, 0)'&&getComputedStyle(b).boxShadow==='none';})()")=="true"});
                await a.Capture("note-direction-menu-"+theme);
                await a.ClickControl("#editorMenu [data-action=direction-rtl]");
                checks.Add(new{name="right-click-direction-real-button-"+theme,passed=await a.Script("document.getElementById('editorMenu').hidden&&editor.querySelector('p').dataset.jotDirection==='rtl'")=="true"});
            }
            await VerifyContextHome(checks,s,a);
            checks.Add(new{name="note-tools-no-renderer-errors",passed=a.RuntimeErrors.Count==0&&b.RuntimeErrors.Count==0});
        }
        finally{s.TrayMenu?.Close();foreach(var w in s.Windows.ToArray())w.ClosePermanently();}
    }

    private async Task VerifyContextHome(List<object> checks,JotSession s,MainWindow note)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="context-home-"+name,passed});
        var home=s.Settings();await home.WaitFor("window.jotReady===true");
        int windows=s.Windows.Count;
        await note.Script("editor.innerHTML='<p>Latest Home menu draft</p>';bookmark=null;onEdit('command');clearTimeout(saveTimer);editor.focus();getSelection().selectAllChildren(editor);rememberSelection();JotEditorMenu.open(20,45,null,true)");
        Check("has-standard-home-icon-and-no-shortcut",await note.Script("(()=>{const b=document.querySelector('#editorMenu [data-action=home]');return b.textContent==='Home'&&b.querySelector('svg').innerHTML===JotDesign.icon('home').innerHTML&&!b.querySelector('kbd').textContent;})()")=="true");
        await note.Script("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}))");
        Check("keyboard-reachable-at-end",await note.Script("document.activeElement.dataset.action==='home'")=="true");
        Check("visible-within-small-menu-after-keyboard-scroll",await note.Script("(()=>{const m=document.getElementById('editorMenu').getBoundingClientRect(),b=document.activeElement.getBoundingClientRect();return b.top>=m.top&&b.bottom<=m.bottom;})()")=="true");
        await note.ClickControl("#editorMenu [data-action=home]");await note.WaitFor("document.getElementById('editorMenu').hidden");
        await home.WaitFor("window.jotReady===true&&!!document.getElementById('homeNew')&&homeData.notes.some(n=>n.plain.includes('Latest Home menu draft'))");
        Check("opens-existing-home-from-settings",s.Windows.Count==windows&&s.Windows.Single(w=>w.Mode=="home")==home&&!home.IsSettingsView&&home.IsVisible);
        Check("saves-latest-draft-before-navigation",(await s.Store.LoadNote(note.NoteId!))!.Value.GetProperty("plain").GetString()!.Contains("Latest Home menu draft"));
        Check("keeps-note-open-and-content-unchanged",note.IsVisible&&await note.Script("editor.textContent==='Latest Home menu draft'")=="true");
        await note.Script("editor.innerHTML='<p><br></p>';bookmark=null;onEdit('command');editor.focus();getSelection().collapse(editor.firstElementChild,0);rememberSelection();JotEditorMenu.open(20,45,null,true);JotEditorMenu.run('home').then(()=>window.emptyHomeDone=true)");await note.WaitFor("window.emptyHomeDone===true");
        Check("works-in-empty-note-without-selection",s.Windows.Count==windows&&await note.Script("!editor.textContent.trim()&&document.getElementById('editorMenu').hidden")=="true");
        await note.Script("const canvas=document.createElement('canvas');canvas.width=20;canvas.height=10;editor.innerHTML='<p><img src=\"'+canvas.toDataURL()+'\"></p>';bookmark=null;onEdit('command');JotEditorMenu.open(20,45,editor.querySelector('img'))");
        Check("also-available-in-image-context",await note.Script("!!document.querySelector('#editorMenu [data-action=home]')&&!!document.querySelector('#editorMenu [data-action=open-image]')")=="true");
        await note.Script("JotEditorMenu.close()");
    }
}
