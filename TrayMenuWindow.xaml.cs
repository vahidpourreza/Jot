using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;

namespace Jot;

// Lightweight custom chrome: no native context menu and no extra WebView process.
public partial class TrayMenuWindow : Window
{
    private readonly bool testing;
    private readonly Func<string,Task> dispatch;
    private readonly ErrorLog log;
    private readonly Dictionary<string,(Button Button,TextBlock Label,string Text)> actions=[];
    internal bool Busy { get; private set; }
    internal Task LastAction { get; private set; } = Task.CompletedTask;
    internal string Theme { get; private set; } = "dark";
    private bool disposed;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle,nint after,int x,int y,int width,int height,uint flags);

    internal TrayMenuWindow(bool testing,Func<string,Task> dispatch,ErrorLog log)
    {
        this.testing=testing;this.dispatch=dispatch;this.log=log;
        InitializeComponent();
        if(testing){Left=Top=-32000;ShowActivated=false;Opacity=0;}
        using var stream=new MemoryStream(AppIcon.LoadColor());
        var bitmap=new IconBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames.MaxBy(frame=>frame.PixelWidth)!;
        bitmap.Freeze();BrandIcon.Source=bitmap;
        AddAction("home","All notes","M4,5 L20,5 M4,12 L20,12 M4,19 L16,19");
        AddAction("new-note","New note","M5,12 L19,12 M12,5 L12,19","Ctrl+N");
        Separator();
        AddAction("settings","Settings","M4,7 L10,7 M16,7 L20,7 M4,17 L8,17 M14,17 L20,17 M16,7 A3,3 0 1 1 10,7 A3,3 0 1 1 16,7 M14,17 A3,3 0 1 1 8,17 A3,3 0 1 1 14,17");
        AddAction("tray-visibility","Tray icon visibility…","M4,15 L12,7 L20,15");
        actions["tray-visibility"].Button.ToolTip="Windows controls the overflow area. Drag Jot beside the clock, or enable it in Taskbar settings.";
        Separator();
        AddAction("quit","Quit Jot","M12,2 L12,11 M6,6 A8,8 0 1 0 18,6");
    }
    private void Separator()
    {
        var line=new Border{Height=1,Margin=new Thickness(8,5,8,5)};
        line.SetResourceReference(Border.BackgroundProperty,"TrayBorder");Actions.Children.Add(line);
    }
    private void AddAction(string key,string text,string geometry,string shortcut="")
    {
        var button=new Button{Tag=key,Style=(Style)Resources["TrayActionStyle"]};
        AutomationProperties.SetName(button,text);
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
        row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var icon=new ShapePath{Data=Geometry.Parse(geometry),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
        icon.SetResourceReference(ShapePath.StrokeProperty,"TrayMuted");
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(icon);
        var box=new Viewbox{Child=canvas,Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Left};
        var label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,1);
        var hint=new TextBlock{Text=shortcut,FontSize=10,VerticalAlignment=VerticalAlignment.Center};hint.SetResourceReference(TextBlock.ForegroundProperty,"TrayMuted");Grid.SetColumn(hint,2);
        row.Children.Add(box);row.Children.Add(label);row.Children.Add(hint);button.Content=row;
        button.Click+=(_,_)=>LastAction=InvokeAction(key);
        actions.Add(key,(button,label,text));Actions.Children.Add(button);
    }
    internal Button ActionButton(string key)=>actions[key].Button;
    internal void ApplyTheme(string theme)
    {
        Theme=theme=="light"?"light":"dark";
        string[] keys=["TraySurface","TrayText","TrayMuted","TrayBorder","TrayHover","TrayFocus"];
        string[] values=Theme=="light"?["#FFFFFF","#262626","#737373","#E5E5E5","#F5F5F5","#A3A3A3"]:["#262626","#FAFAFA","#A3A3A3","#42808080","#3B3B3B","#A3A3A3"];
        for(int i=0;i<keys.Length;i++)Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
    }
    internal static System.Drawing.Point Place(System.Drawing.Point anchor,System.Drawing.Rectangle area,int width,int height)
        => new(Math.Clamp(anchor.X-width,area.Left,Math.Max(area.Left,area.Right-width)),Math.Clamp(anchor.Y-height,area.Top,Math.Max(area.Top,area.Bottom-height)));
    internal void OpenAt(System.Drawing.Point anchor)
    {
        if(disposed)return;
        if(!Busy)MenuError.Visibility=Visibility.Collapsed;
        // First layout offscreen, then use actual native pixel dimensions. This
        // handles scaling/negative monitor origins without mixing DIPs and pixels.
        if(!testing)Left=Top=-32000;
        Show();UpdateLayout();
        if(!testing)
        {
            var handle=new WindowInteropHelper(this).Handle;
            GetWindowRect(handle,out var rect);
            var area=System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
            var point=Place(anchor,area,rect.Right-rect.Left,rect.Bottom-rect.Top);
            SetWindowPos(handle,0,point.X,point.Y,0,0,0x0001|0x0004|0x0010);
            // Crossing monitors can change the window's DPI and physical size.
            UpdateLayout();GetWindowRect(handle,out rect);
            point=Place(anchor,area,rect.Right-rect.Left,rect.Bottom-rect.Top);
            SetWindowPos(handle,0,point.X,point.Y,0,0,0x0001|0x0004|0x0010);
            Activate();
            actions["home"].Button.Focus();
            if(SystemParameters.ClientAreaAnimation)
                MenuSurface.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(110)));
        }
    }
    internal async Task InvokeAction(string key)
    {
        if(Busy||disposed||!actions.TryGetValue(key,out var item))return;
        bool pending=key is "new-note" or "quit";
        Busy=true;
        foreach(var action in actions.Values)action.Button.IsEnabled=false;
        if(pending)
        {
            item.Label.Text=key=="quit"?"Closing…":"Creating…";
            Shortcut.Visibility=Visibility.Collapsed;ProgressRing.Visibility=Visibility.Visible;
            ((RotateTransform)ProgressRing.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(700)){RepeatBehavior=RepeatBehavior.Forever});
        }
        try { await dispatch(key);if(!disposed)Hide(); }
        catch(Exception error)
        {
            log.Error("tray-"+key,error);
            if(!disposed)
            {
                MenuError.Text=key=="quit"?"Could not save all notes. Jot is still running. Please try again.":"This action could not finish. Please try again.";
                MenuError.Visibility=Visibility.Visible;
                // Never steal focus after the user has dismissed a busy menu.
                if(IsVisible)item.Button.Focus();
            }
        }
        finally
        {
            Busy=false;item.Label.Text=item.Text;
            foreach(var action in actions.Values)action.Button.IsEnabled=true;
            ((RotateTransform)ProgressRing.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,null);
            ProgressRing.Visibility=Visibility.Collapsed;Shortcut.Visibility=Visibility.Visible;
            if(!testing&&!disposed&&IsVisible&&MenuError.Visibility==Visibility.Visible)item.Button.Focus();
        }
    }
    internal void Dismiss(){if(!disposed)Hide();}
    private void OnDeactivated(object? sender,EventArgs e)=>Dismiss();
    private void OnKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){Dismiss();e.Handled=true;return;}
        if(Busy)return;
        if(e.Key==Key.N&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){LastAction=InvokeAction("new-note");e.Handled=true;return;}
        if(e.Key is not (Key.Down or Key.Up or Key.Home or Key.End))return;
        var buttons=actions.Values.Select(action=>action.Button).ToArray();
        var index=Array.FindIndex(buttons,button=>button.IsKeyboardFocused);
        int next=e.Key switch{Key.Home=>0,Key.End=>buttons.Length-1,Key.Up=>(index+buttons.Length-1)%buttons.Length,_=>(index+1)%buttons.Length};
        buttons[next].Focus();e.Handled=true;
    }
    protected override void OnClosed(EventArgs e){disposed=true;base.OnClosed(e);}
}
