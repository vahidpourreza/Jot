using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyTrayAndLineHeight(List<object> checks,MainWindow a,MainWindow b)
    {
        var originalTheme=JsonSerializer.Deserialize<string>(await a.Script("model.prefs.theme"));
        var originalHeight=JsonSerializer.Deserialize<double>(await a.Script("model.prefs.lineHeight"));
        var originalHtml=await a.Script("editor.innerHTML");
        await GoToSettings();
        foreach(var height in new[]{1.2,1.95,2.5})
        {
            await a.Script("setPreference({lineHeight:"+JsonSerializer.Serialize(height)+"}).then(()=>window.spacingReady="+JsonSerializer.Serialize(height)+")");
            await a.WaitFor("window.spacingReady==="+JsonSerializer.Serialize(height));
            await b.WaitFor("model.prefs.lineHeight==="+JsonSerializer.Serialize(height));
            await WaitFor("prefs.lineHeight==="+JsonSerializer.Serialize(height));
            checks.Add(new{name="line-height-shared-and-rendered-"+height,passed=await a.Script("Math.abs(parseFloat(getComputedStyle(editor).lineHeight)/parseFloat(getComputedStyle(editor).fontSize)-model.prefs.lineHeight)<.01")=="true"&&await a.Script("editor.innerHTML")==originalHtml});
        }
        checks.Add(new{name="line-height-maximum-disables-plus-without-spinner",passed=await a.Script("document.getElementById('looserLinesButton').disabled&&document.getElementById('looserLinesButton').getAttribute('aria-busy')!=='true'")=="true"&&await Script("document.getElementById('homeLooserLines').disabled")=="true"});
        await ClickControl("#homeTighterLines");await a.WaitFor("model.prefs.lineHeight===2.2");
        await a.ClickControl("#menuButton");await a.ClickControl("#tighterLinesButton");await a.WaitFor("model.prefs.lineHeight===1.95");
        await WaitFor("prefs.lineHeight===1.95");
        checks.Add(new{name="line-height-controls-in-settings-and-more-work",passed=await Script("prefs.lineHeight===1.95")=="true"});
        await a.Capture("line-height-more");await a.Script("closePanels()");
        await a.Reload();checks.Add(new{name="line-height-survives-editor-reload",passed=await a.Script("model.prefs.lineHeight===1.95")=="true"});
        bool badHeightRejected=false;
        try{await store.SavePreferences(JsonSerializer.SerializeToElement(new{lineHeight=0.4}));}catch(InvalidDataException){badHeightRejected=true;}
        checks.Add(new{name="invalid-line-height-rejected-without-changing-preferences",passed=badHeightRejected&&(await store.Load())!.Value.GetProperty("prefs").GetProperty("lineHeight").GetDouble()==1.95});
        await ClickControl("#trayVisibility");checks.Add(new{name="settings-tray-visibility-opens-only-on-user-action",passed=session.TestTrayActions.Contains("tray-visibility")});
        await GoToIndex();
        await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
        var menu=session.TrayMenu!;
        checks.Add(new{name="custom-tray-menu-is-lightweight-offscreen-and-branded",passed=menu.IsVisible&&!menu.ShowInTaskbar&&!menu.Topmost&&!menu.ShowActivated&&menu.Opacity==0&&menu.Left< -10000&&menu.BrandIcon.Source is BitmapSource source&&source.PixelWidth>=128&&menu.ActionButton("settings").Template is not null});
        foreach(var mode in new[]{"light","dark"})
        {
            menu.ApplyTheme(mode);menu.UpdateLayout();
            var image=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(menu.TrayRoot);
            using(var output=File.Create(Path.Combine(testOutput,"tray-menu-"+mode+".png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(output);}
            var color=((SolidColorBrush)menu.MenuSurface.Background).Color;
            checks.Add(new{name="custom-tray-theme-"+mode,passed=mode=="light"?color.R==255:color.R==38});
        }
        bool placed=true;
        foreach(var area in new[]{new System.Drawing.Rectangle(0,0,1920,1040),new System.Drawing.Rectangle(-1600,40,1600,860),new System.Drawing.Rectangle(1920,-1200,2560,1440)})
            foreach(var anchor in new[]{area.Location,new System.Drawing.Point(area.Right,area.Bottom),new System.Drawing.Point(area.Left,area.Bottom)})
                foreach(var scale in new[]{1.0,1.5,2.0})
                {
                    int width=(int)(270*scale),height=(int)(302*scale);var point=TrayMenuWindow.Place(anchor,area,width,height);
                    placed&=point.X>=area.Left&&point.Y>=area.Top&&point.X+width<=area.Right&&point.Y+height<=area.Bottom;
                }
        checks.Add(new{name="tray-menu-placement-clamps-across-monitor-origins-and-scales",passed=placed});
        menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        checks.Add(new{name="tray-menu-escape-dismisses",passed=!menu.IsVisible});

        async Task Click(string key)
        {
            await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
            menu.ActionButton(key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await menu.LastAction;
        }
        int homeCount=session.Windows.Count(window=>window.Mode=="home");
        await Click("settings");await WaitFor("window.jotReady===true&&document.getElementById('settingsHandle')!==null");
        checks.Add(new{name="tray-settings-reuses-index-window",passed=IsSettingsView&&session.Windows.Count(window=>window.Mode=="home")==homeCount&&!menu.IsVisible});
        await Click("home");await WaitFor("window.jotReady===true&&document.getElementById('homeSearch')!==null");
        checks.Add(new{name="tray-notes-reveals-existing-index",passed=!IsSettingsView&&session.Windows.Count(window=>window.Mode=="home")==homeCount});
        var existingIds=session.Windows.Select(window=>window.NoteId).ToHashSet();
        await Click("new-note");var fresh=session.Windows.Single(window=>window.Mode=="note"&&!existingIds.Contains(window.NoteId));
        await fresh.WaitFor("window.jotReady===true");checks.Add(new{name="tray-new-note-creates-independent-editor",passed=fresh.NoteId is not null&&!fresh.Topmost});
        fresh.ClosePermanently();await store.Delete(fresh.NoteId!);await session.Changed(false);
        await Click("tray-visibility");checks.Add(new{name="tray-visibility-action-is-wired-without-opening-os-settings-in-tests",passed=session.TestTrayActions.Count(action=>action=="tray-visibility")>=2});
        await Click("quit");checks.Add(new{name="tray-quit-flushes-notes-through-shared-session",passed=session.TestTrayActions.Contains("quit")&&!menu.Busy});
        await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
        menu.Dismiss();checks.Add(new{name="tray-menu-dismissal-keeps-session-alive",passed=!menu.IsVisible&&session.Windows.Contains(a)&&session.Windows.Contains(b)});

        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureMenu=new TrayMenuWindow(true,_=>pending.Task,session.Log);
        failureMenu.OpenAt(new System.Drawing.Point(0,0));
        failureMenu.ActionButton("quit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks.Add(new{name="tray-quit-pending-shows-spinner-and-disabled-controls",passed=failureMenu.Busy&&failureMenu.ProgressRing.Visibility==Visibility.Visible&&!failureMenu.ActionButton("home").IsEnabled});
        pending.SetException(new IOException("Synthetic save failure"));await failureMenu.LastAction;
        checks.Add(new{name="tray-quit-failure-keeps-menu-usable-and-shows-error",passed=!failureMenu.Busy&&failureMenu.IsVisible&&failureMenu.MenuError.Visibility==Visibility.Visible&&failureMenu.ActionButton("quit").IsEnabled});
        failureMenu.Close();
        var corruptSession=new JotSession(true,Path.Combine(testOutput,"tray-corrupt-session"));
        Directory.CreateDirectory(corruptSession.Store.Root);
        await File.WriteAllTextAsync(corruptSession.Store.FilePath,"invalid-store");
        await corruptSession.ShowTrayMenu(new System.Drawing.Point(0,0));
        checks.Add(new{name="tray-menu-remains-available-when-preferences-cannot-load",passed=corruptSession.TrayMenu is {IsVisible:true} recoveryMenu&&recoveryMenu.ActionButton("quit").IsEnabled});
        corruptSession.TrayMenu?.Close();
        await a.Script("setPreference({lineHeight:"+JsonSerializer.Serialize(originalHeight)+",theme:"+JsonSerializer.Serialize(originalTheme)+"}).then(()=>window.trayTestsRestored=true)");
        await a.WaitFor("window.trayTestsRestored===true");
    }
}
