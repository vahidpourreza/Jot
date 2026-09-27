using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Jot;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TestRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window,out TestRect rectangle);
    private bool testsStarted;
    private async Task<string> Script(string script)=>await Browser.CoreWebView2.ExecuteScriptAsync(script);
    private async Task WaitFor(string predicate,int limit=200)
    {
        for(int i=0;i<limit;i++){if(Browser.CoreWebView2 is not null && await Script(predicate)=="true")return;await Task.Delay(100);}
        throw new TimeoutException(predicate);
    }
    private async Task Capture(string name)
    {
        await WaitFor("typeof visibilityAnimations==='undefined'||visibilityAnimations.size===0");
        await WaitFor("document.getAnimations().every(animation=>animation.playState!=='running'||!Number.isFinite(animation.effect.getTiming().iterations))");
        await Task.Delay(180); // Include settled native-header CSS transitions.
        await using var stream=File.Create(Path.Combine(testOutput,name+".png"));
        await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
    }
    private void RenderWindowSurface(string name)
    {
        WindowRoot.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(WindowRoot);
        using var output=File.Create(Path.Combine(testOutput,name+".png"));
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);
    }
    private async Task ClickControl(string selector)
    {
        await WaitFor("typeof visibilityAnimations==='undefined'||visibilityAnimations.size===0");
        await WaitFor("document.getAnimations().every(animation=>animation.playState!=='running'||!Number.isFinite(animation.effect.getTiming().iterations))");
        using var point=JsonDocument.Parse(await Script("(()=>{const r=document.querySelector("+JsonSerializer.Serialize(selector)+").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()"));
        var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
        var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ClosedHandler(object? sender,EventArgs args)=>closed.TrySetResult();
        Closed+=ClosedHandler;
        try
        {
            foreach(var type in new[]{"mouseMoved","mousePressed","mouseReleased"})
            {
                if(closingPermanently)return;
                var dispatch=Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new {type,x,y,button=type=="mouseMoved"?"none":"left",clickCount=type=="mouseMoved"?0:1}));
                // A real close button may destroy the DevTools target before its reply arrives.
                if(await Task.WhenAny(dispatch,closed.Task).WaitAsync(TimeSpan.FromSeconds(20))==closed.Task)
                {
                    _=dispatch.ContinueWith(task=>{_ = task.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
                    return;
                }
                await dispatch;
            }
        }
        finally { Closed-=ClosedHandler; }
    }
    private async Task Reload()
    {
        var done=new TaskCompletionSource();
        void Handler(object? sender,CoreWebView2NavigationCompletedEventArgs args)=>done.TrySetResult();
        Browser.CoreWebView2.NavigationCompleted+=Handler;Browser.CoreWebView2.Reload();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20));Browser.CoreWebView2.NavigationCompleted-=Handler;
        await WaitFor("window.jotReady===true");
    }
    private async Task GoToSettings()
    {
        await ClickControl("#settingsButton");
        await WaitFor("window.jotReady===true&&document.getElementById('settingsHandle')!==null");
    }
    private async Task GoToIndex()
    {
        await ClickControl("#settingsBack");
        await WaitFor("window.jotReady===true&&document.getElementById('cards')!==null");
    }
    private async Task VerifyFontLimitCursor(List<object> checks,MainWindow note)
    {
        var original=JsonSerializer.Deserialize<int>(await note.Script("model.prefs.fontSize"));
        foreach(var limit in new[]{13,24})
        {
            await note.Script("setPreference({fontSize:"+limit+"}).then(()=>window.limitReady="+limit+")");
            await note.WaitFor("window.limitReady==="+limit);
            await note.ClickControl("#menuButton");
            var button=limit==13?"smallerButton":"largerButton";
            var before=note.TestHostActions.Count(action=>action=="preferences");
            await note.ClickControl("#"+button);
            checks.Add(new{name="font-limit-"+limit+"-hover-is-not-a-loader",passed=await note.Script("(()=>{const b=document.getElementById('"+button+"');return b.disabled&&b.matches(':hover')&&getComputedStyle(b).cursor==='default'&&getComputedStyle(b).animationName==='none'&&b.getAttribute('aria-busy')!=='true'&&b.title.includes('"+limit+" px')&&model.prefs.fontSize==="+limit+";})()")=="true"&&note.TestHostActions.Count(action=>action=="preferences")==before});
            await note.Capture("font-limit-"+limit);
            await note.ClickControl(limit==13?"#largerButton":"#smallerButton");
            await note.WaitFor("model.prefs.fontSize==="+(limit==13?14:23));
            checks.Add(new{name="font-size-can-move-away-from-"+limit,passed=await note.Script("!document.getElementById('smallerButton').disabled&&!document.getElementById('largerButton').disabled")=="true"});
            await note.Script("closePanels()");
        }
        await note.Script("setPreference({fontSize:"+original+"}).then(()=>window.limitRestore=true)");
        await note.WaitFor("window.limitRestore===true");
    }
    private void CaptureCornerMask(List<object> checks)
    {
        // Unit render of this app's WPF surface only; no desktop capture or other windows.
        var bitmap=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();
        using(var context=visual.RenderOpen())
            context.DrawRoundedRectangle(System.Windows.Media.Brushes.White,null,new System.Windows.Rect(8,8,Width-16,Height-16),6,6);
        bitmap.Render(visual);
        var bytes=new byte[(int)Width*(int)Height*4];bitmap.CopyPixels(bytes,(int)Width*4,0);
        bool partial=false;for(int y=8;y<24;y++)for(int x=8;x<24;x++){var alpha=bytes[(y*(int)Width+x)*4+3];if(alpha>0&&alpha<255)partial=true;}
        checks.Add(new {name="six-pixel-corners-have-antialiased-alpha",passed=partial&&Surface.CornerRadius.TopLeft==6&&Browser.Clip is RectangleGeometry clip&&clip.RadiusX==5&&AllowsTransparency});
        using var output=File.Create(Path.Combine(testOutput,"corner-mask.png"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);
    }
    private async Task RunSelfTests()
    {
        if(testsStarted)return;testsStarted=true;Directory.CreateDirectory(testOutput);
        var checks=new List<object>();
        try
        {
            await WaitFor("window.jotReady===true");
            checks.Add(new{name="home-ready",passed=true,elapsedMs=startup.ElapsedMilliseconds});
            await VerifySqliteStorage(checks);
            await VerifySessionRestore(checks);
            await VerifyLifecycleScenarios(checks);
            await VerifyLastWindowClose(checks);
            await VerifySystemCloseProcesses(checks);
            await MeasureIndexSmoothness(checks);
            await VerifyIndexReconciliation(checks);
            checks.Add(new{name="explicit-tray-flag-only-used-for-quiet-start",passed=!App.ShouldStartInTray([])&&App.ShouldStartInTray(["--tray"])&&!App.ShouldStartInTray(["--tray","--self-test"])});
            CaptureCornerMask(checks);
            var dormant=new MainWindow(session,"home");
            dormant.StartInTray();
            checks.Add(new{name="explicit-tray-start-does-not-open-window-or-webview",passed=!dormant.IsVisible&&!dormant.ShowInTaskbar&&dormant.Browser.CoreWebView2 is null});
            dormant.ClosePermanently();
            var desktopProbeSession=new JotSession(false,testOutput);
            foreach(var mode in new[]{"home","note","image"})
            {
                var probe=new MainWindow(desktopProbeSession,mode);
                checks.Add(new{name="taskbar-enabled-for-visible-"+mode,passed=probe.ShowInTaskbar&&!probe.IsVisible&&probe.Browser.CoreWebView2 is null});
                probe.ClosePermanently();
            }
            await ClickControl("#homeNew");
            for(int i=0;i<100&&!session.Windows.Any(w=>w.Mode=="note");i++)await Task.Delay(50);
            var a=session.Windows.First(w=>w.Mode=="note");
            await a.WaitFor("window.jotReady===true");
            var b=await session.NewNote();await b.WaitFor("window.jotReady===true");
            await VerifyNotePreferences(checks,a,b);
            await VerifyNoteTools(checks);
            await VerifyNoteFullscreen(checks);
            await VerifyPerformance(checks,a);
            await MeasureWindowAndImageWork(checks,a);
            checks.Add(new{name="new-note-headers-default-crimson-with-neutral-app-ui",passed=await a.Script("activeNote().color==='crimson'&&model.prefs.accent==='neutral'")=="true"&&await b.Script("activeNote().color==='crimson'")=="true"});
            await a.Script("setInputDirection('rtl')");
            await a.Capture("empty-persian");
            await a.Script("setInputDirection('ltr')");
            await a.Capture("empty-english");
            checks.Add(new{name="native-keyboard-language-mapping",passed=DirectionForLanguage(0x429)=="rtl"&&DirectionForLanguage(0x409)=="ltr"});
            checks.Add(new{name="home-creates-separate-note-windows",passed=a!=b&&a.NoteId!=b.NoteId});
            var again=session.OpenNote(a.NoteId!);
            checks.Add(new{name="one-window-per-note",passed=ReferenceEquals(a,again)});
            checks.Add(new{name="all-test-windows-isolated",passed=session.Windows.All(w=>w.Left< -10000&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Opacity==0)});
            await a.Script("editor.focus()");
            await a.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text="Note A — فارسی"}));
            await b.Script("editor.focus()");
            await b.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text="Note B — English"}));
            await a.Script("saveNow().then(()=>window.savedA=true)");
            await b.Script("saveNow().then(()=>window.savedB=true)");
            await a.WaitFor("window.savedA===true");await b.WaitFor("window.savedB===true");
            var state=(await store.Load())!.Value;
            checks.Add(new{name="concurrent-editors-preserve-both-notes",passed=state.GetProperty("notes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==a.NoteId&&n.GetProperty("plain").GetString()!.Contains("Note A"))&&state.GetProperty("notes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==b.NoteId&&n.GetProperty("plain").GetString()!.Contains("Note B"))});
            await WaitFor("homeData.notes.length>=2");
            await store.SaveMetadata(JsonSerializer.SerializeToElement(new{id=a.NoteId,group="Work"}));
            await ClickControl("[data-note-id='"+a.NoteId+"'] .card-edit");
            await Script("document.getElementById('noteTitleInput').value='Sprint ideas';document.getElementById('metadataForm').requestSubmit()");
            await WaitFor("homeData.notes.some(n=>n.title==='Sprint ideas'&&n.group==='Work')");
            await a.Script("editor.innerHTML='<p>Note A — فارسی revised</p>';onEdit();saveNow().then(()=>window.metaSaved=true)");
            await a.WaitFor("window.metaSaved===true");
            var metadataState=(await store.Load())!.Value.GetProperty("notes").EnumerateArray().First(n=>n.GetProperty("id").GetString()==a.NoteId);
            checks.Add(new{name="title-group-survive-editor-save",passed=metadataState.GetProperty("title").GetString()=="Sprint ideas"&&metadataState.GetProperty("group").GetString()=="Work"});
            checks.Add(new{name="index-has-no-search-or-group-controls",passed=await Script("!document.querySelector('#homeSearch,#groupFilters,#noteGroupInput,.card-group')&&document.querySelectorAll('.note-card').length>=2")=="true"});
            var rootHandle=source!.Handle;
            var windowCount=session.Windows.Count;
            await GoToSettings();
            var settings=this;
            checks.Add(new{name="settings-reuses-index-window",passed=session.Windows.Count==windowCount&&source.Handle==rootHandle&&IsSettingsView&&session.Windows.All(w=>w.Mode!="settings")});
            await a.ClickControl("#menuButton");await a.ClickControl("[data-note-color='blue']");
            await a.WaitFor("activeNote().color==='blue'");await a.Script("closePanels()");
            await b.ClickControl("#menuButton");
            await b.ClickControl("[data-note-color='rose']");
            await b.WaitFor("activeNote().color==='rose'");await b.Script("closePanels()");
            checks.Add(new{name="global-settings-only-expose-theme",passed=await settings.Script("!document.querySelector('[data-weight],#toolbarVisible,#homeFontSize,#homeLineHeight,#trayVisibility')&&document.querySelectorAll('[data-theme-choice]').length===2")=="true"});
            for(int i=0;i<100&&(a.NativeIconKey==""||a.NativeIconKey!=NativeIconKey);i++)await Task.Delay(30);
            checks.Add(new{name="tray-icons-adapt-monochrome-independently-of-colored-app-icon",passed=a.NativeIconKey==NativeIconKey&&b.NativeIconKey==NativeIconKey&&NativeIconKey.StartsWith("monochrome-")});
            checks.Add(new{name="neutral-application-with-independent-note-colors",passed=await b.Script("document.documentElement.dataset.color==='neutral'&&document.documentElement.dataset.coloredIcons==='false'&&app.dataset.noteColor==='rose'")=="true"&&await a.Script("app.dataset.noteColor==='blue'")=="true"});
            checks.Add(new{name="nineteen-design-system-note-colors",passed=await a.Script("document.querySelectorAll('#noteColors button[data-note-color]').length===19")=="true"});
            checks.Add(new{name="global-color-controls-removed",passed=await settings.Script("document.querySelector('#accentChoices,#coloredIcons')===null")=="true"});
            await settings.Capture("settings-neutral");
            settings.RenderWindowSurface("composed-window");
            await GoToIndex();
            checks.Add(new{name="back-restores-unfiltered-index",passed=!IsSettingsView&&source.Handle==rootHandle&&await Script("document.querySelectorAll('.note-card').length===homeData.notes.length")=="true"});
            checks.Add(new{name="index-has-no-promotional-heading",passed=await Script("document.querySelector('.index-heading,.index-eyebrow')===null")=="true"});
            checks.Add(new{name="rename-action-visible-without-grouping",passed=await Script("[...document.querySelectorAll('.card-edit')].every(button=>button.getAttribute('aria-label')==='Rename note')")=="true"});
            await a.Script("editor.innerHTML='<p>Notes list saves the latest draft</p>';onEdit();clearTimeout(saveTimer)");
            await a.ClickControl("#menuButton");await a.ClickControl("#menuNotesButton");
            await WaitFor("homeData.notes.some(n=>n.plain.includes('Notes list saves the latest draft'))");
            checks.Add(new{name="sticky-menu-notes-list-saves-and-reuses-index",passed=a.TestHostActions.Contains("home")&&session.Windows.Count==windowCount&&!IsSettingsView&&await a.Script("document.getElementById('menu').hidden")=="true"});
            await a.Script(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"tests","renderer-tests.js")));
            await a.WaitFor("window.testsFinished===true",500);
            using(var results=JsonDocument.Parse(await a.Script("window.testResults")))
                foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
            await VerifyFontLimitCursor(checks,a);
            await VerifyBidiWriting(checks,a);
            await VerifyEditorContextMenu(checks,a);
            await VerifyContextSurfaces(checks,a);
            await VerifyNoteMotion(checks,a);
            await VerifyInactivePinnedChrome(checks,a);
            foreach(var action in new[]{("#copyButton","clipboard-write"),("#exportButton","export")})
            {
                var before=a.TestHostActions.Count(item=>item==action.Item2);
                await a.ClickControl("#menuButton");await a.ClickControl(action.Item1);
                for(int i=0;i<100&&a.TestHostActions.Count(item=>item==action.Item2)==before;i++)await Task.Delay(20);
                checks.Add(new{name="restored-more-action-"+action.Item2,passed=a.TestHostActions.Count(item=>item==action.Item2)>before&&await a.Script("document.getElementById('menu').hidden")=="true"});
            }
            var appearanceCount=session.Windows.Count;
            await GoToSettings();
            checks.Add(new{name="appearance-removed-from-note-settings-remain-in-index",passed=IsSettingsView&&source.Handle==rootHandle&&session.Windows.Count==appearanceCount&&await a.Script("document.getElementById('appearanceButton')===null")=="true"});
            await GoToIndex();
            await a.Capture("note-with-toolbar");
            a.Post(new { @event="active-window",active=true });await a.WaitFor("app.dataset.activeWindow==='true'");
            await a.ClickControl("#pinButton");await a.WaitFor("document.getElementById('pinButton').getAttribute('aria-pressed')==='true'");
            await Task.Delay(150);await a.Capture("pinned-note-dark");
            await a.ClickControl("#menuButton");await a.Capture("more-options-header");await a.Script("closePanels()");
            await a.ClickControl("#pinButton");await a.WaitFor("document.getElementById('pinButton').getAttribute('aria-pressed')==='false'");
            checks.Add(new{name="text-style-dropdown-removed",passed=await a.Script("document.getElementById('blockMenuButton')===null")=="true"});
            await a.ClickControl("#colorMenuButton");await a.Capture("toolbar-color-menu");await a.Script("closeFormatMenus()");
            var viewer=session.Windows.LastOrDefault(w=>w.Mode=="image");
            if(viewer is not null)
            {
                await viewer.WaitFor("window.jotReady===true");
                checks.Add(new{name="image-opens-separate-original-viewer",passed=await viewer.Script("document.getElementById('fullImage').naturalWidth===600")=="true"});
                await viewer.ClickControl("#actualButton");
                checks.Add(new{name="image-viewer-actual-size",passed=await viewer.Script("document.getElementById('imageStage').dataset.fit==='false'")=="true"});
                await viewer.Capture("image-viewer");
            }
            else checks.Add(new{name="image-opens-separate-original-viewer",passed=false});
            await a.Script("setPreference({toolbarVisible:false}).then(()=>window.toolbarDone=true)");
            await a.WaitFor("window.toolbarDone===true");
            await a.Capture("note-writing-only");
            a.Width=340;a.Height=360;await Task.Delay(250);
            checks.Add(new{name="small-window-no-horizontal-overflow",passed=await a.Script("document.documentElement.scrollWidth<=innerWidth")=="true"});
            await a.Capture("note-small");
            await a.Script("setPreference({toolbarVisible:true}).then(()=>window.smallToolbar=true)");await a.WaitFor("window.smallToolbar===true");
            checks.Add(new{name="small-toolbar-fits",passed=await a.Script("document.getElementById('formatBar').scrollWidth<=document.getElementById('formatBar').clientWidth")=="true"});
            var priorHeight=a.Height;a.Width=320;a.Height=250;await Task.Delay(200);
            await a.Script("document.getElementById('menuButton').click()");
            checks.Add(new{name="full-more-menu-fits-short-window-with-scrollable-options",passed=await a.Script("(()=>{const menu=document.getElementById('menu'),m=menu.getBoundingClientRect(),actions=document.getElementById('menuActions');return m.top===0&&m.left===0&&m.bottom<=innerHeight-24&&m.width===innerWidth&&actions.scrollHeight>actions.clientHeight;})()")=="true"});
            await a.Capture("small-header-menu");
            await a.Script("closePanels()");await Task.Delay(180);
            checks.Add(new{name="bottom-toolbar-fits-minimum-window-in-one-row",passed=await a.Script("(()=>{const f=document.querySelector('.quiet-footer').getBoundingClientRect();return [...document.querySelectorAll('.quiet-footer button')].every(button=>{const r=button.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=f.top&&r.bottom<=f.bottom;});})()")=="true"});
            checks.Add(new{name="footer-svg-centers-align-at-minimum-window-size",passed=await a.Script("(()=>{const icons=[...document.querySelectorAll('.quiet-footer button>svg')].map(svg=>svg.getBoundingClientRect());return icons.length===9&&icons.every(r=>r.width===16&&r.height===16)&&Math.max(...icons.map(r=>r.y+r.height/2))-Math.min(...icons.map(r=>r.y+r.height/2))<.1;})()")=="true"});
            await a.Capture("aligned-toolbar-minimum");
            await a.Script("document.getElementById('menuButton').click()");
            await a.WaitFor("noteMenuAnimation===null");
            checks.Add(new{name="all-colors-visible-and-hittable-at-minimum-window-size",passed=await a.Script("(()=>{const p=document.getElementById('noteColors'),colors=[...p.querySelectorAll('button')];return colors.length===19&&p.scrollWidth<=p.clientWidth&&colors.every(button=>{const r=button.getBoundingClientRect();return r.width>=15&&r.height===40&&button.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));});})()")=="true"});
            await a.ClickControl("[data-note-color='neutral']");await a.WaitFor("activeNote().color==='neutral'");
            checks.Add(new{name="last-color-selects-without-scrolling-at-minimum-size",passed=await a.Script("app.dataset.noteColor==='neutral'&&document.getElementById('noteColors').scrollLeft===0")=="true"});
            await a.ClickControl("[data-note-color='blue']");await a.WaitFor("activeNote().color==='blue'");
            await a.Script("document.getElementById('menuActions').scrollTop=document.getElementById('menuActions').scrollHeight");
            checks.Add(new{name="small-menu-keeps-palette-visible-and-delete-reachable",passed=await a.Script("(()=>{const color=document.getElementById('noteColors').getBoundingClientRect(),del=document.getElementById('deleteButton').getBoundingClientRect(),menu=document.getElementById('menu').getBoundingClientRect();return color.top===0&&color.height===40&&del.top>=color.bottom&&del.bottom<=menu.bottom;})()")=="true"});
            await a.Capture("small-menu-scrolled");
            await a.ClickControl("#deleteButton");await a.WaitFor("document.getElementById('deleteDialog').open");await a.ClickControl("#deleteCancel");
            a.Height=priorHeight;await Task.Delay(150);
            var toolHeight=a.Height;a.Height=250;await Task.Delay(150);
            await a.ClickControl("#colorMenuButton");
            await a.WaitFor("colorMenuAnimation===null");
            checks.Add(new{name="color-picker-fits-short-window",passed=await a.Script("(()=>{const m=document.getElementById('colorMenu').getBoundingClientRect();return m.top>=0&&m.bottom<=innerHeight&&m.left>=0&&m.right<=innerWidth;})()")=="true"});
            await a.Capture("small-writing-tools");await a.Script("closeFormatMenus()");a.Height=toolHeight;await Task.Delay(150);
            GetWindowRect(a.source!.Handle,out var rect);
            var hits=new List<int>();
            foreach(var point in new[]{(rect.Left+9,(rect.Top+rect.Bottom)/2),(rect.Right-10,(rect.Top+rect.Bottom)/2),((rect.Left+rect.Right)/2,rect.Top+9),((rect.Left+rect.Right)/2,rect.Bottom-10)})
            {int packed=((point.Item2&65535)<<16)|(point.Item1&65535);hits.Add((int)SendMessage(a.source.Handle,0x84,0,packed));}
            checks.Add(new{name="resize-hit-zones",passed=hits.SequenceEqual(new[]{10,11,12,15}),actual=hits});
            await a.Script("setAppTheme('light').then(()=>setPreference({toolbarVisible:true})).then(()=>window.prefSaved=true)");await a.WaitFor("window.prefSaved===true");
            await a.Reload();
            await a.ClickControl("#menuButton");await a.Capture("sticky-menu-light");await a.Script("closePanels()");
            checks.Add(new{name="note-reload-preserves-text-images-color-and-preferences",passed=await a.Script("editor.textContent.includes('Meeting notes')&&!!editor.querySelector('img')&&activeNote().color==='blue'&&model.prefs.theme==='light'&&!document.getElementById('formatBar').hidden")=="true"});
            await b.Reload();
            checks.Add(new{name="other-note-still-intact-after-all-operations",passed=await b.Script("editor.textContent.includes('Note B')&&activeNote().color==='rose'")=="true"});
            await WaitFor("homeData.notes.length>=2");
            await Capture("home");
            checks.Add(new{name="simple-index-keeps-every-note-accessible",passed=await Script("document.querySelectorAll('.note-card').length===homeData.notes.length")=="true"});
            await VerifySimplifiedShell(checks,a);
            // Clipboard tests use a DataObject in memory. They never overwrite the user's clipboard.
            var fragment="<p>فارسی <b>English</b></p><table><tr><td>A</td><td>B</td></tr></table>";
            using(var payload=JsonDocument.Parse(JsonSerializer.Serialize(new{html=fragment,text="فارسی English\\nA\\tB"})))
            {
                var clipboard=RichClipboard.Create(payload.RootElement);var html=(string)clipboard.GetData(System.Windows.DataFormats.Html)!;
                var start=int.Parse(System.Text.RegularExpressions.Regex.Match(html,@"StartFragment:(\d+)").Groups[1].Value);
                var end=int.Parse(System.Text.RegularExpressions.Regex.Match(html,@"EndFragment:(\d+)").Groups[1].Value);
                var bytes=Encoding.UTF8.GetBytes(html);
                checks.Add(new{name="native-clipboard-utf8-html-fragment",passed=Encoding.UTF8.GetString(bytes,start,end-start)==fragment&&clipboard.GetDataPresent(System.Windows.DataFormats.UnicodeText)});
            }
            await store.Backup();
            checks.Add(new{name="sqlite-backup-exists",passed=File.Exists(store.BackupPath)});
            var imageData=JsonSerializer.Deserialize<string>(await a.Script("editor.querySelector('img').src"))!;
            await VerifyImageControls(checks,imageData);
            await VerifyWindowLifetimes(checks,a,b,imageData);
            using(var payload=JsonDocument.Parse(JsonSerializer.Serialize(new{html="<img src=\""+imageData+"\">",text="",image=imageData})))
            {
                var clipboard=RichClipboard.Create(payload.RootElement);
                checks.Add(new{name="image-only-copy-has-png-bitmap-and-html",passed=clipboard.GetDataPresent("PNG")&&clipboard.GetDataPresent(System.Windows.DataFormats.Bitmap)&&clipboard.GetDataPresent(System.Windows.DataFormats.Html)});
            }
            using(var payload=JsonDocument.Parse(JsonSerializer.Serialize(new{html="<p>Text</p><img src=\""+imageData+"\">",text="Text",image=imageData})))
            {
                var clipboard=RichClipboard.Create(payload.RootElement);
                var original=(BitmapSource)clipboard.GetData(System.Windows.DataFormats.Bitmap)!;
                checks.Add(new{name="mixed-copy-has-text-html-png-and-bitmap",passed=(string?)clipboard.GetData(System.Windows.DataFormats.UnicodeText)=="Text"&&clipboard.GetDataPresent("PNG")&&clipboard.GetDataPresent(System.Windows.DataFormats.Bitmap)&&clipboard.GetDataPresent(System.Windows.DataFormats.Html)});
                checks.Add(new{name="copied-image-is-original-not-thumbnail",passed=original.PixelWidth==600&&original.PixelHeight==200});
            }
            foreach(bool light in new[]{false,true})
            {
                using var iconBytes=new MemoryStream(AppIcon.RenderMonochrome(light));
                using var icon=new System.Drawing.Icon(iconBytes,32,32);
                using var bitmap=icon.ToBitmap();
                int visible=0;bool gray=true;int total=0;
                for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){
                    var pixel=bitmap.GetPixel(x,y);if(pixel.A<200)continue;visible++;total+=pixel.R;
                    if(Math.Abs(pixel.R-pixel.G)>1||Math.Abs(pixel.R-pixel.B)>1)gray=false;
                }
                var average=visible==0?0:total/visible;
                checks.Add(new{name=light?"dark-glyph-for-light-tray":"light-glyph-for-dark-tray",passed=gray&&visible>30&&(light?average<70:average>200)});
                bitmap.Save(Path.Combine(testOutput,light?"tray-icon-light-background.png":"tray-icon-dark-background.png"));
            }
            await VerifyErrorHandling(checks);
            await VerifyPersonalization(checks,a,b);
            await VerifyReadyPaint(checks,a);
            await VerifyTrayAndLineHeight(checks,a,b);
            var isolated = new NoteStore(Path.Combine(testOutput,"corrupt-store-check"));
            Directory.CreateDirectory(isolated.Root);
            await File.WriteAllTextAsync(isolated.FilePath,"sentinel-broken-json");
            bool rejected=false;
            try { await isolated.Load(); } catch(Microsoft.Data.Sqlite.SqliteException) { rejected=true; }
            checks.Add(new{name="corrupt-store-preserved",passed=rejected&&await File.ReadAllTextAsync(isolated.FilePath)=="sentinel-broken-json"});
            var noteBeforeLanguage=await a.Script("editor.innerHTML");
            await GoToSettings();
            checks.Add(new{name="language-switch-removed",passed=await Script("document.querySelector('[data-language]')===null")=="true"});
            await a.WaitFor("JotI18n.language==='en'");await WaitFor("JotI18n.language==='en'");
            checks.Add(new{name="english-settings-and-back-label",passed=await Script("document.querySelector('#settingsHandle .index-brand span').textContent==='Settings'&&document.getElementById('settingsBack').getAttribute('aria-label')==='Back to Home'")=="true"});
            checks.Add(new{name="ui-language-does-not-change-note-content",passed=noteBeforeLanguage==await a.Script("editor.innerHTML")});
            await a.Script("setInputDirection('rtl')");
            checks.Add(new{name="english-ui-still-supports-persian-and-english",passed=await a.Script("getComputedStyle(editor.querySelector('p')).direction==='ltr'&&[...editor.querySelectorAll('p')].some(p=>getComputedStyle(p).direction==='rtl')")=="true"});
            await settings.Capture("settings-english");
            await GoToIndex();await Capture("index-english");
            checks.Add(new{name="english-index-and-note-ui",passed=await Script("document.getElementById('homeNew').getAttribute('aria-label')==='New note'&&document.getElementById('homeClose').getAttribute('aria-label')==='Close window'")=="true"&&await a.Script("document.getElementById('copyButton').textContent.trim()==='Copy'")=="true"});
            await GoToSettings();
            await settings.Script("preference({language:'fa'}).then(()=>window.legacyLanguageChecked=true)");
            await settings.WaitFor("window.legacyLanguageChecked===true");
            await GoToIndex();
            checks.Add(new{name="legacy-persian-ui-setting-is-ignored",passed=await Script("document.querySelector('.index-app').dir==='ltr'&&document.getElementById('homeNew').getAttribute('aria-label')==='New note'")=="true"});
            a.Post(new { @event="active-window",active=true });await a.WaitFor("app.dataset.activeWindow==='true'");
            await a.Capture("selected-note");
            a.RenderWindowSurface("note-window-active");
            await a.ClickControl("#pinButton");await a.WaitFor("document.getElementById('pinButton').getAttribute('aria-pressed')==='true'");
            await Task.Delay(150);await a.Capture("pinned-note-light");
            await a.ClickControl("#pinButton");await a.WaitFor("document.getElementById('pinButton').getAttribute('aria-pressed')==='false'");
            checks.Add(new{name="neutral-note-body-with-colored-header",passed=await a.Script("getComputedStyle(app).backgroundColor===getComputedStyle(document.documentElement).backgroundColor&&getComputedStyle(document.getElementById('handle'),'::before').backgroundColor!==getComputedStyle(editor).backgroundColor")=="true"});
            a.Post(new { @event="active-window",active=false });await a.WaitFor("app.dataset.activeWindow==='false'");
            await a.Capture("inactive-note");
            a.RenderWindowSurface("note-window-inactive");
            checks.Add(new{name="inactive-note-eight-pixel-color-strip",passed=await a.Script("getComputedStyle(document.getElementById('handle'),'::before').height==='8px'&&getComputedStyle(app).backgroundColor===getComputedStyle(editor).backgroundColor")=="true"});
            await GoToSettings();
            // A failed save must cancel Quit and leave all windows and drafts available.
            await a.Script("window.realId=model.activeId;activeNote().id='invalid';model.activeId='invalid';revision++;clearTimeout(saveTimer)");
            var beforeFailedQuit=session.Windows.Count;
            await ClickControl("#settingsQuit");
            await WaitFor("document.getElementById('settingsQuit').disabled===false&&!document.getElementById('homeError').hidden");
            checks.Add(new{name="quit-cancels-on-save-error",passed=session.Windows.Count==beforeFailedQuit&&await Script("document.getElementById('settingsQuit').getAttribute('aria-busy')==='false'")=="true"});
            await a.Script("activeNote().id=window.realId;model.activeId=window.realId;editor.innerHTML='<p>Quit save sentinel A</p>';onEdit();clearTimeout(saveTimer)");
            await b.Script("editor.innerHTML='<p>Quit save sentinel B</p>';onEdit();clearTimeout(saveTimer)");
            await ClickControl("#settingsQuit");
            for(int i=0;i<150&&!TestHostActions.Contains("quit");i++)await Task.Delay(40);
            var quitData=(await store.Load())!.Value.GetProperty("notes").EnumerateArray().ToArray();
            checks.Add(new{name="quit-flushes-every-open-note",passed=TestHostActions.Contains("quit")&&quitData.Any(n=>n.GetProperty("plain").GetString()!.Contains("Quit save sentinel A"))&&quitData.Any(n=>n.GetProperty("plain").GetString()!.Contains("Quit save sentinel B"))});
            checks.Add(new{name="quit-shows-pending-spinner-and-label",passed=await Script("document.getElementById('settingsQuit').disabled&&document.getElementById('settingsQuit').getAttribute('aria-busy')==='true'&&getComputedStyle(document.querySelector('#settingsQuit .quit-spinner')).display!=='none'")=="true"});
            await MeasureDirectRendering(checks);
            checks.Add(new{name="no-renderer-exceptions",passed=session.Windows.All(w=>w.RuntimeErrors.Count==0)});
            var report=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"results.json"),report);
            using var parsed=JsonDocument.Parse(report);
            bool passed=parsed.RootElement.EnumerateArray().All(result=>result.GetProperty("passed").GetBoolean());
            if (passed) await session.Quit();
            else
            {
                foreach(var window in session.Windows.ToArray())window.ClosePermanently();
                System.Windows.Application.Current.Shutdown(2);
            }
        }
        catch(Exception ex)
        {
            LogFailure(ex);
            await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));
            await File.WriteAllTextAsync(Path.Combine(testOutput,"runtime-errors.json"),JsonSerializer.Serialize(session.Windows.SelectMany(w=>w.RuntimeErrors)));
            foreach(var window in session.Windows.ToArray())window.ClosePermanently();
            System.Windows.Application.Current.Shutdown(1);
        }
    }
}
