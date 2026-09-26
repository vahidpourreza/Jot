using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Jot;

// Packages the approved raster artwork; no logo geometry is reconstructed here.
internal static class AppIcon
{
    private static readonly Lazy<byte[]> Colored = new(() => Render("jot-color.png", null));
    private static readonly Lazy<byte[]> Dark = new(() => Render("jot-tray-mask.png", Color.FromArgb(30,30,30)));
    private static readonly Lazy<byte[]> Light = new(() => Render("jot-tray-mask.png", Color.FromArgb(245,245,245)));
    internal static bool WindowsUsesLightTray()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }
    internal static byte[] RenderColor() => Colored.Value;
    internal static byte[] RenderMonochrome(bool lightBackground) => lightBackground ? Dark.Value : Light.Value;
    private static Rectangle ContentBounds(Bitmap bitmap)
    {
        var pixels=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        int left=bitmap.Width,top=bitmap.Height,right=-1,bottom=-1;
        try
        {
            var row=new byte[bitmap.Width*4];
            for(int y=0;y<bitmap.Height;y++)
            {
                Marshal.Copy(IntPtr.Add(pixels.Scan0,y*pixels.Stride),row,0,row.Length);
                for(int x=0;x<bitmap.Width;x++)
                    if(row[x*4+3]>32){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            }
        }
        finally { bitmap.UnlockBits(pixels); }
        if(right<left)throw new InvalidDataException("The app icon has no visible pixels.");
        return Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    private static byte[] Render(string asset,Color? ink)
    {
        int[] sizes=[256,128,64,48,32,24,20,16];
        using var source=new Bitmap(Path.Combine(AppContext.BaseDirectory,"assets",asset));
        var bounds=ContentBounds(source);
        var pngs=new List<byte[]>();
        using var attributes=new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        if(ink is Color color)
        {
            // A shared alpha master keeps light/dark tray silhouettes identical.
            attributes.SetColorMatrix(new ColorMatrix(new float[][]{
                [0,0,0,0,0],[0,0,0,0,0],[0,0,0,0,0],[0,0,0,1,0],
                [color.R/255f,color.G/255f,color.B/255f,0,1]
            }));
        }
        foreach(int size in sizes)
        {
            using var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using var graphics=Graphics.FromImage(bitmap);
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            graphics.CompositingQuality=CompositingQuality.HighQuality;
            graphics.CompositingMode=CompositingMode.SourceCopy;
            int padding=Math.Max(1,(int)Math.Round(size*.06));
            double scale=(size-2.0*padding)/Math.Max(bounds.Width,bounds.Height);
            int width=Math.Max(1,(int)Math.Round(bounds.Width*scale)),height=Math.Max(1,(int)Math.Round(bounds.Height*scale));
            var target=new Rectangle((size-width)/2,(size-height)/2,width,height);
            graphics.DrawImage(source,target,bounds.X,bounds.Y,bounds.Width,bounds.Height,GraphicsUnit.Pixel,attributes);
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
}
