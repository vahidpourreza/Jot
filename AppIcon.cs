using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace Jot;

internal static class AppIcon
{
    internal static bool WindowsUsesLightTray()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }
    internal static byte[] RenderMonochrome(bool lightBackground, bool staticFile = false)
    {
        int[] sizes = [256,128,64,48,32,24,20,16];
        var pngs=new List<byte[]>();
        foreach(int size in sizes)
        {
            using var bitmap=new Bitmap(size,size);
            using var graphics=Graphics.FromImage(bitmap);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size/24f,size/24f);
            var ink=lightBackground?Color.FromArgb(30,30,30):Color.FromArgb(245,245,245);
            using var shape=NoteShape();
            using var brush=new SolidBrush(ink);
            if(staticFile)
            {
                // A subtle opposite-color keyline keeps Explorer's fixed icon legible
                // on either background; the live tray uses a single adaptive stroke.
                using var halo=new Pen(Color.FromArgb(235,255,255,255),1.2f){LineJoin=LineJoin.Round};
                graphics.DrawPath(halo,shape);
            }
            graphics.FillPath(brush,shape);
            using var buffer=new MemoryStream();bitmap.Save(buffer,ImageFormat.Png);pngs.Add(buffer.ToArray());
        }
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
        int offset=6+16*sizes.Length;
        for(int i=0;i<sizes.Length;i++)
        {
            byte encoded=sizes[i]==256?(byte)0:(byte)sizes[i];
            writer.Write(encoded);writer.Write(encoded);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)pngs[i].Length);writer.Write((uint)offset);
            offset+=pngs[i].Length;
        }
        foreach(var png in pngs)writer.Write(png);
        return output.ToArray();
    }
    private static GraphicsPath NoteShape()
    {
        // Original Jot folded-note mark. Broad negative-space lines stay legible at 16px.
        var shape=new GraphicsPath(FillMode.Alternate);
        shape.AddLine(5,2,13,2);shape.AddLine(13,2,13,8);
        shape.AddBezier(13,8,13,9.1f,13.9f,10,15,10);shape.AddLine(15,10,21,10);
        shape.AddLine(21,10,21,20);shape.AddArc(17,18,4,4,0,90);
        shape.AddLine(19,22,5,22);shape.AddArc(3,18,4,4,90,90);
        shape.AddLine(3,20,3,4);shape.AddArc(3,2,4,4,180,90);shape.CloseFigure();
        shape.AddRectangle(new RectangleF(7,12,10,2));shape.AddRectangle(new RectangleF(7,16,7,2));
        shape.AddPolygon([new PointF(15,2),new PointF(21,8),new PointF(15,8)]);
        return shape;
    }
}
