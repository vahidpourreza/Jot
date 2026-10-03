using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Jot;

internal sealed class TaskbarMenu(Application app,ErrorLog log)
{
    private bool? currentLight;
    internal const string AppId="Jot.PersonalNotes";
    internal static readonly (TaskbarAction Action,string Title,string Description,string Geometry)[] Items=
    [
        (TaskbarAction.Home,"Home","Open your note library","M3,10 L12,3 L21,10 M5,9 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,9"),
        (TaskbarAction.NewNote,"New note","Create a new note","M5,12 L19,12 M12,5 L12,19"),
        (TaskbarAction.Settings,"Settings","Open Jot settings","M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12")
    ];
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    internal static void SetIdentity()=>Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
    internal static JumpList Create(string executable,string directory,bool light)
    {
        var list=new JumpList{ShowRecentCategory=false,ShowFrequentCategory=false};
        foreach(var item in Items)list.JumpItems.Add(new JumpTask{
            Title=item.Title,Description=item.Description,ApplicationPath=executable,WorkingDirectory=directory,
            Arguments=TaskbarCommands.Flag+" "+TaskbarCommands.Argument(item.Action),
            IconResourcePath=Path.Combine(directory,"assets","taskbar",TaskbarCommands.Argument(item.Action)+(light?"-dark.ico":"-light.ico")),IconResourceIndex=0
        });
        return list;
    }
    internal void Refresh()
    {
        try
        {
            bool light=AppIcon.WindowsUsesLightTray();if(currentLight==light)return;
            var list=Create(Environment.ProcessPath!,AppContext.BaseDirectory,light);
            list.JumpItemsRejected+=(_,e)=>{if(e.RejectedItems.Count>0)log.Error("taskbar-items-rejected",new InvalidOperationException("Shell rejected taskbar actions."));};
            JumpList.SetJumpList(app,list);
            currentLight=light;
        }
        catch(Exception error){log.Error("taskbar-menu",error);}
    }
    // Export existing Lucide-style UI glyphs, not the app logo. These native
    // resources let Explorer render its own menu, including theme and DPI.
    internal static void ExportIcons(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(var item in Items)foreach(bool lightInk in new[]{false,true})
        {
            int[] sizes=[16,20,24,32,48,64];var frames=new List<byte[]>();
            foreach(int size in sizes)
            {
                var visual=new DrawingVisual();
                using(var context=visual.RenderOpen())
                {
                    context.PushTransform(new ScaleTransform(size/24d,size/24d));
                    var pen=new Pen(new SolidColorBrush(lightInk?Color.FromRgb(220,220,220):Color.FromRgb(70,70,70)),1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
                    context.DrawGeometry(null,pen,Geometry.Parse(item.Geometry));context.Pop();
                }
                var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var png=new MemoryStream();encoder.Save(png);frames.Add(png.ToArray());
            }
            using var writer=new BinaryWriter(File.Create(Path.Combine(directory,TaskbarCommands.Argument(item.Action)+(lightInk?"-light.ico":"-dark.ico"))));
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++)
            {
                writer.Write((byte)sizes[i]);writer.Write((byte)sizes[i]);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;
            }
            foreach(var frame in frames)writer.Write(frame);
        }
    }
}
