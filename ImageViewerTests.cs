using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyImageControls(List<object> checks,string image)
    {
        var viewer=session.Image(image);
        try
        {
            await viewer.WaitFor("window.jotReady===true");
            async Task Check(string name,string predicate)=>checks.Add(new{name="image-controls-"+name,passed=await viewer.Script(predicate)=="true"});
            await Check("fit-default-original-intact","fitMode&&fullImage.naturalWidth===600&&fullImage.naturalHeight===200&&zoom<=1");
            await viewer.ClickControl("#actualButton");await Check("original-size","zoom===1&&!fitMode&&fullImage.getBoundingClientRect().width===600");
            await viewer.ClickControl("#zoomIn");await Check("zoom-in-and-percentage","zoom===1.25&&document.getElementById('imageZoom').value==='125%'");
            await viewer.ClickControl("#zoomOut");await Check("zoom-out","zoom===1");
            await viewer.Script("setZoom(100)");await Check("upper-bound","zoom===8&&document.getElementById('zoomIn').disabled");
            await viewer.ClickControl("#imageCopy");await viewer.WaitFor("!document.getElementById('imageCopy').disabled");
            checks.Add(new{name="image-controls-copy-at-800-percent-retains-original-pixels",passed=viewer.TestClipboardData?.GetData(DataFormats.Bitmap) is BitmapSource bitmap&&bitmap.PixelWidth==600&&bitmap.PixelHeight==200});
            var snapshot=ClipboardReader.Read(viewer.TestClipboardData);
            checks.Add(new{name="clipboard-native-image-reader-retains-png",passed=snapshot.image?.StartsWith("data:image/png;base64,")==true&&snapshot.html.Contains("<img")});
            await viewer.Script("setZoom(.001)");await Check("lower-bound","zoom===.1&&document.getElementById('zoomOut').disabled");
            viewer.Width=360;viewer.Height=280;await Task.Delay(150);await viewer.ClickControl("#fitButton");
            await Check("minimum-size-fit-and-controls","(()=>{const r=fullImage.getBoundingClientRect(),s=stage.getBoundingClientRect();return fitMode&&r.left>=s.left&&r.right<=s.right&&r.bottom<=s.bottom&&[...document.querySelectorAll('button')].every(b=>{const x=b.getBoundingClientRect();return x.left>=0&&x.right<=innerWidth&&x.top>=0&&x.bottom<=innerHeight;});})()");
            await viewer.Capture("image-viewer-small");
            await viewer.Script("fit(false)");
            await viewer.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mouseWheel\",\"x\":150,\"y\":140,\"deltaX\":0,\"deltaY\":-120}");
            await viewer.WaitFor("zoom>1");await Check("wheel-zoom","zoom>1&&!fitMode");
            await viewer.Script("setZoom(3);stage.scrollLeft=300;stage.scrollTop=100;window.panBefore=stage.scrollLeft");
            foreach(var item in new[]{new{type="mousePressed",x=170,y=150,button="left",buttons=1},new{type="mouseMoved",x=120,y=150,button="left",buttons=1},new{type="mouseReleased",x=120,y=150,button="left",buttons=0}})
                await viewer.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{item.type,item.x,item.y,item.button,item.buttons,clickCount=item.type=="mouseMoved"?0:1}));
            await Check("drag-to-pan","stage.scrollLeft>window.panBefore&&stage.dataset.dragging==='false'");
            var bounds=new Rect(viewer.Left,viewer.Top,viewer.Width,viewer.Height);
            await viewer.ClickControl("#imageFullscreen");await viewer.WaitFor("fullscreen===true&&!fullscreenBusy");
            checks.Add(new{name="image-controls-fullscreen-borderless-and-offscreen",passed=viewer.IsImageFullscreen&&viewer.Surface.Margin.Left==0&&viewer.Browser.Margin.Left==0&&viewer.Browser.Clip is null&&viewer.ResizeMode==ResizeMode.NoResize&&viewer.Width==1200&&viewer.Height==800&&viewer.Left< -10000&&!viewer.Topmost&&!viewer.ShowActivated&&!viewer.ShowInTaskbar&&viewer.Opacity==0});
            await viewer.ClickControl("#fitButton");await viewer.Capture("image-fullscreen");
            await viewer.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyDown\",\"key\":\"Escape\",\"code\":\"Escape\",\"windowsVirtualKeyCode\":27}");
            await viewer.WaitFor("fullscreen===false&&!fullscreenBusy");
            checks.Add(new{name="image-controls-escape-restores-window-without-closing",passed=!viewer.IsImageFullscreen&&!viewer.windowClosed&&new Rect(viewer.Left,viewer.Top,viewer.Width,viewer.Height)==bounds&&viewer.Surface.CornerRadius.TopLeft==6&&viewer.Browser.Clip is not null&&viewer.ResizeMode==ResizeMode.CanResize});
            for(int i=0;i<3;i++)
            {
                await viewer.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyDown\",\"key\":\"F11\",\"code\":\"F11\",\"windowsVirtualKeyCode\":122}");await viewer.WaitFor("fullscreen===true&&!fullscreenBusy");
                await viewer.Script("setFullscreen(false)");await viewer.WaitFor("fullscreen===false&&!fullscreenBusy");
            }
            await Check("repeated-fullscreen-original-preserved","fullImage.naturalWidth===600&&fullImage.naturalHeight===200&&fitMode");
            checks.Add(new{name="image-controls-no-runtime-errors",passed=viewer.RuntimeErrors.Count==0});
            await viewer.Script("setFullscreen(true)");await viewer.WaitFor("fullscreen===true&&!fullscreenBusy");await viewer.ClickControl("#imageClose");
            for(int wait=0;wait<250&&!viewer.windowClosed;wait++)await Task.Delay(20);
            checks.Add(new{name="image-controls-close-while-fullscreen-safe",passed=viewer.windowClosed&&viewer.browserDisposed&&!viewer.Post(new{@event="late-fullscreen"})});
        }
        finally{viewer.ClosePermanently();}
    }
}
