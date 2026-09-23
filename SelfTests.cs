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
        using var point=JsonDocument.Parse(await Script("(()=>{const r=document.querySelector("+JsonSerializer.Serialize(selector)+").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()"));
        var x=point.RootElement.GetProperty("x").GetDouble();var y=point.RootElement.GetProperty("y").GetDouble();
        foreach(var type in new[]{"mouseMoved","mousePressed","mouseReleased"})
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new {type,x,y,button=type=="mouseMoved"?"none":"left",clickCount=type=="mouseMoved"?0:1}));
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
        await WaitFor("window.jotReady===true&&document.getElementById('homeSearch')!==null");
    }
    private void CaptureCornerMask(List<object> checks)
    {
        // Unit render of this app's WPF surface only; no desktop capture or other windows.
        var bitmap=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();
        using(var context=visual.RenderOpen())
            context.DrawRoundedRectangle(System.Windows.Media.Brushes.White,null,new System.Windows.Rect(8,8,Width-16,Height-16),14,14);
        bitmap.Render(visual);
        var bytes=new byte[(int)Width*(int)Height*4];bitmap.CopyPixels(bytes,(int)Width*4,0);
        bool partial=false;for(int y=8;y<24;y++)for(int x=8;x<24;x++){var alpha=bytes[(y*(int)Width+x)*4+3];if(alpha>0&&alpha<255)partial=true;}
        checks.Add(new {name="rounded-surface-has-antialiased-alpha",passed=partial&&Browser.Clip is RectangleGeometry&&AllowsTransparency});
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
            CaptureCornerMask(checks);
            var dormant=new MainWindow(session,"home");
            dormant.StartInTray();
            checks.Add(new{name="tray-start-does-not-open-window-or-webview",passed=!dormant.IsVisible&&!dormant.ShowInTaskbar&&dormant.Browser.CoreWebView2 is null});
            dormant.ClosePermanently();
            await ClickControl("#homeNew");
            for(int i=0;i<100&&!session.Windows.Any(w=>w.Mode=="note");i++)await Task.Delay(50);
            var a=session.Windows.First(w=>w.Mode=="note");
            await a.WaitFor("window.jotReady===true");
            var b=await session.NewNote();await b.WaitFor("window.jotReady===true");
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
            await ClickControl("[data-note-id='"+a.NoteId+"'] .card-edit");
            await Script("document.getElementById('noteTitleInput').value='Sprint ideas';document.getElementById('noteGroupInput').value='Work';document.getElementById('metadataForm').requestSubmit()");
            await WaitFor("homeData.notes.some(n=>n.title==='Sprint ideas'&&n.group==='Work')");
            await a.Script("editor.innerHTML='<p>Note A — فارسی revised</p>';onEdit();saveNow().then(()=>window.metaSaved=true)");
            await a.WaitFor("window.metaSaved===true");
            var metadataState=(await store.Load())!.Value.GetProperty("notes").EnumerateArray().First(n=>n.GetProperty("id").GetString()==a.NoteId);
            checks.Add(new{name="title-group-survive-editor-save",passed=metadataState.GetProperty("title").GetString()=="Sprint ideas"&&metadataState.GetProperty("group").GetString()=="Work"});
            await ClickControl("[data-group='Work']");
            checks.Add(new{name="group-filter",passed=await Script("document.querySelectorAll('.note-card').length===1")=="true"});
            var rootHandle=source!.Handle;
            var windowCount=session.Windows.Count;
            await GoToSettings();
            var settings=this;
            checks.Add(new{name="settings-reuses-index-window",passed=session.Windows.Count==windowCount&&source.Handle==rootHandle&&IsSettingsView&&session.Windows.All(w=>w.Mode!="settings")});
            await settings.ClickControl("[data-accent='blue']");
            await a.WaitFor("model.prefs.accent==='blue'");await b.WaitFor("model.prefs.accent==='blue'");
            await settings.ClickControl("#coloredIcons");await settings.ClickControl("[data-weight='2.4']");
            await a.WaitFor("model.prefs.coloredIcons===true&&model.prefs.iconWeight===2.4");
            for(int i=0;i<100&&(a.NativeIconKey==""||a.NativeIconKey!=NativeIconKey);i++)await Task.Delay(30);
            checks.Add(new{name="native-icons-remain-monochrome",passed=a.NativeIconKey==NativeIconKey&&b.NativeIconKey==NativeIconKey&&NativeIconKey.StartsWith("monochrome-")});
            checks.Add(new{name="live-appearance-sync-across-windows",passed=await b.Script("document.documentElement.dataset.color==='blue'&&document.documentElement.dataset.coloredIcons==='true'")=="true"});
            checks.Add(new{name="nineteen-design-system-accents",passed=await settings.Script("document.querySelectorAll('[data-accent]').length===19")=="true"});
            await settings.Capture("settings-persian");
            settings.RenderWindowSurface("composed-window");
            await GoToIndex();
            checks.Add(new{name="back-restores-index-group",passed=!IsSettingsView&&source.Handle==rootHandle&&await Script("currentGroup==='Work'&&document.querySelectorAll('.note-card').length===1")=="true"});
            await ClickControl("[data-group='*']");
            checks.Add(new{name="index-has-no-promotional-heading",passed=await Script("document.querySelector('.index-heading,.index-eyebrow')===null")=="true"});
            checks.Add(new{name="grouping-action-visible",passed=await Script("[...document.querySelectorAll('.card-edit')].every(button=>button.textContent.includes('Title and group'))")=="true"});
            await a.Script(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"tests","renderer-tests.js")));
            await a.WaitFor("window.testsFinished===true",500);
            using(var results=JsonDocument.Parse(await a.Script("window.testResults")))
                foreach(var result in results.RootElement.EnumerateArray())checks.Add(result.Clone());
            await a.Capture("note-with-toolbar");
            await a.ClickControl("#blockMenuButton");await a.Capture("toolbar-style-menu");await a.Script("closeFormatMenus()");
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
            GetWindowRect(a.source!.Handle,out var rect);
            var hits=new List<int>();
            foreach(var point in new[]{(rect.Left+9,(rect.Top+rect.Bottom)/2),(rect.Right-10,(rect.Top+rect.Bottom)/2),((rect.Left+rect.Right)/2,rect.Top+9),((rect.Left+rect.Right)/2,rect.Bottom-10)})
            {int packed=((point.Item2&65535)<<16)|(point.Item1&65535);hits.Add((int)SendMessage(a.source.Handle,0x84,0,packed));}
            checks.Add(new{name="resize-hit-zones",passed=hits.SequenceEqual(new[]{10,11,12,15}),actual=hits});
            await a.Script("setPreference({theme:'light',toolbarVisible:true}).then(()=>window.prefSaved=true)");await a.WaitFor("window.prefSaved===true");
            await a.Reload();
            checks.Add(new{name="note-reload-preserves-text-images-and-preferences",passed=await a.Script("editor.textContent.includes('Meeting notes')&&!!editor.querySelector('img')&&model.prefs.theme==='light'&&!document.getElementById('formatBar').hidden")=="true"});
            await b.Reload();
            checks.Add(new{name="other-note-still-intact-after-all-operations",passed=await b.Script("editor.textContent.includes('Note B')")=="true"});
            await WaitFor("homeData.notes.length>=2");
            await Capture("home");
            await Script("document.getElementById('homeSearch').value='Note B';renderCards()");
            checks.Add(new{name="home-search-finds-note",passed=await Script("document.querySelectorAll('.note-card').length===1")=="true"});
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
            checks.Add(new{name="backup-exists",passed=File.Exists(store.FilePath+".bak")});
            var imageData=JsonSerializer.Deserialize<string>(await a.Script("editor.querySelector('img').src"))!;
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
            var isolated = new NoteStore(Path.Combine(testOutput,"corrupt-store-check"));
            Directory.CreateDirectory(isolated.Root);
            await File.WriteAllTextAsync(isolated.FilePath,"sentinel-broken-json");
            bool rejected=false;
            try { await isolated.Load(); } catch(JsonException) { rejected=true; }
            checks.Add(new{name="corrupt-store-preserved",passed=rejected&&await File.ReadAllTextAsync(isolated.FilePath)=="sentinel-broken-json"});
            var noteBeforeLanguage=await a.Script("editor.innerHTML");
            await GoToSettings();
            checks.Add(new{name="language-switch-removed",passed=await Script("document.querySelector('[data-language]')===null")=="true"});
            await a.WaitFor("JotI18n.language==='en'");await WaitFor("JotI18n.language==='en'");
            checks.Add(new{name="english-settings-and-back-label",passed=await Script("document.querySelector('.settings-header h1').textContent==='Settings'&&document.getElementById('settingsBack').textContent.includes('Back to notes')")=="true"});
            checks.Add(new{name="ui-language-does-not-change-note-content",passed=noteBeforeLanguage==await a.Script("editor.innerHTML")});
            await a.Script("setInputDirection('rtl')");
            checks.Add(new{name="english-ui-still-supports-persian-and-english",passed=await a.Script("getComputedStyle(editor.querySelector('p')).direction==='ltr'&&[...editor.querySelectorAll('p')].some(p=>getComputedStyle(p).direction==='rtl')")=="true"});
            await settings.Capture("settings-english");
            await GoToIndex();await Capture("index-english");
            checks.Add(new{name="english-index-and-note-ui",passed=await Script("document.getElementById('homeNew').textContent.includes('New note')&&document.querySelector('#homeQuit .quit-idle').textContent==='Quit Jot'")=="true"&&await a.Script("document.getElementById('copyTextButton').textContent.includes('Copy text only')")=="true"});
            await GoToSettings();
            await settings.Script("preference({language:'fa'}).then(()=>window.legacyLanguageChecked=true)");
            await settings.WaitFor("window.legacyLanguageChecked===true");
            await GoToIndex();
            checks.Add(new{name="legacy-persian-ui-setting-is-ignored",passed=await Script("document.querySelector('.index-app').dir==='ltr'&&document.getElementById('homeNew').textContent.includes('New note')")=="true"});
            a.Post(new { @event="active-window",active=true });await a.WaitFor("app.dataset.activeWindow==='true'");
            await a.Capture("selected-note");
            checks.Add(new{name="neutral-note-body-with-colored-header",passed=await a.Script("getComputedStyle(app).backgroundColor===getComputedStyle(document.documentElement).backgroundColor&&getComputedStyle(document.getElementById('handle')).backgroundColor!==getComputedStyle(editor).backgroundColor")=="true"});
            a.Post(new { @event="active-window",active=false });await a.WaitFor("app.dataset.activeWindow==='false'");
            await a.Capture("inactive-note");
            checks.Add(new{name="inactive-note-top-color-strip",passed=await a.Script("getComputedStyle(document.getElementById('handle')).borderTopWidth==='4px'&&getComputedStyle(app).backgroundColor===getComputedStyle(editor).backgroundColor")=="true"});
            // A failed save must cancel Quit and leave all windows and drafts available.
            await a.Script("window.realId=model.activeId;activeNote().id='invalid';model.activeId='invalid';revision++;clearTimeout(saveTimer)");
            var beforeFailedQuit=session.Windows.Count;
            await ClickControl("#homeQuit");
            await WaitFor("document.getElementById('homeQuit').disabled===false&&!document.getElementById('homeError').hidden");
            checks.Add(new{name="quit-cancels-on-save-error",passed=session.Windows.Count==beforeFailedQuit&&await Script("document.getElementById('homeQuit').getAttribute('aria-busy')==='false'")=="true"});
            await a.Script("activeNote().id=window.realId;model.activeId=window.realId;editor.innerHTML='<p>Quit save sentinel A</p>';onEdit();clearTimeout(saveTimer)");
            await b.Script("editor.innerHTML='<p>Quit save sentinel B</p>';onEdit();clearTimeout(saveTimer)");
            await ClickControl("#homeQuit");
            for(int i=0;i<150&&!TestHostActions.Contains("quit");i++)await Task.Delay(40);
            var quitData=(await store.Load())!.Value.GetProperty("notes").EnumerateArray().ToArray();
            checks.Add(new{name="quit-flushes-every-open-note",passed=TestHostActions.Contains("quit")&&quitData.Any(n=>n.GetProperty("plain").GetString()!.Contains("Quit save sentinel A"))&&quitData.Any(n=>n.GetProperty("plain").GetString()!.Contains("Quit save sentinel B"))});
            checks.Add(new{name="quit-shows-pending-spinner-and-label",passed=await Script("document.getElementById('homeQuit').disabled&&document.getElementById('homeQuit').getAttribute('aria-busy')==='true'&&getComputedStyle(document.querySelector('#homeQuit .quit-spinner')).display!=='none'")=="true"});
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
