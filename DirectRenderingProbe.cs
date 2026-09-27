using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using Forms=System.Windows.Forms;

namespace Jot;

public partial class MainWindow
{
    // Measurement-only native host. Never selected for production: it does not
    // yet provide Jot's approved antialiased corners, shadow or full host bridge.
    private sealed class OffscreenProbeForm:Forms.Form
    {
        protected override bool ShowWithoutActivation=>true;
        protected override Forms.CreateParams CreateParams
        {
            get{var value=base.CreateParams;value.ExStyle|=0x08000000|0x80;return value;}
        }
    }
    private async Task MeasureDirectRendering(List<object> checks)
    {
        if(!testing)throw new InvalidOperationException("This probe is isolated-test-only.");
        using var form=new OffscreenProbeForm{StartPosition=Forms.FormStartPosition.Manual,Location=new System.Drawing.Point(-32000,-32000),
            Size=new System.Drawing.Size(480,460),FormBorderStyle=Forms.FormBorderStyle.None,ShowInTaskbar=false,Padding=new Forms.Padding(9)};
        using var browser=new WebView2{Dock=Forms.DockStyle.Fill,DefaultBackgroundColor=System.Drawing.Color.FromArgb(23,23,23)};
        form.Controls.Add(browser);form.Show();
        var environment=await BrowserRuntime.CreateAsync(Path.Combine(testOutput,"direct-probe-profile"),true);
        await browser.EnsureCoreWebView2Async(environment);
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id=Guid.NewGuid().ToString();
        browser.CoreWebView2.NewWindowRequested+=(_,args)=>args.Handled=true;
        browser.CoreWebView2.PermissionRequested+=(_,args)=>args.State=Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
        browser.CoreWebView2.WebMessageReceived+=(_,args)=>{
            using var message=JsonDocument.Parse(args.WebMessageAsJson);var root=message.RootElement;
            var action=root.GetProperty("action").GetString();object? value=null;
            if(action=="note-load")value=new{context=new{noteId=id,inputDirection="ltr",active=true},model=new{version=2,activeId=id,prefs=NoteStore.Defaults(),notes=new[]{new{id,html="<p dir=\"auto\"><br></p>",plain="",color="crimson",updatedAt=0}}}};
            else if(action=="input-language")value="ltr";
            else if(action=="editor-ready")ready.TrySetResult();
            else if(action=="editor-failed")ready.TrySetException(new InvalidOperationException("Direct probe failed to initialize."));
            if(root.TryGetProperty("id",out var sequence))browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{id=sequence.GetInt32(),ok=true,value}));
        };
        browser.CoreWebView2.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory,"index.html")).AbsoluteUri);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var samples=new List<double>();
        for(int i=0;i<24;i++){
            var watch=Stopwatch.StartNew();form.Width=400+i*7;form.Height=330+i*5;form.PerformLayout();samples.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(20);
        }
        samples.Sort();
        await File.WriteAllTextAsync(Path.Combine(testOutput,"direct-host-probe.json"),JsonSerializer.Serialize(new{
            resizeLayout=new{medianMs=samples[12],p95Ms=samples[22]},
            limitation="Experimental standard WebView2 host with the same editor HTML. No production bridge/corners/shadow parity; no live drag/frame-rate measurement."
        },new JsonSerializerOptions{WriteIndented=true}));
        checks.Add(new{name="direct-rendering-probe-is-offscreen-without-user-data-or-taskbar",passed=form.Left< -10000&&!form.TopMost&&!form.ShowInTaskbar&&!Forms.Screen.AllScreens.Any(screen=>screen.Bounds.IntersectsWith(form.Bounds))});
    }
}
