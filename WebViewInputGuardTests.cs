using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyWebViewInputGuard(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="webview-input-guard-"+name,passed});
        const long signature=WebViewInputGuard.Layered|WebViewInputGuard.NoActivate|WebViewInputGuard.ToolWindow|WebViewInputGuard.NoRedirectionBitmap;
        const long popup=0x80000000;
        bool Candidate(string name="Chrome_WidgetWin_1",long style=popup,long extended=signature,bool alphaAvailable=true,byte alpha=0,uint flags=2)=>
            WebViewInputGuard.IsInvisibleCompositionSurface(name,style,extended,alphaAvailable,alpha,flags);
        Check("matches-observed-zero-alpha-composition-window",Candidate());
        Check("never-matches-wpf-window",!Candidate(name:"HwndWrapper[Jot]"));
        Check("never-matches-opaque-browser-popup",!Candidate(alpha:255));
        Check("never-matches-partially-visible-browser-popup",!Candidate(alpha:1));
        Check("never-matches-child-control",!Candidate(style:popup|0x40000000));
        Check("requires-explicit-layered-alpha",!Candidate(alphaAvailable:false)&&!Candidate(flags:1));
        foreach(var required in new[]{WebViewInputGuard.Layered,WebViewInputGuard.NoActivate,WebViewInputGuard.ToolWindow,WebViewInputGuard.NoRedirectionBitmap})
            Check("requires-signature-style-"+required.ToString("x"),!Candidate(extended:signature&~required));

        using var fixture=new InputGuardFixture();
        using var unrelated=new InputGuardFixture();
        using var alreadyTransparent=new InputGuardFixture(WebViewInputGuard.Transparent);
        using var opaque=new InputGuardFixture(alpha:255);
        long original=fixture.ExtendedStyle;
        using var guard=new WebViewInputGuard(Environment.ProcessId,Dispatcher, new ErrorLog(Path.Combine(testOutput,"input-guard")),[fixture.Handle,alreadyTransparent.Handle,opaque.Handle]);
        Check("offscreen-window-is-made-click-through",(fixture.ExtendedStyle&WebViewInputGuard.Transparent)!=0&&guard.ProtectedCount==1);
        Check("preserves-all-other-renderer-window-styles",fixture.ExtendedStyle==(original|WebViewInputGuard.Transparent));
        Check("other-owned-fixture-is-never-touched",unrelated.ExtendedStyle==original);
        Check("opaque-renderer-keeps-input",(opaque.ExtendedStyle&WebViewInputGuard.Transparent)==0);
        Check("existing-runtime-transparency-is-not-owned",(alreadyTransparent.ExtendedStyle&WebViewInputGuard.Transparent)!=0&&guard.ProtectedCount==1);

        fixture.SetAlpha(255);fixture.NotifyStateChanged();
        await WaitUntil(()=>fixture.ExtendedStyle==original);
        Check("visible-popup-regains-input-through-native-event",fixture.ExtendedStyle==original&&guard.ProtectedCount==0);
        alreadyTransparent.SetAlpha(255);alreadyTransparent.NotifyStateChanged();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check("does-not-remove-runtime-owned-click-through",(alreadyTransparent.ExtendedStyle&WebViewInputGuard.Transparent)!=0);
        fixture.SetAlpha(0);fixture.NotifyStateChanged();
        await WaitUntil(()=>(fixture.ExtendedStyle&WebViewInputGuard.Transparent)!=0);
        Check("subsequent-invisible-surface-is-protected",(fixture.ExtendedStyle&WebViewInputGuard.Transparent)!=0);

        // Renderer recreation/style writes may replace the complete style word.
        fixture.SetExtendedStyle(original);fixture.NotifyStateChanged();
        await WaitUntil(()=>(fixture.ExtendedStyle&WebViewInputGuard.Transparent)!=0);
        Check("recovers-if-runtime-replaces-extended-styles",fixture.ExtendedStyle==(original|WebViewInputGuard.Transparent));
        fixture.SetExtendedStyle(fixture.ExtendedStyle|0x40000);fixture.SetAlpha(255);fixture.NotifyStateChanged();
        await WaitUntil(()=>(fixture.ExtendedStyle&WebViewInputGuard.Transparent)==0);
        Check("visibility-restore-preserves-new-runtime-style-bits",fixture.ExtendedStyle==(original|0x40000));

        fixture.SetAlpha(0);guard.Refresh();guard.Dispose();
        Check("quit-does-not-reenable-invisible-desktop-overlay",(fixture.ExtendedStyle&WebViewInputGuard.Transparent)!=0);
        fixture.SetExtendedStyle(original);fixture.NotifyStateChanged();
        await Task.Delay(40);await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check("disposed-guard-stops-processing-native-events",fixture.ExtendedStyle==original);
        Check("all-native-fixtures-remain-offscreen-and-nonactivating",new[]{fixture,unrelated,alreadyTransparent,opaque}.All(item=>item.IsIsolated));
        Check("test-created-no-live-process-or-user-data-targets",session.Testing);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for(int attempt=0;attempt<50&&!condition();attempt++)await Task.Delay(20);
    }
}

// Native fixtures never inject clicks, move the system cursor, activate windows,
// or enumerate/mutate a live browser. Every HWND is created at -32000 and the
// guard is allowlisted to the exact fixture handles in this isolated process.
internal sealed class InputGuardFixture : IDisposable
{
    private delegate nint WindowProc(nint window,uint message,nint wParam,nint lParam);
    private static readonly WindowProc procedure=DefWindowProc;
    private static bool registered;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size,Style;public nint Procedure;public int ClassExtra,WindowExtra;
        public nint Instance,Icon,Cursor,Background;public string? Menu;public string Class;public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Bounds {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint window,uint message,nint wParam,nint lParam);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")]private static extern bool SetLayeredWindowAttributes(nint window,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out Bounds bounds);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetStyle(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetStyle(nint window,int index,nint style);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll")]private static extern void NotifyWinEvent(uint eventType,nint window,int objectId,int childId);
    internal nint Handle {get;private set;}
    internal long ExtendedStyle=>GetStyle(Handle,-20).ToInt64();
    internal bool IsIsolated=>GetWindowRect(Handle,out var bounds)&&bounds.Right< -10000&&bounds.Bottom< -10000&&(ExtendedStyle&WebViewInputGuard.NoActivate)!=0&&(ExtendedStyle&8)==0;
    internal InputGuardFixture(long extraStyle=0,byte alpha=0)
    {
        var module=GetModuleHandle(null);
        if(!registered)
        {
            var windowClass=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Procedure=Marshal.GetFunctionPointerForDelegate(procedure),Instance=module,Class="Chrome_WidgetWin_1"};
            if(RegisterClassEx(ref windowClass)==0)throw new Win32Exception(Marshal.GetLastPInvokeError());registered=true;
        }
        Handle=CreateWindowEx((uint)(WebViewInputGuard.Layered|WebViewInputGuard.NoActivate|WebViewInputGuard.ToolWindow|WebViewInputGuard.NoRedirectionBitmap|extraStyle),"Chrome_WidgetWin_1","Jot isolated input fixture",0x80000000,-32000,-32000,240,180,0,0,module,0);
        if(Handle==0)throw new Win32Exception(Marshal.GetLastPInvokeError());
        if(!IsIsolated)throw new InvalidOperationException("Native input fixture must stay offscreen.");
        SetAlpha(alpha);ShowWindow(Handle,4);
    }
    internal void SetAlpha(byte alpha)=>SetLayeredWindowAttributes(Handle,0,alpha,2);
    internal void SetExtendedStyle(long style)=>SetStyle(Handle,-20,new nint(style));
    internal void NotifyStateChanged()=>NotifyWinEvent(0x800A,Handle,0,0);
    public void Dispose(){if(Handle!=0){DestroyWindow(Handle);Handle=0;}}
}
