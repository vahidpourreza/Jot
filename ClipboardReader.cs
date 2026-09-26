using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Jot;

internal sealed record ClipboardContent(string text,string html,string? image);
internal static class ClipboardReader
{
    private const int Limit=8*1024*1024;
    internal static async Task<ClipboardContent> ReadAsync(ErrorLog log)
    {
        await RichClipboard.AccessGate.WaitAsync();
        try
        {
            ClipboardContent result=new("","",null);
            await RichClipboard.RetryAsync(()=>result=Read(Clipboard.GetDataObject()),log,operation:"clipboard-read");
            return result;
        }
        finally{RichClipboard.AccessGate.Release();}
    }
    internal static ClipboardContent Read(IDataObject? data)
    {
        if(data is null)return new("","",null);
        var text=data.GetDataPresent(DataFormats.UnicodeText)?ReadString(data.GetData(DataFormats.UnicodeText)):"";
        var html="";
        if(data.GetDataPresent(DataFormats.Html))
        {
            try{html=Fragment(ReadString(data.GetData(DataFormats.Html)));}
            catch(InvalidDataException) when(text.Length>0){/* Plain text is the preferred safe fallback. */}
        }
        string? image=null;
        if(text.Length==0||Regex.IsMatch(html,"<img\\b",RegexOptions.IgnoreCase))
        {
            var png=data.GetDataPresent("PNG")?data.GetData("PNG"):null;
            if(png is Stream or byte[])
            {
                var bytes=png is Stream stream?ReadBytes(stream):(byte[])png;
                if(bytes.Length>Limit)throw new InvalidDataException("Clipboard image exceeds 8 MB.");
                using var input=new MemoryStream(bytes);var bitmap=BitmapFrame.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);CheckPixels(bitmap);
                image="data:image/png;base64,"+Convert.ToBase64String(bytes);
            }
            else if(data.GetDataPresent(DataFormats.Bitmap)&&data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                CheckPixels(bitmap);using var output=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);
                if(output.Length>Limit)throw new InvalidDataException("Clipboard image exceeds 8 MB. Insert a smaller image instead.");
                image="data:image/png;base64,"+Convert.ToBase64String(output.ToArray());
            }
        }
        return new(text,html,image);
    }
    private static void CheckPixels(BitmapSource bitmap)
    {
        if(bitmap.PixelWidth<1||bitmap.PixelHeight<1||(long)bitmap.PixelWidth*bitmap.PixelHeight>40_000_000)throw new InvalidDataException("Clipboard image is too large to paste safely.");
    }
    private static byte[] ReadBytes(Stream stream)
    {
        long position=stream.CanSeek?stream.Position:0;
        try
        {
            if(stream.CanSeek){if(stream.Length>Limit)throw new InvalidDataException("Clipboard content exceeds 8 MB.");stream.Position=0;}
            using var output=new MemoryStream();var buffer=new byte[65536];int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+read>Limit)throw new InvalidDataException("Clipboard content exceeds 8 MB.");output.Write(buffer,0,read);}
            return output.ToArray();
        }
        finally{if(stream.CanSeek)stream.Position=position;}
    }
    private static string ReadString(object? value)
    {
        var text=value switch{string s=>s,Stream stream=>Encoding.UTF8.GetString(ReadBytes(stream)),_=>""};
        if(text.Length>Limit)throw new InvalidDataException("Clipboard text exceeds 8 MB.");return text.TrimEnd('\0');
    }
    internal static string Fragment(string value)
    {
        var header=value[..Math.Min(4096,value.Length)];
        var start=Regex.Match(header,@"(?m)^StartFragment:\s*(\d+)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var end=Regex.Match(header,@"(?m)^EndFragment:\s*(\d+)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(start.Success&&end.Success&&int.TryParse(start.Groups[1].Value,out int first)&&int.TryParse(end.Groups[1].Value,out int last))
        {
            var bytes=Encoding.UTF8.GetBytes(value);
            if(first>=0&&last>=first&&last<=bytes.Length)
                try{return new UTF8Encoding(false,true).GetString(bytes,first,last-first);}catch(DecoderFallbackException){ }
        }
        const string opening="<!--StartFragment-->",closing="<!--EndFragment-->";
        int from=value.IndexOf(opening,StringComparison.OrdinalIgnoreCase),to=value.IndexOf(closing,StringComparison.OrdinalIgnoreCase);
        if(from>=0&&to>=from+opening.Length)return value[(from+opening.Length)..to];
        if(header.StartsWith("Version:",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Clipboard HTML could not be read safely. Copy the selection again.");
        return value;
    }
}
