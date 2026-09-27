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
    private bool positioning;
    private System.Drawing.Point? lastAnchor;
    private System.Drawing.Rectangle? lastIconBounds;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint handle,uint message,nint wParam,nint lParam);

    internal TrayMenuWindow(bool testing,Func<string,Task> dispatch,ErrorLog log)
    {
        this.testing=testing;this.dispatch=dispatch;this.log=log;
        InitializeComponent();
        SizeChanged+=(_,_)=>{if(!testing&&IsVisible&&!positioning)PositionAtAnchor();};
        if(testing){Left=Top=-32000;ShowActivated=false;Opacity=0;}
        using var stream=new MemoryStream(AppIcon.LoadColor());
        var bitmap=new IconBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames.MaxBy(frame=>frame.PixelWidth)!;
        bitmap.Freeze();BrandIcon.Source=bitmap;
        AddAction("home","Home","M3,10 L12,3 L21,10 M5,9 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9");
        AddAction("new-note","New note","M5,12 L19,12 M12,5 L12,19");
        AddAction("settings","Settings","M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12");
        Separator();
        AddAction("quit","Quit Jot","M12,2 L12,11 M6,6 A8,8 0 1 0 18,6");
    }
    private void Separator()
    {
        var line=new Border{Height=1,Margin=new Thickness(8,5,8,5)};
        line.SetResourceReference(Border.BackgroundProperty,"TrayBorder");Actions.Children.Add(line);
    }
    private void AddAction(string key,string text,string geometry)
    {
        var button=new Button{Tag=key,Style=(Style)Resources["TrayActionStyle"]};
        AutomationProperties.SetName(button,text);
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var icon=new ShapePath{Data=Geometry.Parse(geometry),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
        icon.SetResourceReference(ShapePath.StrokeProperty,"TrayMuted");
        var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(icon);
        var box=new Viewbox{Child=canvas,Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Left};
        var label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,1);
        row.Children.Add(box);row.Children.Add(label);button.Content=row;
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
    private void PositionAtAnchor()
    {
        if(lastAnchor is not { } anchor||disposed||testing)return;
        MoveMenuToIcon(lastIconBounds??new(anchor.X,anchor.Y,1,1),System.Windows.Forms.Screen.FromPoint(anchor).Bounds);
    }
    private void MoveMenuToIcon(System.Drawing.Rectangle icon,System.Drawing.Rectangle screen)
    {
        positioning=true;
        try
        {
            var handle=new WindowInteropHelper(this).Handle;
            for(int pass=0;pass<3;pass++)
            {
                UpdateLayout();if(!GetWindowRect(handle,out var window))return;
                var card=MenuPixelBounds();if(card.Width<=0||card.Height<=0)return;
                var desired=PlaceAtIcon(icon,screen,card.Width,card.Height);
                // Use the measured WPF border offset, not an assumed shadow/DPI
                // margin. Both the visible corner and SetWindowPos use pixels.
                SetWindowPos(handle,0,desired.X-(card.Left-window.Left),desired.Y-(card.Top-window.Top),0,0,0x0001|0x0004|0x0010);
            }
        }
        finally{positioning=false;}
    }
    internal System.Drawing.Rectangle MenuPixelBounds()
    {
        var first=MenuSurface.PointToScreen(new Point(0,0));
        var last=MenuSurface.PointToScreen(new Point(MenuSurface.ActualWidth,MenuSurface.ActualHeight));
        return System.Drawing.Rectangle.FromLTRB((int)Math.Round(first.X),(int)Math.Round(first.Y),(int)Math.Round(last.X),(int)Math.Round(last.Y));
    }
    internal void PositionOffscreenForTest(System.Drawing.Rectangle icon,System.Drawing.Rectangle screen)
    {
        if(!testing||Opacity!=0||ShowActivated||ShowInTaskbar||Topmost||screen.Right>=-10000||screen.Bottom>=-10000||!screen.Contains(icon))
            throw new InvalidOperationException("Refusing a positioning test on the desktop.");
        MoveMenuToIcon(icon,screen);
    }
    internal static System.Drawing.Point PlaceAtIcon(System.Drawing.Rectangle icon,System.Drawing.Rectangle screen,int width,int height)
    {
        // Exact requested anchor: visible menu bottom-right = visible icon
        // top-left. No gap, centering, work-area offset, or right-edge alignment.
        int x=icon.Left-width,y=icon.Top-height;
        if(x<screen.Left)x=icon.Right;
        if(y<screen.Top)y=icon.Bottom;
        return new(Math.Clamp(x,screen.Left,Math.Max(screen.Left,screen.Right-width)),Math.Clamp(y,screen.Top,Math.Max(screen.Top,screen.Bottom-height)));
    }
    internal void OpenAt(System.Drawing.Point anchor,System.Drawing.Rectangle? iconBounds=null)
    {
        if(disposed)return;
        lastAnchor=anchor;
        lastIconBounds=iconBounds;
        FocusManager.SetFocusedElement(this,TrayRoot);
        if(!Busy)MenuError.Visibility=Visibility.Collapsed;
        // First layout offscreen, then use actual native pixel dimensions. This
        // handles scaling/negative monitor origins without mixing DIPs and pixels.
        if(!testing){Opacity=0;Left=Top=-32000;Topmost=true;}
        Show();UpdateLayout();
        if(!testing)
        {
            PositionAtAnchor();Opacity=1;
            var handle=new WindowInteropHelper(this).Handle;
            SetForegroundWindow(handle);Activate();PostMessage(handle,0,0,0);
            // Mouse invocation should not paint a keyboard-selection outline.
            Keyboard.Focus(TrayRoot);
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
            ProgressRing.Visibility=Visibility.Visible;
            ((RotateTransform)ProgressRing.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(700)){RepeatBehavior=RepeatBehavior.Forever});
        }
        try { await dispatch(key);if(!disposed)Dismiss(); }
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
            ProgressRing.Visibility=Visibility.Collapsed;
            if(!testing&&!disposed&&IsVisible&&MenuError.Visibility==Visibility.Visible)item.Button.Focus();
        }
    }
    internal void Dismiss(){if(!disposed){Hide();if(!testing){Topmost=false;PostMessage(new WindowInteropHelper(this).Handle,0,0,0);}}}
    private void OnDeactivated(object? sender,EventArgs e)=>Dismiss();
    private void OnKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){Dismiss();e.Handled=true;return;}
        if(Busy)return;
        if(e.Key is not (Key.Down or Key.Up or Key.Home or Key.End))return;
        var buttons=actions.Values.Select(action=>action.Button).ToArray();
        var index=Array.FindIndex(buttons,button=>button.IsKeyboardFocused);
        int next=e.Key switch{Key.Home=>0,Key.End=>buttons.Length-1,Key.Up=>index<0?buttons.Length-1:(index+buttons.Length-1)%buttons.Length,_=>(index+1)%buttons.Length};
        buttons[next].Focus();e.Handled=true;
    }
    protected override void OnClosed(EventArgs e){disposed=true;base.OnClosed(e);}
}
