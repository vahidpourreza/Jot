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
        int[] sizes = [128,64,48,32,24,16];
        var pngs=new List<byte[]>();
        foreach(int size in sizes)
        {
            using var bitmap=new Bitmap(size,size);
            using var graphics=Graphics.FromImage(bitmap);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size/24f,size/24f);
            var ink=lightBackground?Color.FromArgb(30,30,30):Color.FromArgb(245,245,245);
            using var pen=new Pen(ink,2);
            pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
            if(staticFile)
            {
                // A subtle opposite-color keyline keeps Explorer's fixed icon legible
                // on either background; the live tray uses a single adaptive stroke.
                using var halo=new Pen(Color.FromArgb(225,255,255,255),2.9f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
                DrawNotepad(graphics,halo);
            }
            DrawNotepad(graphics,pen);
            using var buffer=new MemoryStream();bitmap.Save(buffer,ImageFormat.Png);pngs.Add(buffer.ToArray());
        }
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
        int offset=6+16*sizes.Length;
        for(int i=0;i<sizes.Length;i++)
        {
            writer.Write((byte)sizes[i]);writer.Write((byte)sizes[i]);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)pngs[i].Length);writer.Write((uint)offset);
            offset+=pngs[i].Length;
        }
        foreach(var png in pngs)writer.Write(png);
        return output.ToArray();
    }
    private static void DrawNotepad(Graphics graphics,Pen pen)
    {
        // Lucide notepad-text, original 24x24 geometry (ISC).
        graphics.DrawLine(pen,8,2,8,6);graphics.DrawLine(pen,12,2,12,6);graphics.DrawLine(pen,16,2,16,6);
        using var rectangle=new GraphicsPath();
        rectangle.AddArc(4,4,4,4,180,90);rectangle.AddArc(16,4,4,4,270,90);
        rectangle.AddArc(16,18,4,4,0,90);rectangle.AddArc(4,18,4,4,90,90);rectangle.CloseFigure();
        graphics.DrawPath(pen,rectangle);
        graphics.DrawLine(pen,8,10,14,10);graphics.DrawLine(pen,8,14,16,14);graphics.DrawLine(pen,8,18,13,18);
    }
}
