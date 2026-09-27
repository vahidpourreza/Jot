using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Jot;

internal static class BrowserRuntime
{
#if JOT_OFFLINE_PACKAGE
    internal const bool RequiresBundledRuntime=true;
#else
    internal const bool RequiresBundledRuntime=false;
#endif
    internal static string? Resolve(string? directory=null)
    {
        directory??=AppContext.BaseDirectory;
        var manifest=Path.Combine(directory,"jot-package.json");
        var runtime=Path.Combine(directory,"WebView2Runtime");
        var executable=Path.Combine(runtime,"msedgewebview2.exe");
        if(!File.Exists(manifest)&&!RequiresBundledRuntime)return null; // Ordinary developer build.
        if(!File.Exists(manifest)||!File.Exists(executable))throw new FileNotFoundException("Jot's bundled runtime is missing. Run the Jot installer again to repair it.");
        using var data=JsonDocument.Parse(File.ReadAllBytes(manifest));
        var expected=data.RootElement.GetProperty("webView2Version").GetString();
        if(string.IsNullOrEmpty(expected)||FileVersionInfo.GetVersionInfo(executable).ProductVersion!=expected)
            throw new InvalidDataException("Jot's bundled runtime does not match its package. Run the installer again to repair it.");
        return runtime;
    }
    internal static Task<CoreWebView2Environment> CreateAsync(string profile,bool testing)
    {
        var runtime=Resolve();
        if(runtime is not null)
        {
            // This process only: an inherited machine/dev override must not select
            // an installed browser instead of the offline package's own runtime.
            Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",runtime,EnvironmentVariableTarget.Process);
        }
        return CoreWebView2Environment.CreateAsync(runtime,profile,testing?new CoreWebView2EnvironmentOptions("--disable-backgrounding-occluded-windows"):null);
    }
}
