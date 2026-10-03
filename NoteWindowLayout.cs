using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Jot;
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct NotePixelRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",EntryPoint="GetWindowRect")] private static extern bool GetNoteRect(nint handle,out NotePixelRect rect);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")] private static extern bool SetNoteBounds(nint handle,nint after,int x,int y,int width,int height,uint flags);
    internal SavedNoteWindow? RestoredLayout { get; private set; }
    private bool layoutPending;
    internal SavedNoteWindow CaptureNoteLayout()
    {
        if(Mode!="note"||NoteId is null)throw new InvalidOperationException("Only notes have a work layout.");
        if((IsNoteFullscreen||IsFullscreenTransitioning)&&fullscreenNoteLayout is not null)return fullscreenNoteLayout with{NoteId=NoteId,Tabs=null};
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==0||!GetNoteRect(handle,out var rect))throw new InvalidOperationException("Could not remember the note window position.");
        return new(NoteId,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
    }
    internal static SavedNoteWindow FitNoteLayout(SavedNoteWindow saved,System.Drawing.Rectangle area)
    {
        int width=Math.Min(Math.Max(360,saved.Width),Math.Max(200,area.Width-16));
        int height=Math.Min(Math.Max(280,saved.Height),Math.Max(120,area.Height-16));
        return saved with{Width=width,Height=height,X=Math.Clamp(saved.X,area.Left+8,Math.Max(area.Left+8,area.Right-width-8)),Y=Math.Clamp(saved.Y,area.Top+8,Math.Max(area.Top+8,area.Bottom-height-8))};
    }
    internal void RestoreNoteLayout(SavedNoteWindow saved)
    {
        var area=testing?new System.Drawing.Rectangle(0,0,1920,1040):System.Windows.Forms.Screen.FromRectangle(new(saved.X,saved.Y,saved.Width,saved.Height)).WorkingArea;
        RestoredLayout=FitNoteLayout(saved,area);
        if(testing){Width=RestoredLayout.Width;Height=RestoredLayout.Height;return;} // Never move a test onto the desktop.
        layoutPending=true;
    }
    private void ApplyPendingNoteLayout()
    {
        if(!layoutPending||RestoredLayout is null||testing)return;
        layoutPending=false;var handle=new WindowInteropHelper(this).Handle;
        SetNoteBounds(handle,0,RestoredLayout.X,RestoredLayout.Y,RestoredLayout.Width,RestoredLayout.Height,0x0004|0x0010);
    }
}
