using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using PixelBounds = System.Drawing.Rectangle;

namespace Jot;

public partial class MainWindow
{
    internal bool IsWindowFullscreen { get; private set; }
    internal bool IsFullscreenTransitioning { get; private set; }
    internal bool IsImageFullscreen=>Mode=="image"&&IsWindowFullscreen;
    internal bool IsNoteFullscreen=>Mode=="note"&&IsWindowFullscreen;
    internal readonly List<PixelBounds> FullscreenFrameSamples=[];
    private readonly SemaphoreSlim fullscreenGate=new(1,1);
    private SavedNoteWindow? fullscreenNoteLayout;
    private PixelBounds fullscreenRestorePixels;
    private Rect fullscreenRestoreBounds;
    private WindowState fullscreenRestoreState;
    private ResizeMode fullscreenRestoreResize;
    private Effect? fullscreenRestoreEffect;
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SetWindowPos(nint hwnd,nint insertAfter,int x,int y,int width,int height,uint flags);

    private Task<bool> SetImageFullscreen(bool enabled)
    {
        if(Mode!="image")throw new InvalidOperationException("Fullscreen is available in the image viewer.");
        return SetWindowFullscreen(enabled);
    }
    private Task<bool> SetNoteFullscreen(bool enabled)
    {
        if(Mode!="note")throw new InvalidOperationException("Only notes can use note fullscreen.");
        return SetWindowFullscreen(enabled);
    }
    internal PixelBounds ReadWindowPixels()
    {
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==0||!GetNoteRect(handle,out var rect))throw new InvalidOperationException("Could not read the window bounds.");
        return PixelBounds.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
    }
    private void MoveFullscreenFrame(PixelBounds bounds,bool fullscreen)
    {
        if(testing&&(bounds.Right>=-10000||bounds.Bottom>=-10000||Topmost))
            throw new InvalidOperationException("Refusing a fullscreen test on the desktop.");
        double normal=fullscreen?0:1;
        Surface.Margin=new Thickness(8*normal);Surface.CornerRadius=new CornerRadius(6*normal);Surface.BorderThickness=new Thickness(normal);
        Surface.Effect=normal==1?fullscreenRestoreEffect:null;Browser.Margin=new Thickness(9*normal);Browser.Clip=null;
        var handle=new WindowInteropHelper(this).Handle;
        if(!SetWindowPos(handle,0,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0004|0x0010|0x0020))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not resize the note window.");
        // Native resize schedules layout; use its known size for clipping.
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);
        if(normal>0)Browser.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,Math.Max(0,bounds.Width/dpi.DpiScaleX-18*normal),Math.Max(0,bounds.Height/dpi.DpiScaleY-18*normal)),5*normal,5*normal);
        if(testing)FullscreenFrameSamples.Add(ReadWindowPixels());
    }
    private async Task<bool> SetWindowFullscreen(bool enabled,PixelBounds? dragBounds=null)
    {
        if(closingPermanently)return IsWindowFullscreen;
        var token=windowLifetime.Token;
        await fullscreenGate.WaitAsync(token);
        bool previous=IsWindowFullscreen;
        var start=default(PixelBounds);
        try
        {
            if(closingPermanently||enabled==IsWindowFullscreen)return IsWindowFullscreen;
            start=ReadWindowPixels();
            if(enabled)
            {
                fullscreenRestoreState=WindowState;
                // First obtain the real normal rectangle even if Windows had
                // maximized the note. Notes restore small, never to a maximized shell.
                if(WindowState!=WindowState.Normal){WindowState=WindowState.Normal;UpdateLayout();}
                fullscreenRestorePixels=ReadWindowPixels();
                fullscreenRestoreBounds=new Rect(Left,Top,Width,Height);
                fullscreenRestoreResize=ResizeMode;fullscreenRestoreEffect=Surface.Effect;
                if(Mode=="note")fullscreenNoteLayout=CaptureNoteLayout();
            }
            IsFullscreenTransitioning=true;IsWindowFullscreen=enabled;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Normal;
            var target=enabled?(testing?new PixelBounds(-32000,-32000,1200,800):System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds):dragBounds??fullscreenRestorePixels;
            if(testing)FullscreenFrameSamples.Clear();
            if(!enabled)
            {
                // WPF may retain the old desired Width/Height after a native
                // resize. Restore its properties AND explicitly set the HWND.
                Left=fullscreenRestoreBounds.Left;Top=fullscreenRestoreBounds.Top;Width=fullscreenRestoreBounds.Width;Height=fullscreenRestoreBounds.Height;
                ResizeMode=fullscreenRestoreResize;
                MoveFullscreenFrame(target,false);
                if(Mode=="image")WindowState=fullscreenRestoreState;
                fullscreenNoteLayout=null;
            }
            else MoveFullscreenFrame(target,true);
            if(Mode=="note")Post(new{@event="note-fullscreen",enabled=IsNoteFullscreen});
            return IsWindowFullscreen;
        }
        catch(OperationCanceledException) when(closingPermanently){return IsWindowFullscreen;}
        catch
        {
            if(!closingPermanently&&start.Width>0)
            {
                IsWindowFullscreen=previous;
                try{MoveFullscreenFrame(start,previous);}catch(Exception error){session.Log.Error("fullscreen-rollback",error,Mode);}
                ResizeMode=previous?ResizeMode.NoResize:fullscreenRestoreResize;
                if(!previous)fullscreenNoteLayout=null;
            }
            throw;
        }
        finally{IsFullscreenTransitioning=false;if(!closingPermanently){UpdateLayout();RoundWindow();}fullscreenGate.Release();}
    }
}
