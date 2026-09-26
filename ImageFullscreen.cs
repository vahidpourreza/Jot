using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Effects;

namespace Jot;

public partial class MainWindow
{
    internal bool IsImageFullscreen { get; private set; }
    private Rect imageRestoreBounds;
    private WindowState imageRestoreState;
    private ResizeMode imageRestoreResize;
    private Effect? imageRestoreEffect;
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SetWindowPos(nint hwnd,nint insertAfter,int x,int y,int width,int height,uint flags);

    private bool SetImageFullscreen(bool enabled)
    {
        if(Mode!="image")throw new InvalidOperationException("Fullscreen is available in the image viewer.");
        if(closingPermanently||enabled==IsImageFullscreen)return IsImageFullscreen;
        if(enabled)
        {
            imageRestoreState=WindowState;
            imageRestoreBounds=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;
            imageRestoreResize=ResizeMode;imageRestoreEffect=Surface.Effect;
            IsImageFullscreen=true;WindowState=WindowState.Normal;ResizeMode=ResizeMode.NoResize;
            Surface.Margin=new Thickness(0);Surface.CornerRadius=new CornerRadius(0);Surface.BorderThickness=new Thickness(0);Surface.Effect=null;
            Browser.Margin=new Thickness(0);Browser.Clip=null;
            if(testing)
            {
                // Never touch a real monitor's work area, focus or taskbar in a test.
                Left=-32000;Top=-32000;Width=1200;Height=800;
            }
            else
            {
                var handle=new WindowInteropHelper(this).Handle;
                var bounds=System.Windows.Forms.Screen.FromHandle(handle).Bounds;
                // Win32 uses physical monitor pixels, avoiding mixed-DPI coordinate scaling.
                if(!SetWindowPos(handle,0,bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0004|0x0010|0x0020))
                {
                    int code=Marshal.GetLastWin32Error();SetImageFullscreen(false);
                    throw new System.ComponentModel.Win32Exception(code,"Could not enter fullscreen.");
                }
            }
        }
        else
        {
            IsImageFullscreen=false;WindowState=WindowState.Normal;
            Surface.Margin=new Thickness(8);Surface.CornerRadius=new CornerRadius(6);Surface.BorderThickness=new Thickness(1);Surface.Effect=imageRestoreEffect;
            Browser.Margin=new Thickness(9);ResizeMode=imageRestoreResize;
            Left=imageRestoreBounds.Left;Top=imageRestoreBounds.Top;Width=imageRestoreBounds.Width;Height=imageRestoreBounds.Height;
            WindowState=imageRestoreState;RoundWindow();
        }
        return IsImageFullscreen;
    }
}
