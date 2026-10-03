using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using PixelBounds=System.Drawing.Rectangle;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyNativeWindowPresentation(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="native-window-"+name,passed});
        foreach(var fixture in new[]{
            ("bottom",new PixelBounds(0,0,1920,1080),new PixelBounds(0,0,1920,1040),new PixelBounds(0,0,1920,1040)),
            ("top",new PixelBounds(-2560,0,2560,1440),new PixelBounds(-2560,48,2560,1392),new PixelBounds(0,48,2560,1392)),
            ("left",new PixelBounds(1920,-1440,2560,1440),new PixelBounds(1980,-1440,2500,1440),new PixelBounds(60,0,2500,1440)),
            ("right",new PixelBounds(-1920,-1080,1920,1080),new PixelBounds(-1920,-1080,1860,1080),new PixelBounds(0,0,1860,1080))
        })Check("work-area-"+fixture.Item1+"-handles-monitor-origin",RelativeMaximizeArea(fixture.Item2,fixture.Item3)==fixture.Item4);
        var s=new JotSession(true,Path.Combine(testOutput,"native-window-presentation")){ExerciseLifecycle=true};
        try
        {
            await s.Store.SavePreferences(JsonSerializer.SerializeToElement(new{newNoteTarget="window",workspacePinned=false}));
            var note=await s.NewNote();await note.WaitFor("window.jotReady===true");
            var home=s.Home();await home.WaitFor("window.jotReady===true");
            foreach(var window in new[]{note,home})
            {
                window.Left=-31600;window.Top=-31500;window.Width=480;window.Height=400;window.UpdateLayout();
                await Task.Delay(40);var normal=window.ReadWindowPixels();
                var handle=window.source!.Handle;
                SendMessage(handle,0x0112,0xF030,0); // Windows' native SC_MAXIMIZE path; test hook keeps it offscreen.
                await window.WaitFor(window.Mode=="home"?"fullscreen":"noteFullscreen");
                Check(window.Mode+"-native-maximize-synchronizes-header-and-removes-frame",window.IsWindowFullscreen&&window.Surface.Margin.Left==0&&window.Browser.Margin.Left==0&&window.Browser.Clip is null&&window.ReadWindowPixels()==new PixelBounds(-32000,-32000,1200,760));
                Check(window.Mode+"-maximize-retains-native-resize-capability",window.ResizeMode==ResizeMode.CanResize);
                if(window.Mode=="note")Check("native-maximize-keeps-small-session-size",window.CaptureNoteLayout().Width==normal.Width&&window.CaptureNoteLayout().X==normal.X);
                SendMessage(handle,0x0112,0xF120,0); // SC_RESTORE / Win+Down.
                await window.WaitFor(window.Mode=="home"?"!fullscreen":"!noteFullscreen");
                Check(window.Mode+"-native-restore-returns-exact-normal-bounds",window.ReadWindowPixels()==normal&&!window.IsWindowFullscreen&&window.Surface.Margin.Left==8&&window.Browser.Margin.Left==9);
                // Verify the actual hook's MINMAXINFO writing without a desktop resize.
                var data=Marshal.AllocHGlobal(Marshal.SizeOf<WindowMinMaxInfo>());
                try
                {
                    Marshal.StructureToPtr(new WindowMinMaxInfo{MinTrackSize=new(111,222),MaxTrackSize=new(9999,8888)},data,false);
                    bool handled=false;var consumed=window.HandleWindowPresentationMessage(handle,0x0024,0,data,ref handled);
                    var actual=Marshal.PtrToStructure<WindowMinMaxInfo>(data);
                    var monitor=System.Windows.Forms.Screen.FromHandle(handle);var expected=RelativeMaximizeArea(monitor.Bounds,monitor.WorkingArea);
                    var scale=System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    Check(window.Mode+"-native-max-bounds-use-work-area-and-preserve-resize-limits",consumed&&handled&&actual.MaxPosition.X==expected.X&&actual.MaxPosition.Y==expected.Y&&actual.MaxSize.X==expected.Width&&actual.MaxSize.Y==expected.Height&&actual.MinTrackSize.X>=window.MinWidth*scale.DpiScaleX&&actual.MinTrackSize.Y>=window.MinHeight*scale.DpiScaleY&&actual.MaxTrackSize.Y==8888);
                }
                finally{Marshal.FreeHGlobal(data);}
                window.TestDragPoint=new(-31500,-31400);
                SendMessage(handle,0x0112,0xF030,0);
                await window.DragHeader(JsonSerializer.SerializeToElement(new{restore=true,anchorX=.5,anchorY=15}));
                var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
                Check(window.Mode+"-native-maximize-restores-under-header-drag",!window.IsWindowFullscreen&&window.ReadWindowPixels()==PlaceDraggedNote(normal,window.TestDragPoint.Value,.5,15,dpi)&&window.TestHostActions.Contains("native-drag-handoff"));
                window.TestDragPoint=null;
            }
            var viewBefore=(await s.Store.LoadNote(note.NoteId!))!.Value.GetProperty("view").GetRawText();
            await home.SetWorkspacePinned(true);
            Check("workspace-pin-persists-without-touching-note-pin",home.WorkspacePinned&&(await s.Store.LoadPreferences()).GetProperty("workspacePinned").GetBoolean()&&(await s.Store.LoadNote(note.NoteId!))!.Value.GetProperty("view").GetRawText()==viewBefore);
            await home.SwitchHomeView(true);await home.SwitchHomeView(false);
            Check("workspace-pin-survives-home-settings-navigation",home.WorkspacePinned);
            await home.SetWorkspacePinned(false);
            Check("workspace-unpin-persists",!home.WorkspacePinned&&!(await s.Store.LoadPreferences()).GetProperty("workspacePinned").GetBoolean());
            home.ApplyWorkspacePin(JsonSerializer.SerializeToElement(new{workspacePinned=true}));
            note.ApplyWorkspacePin(JsonSerializer.SerializeToElement(new{workspacePinned=true}));
            Check("workspace-pin-applies-only-to-workspace",home.WorkspacePinned&&!note.WorkspacePinned);
            foreach(var window in new[]{note,home})Check(window.Mode+"-tests-remain-isolated",window.Opacity==0&&!window.ShowActivated&&!window.ShowInTaskbar&&!window.Topmost&&window.Left< -10000&&window.Top< -10000&&window.RuntimeErrors.Count==0);
        }
        finally{foreach(var window in s.Windows.ToArray())window.ClosePermanently();}
    }
}
