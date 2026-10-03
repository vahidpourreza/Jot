using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using PixelBounds=System.Drawing.Rectangle;

namespace Jot;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMinMaxInfo
    {
        public System.Drawing.Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;
    }
    [DllImport("user32.dll",EntryPoint="IsZoomed")]
    private static extern bool IsNativeMaximized(nint hwnd);
    private bool presentationInitialized,normalWindowBoundsKnown;
    internal bool WorkspacePinned { get; private set; }

    private void InitializeWindowPresentation()
    {
        if(presentationInitialized||Mode is not ("home" or "note"))return;
        presentationInitialized=true;
        fullscreenRestoreEffect=Surface.Effect;
        StateChanged+=OnNativeWindowStateChanged;
        LocationChanged+=OnNormalWindowLocationChanged;
        SizeChanged+=OnNormalWindowSizeChanged;
        RememberNormalWindowBounds();
    }
    private void StopWindowPresentation()
    {
        StateChanged-=OnNativeWindowStateChanged;
        LocationChanged-=OnNormalWindowLocationChanged;
        SizeChanged-=OnNormalWindowSizeChanged;
    }
    private void OnNormalWindowLocationChanged(object? sender,EventArgs args)=>RememberNormalWindowBounds();
    private void OnNormalWindowSizeChanged(object sender,SizeChangedEventArgs args)=>RememberNormalWindowBounds();
    private void RememberNormalWindowBounds()
    {
        if(!presentationInitialized||closingPermanently||Mode is not ("home" or "note")||
           IsWindowFullscreen||IsFullscreenTransitioning||WindowState!=WindowState.Normal)return;
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==0||IsNativeMaximized(handle)||!GetNoteRect(handle,out var bounds))return;
        var pixels=PixelBounds.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
        if(pixels.Width<MinWidth||pixels.Height<MinHeight)return;
        fullscreenRestorePixels=pixels;
        fullscreenRestoreBounds=new Rect(Left,Top,Width,Height);
        fullscreenRestoreResize=ResizeMode;fullscreenRestoreState=WindowState.Normal;
        if(Mode=="note"&&NoteId is not null)fullscreenNoteLayout=new(NoteId,pixels.Left,pixels.Top,pixels.Width,pixels.Height);
        normalWindowBoundsKnown=true;
    }
    private void NotifyWindowPresentation()
    {
        if(Mode=="note")Post(new{@event="note-fullscreen",enabled=IsNoteFullscreen});
        else if(Mode=="home")Post(new{@event="window-fullscreen",enabled=IsWindowFullscreen});
    }
    private void OnNativeWindowStateChanged(object? sender,EventArgs args)
    {
        if(closingPermanently||IsFullscreenTransitioning||Mode is not ("home" or "note")||WindowState==WindowState.Minimized)return;
        bool maximized=WindowState==WindowState.Maximized;
        if(maximized==IsWindowFullscreen)return;
        // Win+Up and dragging to the top edge enter here without a web command.
        // The earlier normal HWND rectangle remains the durable restore size.
        IsWindowFullscreen=maximized;
        ApplyFullscreenChrome(maximized);
        if(!maximized){fullscreenNoteLayout=null;RememberNormalWindowBounds();}
        UpdateLayout();RoundWindow();NotifyWindowPresentation();
    }
    internal static PixelBounds RelativeMaximizeArea(PixelBounds monitor,PixelBounds workArea)=>
        new(workArea.Left-monitor.Left,workArea.Top-monitor.Top,workArea.Width,workArea.Height);

    // Returns true only for a message fully consumed here. Ordinary native
    // maximize/restore stays with Windows; StateChanged synchronizes our chrome.
    private bool HandleWindowPresentationMessage(nint hwnd,int msg,nint wParam,nint lParam,ref bool handled)
    {
        if(Mode is not ("home" or "note")||closingPermanently)return false;
        if(msg==0x0024&&lParam!=0) // WM_GETMINMAXINFO
        {
            var monitor=System.Windows.Forms.Screen.FromHandle(hwnd);
            var area=RelativeMaximizeArea(monitor.Bounds,monitor.WorkingArea);
            var info=Marshal.PtrToStructure<WindowMinMaxInfo>(lParam);
            info.MaxPosition=new(area.X,area.Y);info.MaxSize=new(area.Width,area.Height);
            var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);
            info.MinTrackSize=new(Math.Max(info.MinTrackSize.X,(int)Math.Ceiling(MinWidth*dpi.DpiScaleX)),
                Math.Max(info.MinTrackSize.Y,(int)Math.Ceiling(MinHeight*dpi.DpiScaleY)));
            Marshal.StructureToPtr(info,lParam,false);
            handled=true;return true;
        }
        if(msg==0x0112) // WM_SYSCOMMAND: shell, keyboard and native drag-to-top
        {
            long action=wParam.ToInt64()&0xFFF0;
            if(action==0xF030)RememberNormalWindowBounds(); // SC_MAXIMIZE
            if(testing&&(action==0xF030||action==0xF120&&IsWindowFullscreen))
            {
                // Exercise the native message hook without letting Windows move
                // any isolated test HWND onto the user's desktop.
                handled=true;
                _=SetWindowFullscreen(action==0xF030);
                return true;
            }
        }
        if(msg==0x0231)RememberNormalWindowBounds(); // WM_ENTERSIZEMOVE
        return false;
    }
    internal void RestoreVisibleWindowState()
    {
        if(WindowState==WindowState.Minimized)WindowState=IsWindowFullscreen?WindowState.Maximized:WindowState.Normal;
    }
    internal void ApplyWorkspacePin(JsonElement preferences)
    {
        if(Mode!="home"||closingPermanently)return;
        bool enabled=preferences.TryGetProperty("workspacePinned",out var setting)&&setting.ValueKind==JsonValueKind.True;
        WorkspacePinned=enabled;
        if(!testing)Topmost=enabled;
        Post(new{@event="workspace-pin",pinned=enabled});
    }
    internal async Task<bool> SetWorkspacePinned(bool enabled)
    {
        if(Mode!="home")throw new InvalidOperationException("Keep the Jot workspace on top from its own window.");
        var saved=await store.SavePreferences(JsonSerializer.SerializeToElement(new{workspacePinned=enabled}));
        var preferences=saved.GetProperty("prefs");
        ApplyWorkspacePin(preferences);
        await session.Changed();
        return WorkspacePinned;
    }
}
