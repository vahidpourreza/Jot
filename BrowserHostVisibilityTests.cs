using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyBrowserHostVisibility(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="browser-host-visibility-"+name,passed});
        foreach(var state in new[]{WindowState.Normal,WindowState.Minimized,WindowState.Maximized})
        {
            Check("hidden-host-"+state,BrowserHostVisibility(false,state)==Visibility.Hidden);
            Check("visible-host-"+state,BrowserHostVisibility(true,state)==(state==WindowState.Minimized?Visibility.Hidden:Visibility.Visible));
        }
        var testSession=new JotSession(true,Path.Combine(testOutput,"browser-host-visibility")){ExerciseLifecycle=true};
        try
        {
            await testSession.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window"}));
            var note=await testSession.NewNote();
            var home=testSession.Home();
            var image=testSession.Image("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jV+YAAAAASUVORK5CYII=");
            foreach(var window in new[]{home,note,image})
            {
                await window.WaitFor("window.jotReady===true");
                window.UpdateLayout();
                double width=window.Browser.ActualWidth,height=window.Browser.ActualHeight;
                // Reflection is confined to this test: verify the supported WPF
                // visibility property reaches the SDK's actual native controller.
                var sdkBase=typeof(WebView2CompositionControl).GetField("m_webview2Base",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window.Browser)!;
                var controller=(CoreWebView2Controller)sdkBase.GetType().GetProperty("CoreWebView2Controller",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(sdkBase)!;
                Check(window.Mode+"-zero-opacity-offscreen-fixture-still-renders",window.Opacity==0&&window.Browser.Visibility==Visibility.Visible&&controller.IsVisible);
                window.WindowState=WindowState.Minimized;window.UpdateLayout();
                Check(window.Mode+"-native-minimize-hides-controller",window.IsVisible&&window.Browser.Visibility==Visibility.Hidden&&!controller.IsVisible);
                Check(window.Mode+"-minimize-does-not-collapse-editor-layout",window.Browser.ActualWidth==width&&window.Browser.ActualHeight==height);
                window.WindowState=WindowState.Normal;window.UpdateLayout();
                Check(window.Mode+"-restore-reveals-controller-without-reload",window.Browser.Visibility==Visibility.Visible&&controller.IsVisible&&await window.Script("window.jotReady===true")=="true");
                window.Hide();window.UpdateLayout();
                Check(window.Mode+"-hidden-host-hides-controller",window.Browser.Visibility==Visibility.Hidden&&!controller.IsVisible);
                window.Reveal();window.UpdateLayout();
                Check(window.Mode+"-reshow-preserves-layout",window.Browser.Visibility==Visibility.Visible&&controller.IsVisible&&window.Browser.ActualWidth==width&&window.Browser.ActualHeight==height);
            }
            await note.Script("editor.innerHTML='<p>Restored </p>';editor.focus();const range=document.createRange();range.selectNodeContents(editor.firstElementChild);range.collapse(false);getSelection().removeAllRanges();getSelection().addRange(range)");
            await note.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text="typing"}));
            Check("typing-resumes-after-minimize-and-hide",await note.Script("editor.textContent==='Restored typing'")=="true");
            Check("all-fixtures-remain-isolated",testSession.Windows.All(window=>window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.Left< -10000&&window.Top< -10000&&window.RuntimeErrors.Count==0));
        }
        finally
        {
            foreach(var window in testSession.Windows.ToArray())window.ClosePermanently();
            testSession.DisposeWebViewInputProtection();
        }
    }
}
