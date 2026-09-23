using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Jot;

internal static class RichClipboard
{
    internal const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private static readonly SemaphoreSlim writeGate = new(1, 1);
    private static int latestWrite;
    private static readonly int[] retryDelays = [50, 90, 150, 250, 400, 550];
    internal static bool IsBusy(Exception error) => error is ExternalException && error.HResult == ClipboardBusyHResult;

    // The delay lets the WebView copy event release the clipboard before the native
    // PNG/HTML write. All attempts resume on the caller's WPF STA context.
    internal static async Task WriteAsync(DataObject data, ErrorLog log)
    {
        int request = Interlocked.Increment(ref latestWrite);
        await writeGate.WaitAsync();
        try
        {
            await Task.Delay(60);
            if (request != Volatile.Read(ref latestWrite)) return;
            await RetryAsync(() => {
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream stream && stream.CanSeek) stream.Position = 0;
                System.Windows.Clipboard.SetDataObject(data, true);
            }, log, () => request != Volatile.Read(ref latestWrite));
        }
        finally { writeGate.Release(); }
    }
    internal static async Task RetryAsync(Action write, ErrorLog log, Func<bool>? superseded = null, Func<int,Task>? delay = null)
    {
        delay ??= milliseconds => Task.Delay(milliseconds);
        for (int attempt = 0; ; attempt++)
        {
            if (superseded?.Invoke() == true) { log.Event("clipboard-write", "superseded", attempt); return; }
            try
            {
                write();
                if (attempt > 0) log.Event("clipboard-write", "recovered", attempt + 1);
                return;
            }
            catch (ExternalException error) when (IsBusy(error))
            {
                if (attempt == 0) log.Error("clipboard-busy", error);
                if (attempt >= retryDelays.Length) throw;
                await delay(retryDelays[attempt]);
            }
        }
    }
    internal static string HtmlFormat(string fragment)
    {
        const string prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        const string template = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        int start = Encoding.UTF8.GetByteCount(string.Format(template, 0, 0, 0, 0));
        int fragmentStart = start + Encoding.UTF8.GetByteCount(prefix);
        int fragmentEnd = fragmentStart + Encoding.UTF8.GetByteCount(fragment);
        int end = fragmentEnd + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(template, start, end, fragmentStart, fragmentEnd) + prefix + fragment + suffix;
    }
    internal static DataObject Create(JsonElement value)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, value.GetProperty("text").GetString() ?? "");
        data.SetData(DataFormats.Html, HtmlFormat(value.GetProperty("html").GetString() ?? ""));
        if (value.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String)
        {
            var src = image.GetString()!;
            if (src.StartsWith("data:image/", StringComparison.Ordinal) && src.Length < 12*1024*1024)
            {
                var bytes = Convert.FromBase64String(src[(src.IndexOf(',')+1)..]);
                using var stream = new MemoryStream(bytes);
                var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                data.SetData(DataFormats.Bitmap, bitmap);
                var png = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(bitmap); encoder.Save(png); png.Position = 0;
                data.SetData("PNG", png);
            }
        }
        return data;
    }
    internal static async Task<string> ImportImage(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("تصویر فقط از نشانی HTTPS قابل دریافت است.");
        using var handler = new HttpClientHandler { UseCookies = false, MaxAutomaticRedirections = 3 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 8*1024*1024) throw new InvalidDataException("تصویر بزرگ‌تر از ۸ مگابایت است.");
        using var bytes = new MemoryStream();
        using var input = await response.Content.ReadAsStreamAsync();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + read > 8*1024*1024) throw new InvalidDataException("تصویر بزرگ‌تر از ۸ مگابایت است.");
            await bytes.WriteAsync(buffer.AsMemory(0,read));
        }
        var content = bytes.ToArray();
        string? type = content.Length > 8 && content[0] == 137 && content[1] == 80 ? "png" :
            content.Length > 3 && content[0] == 255 && content[1] == 216 ? "jpeg" :
            content.Length > 12 && Encoding.ASCII.GetString(content,0,4) == "RIFF" && Encoding.ASCII.GetString(content,8,4) == "WEBP" ? "webp" :
            content.Length > 6 && Encoding.ASCII.GetString(content,0,3) == "GIF" ? "gif" : null;
        if (type is null) throw new InvalidDataException("فرمت تصویر پشتیبانی نمی‌شود.");
        return "data:image/" + type + ";base64," + Convert.ToBase64String(content);
    }
}
