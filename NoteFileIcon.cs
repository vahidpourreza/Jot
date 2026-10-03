using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Jot;

// Native file icon: a document silhouette carrying the unchanged approved mark.
// Geometry is rendered per Windows icon size so the 16px border remains legible.
internal static class NoteFileIcon
{
    internal static readonly int[] Sizes=[256,128,64,48,32,24,20,16];
    internal static Bitmap RenderFrame(int size)
    {
        using var source=new Bitmap(Path.Combine(AppContext.BaseDirectory,"assets","jot-color.png"));
        var bounds=AppIcon.ContentBounds(source);
        using var canvas=new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(canvas))
        {
            graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            graphics.ScaleTransform(size*4/256f,size*4/256f);
            using var paper=new GraphicsPath();paper.AddPolygon(new PointF[]{new(46,14),new(154,14),new(210,70),new(210,240),new(46,240)});
            using var paperFill=new SolidBrush(Color.FromArgb(252,253,255));using var outline=new Pen(Color.FromArgb(143,154,170),Math.Max(5,256f/size*.8f)){LineJoin=LineJoin.Round};
            graphics.FillPath(paperFill,paper);graphics.DrawPath(outline,paper);
            using var fold=new GraphicsPath();fold.AddPolygon(new PointF[]{new(154,14),new(154,70),new(210,70)});
            using var foldFill=new SolidBrush(Color.FromArgb(219,226,237));graphics.FillPath(foldFill,fold);
            graphics.DrawLines(outline,new PointF[]{new(154,18),new(154,70),new(206,70)});
            using var attributes=new ImageAttributes();attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(source,new Rectangle(58,86,144,144),bounds.X,bounds.Y,bounds.Width,bounds.Height,GraphicsUnit.Pixel,attributes);
        }
        var output=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(output))
        {graphics.CompositingMode=CompositingMode.SourceCopy;graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.DrawImage(canvas,new Rectangle(0,0,size,size));}
        return output;
    }
    internal static byte[] Render()
    {
        var frames=new List<byte[]>();
        foreach(var size in Sizes){using var image=RenderFrame(size);using var png=new MemoryStream();image.Save(png,ImageFormat.Png);frames.Add(png.ToArray());}
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)Sizes.Length);int offset=6+Sizes.Length*16;
        for(int i=0;i<Sizes.Length;i++)
        {var side=Sizes[i]==256?(byte)0:(byte)Sizes[i];writer.Write(side);writer.Write(side);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)frames[i].Length);writer.Write((uint)offset);offset+=frames[i].Length;}
        foreach(var frame in frames)writer.Write(frame);return output.ToArray();
    }
    internal static void Export(string path)
    {
        File.WriteAllBytes(path,Render());using var preview=RenderFrame(256);preview.Save(Path.ChangeExtension(path,".png"),ImageFormat.Png);
        using var sheet=new Bitmap(520,240,PixelFormat.Format32bppArgb);using var graphics=Graphics.FromImage(sheet);
        graphics.Clear(Color.FromArgb(245,245,247));using var dark=new SolidBrush(Color.FromArgb(30,30,33));graphics.FillRectangle(dark,0,120,520,120);
        foreach(int top in new[]{0,120}){int x=18;foreach(int size in new[]{16,20,24,32,48,64}){using var frame=RenderFrame(size);graphics.DrawImageUnscaled(frame,x,top+(120-size)/2);x+=size+24;}}
        sheet.Save(Path.Combine(Path.GetDirectoryName(path)!,"jot-file-sizes.png"),ImageFormat.Png);
    }
}
