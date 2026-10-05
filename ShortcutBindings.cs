using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

// Physical-key chords are independent of the current English/Persian layout.
// This catalog is sent to the renderer; labels/defaults are not duplicated in JS.
internal static class ShortcutBindings
{
    internal sealed record Action(string Id,string Label,string Group,string Scope,string DefaultChord,string? Note=null);
    internal static readonly Action[] Actions=[
        new("new-note","New note","Notes & navigation","app","Ctrl+KeyN","Uses your Open mode setting."),
        new("new-tab","New tab","Notes & navigation","app","Ctrl+KeyT"),
        new("reopen-tab","Reopen closed tab","Tabs","app","Ctrl+Shift+KeyT","Restores the last closed note tab; deleted notes are not reopened."),
        new("close","Close current note or tab","Notes & navigation","app","Ctrl+KeyW","Home stays open. Your note stays in the library."),
        new("next-tab","Next tab","Notes & navigation","app","Ctrl+Tab","Hold Ctrl to preview recent tabs; release to switch."),
        new("previous-tab","Previous tab","Notes & navigation","app","Ctrl+Shift+Tab","Cycle backward in the tab chooser."),
        new("home","Home","Notes & navigation","app","Ctrl+Shift+KeyH"),
        new("settings","Settings","Notes & navigation","app","Ctrl+Comma"),
        new("shortcuts","Keyboard shortcuts","Notes & navigation","app","Ctrl+Shift+KeyP"),
        ..Enumerable.Range(1,9).Select(i=>new Action("tab-"+i,i==9?"Go to last tab":"Go to tab "+i,"Tabs","app","Ctrl+Digit"+i,i==1?"Home is tab 1.":null)),
        new("file-open","Open file","Files","file","Ctrl+KeyO"),
        new("file-save","Save file","Files","file","Ctrl+KeyS","Updates the linked file; library drafts save automatically."),
        new("file-save-as","Save as","Files","file","Ctrl+Shift+KeyS","Choose a name, location, and file type."),
        new("note-text-larger","Increase note text size","Writing","writing","Ctrl+Equal","Also Ctrl+mouse wheel up. Changes only this note."),
        new("note-text-smaller","Decrease note text size","Writing","writing","Ctrl+Minus","Also Ctrl+mouse wheel down. Changes only this note."),
        new("note-text-default","Use default text size","Writing","writing","Ctrl+Digit0","Follow the app's writing default again."),
        new("global-new-note","New note","Windows shortcuts","global","Ctrl+Alt+KeyN"),
        new("global-show-jot","Show Jot","Windows shortcuts","global","Ctrl+Alt+KeyJ")
    ];
    internal static readonly string[] Keys=[..Enumerable.Range('A',26).Select(i=>"Key"+(char)i),..Enumerable.Range(0,10).Select(i=>"Digit"+i),..Enumerable.Range(1,11).Select(i=>"F"+i),"Tab","Comma","Period","Slash","Semicolon","Quote","BracketLeft","BracketRight","Backslash","Minus","Equal"];
    internal static readonly string[] Reserved=["Ctrl+KeyA","Ctrl+KeyB","Ctrl+KeyC","Ctrl+KeyI","Ctrl+KeyU","Ctrl+KeyV","Ctrl+KeyX","Ctrl+KeyY","Ctrl+KeyZ","Ctrl+Shift+KeyA","Ctrl+Shift+KeyB","Ctrl+Shift+KeyC","Ctrl+Shift+KeyI","Ctrl+Shift+KeyU","Ctrl+Shift+KeyV","Ctrl+Shift+KeyX","Ctrl+Shift+KeyY","Ctrl+Shift+KeyZ","Alt+Tab","Alt+Shift+Tab","Alt+F4","Ctrl+F4","Ctrl+Shift+Tab","Ctrl+Tab","Ctrl+Alt+Tab","Ctrl+Alt+Shift+Tab"];
    internal static string Label(string chord)=>chord.Replace("Key","").Replace("Digit","").Replace("Comma",",").Replace("Period",".").Replace("Slash","/").Replace("Semicolon",";").Replace("Quote","'").Replace("BracketLeft","[").Replace("BracketRight","]").Replace("Backslash","\\").Replace("Minus","−").Replace("Equal","=");
    internal static JsonObject Overrides(JsonElement prefs)=>prefs.TryGetProperty("shortcutBindings",out var value)?ValidateOverrides(value):new();
    internal static void NormalizeStored(JsonObject prefs)
    {
        // Older profiles have no overrides. A corrupt/imported map must never
        // prevent opening notes or silently register fallback Windows keys.
        if(!prefs.ContainsKey("shortcutBindings"))prefs["shortcutBindings"]=new JsonObject();
        // A newly introduced default must not erase an older user assignment.
        // Keep Ctrl+Shift+T where the user put it and leave Reopen unassigned.
        if(prefs["shortcutBindings"] is JsonObject stored&&!stored.ContainsKey("reopen-tab")&&
           stored.Any(pair=>pair.Value is JsonValue value&&value.TryGetValue<string>(out var chord)&&chord=="Ctrl+Shift+KeyT"))stored["reopen-tab"]=null;
        if(prefs["shortcutBindings"] is JsonObject writingBindings)
            foreach(var action in Actions.Where(action=>action.Scope=="writing"))
                if(!writingBindings.ContainsKey(action.Id)&&writingBindings.Any(pair=>pair.Value is JsonValue value&&value.TryGetValue<string>(out var chord)&&chord==action.DefaultChord))
                    writingBindings[action.Id]=null;
        try{prefs["shortcutBindings"]=ValidateOverrides(JsonSerializer.SerializeToElement(prefs["shortcutBindings"]));}
        catch(InvalidDataException){prefs["shortcutBindings"]=new JsonObject();prefs["globalShortcuts"]=false;}
        if(prefs["globalShortcuts"] is not JsonValue flag||!flag.TryGetValue<bool>(out _))prefs["globalShortcuts"]=false;
    }
    internal static JsonObject ValidateOverrides(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object||value.GetRawText().Length>8192)throw new InvalidDataException("Choose valid keyboard shortcuts.");
        var result=new JsonObject();
        foreach(var field in value.EnumerateObject())
        {
            var action=Actions.FirstOrDefault(a=>a.Id==field.Name)??throw new InvalidDataException("Unknown keyboard shortcut.");
            if(result.ContainsKey(field.Name))throw new InvalidDataException("Duplicate keyboard shortcut setting.");
            if(field.Value.ValueKind==JsonValueKind.Null){result[field.Name]=null;continue;}
            if(field.Value.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Choose a valid key combination.");
            var chord=field.Value.GetString()!;ValidateChord(action,chord);result[field.Name]=chord;
        }
        var used=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var action in Actions)
        {
            var chord=result.ContainsKey(action.Id)?result[action.Id]?.GetValue<string>():action.DefaultChord;if(chord is null)continue;
            if(used.TryGetValue(chord,out var other))throw new InvalidDataException(Label(chord)+" is already assigned to "+other+".");
            used[chord]=action.Label;
        }
        return result;
    }
    internal static void ValidateChord(Action action,string chord)
    {
        if(chord.Length>64)throw new InvalidDataException("Choose a valid key combination.");
        var parts=chord.Split('+');var key=parts[^1];var modifiers=parts[..^1];
        var ctrl=modifiers.Contains("Ctrl");var alt=modifiers.Contains("Alt");var shift=modifiers.Contains("Shift");
        var canonical=(ctrl?"Ctrl+":"")+(alt?"Alt+":"")+(shift?"Shift+":"")+key;
        if(chord!=canonical||!Keys.Contains(key)||(!ctrl&&!alt&&!key.StartsWith('F'))||(!ctrl&&!alt&&shift))
            throw new InvalidDataException("Use Ctrl or Alt with a key, or a function key (F1–F11).");
        if(action.Scope!="global"&&ctrl&&alt)throw new InvalidDataException("Ctrl+Alt is reserved for AltGr typing in app shortcuts.");
        if(action.Scope=="global"&&!ctrl&&!alt)throw new InvalidDataException("Windows shortcuts need Ctrl or Alt so ordinary function keys stay available.");
        if(Reserved.Contains(chord)&&chord!=action.DefaultChord||key=="Tab"&&action.Id is not ("next-tab" or "previous-tab"))
            throw new InvalidDataException(Label(chord)+" is reserved for standard text editing or Windows controls.");
    }
    internal static Dictionary<string,string?> Resolve(JsonObject overrides)=>Actions.ToDictionary(a=>a.Id,a=>overrides.ContainsKey(a.Id)?overrides[a.Id]?.GetValue<string>():a.DefaultChord);
    internal static object Config(JsonElement prefs)
    {
        var overrides=Overrides(prefs);
        return new{actions=Actions.Select(a=>new{id=a.Id,label=a.Label,group=a.Group,scope=a.Scope,defaultChord=a.DefaultChord,note=a.Note}),bindings=Resolve(overrides),overrides,keys=Keys,reserved=Reserved};
    }
    internal static (uint Modifiers,uint Key) NativeChord(string chord)
    {
        var parts=chord.Split('+');uint modifiers=0x4000;if(parts.Contains("Ctrl"))modifiers|=2;if(parts.Contains("Alt"))modifiers|=1;if(parts.Contains("Shift"))modifiers|=4;
        var key=parts[^1];uint code=key.StartsWith("Key",StringComparison.Ordinal)?key[3]:key.StartsWith("Digit",StringComparison.Ordinal)?key[5]:key.StartsWith('F')?(uint)(111+int.Parse(key.AsSpan(1))):key switch{"Tab"=>9,"Comma"=>188,"Period"=>190,"Slash"=>191,"Semicolon"=>186,"Quote"=>222,"BracketLeft"=>219,"BracketRight"=>221,"Backslash"=>220,"Minus"=>189,"Equal"=>187,_=>throw new InvalidDataException("Unknown shortcut key.")};
        return(modifiers,code);
    }
}
