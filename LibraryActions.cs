using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jot;
public partial class MainWindow
{
    internal string? TestExportHtml;
    private void RequireHome(){if(Mode!="home")throw new InvalidOperationException("This action is available from Home.");}
    private async Task<bool> CopyLibraryNote(string id)
    {
        RequireHome();var note=await session.ReadLatestNote(id);var html=note.GetProperty("html").GetString()!;
        var images=Regex.Matches(html,"<img\\b[^>]*\\bsrc=[\"'](data:image/[^\"']+)[\"']",RegexOptions.IgnoreCase);
        var data=RichClipboard.Create(JsonSerializer.SerializeToElement(new{html,text=note.GetProperty("plain").GetString(),image=images.Count==1?images[0].Groups[1].Value:null}));
        if(testing){TestClipboardData=data;return TestClipboardWriteAccepted;}
        return await RichClipboard.WriteAsync(data,session.Log);
    }
    private async Task ExportLibraryNote(string id)
    {
        RequireHome();var note=await session.ReadLatestNote(id);var html=ExportDocument(note.GetProperty("html").GetString()!);
        if(testing){TestExportHtml=html;return;}
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="HTML note (*.html)|*.html",FileName="Jot-note.html"};
        if(dialog.ShowDialog(this)==true)await File.WriteAllTextAsync(dialog.FileName,html);
    }
}
