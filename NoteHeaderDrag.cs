using System.Runtime.InteropServices;
using System.Text.Json;
using PixelBounds=System.Drawing.Rectangle;
using PixelPoint=System.Drawing.Point;

namespace Jot;
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    internal PixelPoint? TestDragPoint;

    internal static PixelBounds PlaceDraggedNote(PixelBounds normal,PixelPoint pointer,double anchorX,double anchorY,double dpi)
    {
        // Keep the grabbed horizontal fraction and header height beneath the
        // cursor. Coordinates are native pixels, including negative monitors.
        int inset=(int)Math.Round(9*dpi);
        int x=pointer.X-inset-(int)Math.Round(Math.Clamp(anchorX,0,1)*Math.Max(1,normal.Width-2*inset));
        int y=pointer.Y-inset-(int)Math.Round(Math.Clamp(anchorY,0,34)*dpi);
        return new(x,y,normal.Width,normal.Height);
    }

    private async Task DragHeader(JsonElement payload)
    {
        if(closingPermanently||HideRequested||IsFullscreenTransitioning)return;
        PixelPoint pointer;
        if(testing)
        {
            if(TestDragPoint is not {} fixture)return;
            pointer=fixture; // Never inspect/move the real cursor in tests.
        }
        else
        {
            // The bridge is asynchronous: a released click must not start a
            // late native move loop. Account for swapped mouse buttons too.
            int key=System.Windows.Forms.SystemInformation.MouseButtonsSwapped?2:1;
            if(GetAsyncKeyState(key)>=0||!GetCursorPos(out pointer))return;
        }
        if(IsWindowFullscreen)
        {
            if(Mode is not ("home" or "note")||payload.ValueKind!=JsonValueKind.Object||
               !payload.TryGetProperty("restore",out var restore)||!restore.GetBoolean())return;
            double x=payload.GetProperty("anchorX").GetDouble(),y=payload.GetProperty("anchorY").GetDouble();
            if(!double.IsFinite(x)||!double.IsFinite(y))return;
            var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            await SetWindowFullscreen(false,PlaceDraggedNote(fullscreenRestorePixels,pointer,x,y,dpi));
        }
        if(closingPermanently||IsWindowFullscreen)return;
        if(testing){TestHostActions.Add("native-drag-handoff");return;}
        // Reply to the WebView before entering Windows' modal move loop, so a
        // long drag cannot time out the bridge request.
        _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,()=>{
            int key=System.Windows.Forms.SystemInformation.MouseButtonsSwapped?2:1;
            if(closingPermanently||HideRequested||IsWindowFullscreen||GetAsyncKeyState(key)>=0||!GetCursorPos(out var current))return;
            ReleaseCapture();
            // WM_NCLBUTTONDOWN/HTCAPTION expects signed screen coordinates.
            int packed=unchecked((ushort)current.X|((ushort)current.Y<<16));
            SendMessage(source!.Handle,0x00A1,2,new nint(packed));
        });
    }
}
