using System.Reflection;
using System.Runtime.InteropServices;

namespace Jot;

internal static class TrayIconBounds
{
    // NotifyIcon has no public rectangle API. Isolate the .NET 10 identity
    // adapter; if a future runtime changes it, keep a safe click-point fallback.
    private static readonly FieldInfo? IdField=typeof(System.Windows.Forms.NotifyIcon).GetField("_id",BindingFlags.Instance|BindingFlags.NonPublic);
    private static readonly FieldInfo? WindowField=typeof(System.Windows.Forms.NotifyIcon).GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic);
    [StructLayout(LayoutKind.Sequential)] internal struct Identifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct PixelRect { public int Left,Top,Right,Bottom; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier icon,out PixelRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X,Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int metric,uint dpi);
    internal static bool TryIdentify(System.Windows.Forms.NotifyIcon icon,out Identifier identifier)
    {
        identifier=default;
        if(IdField?.GetValue(icon) is not uint id||WindowField?.GetValue(icon) is not System.Windows.Forms.NativeWindow window)return false;
        identifier=new(){Size=(uint)Marshal.SizeOf<Identifier>(),Window=window.Handle,Id=id};return true;
    }
    internal static System.Drawing.Rectangle Resolve(System.Windows.Forms.NotifyIcon icon,System.Drawing.Point click)
    {
        if(TryIdentify(icon,out var id)&&id.Window!=0&&Shell_NotifyIconGetRect(ref id,out var rect)>=0)
        {
            var bounds=System.Drawing.Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
            if(bounds.Width>0&&bounds.Height>0&&bounds.Contains(click))
            {
                if(icon.Icon is not { } drawing)return bounds;
                using var bitmap=drawing.ToBitmap();
                var dpi=GetDpiForWindow(WindowFromPoint(new(){X=click.X,Y=click.Y}));
                var size=dpi==0?drawing.Size:new System.Drawing.Size(GetSystemMetricsForDpi(49,dpi),GetSystemMetricsForDpi(50,dpi));
                if(size.Width<=0||size.Height<=0)size=drawing.Size;
                return VisibleBounds(bounds,size,InkBounds(bitmap),bitmap.Size);
            }
        }
        return new(click.X,click.Y,1,1);
    }
    internal static System.Drawing.Rectangle InkBounds(System.Drawing.Bitmap bitmap)
    {
        int left=bitmap.Width,top=bitmap.Height,right=0,bottom=0;
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).A>0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
        return right>left&&bottom>top?System.Drawing.Rectangle.FromLTRB(left,top,right,bottom):new(0,0,bitmap.Width,bitmap.Height);
    }
    internal static System.Drawing.Rectangle VisibleBounds(System.Drawing.Rectangle cell,System.Drawing.Size imageSize,System.Drawing.Rectangle ink,System.Drawing.Size bitmapSize)
    {
        // The shell's hit rectangle can include the taskbar/overflow cell's
        // padding. Center the rendered small icon there, then exclude the ICO's
        // own transparent margin so the anchor is the visible paper mark.
        int width=Math.Min(cell.Width,imageSize.Width),height=Math.Min(cell.Height,imageSize.Height);
        int x=cell.Left+(cell.Width-width)/2,y=cell.Top+(cell.Height-height)/2;
        return System.Drawing.Rectangle.FromLTRB(x+(int)Math.Floor(ink.Left*(double)width/bitmapSize.Width),y+(int)Math.Floor(ink.Top*(double)height/bitmapSize.Height),x+(int)Math.Ceiling(ink.Right*(double)width/bitmapSize.Width),y+(int)Math.Ceiling(ink.Bottom*(double)height/bitmapSize.Height));
    }
}
