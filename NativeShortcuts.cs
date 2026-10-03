using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Interop;

namespace Jot;

// Keep registration transactional and injectable: tests never reserve real keys.
internal sealed class ShortcutRegistration : IDisposable
{
    internal const int NewNoteId=0xB401, ShowJotId=0xB402;
    private readonly Func<int,uint,uint,bool> register;
    private readonly Action<int> unregister;
    private readonly Dictionary<int,(uint Modifiers,uint Key,string Chord)> registered=[];
    private bool enabled;
    internal ShortcutRegistration(Func<int,uint,bool> register,Action<int> unregister):this((id,modifiers,key)=>register(id,key),unregister){}
    internal ShortcutRegistration(Func<int,uint,uint,bool> register,Action<int> unregister){this.register=register;this.unregister=unregister;}
    internal bool Enabled=>enabled;
    internal void SetEnabled(bool value)=>Configure(value,new JsonObject());
    internal void Configure(bool value,JsonObject overrides)
    {
        var resolved=ShortcutBindings.Resolve(overrides);
        var desired=new Dictionary<int,(uint Modifiers,uint Key,string Chord)>();
        if(value)foreach(var item in new[]{(NewNoteId,"global-new-note"),(ShowJotId,"global-show-jot")})
        {
            if(resolved[item.Item2] is not { } chord)continue;
            var native=ShortcutBindings.NativeChord(chord);desired[item.Item1]=(native.Modifiers,native.Key,chord);
        }
        if(enabled==value&&registered.Count==desired.Count&&registered.All(pair=>desired.TryGetValue(pair.Key,out var key)&&key==pair.Value))return;
        var previous=new Dictionary<int,(uint Modifiers,uint Key,string Chord)>(registered);var wasEnabled=enabled;
        Release();
        try
        {
            foreach(var pair in desired)
            {
                if(!register(pair.Key,pair.Value.Modifiers,pair.Value.Key))throw new InvalidOperationException(ShortcutBindings.Label(pair.Value.Chord)+" is already in use by another app.");
                registered.Add(pair.Key,pair.Value);
            }
            enabled=value;
        }
        catch(Exception error)
        {
            Release();var restored=true;
            foreach(var pair in previous){if(register(pair.Key,pair.Value.Modifiers,pair.Value.Key))registered.Add(pair.Key,pair.Value);else restored=false;}
            enabled=wasEnabled&&restored;
            if(!restored){Release();throw new InvalidOperationException(error.Message+" The previous shortcuts could not be restored; Windows shortcuts are off. Turn them on again to retry.",error);}
            throw new InvalidOperationException(error.Message+(wasEnabled?" Your previous shortcuts are still active.":" Windows shortcuts are still off."),error);
        }
    }
    internal bool Contains(int id)=>registered.ContainsKey(id);
    private void Release(){foreach(var id in registered.Keys)unregister(id);registered.Clear();enabled=false;}
    public void Dispose()=>Release();
}

internal sealed class NativeShortcuts : IDisposable
{
    private readonly HwndSource source;
    private readonly ShortcutRegistration registration;
    private readonly Action<string> invoke;
    [DllImport("user32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint virtualKey);
    [DllImport("user32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window,int id);
    internal NativeShortcuts(Action<string> invoke)
    {
        this.invoke=invoke;
        // A message-only host survives individual note/workspace closes and
        // never appears in the taskbar, window list, or on the desktop.
        source=new HwndSource(new HwndSourceParameters("Jot shortcut host"){ParentWindow=new nint(-3),WindowStyle=0,Width=0,Height=0});
        registration=new((id,modifiers,key)=>RegisterHotKey(source.Handle,id,modifiers,key),id=>UnregisterHotKey(source.Handle,id));
        source.AddHook(Message);
    }
    internal bool Enabled=>registration.Enabled;
    internal void SetEnabled(bool enabled)=>registration.SetEnabled(enabled);
    internal void Configure(bool enabled,JsonObject overrides)=>registration.Configure(enabled,overrides);
    private nint Message(nint window,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message==0x0312&&registration.Contains((int)wParam))
        {
            handled=true;invoke((int)wParam==ShortcutRegistration.NewNoteId?"new-note":"show-jot");
        }
        return 0;
    }
    public void Dispose(){registration.Dispose();source.RemoveHook(Message);source.Dispose();}
}

internal sealed partial class JotSession
{
    private NativeShortcuts? nativeShortcuts;
    private bool globalShortcutsRequested;
    private string? globalShortcutError;
    internal object ShortcutStatus=>new{enabled=globalShortcutsRequested,active=nativeShortcuts?.Enabled==true,error=globalShortcutError,testing=Testing};
    internal void ConfigureGlobalShortcuts(bool enabled)
        =>ConfigureGlobalShortcuts(enabled,new JsonObject());
    internal void ConfigureGlobalShortcuts(bool enabled,JsonObject overrides)
    {
        if(Testing){globalShortcutsRequested=enabled;globalShortcutError=null;return;}
        try
        {
            if(enabled){nativeShortcuts??=new NativeShortcuts(InvokeGlobalShortcut);nativeShortcuts.Configure(true,overrides);}
            else nativeShortcuts?.SetEnabled(false);
            globalShortcutsRequested=enabled;globalShortcutError=null;
        }
        catch(Exception error){globalShortcutsRequested=nativeShortcuts?.Enabled==true;globalShortcutError=error.Message;throw;}
        finally{foreach(var window in Windows.ToArray())window.Post(new{@event="shortcuts-status",status=ShortcutStatus});}
    }
    internal async Task RefreshGlobalShortcuts()
    {
        var prefs=await Store.LoadPreferences();
        try{ConfigureGlobalShortcuts(prefs.TryGetProperty("globalShortcuts",out var enabled)&&enabled.ValueKind==JsonValueKind.True,ShortcutBindings.Overrides(prefs));}
        catch(Exception error){Log.Error("global-shortcuts",error);}
    }
    private async void InvokeGlobalShortcut(string command)
    {
        if(quitting)return;
        try{if(command=="new-note")await NewNote();else Home();}
        catch(Exception error){Log.Error("global-shortcut",error);if(!quitting)Home().ShowWarning("Could not complete the shortcut. Your notes are safe; please try again.");}
    }
    internal void DisposeGlobalShortcuts(){nativeShortcuts?.Dispose();nativeShortcuts=null;globalShortcutsRequested=false;}
    internal async Task OpenShortcuts()
    {
        var window=Settings();await window.ShowShortcutsPage();
    }
}

public partial class MainWindow
{
    internal async Task ShowShortcutsPage()
    {
        if(Mode!="home")throw new InvalidOperationException("Shortcuts are available in Settings.");
        await SwitchHomeView(true);Post(new{@event="shortcuts-open"});
    }
    internal async Task HandleAppShortcut(string command)
    {
        if(Mode is not ("home" or "note")||closingPermanently)return;
        await session.WaitForOpeningModeChange();await session.WaitForLibraryOperations();
        switch(command)
        {
            case "new-note":await Flush();await session.NewNote();break;
            case "new-tab":
                await Flush();var workspace=Mode=="home"?this:session.Home();
                await session.NewTab(workspace);break;
            case "reopen-tab":
                var tabsHost=Mode=="home"?this:session.Windows.FirstOrDefault(window=>window.Mode=="home");
                if(tabsHost is not null&&tabsHost.CanReopenClosedTab){tabsHost.Reveal();await session.ReopenClosedTab(tabsHost);}break;
            case "close":
                if(Mode=="note")await HideAfterSaving();
                else if(ActiveWorkspaceTab!="home")await CloseNoteTab(ActiveWorkspaceTab);
                break;
            case "settings":session.Settings();break;
            case "home":session.Home();break;
            case "shortcuts":await session.OpenShortcuts();break;
            case "next-tab":case "previous-tab":
                if(Mode!="home")break;
                var tabs=ShortcutTabs();var index=Array.IndexOf(tabs,ActiveWorkspaceTab);
                await SwitchNoteTab(tabs[(index+(command=="next-tab"?1:tabs.Length-1))%tabs.Length]);break;
            default:
                if(command.StartsWith("tab-",StringComparison.Ordinal)&&int.TryParse(command.AsSpan(4),out var position)&&position>=1&&position<=9)
                {
                    if(Mode!="home")break;
                    var ordered=ShortcutTabs();var target=position==9?ordered.Length-1:position-1;
                    if(target<ordered.Length)await SwitchNoteTab(ordered[target]);break;
                }
                throw new InvalidDataException("Unknown keyboard shortcut.");
        }
    }
    private string[] ShortcutTabs()=>new[]{"home"}.Concat(NoteTabIds).Concat(SettingsTabOpen?new[]{"settings"}:[]).ToArray();
}
