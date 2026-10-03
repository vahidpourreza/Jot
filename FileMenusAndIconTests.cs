using System.Drawing;
using System.IO;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyFileMenusAndIcon(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="file-menus-icon-"+name,passed});
        var root=Path.Combine(testOutput,"file-menus-icon");Directory.CreateDirectory(root);
        var session=new JotSession(true,root){ExerciseLifecycle=true};
        try
        {
            await session.Store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=22,lineHeight=1.5,newNoteTarget="window"}));
            var home=session.Home();await home.WaitFor("window.jotReady===true");
            Check("no-file-buttons-in-library-or-more-panel",await home.Script("!document.querySelector('#homeOpenFile,#noteOpenFile,#noteSaveFile,#noteSaveAs')")=="true");
            var homeTab=".workspace-tab[data-home=true] [role=tab]";
            await home.RightClickControl(homeTab);
            Check("app-icon-menu-has-file-actions-and-settings",await home.Script("['file-open','file-save','file-save-as','app-settings'].every(id=>document.querySelector('#contentContextMenu [data-action='+id+']'))&&document.querySelector('#contentContextMenu [data-action=file-save]').disabled")=="true");
            var input=Path.Combine(root,"Open from menu.txt");await File.WriteAllTextAsync(input,"A note opened from the file menu.");home.TestOpenNoteFilePath=input;
            await home.ClickControl("#contentContextMenu [data-action=file-open]");
            for(int i=0;i<150&&!session.Windows.Any(w=>w.Mode=="note");i++)await Task.Delay(30);
            var note=session.Windows.Single(w=>w.Mode=="note");await note.WaitFor("window.jotReady===true");
            await note.RightClickControl("#editor");
            Check("editor-context-has-file-actions-with-standard-shortcuts",await note.Script("['file-open','file-save','file-save-as'].every(id=>document.querySelector('#editorMenu [data-action='+id+']'))&&document.querySelector('#editorMenu [data-action=file-save] kbd').textContent==='Ctrl+S'")=="true");
            note.TestSaveNoteFilePath=Path.Combine(root,"Menu saved.jot");
            await note.ClickControl("#editorMenu [data-action=file-save-as]");await note.WaitFor("document.getElementById('editorMenu').hidden");
            Check("save-menu-returns-focus-to-writing-with-design-system-feedback",await note.Script("document.activeElement===editor&&JotToast.get('file-saved')?.type==='success'&&document.getElementById('error').hidden")=="true");
            var saved=await NoteFileFormat.Read(note.TestSaveNoteFilePath);
            Check("save-as-snapshots-inherited-typography-without-changing-note-override",saved.Note["view"]!["fontSize"]!.GetValue<int>()==22&&saved.Note["view"]!["lineHeight"]!.GetValue<double>()==1.5&&!(await session.Store.LoadNote(note.NoteId!))!.Value.GetProperty("view").TryGetProperty("fontSize",out _));
            note.Width=360;note.Height=520;await Task.Delay(60);await note.ClickControl("#menuButton");
            Check("nineteen-separated-colors-fit-one-row",await note.Script("(()=>{const buttons=[...document.querySelectorAll('#noteColors button')],rects=buttons.map(b=>b.getBoundingClientRect());return buttons.length===19&&rects.every(r=>r.top===rects[0].top&&r.left>=0&&r.right<=innerWidth)&&rects.slice(1).every((r,i)=>r.left-rects[i].right>=1)})()") == "true");
            await note.Capture("note-colors-and-inherited-controls");await note.Script("closePanels()");
            var id=note.NoteId!;await session.OpenAsTab(id);await home.WaitFor("window.JotWorkspace.view==='note'");
            await home.RightClickControl(".workspace-note-tabs [role=tab]");
            Check("tab-menu-no-longer-duplicates-file-actions",await home.Script("!document.querySelector('#contentContextMenu [data-action=save-file],#contentContextMenu [data-action=save-as]')")=="true");
            await home.Script("JotMenus.close()");await home.RightClickControl(homeTab);
            Check("current-note-save-enabled-in-app-icon-menu",await home.Script("!document.querySelector('#contentContextMenu [data-action=file-save]').disabled&&!document.querySelector('#contentContextMenu [data-action=file-save-as]').disabled")=="true");
            await home.Capture("file-menu-current-note");await home.Script("JotMenus.close()");
            Check("tests-remain-isolated",session.Windows.All(w=>w.Left< -10000&&w.Opacity==0&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost));
        }
        finally{foreach(var window in session.Windows.ToArray())window.ClosePermanently();}

        var icon=NoteFileIcon.Render();using var stream=new MemoryStream(icon);using var reader=new BinaryReader(stream);
        Check("icon-has-eight-native-frames",reader.ReadUInt16()==0&&reader.ReadUInt16()==1&&reader.ReadUInt16()==8);
        foreach(var size in NoteFileIcon.Sizes)
        {
            var encoded=reader.ReadByte();reader.ReadByte();reader.ReadUInt16();reader.ReadUInt16();var bits=reader.ReadUInt16();var length=reader.ReadInt32();var offset=reader.ReadInt32();
            using var frameStream=new MemoryStream(icon,offset,length);using var bitmap=new Bitmap(frameStream);var colorful=false;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.A>200&&Math.Max(pixel.R,Math.Max(pixel.G,pixel.B))-Math.Min(pixel.R,Math.Min(pixel.G,pixel.B))>50)colorful=true;}
            Check("document-and-brand-survive-at-"+size,(encoded==0?256:encoded)==size&&bits==32&&bitmap.Width==size&&bitmap.Height==size&&bitmap.GetPixel(0,0).A==0&&colorful);
        }
        Check("document-icon-is-distinct-from-app-icon",!icon.SequenceEqual(AppIcon.LoadColor()));
    }
}
