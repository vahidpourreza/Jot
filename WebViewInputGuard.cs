using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Jot;

// The composition renderer has its own invisible top-level HWND. Some WebView2
// versions leave it visible over the desktop (alpha 0, no WS_EX_TRANSPARENT),
// swallowing clicks even while Jot itself is minimized. Only that exact surface
// in this session's browser process is made click-through; WPF remains the input
// host. Do not hide the renderer HWND: graphics capture still needs it.
internal sealed class WebViewInputGuard : IDisposable
{
    internal const long Transparent=0x20,Layered=0x80000,NoActivate=0x8000000,ToolWindow=0x80,NoRedirectionBitmap=0x200000;
    private const long CompositionStyles=Layered|NoActivate|ToolWindow|NoRedirectionBitmap;
    private const long ChildWindow=0x40000000;
    private readonly int processId;
    private readonly Process process;
    private readonly Dispatcher dispatcher;
    private readonly ErrorLog log;
    private readonly WinEventProc callback;
    private readonly List<nint> hooks=[];
    private readonly Dictionary<nint,uint> protectedWindows=[];
    private readonly HashSet<nint>? testWindows;
    private bool queued,disposed;
    internal int ProtectedCount=>protectedWindows.Count;
    internal bool IsAlive=>!disposed&&!process.HasExited;

    private delegate void WinEventProc(nint hook,uint eventType,nint window,int objectId,int childId,uint thread,uint time);
    private delegate bool EnumWindowProc(nint window,nint state);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint start,uint end,nint module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback,nint state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint window,StringBuilder name,int count);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowStyle(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] private static extern nint SetWindowStyle(nint window,int index,nint value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint window,out uint color,out byte alpha,out uint flags);

    internal WebViewInputGuard(int browserProcessId,Dispatcher dispatcher,ErrorLog log,HashSet<nint>? testWindows=null)
    {
        if(browserProcessId<=0)throw new ArgumentOutOfRangeException(nameof(browserProcessId));
        this.processId=browserProcessId;this.dispatcher=dispatcher;this.log=log;this.testWindows=testWindows;
        process=Process.GetProcessById(browserProcessId);
        // Holding the process object binds HasExited to the original process,
        // rather than trusting a PID that Windows might later recycle.
        _=process.Handle;
        process.Exited+=OnProcessExited;
        process.EnableRaisingEvents=true;
        callback=OnWindowEvent;
        // Process-filtered, out-of-context notifications, not a global mouse
        // hook. Coalesce bursts; there is no polling timer or idle work.
        foreach(var range in new[]{(0x8000u,0x8003u),(0x800Au,0x800Bu),(0x8017u,0x8018u)})
        {
            var hook=SetWinEventHook(range.Item1,range.Item2,0,callback,(uint)processId,0,0);
            if(hook!=0)hooks.Add(hook);
        }
        if(hooks.Count!=3)log.Event("webview-input-guard","hook-unavailable");
        Refresh();
    }

    internal static bool IsInvisibleCompositionSurface(string className,long style,long extendedStyle,bool hasAlpha,byte alpha,uint alphaFlags)=>
        className=="Chrome_WidgetWin_1"&&(style&ChildWindow)==0&&
        (extendedStyle&CompositionStyles)==CompositionStyles&&hasAlpha&&(alphaFlags&2)!=0&&alpha==0;

    private void OnWindowEvent(nint hook,uint eventType,nint window,int objectId,int childId,uint thread,uint time)
    {
        if(disposed||window==0||objectId!=0||childId!=0)return;
        if(eventType==0x8001)protectedWindows.Remove(window); // HWNDs can be recycled within one browser thread.
        RequestRefresh();
    }
    private void OnProcessExited(object? sender,EventArgs args)
    {
        if(!dispatcher.HasShutdownStarted)_=dispatcher.BeginInvoke(DispatcherPriority.Send,Dispose);
    }
    internal void RequestRefresh()
    {
        if(disposed||queued||dispatcher.HasShutdownStarted)return;
        queued=true;
        _=dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{queued=false;if(!disposed)Refresh();});
    }
    internal void Refresh()
    {
        if(disposed||process.HasExited)return;
        foreach(var entry in protectedWindows.ToArray())
        {
            var thread=GetWindowThreadProcessId(entry.Key,out var owner);
            if(owner!=processId||thread!=entry.Value){protectedWindows.Remove(entry.Key);continue;}
            Inspect(entry.Key,thread);
        }
        EnumWindows((window,_)=>{
            var thread=GetWindowThreadProcessId(window,out var owner);
            if(owner==processId&&(testWindows is null||testWindows.Contains(window)))Inspect(window,thread);
            return true;
        },0);
    }
    private void Inspect(nint window,uint thread)
    {
        var name=new StringBuilder(64);GetClassName(window,name,name.Capacity);
        long style=GetWindowStyle(window,-16).ToInt64(),extended=GetWindowStyle(window,-20).ToInt64();
        bool hasAlpha=GetLayeredWindowAttributes(window,out _,out var alpha,out var alphaFlags);
        bool invisible=IsInvisibleCompositionSurface(name.ToString(),style,extended,hasAlpha,alpha,alphaFlags);
        bool ours=protectedWindows.ContainsKey(window);
        if(invisible)
        {
            if((extended&Transparent)!=0)return;
            // Re-read ownership immediately before the only mutation. Never
            // alter Jot's WPF host, another app, or an opaque browser popup.
            if(GetWindowThreadProcessId(window,out var owner)!=thread||owner!=processId)return;
            if(!SetStyle(window,extended|Transparent))return;
            protectedWindows[window]=thread;
            if(!ours)log.Event("webview-input-overlay","made-click-through");
        }
        else if(ours)
        {
            // A renderer popup that becomes genuinely visible must regain input.
            // Clear only the bit we added, preserving other live runtime changes.
            if((extended&Transparent)!=0&&!SetStyle(window,extended&~Transparent))return;
            protectedWindows.Remove(window);
        }
    }
    private bool SetStyle(nint window,long style)
    {
        Marshal.SetLastPInvokeError(0);
        if(SetWindowStyle(window,-20,new nint(style))!=0||Marshal.GetLastPInvokeError()==0)return true;
        log.Error("webview-input-guard",new Win32Exception(Marshal.GetLastPInvokeError()));return false;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(var hook in hooks)UnhookWinEvent(hook);
        hooks.Clear();protectedWindows.Clear();process.Exited-=OnProcessExited;process.Dispose();
        // Do not re-enable invisible renderer hit testing during asynchronous
        // browser teardown. These HWNDs belong to the closing browser process.
    }
}

internal sealed partial class JotSession
{
    private readonly Dictionary<int,WebViewInputGuard> webViewInputGuards=[];
    internal void ProtectWebViewInput(CoreWebView2 core)
    {
        int processId=checked((int)core.BrowserProcessId);
        if(webViewInputGuards.TryGetValue(processId,out var existing))
        {
            if(existing.IsAlive){existing.RequestRefresh();return;}
            existing.Dispose();webViewInputGuards.Remove(processId);
        }
        try{webViewInputGuards.Add(processId,new(processId,Dispatcher.CurrentDispatcher,Log));}
        catch(Exception error){Log.Error("webview-input-guard",error);}
    }
    internal void RefreshWebViewInputProtection(){foreach(var guard in webViewInputGuards.Values)guard.RequestRefresh();}
    internal void DisposeWebViewInputProtection(){foreach(var guard in webViewInputGuards.Values)guard.Dispose();webViewInputGuards.Clear();}
}
