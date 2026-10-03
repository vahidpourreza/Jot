using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyPersonalization(List<object> checks,MainWindow a,MainWindow b)
    {
        var preservedA=await a.Script("editor.innerHTML");
        var preservedB=await b.Script("editor.innerHTML");
        var c=await session.NewNote();await c.WaitFor("window.jotReady===true");
        await c.Script("editor.innerHTML='<p>Delete test فارسی English</p>';onEdit();saveNow().then(()=>window.draftSaved=true)");
        await c.WaitFor("window.draftSaved===true");
        await c.Script("document.getElementById('menuButton').click();document.getElementById('deleteButton').click()");
        checks.Add(new{name="delete-confirmation-defaults-to-cancel",passed=await c.Script("document.getElementById('deleteDialog').open&&document.activeElement.id==='deleteCancel'")=="true"});
        await c.Capture("delete-confirmation");
        await c.ClickControl("#deleteCancel");
        checks.Add(new{name="cancel-deletion-keeps-note-and-window",passed=session.Windows.Contains(c)&&(await store.Load())!.Value.GetProperty("notes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==c.NoteId)});

        // Simulate an inaccessible recovery location without changing live user data.
        var trash=Path.Combine(store.Root,"trash");
        var earlierTrash=Path.Combine(store.Root,"trash-before-delete-check");
        if(Directory.Exists(trash))Directory.Move(trash,earlierTrash);
        await File.WriteAllTextAsync(trash,"blocked recovery path");
        await c.Script("document.getElementById('deleteButton').click();document.getElementById('deleteConfirm').click()");
        await c.WaitFor("!document.getElementById('deleteError').hidden&&!document.getElementById('deleteConfirm').disabled");
        checks.Add(new{name="failed-delete-leaves-editor-and-store-intact",passed=session.Windows.Contains(c)&&await c.Script("editor.isContentEditable&&editor.textContent.includes('Delete test')")=="true"&&(await store.Load())!.Value.GetProperty("notes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==c.NoteId)});
        File.Move(trash,Path.Combine(store.Root,"blocked-recovery-test.txt"));
        if(Directory.Exists(earlierTrash))Directory.Move(earlierTrash,trash);
        await c.ClickControl("#deleteCancel");

        // A changed draft (including its original embedded image) must be in recovery.
        var image=JsonSerializer.Deserialize<string>(await a.Script("editor.querySelector('img').src"))!;
        var pending=await c.Script("editor.innerHTML='<p>Latest unsaved delete draft</p><img src=\"'+"+JsonSerializer.Serialize(image)+"+'\">';onEdit();clearTimeout(saveTimer);document.getElementById('deleteButton').click();document.getElementById('deleteConfirm').click();document.getElementById('deleteConfirm').getAttribute('aria-busy')==='true'&&!editor.isContentEditable&&document.getElementById('deleteConfirm').textContent==='Deleting…'");
        checks.Add(new{name="delete-shows-disabled-pending-state",passed=pending=="true"});
        for(int i=0;i<200&&session.Windows.Contains(c);i++)await Task.Delay(40);
        checks.Add(new{name="delete-removes-only-selected-note-and-closes-its-window",passed=!session.Windows.Contains(c)&&session.Windows.Contains(a)&&session.Windows.Contains(b)&&!(await store.Load())!.Value.GetProperty("notes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==c.NoteId)});
        await WaitFor("!homeData.notes.some(n=>n.id==="+JsonSerializer.Serialize(c.NoteId)+")");
        using var archive=JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(trash,c.NoteId+"-*.json").Single()));
        var archivedNote=archive.RootElement.GetProperty("note");
        checks.Add(new{name="deleted-note-recovery-retains-latest-draft-and-full-image",passed=archivedNote.GetProperty("plain").GetString()!.Contains("Latest unsaved")&&archivedNote.GetProperty("html").GetString()!.Contains(image)});
        bool staleRejected=false;
        try{await store.SaveNote(archivedNote);}catch(InvalidDataException){staleRejected=true;}
        checks.Add(new{name="stale-save-cannot-resurrect-deleted-note",passed=staleRejected});
        await a.Script("request('open-note',"+JsonSerializer.Serialize(c.NoteId)+").then(()=>window.deletedOpenRejected=false).catch(()=>window.deletedOpenRejected=true)");
        await a.WaitFor("window.deletedOpenRejected!==undefined");
        checks.Add(new{name="deleted-note-cannot-be-reopened",passed=await a.Script("window.deletedOpenRejected")=="true"});
        await a.Script("request('note-delete',"+JsonSerializer.Serialize(b.NoteId)+").then(()=>window.crossDeleteRejected=false).catch(()=>window.crossDeleteRejected=true)");
        await a.WaitFor("window.crossDeleteRejected!==undefined");
        checks.Add(new{name="note-window-cannot-delete-another-note",passed=await a.Script("window.crossDeleteRejected")=="true"&&session.Windows.Contains(b)});
        checks.Add(new{name="other-editors-unchanged-by-delete",passed=preservedA==await a.Script("editor.innerHTML")&&preservedB==await b.Script("editor.innerHTML")});
        checks.Add(new{name="deleted-editor-no-runtime-exceptions",passed=c.RuntimeErrors.Count==0});

        var legacyStore=new NoteStore(Path.Combine(testOutput,"legacy-color-test"));
        var id=await legacyStore.Create();
        var legacy=JsonNode.Parse((await legacyStore.Load())!.Value.GetRawText())!;
        legacy["notes"]![0]!.AsObject().Remove("color");legacy["prefs"]!["accent"]="teal";
        await legacyStore.Save(JsonSerializer.SerializeToElement(legacy));
        await legacyStore.SavePreferences(JsonSerializer.SerializeToElement(new{theme="light",accent="red",coloredIcons=true}));
        var migrated=(await legacyStore.Load())!.Value;
        checks.Add(new{name="legacy-header-color-migrates-without-global-accent",passed=migrated.GetProperty("notes")[0].GetProperty("color").GetString()=="teal"&&migrated.GetProperty("prefs").GetProperty("accent").GetString()=="neutral"&&!migrated.GetProperty("prefs").GetProperty("coloredIcons").GetBoolean()});
        bool badColorRejected=false;
        try{await legacyStore.SaveMetadata(JsonSerializer.SerializeToElement(new{id,color="unlisted-color"}));}catch(InvalidDataException){badColorRejected=true;}
        checks.Add(new{name="note-color-accepts-only-known-palette",passed=badColorRejected});
        await SetStoreTrigger(legacyStore,"CREATE TRIGGER fail_delete BEFORE DELETE ON notes BEGIN SELECT RAISE(ABORT,'synthetic delete failure');END;");
        bool deleteWriteRejected=false;
        try{await legacyStore.Delete(id);}catch(Microsoft.Data.Sqlite.SqliteException){deleteWriteRejected=true;}
        checks.Add(new{name="failed-store-write-keeps-note-after-archiving",passed=deleteWriteRejected&&(await legacyStore.Load())!.Value.GetProperty("notes").GetArrayLength()==1&&Directory.GetFiles(Path.Combine(legacyStore.Root,"trash"),"*.json").Length==1});
        await SetStoreTrigger(legacyStore,"DROP TRIGGER fail_delete;");
        await legacyStore.Delete(id);
        checks.Add(new{name="deleting-last-note-leaves-valid-empty-store",passed=(await legacyStore.Load())!.Value.GetProperty("notes").GetArrayLength()==0});
        VerifyIconSizes(checks);
    }

    private void VerifyIconSizes(List<object> checks)
    {
        using var board=new System.Drawing.Bitmap(650,540);
        using var graphics=System.Drawing.Graphics.FromImage(board);
        graphics.Clear(System.Drawing.Color.FromArgb(245,245,245));
        using var dark=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(24,24,24));graphics.FillRectangle(dark,0,145,410,125);graphics.FillRectangle(dark,0,400,410,125);
        using var font=new System.Drawing.Font("Segoe UI",10);
        graphics.DrawString("Jot · faceted stack / native icon exports",font,System.Drawing.Brushes.Black,12,6);
        bool colorVisible=true,transparent=true,fullSlot=true;
        foreach(bool light in new[]{true,false})
        {
            using var stream=new MemoryStream(AppIcon.RenderColor());
            int x=14,y=light?50:170;
            foreach(int size in new[]{16,20,24,32,48,64})
            {
                using var icon=new System.Drawing.Icon(stream,size,size);using var bitmap=icon.ToBitmap();
                int colored=0,left=size,right=0,top=size,bottom=0;
                for(int row=0;row<bitmap.Height;row++)for(int col=0;col<bitmap.Width;col++){var p=bitmap.GetPixel(col,row);if(p.A>200){left=Math.Min(left,col);right=Math.Max(right,col);top=Math.Min(top,row);bottom=Math.Max(bottom,row);if(Math.Max(p.R,Math.Max(p.G,p.B))-Math.Min(p.R,Math.Min(p.G,p.B))>40)colored++;}}
                colorVisible &= colored>size*size/4;
                fullSlot &= Math.Max(right-left+1,bottom-top+1)>=size-2;
                transparent &= bitmap.GetPixel(0,0).A==0&&bitmap.GetPixel(size-1,size-1).A==0;
                graphics.DrawImageUnscaled(bitmap,x,y);graphics.DrawString(size.ToString(),font,light?System.Drawing.Brushes.Black:System.Drawing.Brushes.White,x,y+68);x+=size+20;stream.Position=0;
            }
        }
        using(var grid=new System.Drawing.Pen(System.Drawing.Color.FromArgb(45,100,100,100)))
            for(int offset=0;offset<=128;offset+=32){graphics.DrawLine(grid,480+offset,40,480+offset,168);graphics.DrawLine(grid,480,40+offset,608,40+offset);}
        using(var largeStream=new MemoryStream(AppIcon.RenderColor()))
        using(var large=new System.Drawing.Icon(largeStream,128,128))
        using(var bitmap=large.ToBitmap())graphics.DrawImageUnscaled(bitmap,480,40);
        graphics.DrawString("Approved faceted stack",font,System.Drawing.Brushes.Black,425,190);
        graphics.DrawString("128 px packaged icon",font,System.Drawing.Brushes.Black,445,212);
        graphics.DrawString("Tighter taskbar crop",font,System.Drawing.Brushes.Black,445,234);
        graphics.DrawString("Color app / taskbar",font,System.Drawing.Brushes.Black,445,270);
        graphics.DrawString("One tray alpha master",font,System.Drawing.Brushes.Black,445,300);
        graphics.DrawString("Dark / light adaptive ink",font,System.Drawing.Brushes.Black,445,322);
        graphics.DrawString("Same shape in both modes",font,System.Drawing.Brushes.Black,445,344);
        var allVisible=true;
        foreach(var light in new[]{true,false})
        {
            using var stream=new MemoryStream(AppIcon.RenderMonochrome(light));
            var x=14;
            foreach(var size in new[]{16,20,24,32,48,64})
            {
                using var icon=new System.Drawing.Icon(stream,size,size);using var bitmap=icon.ToBitmap();
                int opaque=0;for(int y=0;y<bitmap.Height;y++)for(int col=0;col<bitmap.Width;col++)if(bitmap.GetPixel(col,y).A>200)opaque++;
                allVisible &= bitmap.Width==size&&opaque>size*size/4;
                int drawY=light?300:425;
                graphics.DrawImageUnscaled(bitmap,x,drawY);graphics.DrawString(size.ToString(),font,light?System.Drawing.Brushes.Black:System.Drawing.Brushes.White,x,drawY+68);x+=size+20;
                stream.Position=0;
            }
        }
        board.Save(Path.Combine(testOutput,"icon-sizes-light-dark.png"));
        checks.Add(new{name="custom-monochrome-icon-legible-at-small-sizes",passed=allVisible});
        checks.Add(new{name="colored-app-icon-visible-in-all-small-sizes",passed=colorVisible&&transparent});
        checks.Add(new{name="taskbar-artwork-fills-icon-slot",passed=fullSlot});
        checks.Add(new{name="native-window-uses-high-resolution-colored-icon",passed=Icon is System.Windows.Media.Imaging.BitmapSource source&&source.PixelWidth>=128});
        using(var darkStream=new MemoryStream(AppIcon.RenderMonochrome(true)))
        using(var lightStream=new MemoryStream(AppIcon.RenderMonochrome(false)))
        using(var darkIcon=new System.Drawing.Icon(darkStream,32,32))
        using(var lightIcon=new System.Drawing.Icon(lightStream,32,32))
        using(var darkBitmap=darkIcon.ToBitmap())
        using(var lightBitmap=lightIcon.ToBitmap())
        {
            bool sameAlpha=true;for(int y=0;y<32;y++)for(int x=0;x<32;x++)sameAlpha&=darkBitmap.GetPixel(x,y).A==lightBitmap.GetPixel(x,y).A;
            checks.Add(new{name="light-dark-tray-shape-is-identical",passed=sameAlpha});
        }
        checks.Add(new{name="packaged-icons-match-generated-assets",passed=AppIcon.LoadColor().SequenceEqual(AppIcon.RenderColor())&&AppIcon.LoadMonochrome(true).SequenceEqual(AppIcon.RenderMonochrome(true))&&AppIcon.LoadMonochrome(false).SequenceEqual(AppIcon.RenderMonochrome(false))});
        using var reader=new BinaryReader(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"assets","jot.ico")));
        reader.ReadUInt16();reader.ReadUInt16();var count=reader.ReadUInt16();var sizes=new List<int>();
        for(int i=0;i<count;i++){var size=reader.ReadByte();sizes.Add(size==0?256:size);reader.ReadBytes(15);}
        checks.Add(new{name="executable-icon-includes-eight-native-resolutions",passed=sizes.Order().SequenceEqual(new[]{16,20,24,32,48,64,128,256})});
    }
}
