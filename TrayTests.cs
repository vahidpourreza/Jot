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
        var otherHeight=await b.Script("model.prefs.lineHeight");
        await GoToSettings();
        var defaultHeight=await Script("prefs.lineHeight");
        foreach(var height in new[]{1.2,1.95,2.5})
        {
            await a.Script("setPreference({lineHeight:"+JsonSerializer.Serialize(height)+"}).then(()=>window.spacingReady="+JsonSerializer.Serialize(height)+")");
            await a.WaitFor("window.spacingReady==="+JsonSerializer.Serialize(height));
            checks.Add(new{name="line-height-independent-and-rendered-"+height,passed=await a.Script("Math.abs(parseFloat(getComputedStyle(editor).lineHeight)/parseFloat(getComputedStyle(editor).fontSize)-model.prefs.lineHeight)<.01")=="true"&&await a.Script("editor.innerHTML")==originalHtml&&await b.Script("model.prefs.lineHeight")==otherHeight&&await Script("prefs.lineHeight")==defaultHeight});
        }
        checks.Add(new{name="line-height-maximum-disables-plus-without-spinner",passed=await a.Script("document.getElementById('looserLinesButton').disabled&&document.getElementById('looserLinesButton').getAttribute('aria-busy')!=='true'")=="true"});
        checks.Add(new{name="global-writing-controls-removed",passed=await Script("!document.querySelector('#homeTighterLines,#homeLooserLines,#homeFontSize,#toolbarVisible,[data-weight]')")=="true"});
        await a.ClickControl("#menuButton");await a.ClickControl("#tighterLinesButton");await a.WaitFor("model.prefs.lineHeight===2.2");await a.ClickControl("#tighterLinesButton");await a.WaitFor("model.prefs.lineHeight===1.95");
        checks.Add(new{name="note-line-height-controls-do-not-change-global-defaults",passed=await Script("prefs.lineHeight==="+defaultHeight)=="true"});
        await a.Capture("line-height-more");await a.Script("closePanels()");
        await a.Reload();checks.Add(new{name="line-height-survives-editor-reload",passed=await a.Script("model.prefs.lineHeight===1.95")=="true"});
        bool badHeightRejected=false;
        try{await store.SavePreferences(JsonSerializer.SerializeToElement(new{lineHeight=0.4}));}catch(InvalidDataException){badHeightRejected=true;}
        checks.Add(new{name="invalid-line-height-rejected-without-changing-preferences",passed=badHeightRejected&&(await store.Load())!.Value.GetProperty("prefs").GetProperty("lineHeight").GetRawText()==defaultHeight});
        checks.Add(new{name="settings-has-no-windows-link",passed=await Script("!document.getElementById('trayVisibility')&&!document.body.textContent.includes('Windows settings')")=="true"});
        await GoToIndex();
        await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
        var menu=session.TrayMenu!;
        checks.Add(new{name="custom-tray-menu-is-lightweight-offscreen-and-branded",passed=menu.IsVisible&&!menu.ShowInTaskbar&&!menu.Topmost&&!menu.ShowActivated&&menu.Opacity==0&&menu.Left< -10000&&menu.BrandIcon.Source is BitmapSource source&&source.PixelWidth>=128&&menu.ActionButton("settings").Template is not null});
        checks.Add(new{name="tray-menu-compact-four-actions-without-shortcut-labels",passed=menu.ActualWidth==212&&menu.ActualHeight<=210&&menu.Actions.Children.OfType<Button>().Count()==4&&menu.FindName("Shortcut") is null&&menu.Actions.Children.OfType<Button>().All(button=>button.Height==32&&!button.IsKeyboardFocused)});
        FocusManager.SetFocusedElement(menu,menu.ActionButton("settings"));menu.Dismiss();
        await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
        checks.Add(new{name="tray-reopen-clears-previous-settings-selection",passed=ReferenceEquals(FocusManager.GetFocusedElement(menu),menu.TrayRoot)&&menu.Actions.Children.OfType<Button>().All(button=>!button.IsKeyboardFocused)});
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
                    int width=(int)(212*scale),height=(int)(210*scale);var point=TrayMenuWindow.PlaceAtIcon(new(anchor.X,anchor.Y,16,16),area,width,height);
                    placed&=point.X>=area.Left&&point.Y>=area.Top&&point.X+width<=area.Right&&point.Y+height<=area.Bottom;
                }
        checks.Add(new{name="tray-menu-placement-clamps-across-monitor-origins-and-scales",passed=placed});
        VerifyTrayEdges(checks);
        VerifyTrayOverflow(checks);
        await VerifyTrayNativeAnchor(checks);
        menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        checks.Add(new{name="tray-menu-escape-dismisses",passed=!menu.IsVisible});

        async Task Click(string key)
        {
            await session.ShowTrayMenu(new System.Drawing.Point(1500,900));
            menu.ActionButton(key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await menu.LastAction;
        }
        int homeCount=session.Windows.Count(window=>window.Mode=="home");
        await Click("settings");await WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='settings'");
        checks.Add(new{name="tray-settings-reuses-index-window",passed=IsSettingsView&&session.Windows.Count(window=>window.Mode=="home")==homeCount&&!menu.IsVisible});
        await Click("home");await WaitFor("window.jotReady===true&&window.JotWorkspace?.view==='home'");
        checks.Add(new{name="tray-notes-reveals-existing-index",passed=!IsSettingsView&&session.Windows.Count(window=>window.Mode=="home")==homeCount});
        var existingIds=session.Windows.Select(window=>window.NoteId).ToHashSet();
        await Click("new-note");var fresh=session.Windows.Single(window=>window.Mode=="note"&&!existingIds.Contains(window.NoteId));
        await fresh.WaitFor("window.jotReady===true");checks.Add(new{name="tray-new-note-creates-independent-editor",passed=fresh.NoteId is not null&&!fresh.Topmost});
        fresh.ClosePermanently();await store.Delete(fresh.NoteId!);await session.Changed(false);
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
        await a.Script("setAppTheme("+JsonSerializer.Serialize(originalTheme)+").then(()=>setPreference({lineHeight:"+JsonSerializer.Serialize(originalHeight)+"})).then(()=>window.trayTestsRestored=true)");
        await a.WaitFor("window.trayTestsRestored===true");
    }
    private static void VerifyTrayEdges(List<object> checks)
    {
        using var icon=new System.Windows.Forms.NotifyIcon();
        checks.Add(new{name="tray-net10-identity-adapter-available-without-registering-icon",passed=!icon.Visible&&TrayIconBounds.TryIdentify(icon,out var identifier)&&identifier.Id>0&&identifier.Window==0});
        checks.Add(new{name="tray-unregistered-icon-has-safe-click-fallback",passed=TrayIconBounds.Resolve(icon,new(123,456))==new System.Drawing.Rectangle(123,456,1,1)});
        foreach(var origin in new[]{new System.Drawing.Point(0,0),new System.Drawing.Point(-1920,40),new System.Drawing.Point(1920,-1440)})
        foreach(var scale in new[]{1.0,1.25,1.5,2.0})
        {
            var screen=new System.Drawing.Rectangle(origin.X,origin.Y,1920,1080);
            int width=(int)(200*scale),height=(int)(182*scale),side=(int)(16*scale);
            foreach(var bounds in new[]{new System.Drawing.Rectangle(screen.Right-200,screen.Bottom-30,side,side),new System.Drawing.Rectangle(screen.Left+2,screen.Top+2,side,side),new System.Drawing.Rectangle(screen.Right-30,screen.Top+2,side,side),new System.Drawing.Rectangle(screen.Left+2,screen.Bottom-30,side,side)})
            {
                var p=TrayMenuWindow.PlaceAtIcon(bounds,screen,width,height);var menu=new System.Drawing.Rectangle(p,new(width,height));
                bool atCorner=(bounds.Left-width<screen.Left?menu.Left==bounds.Right:menu.Right==bounds.Left)&&(bounds.Top-height<screen.Top?menu.Top==bounds.Bottom:menu.Bottom==bounds.Top);
                checks.Add(new{name=$"tray-exact-corner-or-edge-flip-{origin.X}-{origin.Y}-{scale}-{bounds.X}-{bounds.Y}",passed=screen.Contains(menu)&&!menu.IntersectsWith(bounds)&&atCorner});
            }
        }
    }
    private static void VerifyTrayOverflow(List<object> checks)
    {
        foreach(var scale in new[]{1.0,1.25,1.5,2.0})
        {
            int side=(int)(16*scale);var alpha=new System.Drawing.Rectangle(1,1,14,14);
            foreach(bool overflow in new[]{false,true})
            {
                var cell=overflow?new System.Drawing.Rectangle(1115,820,(int)(40*scale),(int)(40*scale)):new System.Drawing.Rectangle(1144,860,(int)(24*scale),(int)(40*scale));
                var visible=TrayIconBounds.VisibleBounds(cell,new(side,side),alpha,new(16,16));
                var point=TrayMenuWindow.PlaceAtIcon(visible,new(0,0,1920,1200),200,182);
                checks.Add(new{name=$"tray-removes-cell-and-transparent-icon-padding-{scale}-{overflow}",passed=point.X+200==visible.Left&&point.Y+182==visible.Top&&visible.Left>cell.Left&&visible.Top>cell.Top});
            }
        }
        using var bitmap=new System.Drawing.Bitmap(16,16);
        bitmap.SetPixel(2,3,System.Drawing.Color.White);bitmap.SetPixel(12,14,System.Drawing.Color.White);
        checks.Add(new{name="tray-alpha-bounds-exclude-transparent-border",passed=TrayIconBounds.InkBounds(bitmap)==new System.Drawing.Rectangle(2,3,11,12)});
    }
    private async Task VerifyTrayNativeAnchor(List<object> checks)
    {
        var menu=new TrayMenuWindow(true,_=>Task.CompletedTask,session.Log);
        var measurements=new List<object>();
        try
        {
            menu.OpenAt(new System.Drawing.Point(-30400,-31100));
            foreach(var scale in new[]{1.0,1.25,1.5,2.0})
            foreach(var theme in new[]{"dark","light"})
            {
                menu.ApplyTheme(theme);menu.TrayRoot.LayoutTransform=new ScaleTransform(scale,scale);menu.Width=212*scale;menu.UpdateLayout();await Task.Delay(30);
                var icon=new System.Drawing.Rectangle(-30400,-31100,16,16);
                menu.PositionOffscreenForTest(icon,new(-32000,-32000,1920,1080));menu.UpdateLayout();
                var card=menu.MenuPixelBounds();int dx=card.Right-icon.Left,dy=card.Bottom-icon.Top;
                measurements.Add(new{scale,theme,iconX=icon.Left,iconY=icon.Top,menuRight=card.Right,menuBottom=card.Bottom,dx,dy});
                checks.Add(new{name=$"tray-real-window-visible-corner-meets-icon-{scale}-{theme}",passed=dx==0&&dy==0&&menu.Left< -10000&&menu.Top< -10000&&menu.Opacity==0&&!menu.Topmost&&!menu.ShowActivated&&!menu.ShowInTaskbar});
                // Growing error text must keep exactly the same anchored corner.
                menu.MenuError.Text="A test error that expands this menu and must not move its anchor.";menu.MenuError.Visibility=Visibility.Visible;menu.UpdateLayout();
                menu.PositionOffscreenForTest(icon,new(-32000,-32000,1920,1080));card=menu.MenuPixelBounds();
                checks.Add(new{name=$"tray-real-window-expanded-menu-keeps-anchor-{scale}-{theme}",passed=card.Right==icon.Left&&card.Bottom==icon.Top});
                menu.MenuError.Visibility=Visibility.Collapsed;
            }
        }
        finally{menu.Close();}
        await File.WriteAllTextAsync(Path.Combine(testOutput,"tray-anchor-measurements.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
    }
}
