using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

public partial class MainWindow
{
    private async Task VerifyReopenShortcutMigration(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="reopen-shortcut-"+name,passed});
        const string chord="Ctrl+Shift+KeyT";
        Check("new-profile-gets-standard-browser-binding",ShortcutBindings.Resolve(new())["reopen-tab"]==chord);
        foreach(var existingAction in new[]{"new-note","file-save","global-new-note"})
        {
            var prefs=new JsonObject{["globalShortcuts"]=true,["shortcutBindings"]=new JsonObject{[existingAction]=chord,["global-show-jot"]="Ctrl+Alt+KeyG",["close"]=null}};
            ShortcutBindings.NormalizeStored(prefs);var overrides=prefs["shortcutBindings"]!.AsObject();var effective=ShortcutBindings.Resolve(overrides);
            Check("new-default-preserves-legacy-assignment-"+existingAction,effective[existingAction]==chord&&effective["reopen-tab"] is null&&effective["global-show-jot"]=="Ctrl+Alt+KeyG"&&effective["close"] is null&&prefs["globalShortcuts"]!.GetValue<bool>()&&overrides.Count==4);
            var before=prefs.ToJsonString();ShortcutBindings.NormalizeStored(prefs);
            Check("migration-idempotent-"+existingAction,prefs.ToJsonString()==before);
        }
        var assigned=new JsonObject{["globalShortcuts"]=false,["shortcutBindings"]=new JsonObject{["file-save"]=chord,["reopen-tab"]="Ctrl+Shift+KeyR"}};
        ShortcutBindings.NormalizeStored(assigned);
        Check("explicit-reopen-choice-is-kept",ShortcutBindings.Resolve(assigned["shortcutBindings"]!.AsObject())["reopen-tab"]=="Ctrl+Shift+KeyR");
        var store=new NoteStore(Path.Combine(testOutput,"reopen-shortcut-migration","data"));await store.LoadPreferences();
        // Emulate an older database before Reopen existed; no live shortcuts are registered.
        await SetStoreTrigger(store,"INSERT INTO settings(key,value) VALUES('shortcutBindings','{\"new-note\":\"Ctrl+Shift+KeyT\",\"file-save\":\"Ctrl+Shift+KeyD\",\"close\":null}') ON CONFLICT(key) DO UPDATE SET value=excluded.value;");
        var loaded=await store.LoadPreferences();
        Check("legacy-database-load-preserves-entire-map",loaded.GetProperty("shortcutBindings").GetProperty("new-note").GetString()==chord&&loaded.GetProperty("shortcutBindings").GetProperty("file-save").GetString()=="Ctrl+Shift+KeyD"&&loaded.GetProperty("shortcutBindings").GetProperty("close").ValueKind==JsonValueKind.Null&&loaded.GetProperty("shortcutBindings").GetProperty("reopen-tab").ValueKind==JsonValueKind.Null);
        await store.SavePreferences(JsonSerializer.SerializeToElement(new{fontSize=18}));var reopened=await new NoteStore(store.Root).LoadPreferences();
        Check("next-save-persists-safe-migration",reopened.GetProperty("shortcutBindings").GetRawText()==loaded.GetProperty("shortcutBindings").GetRawText()&&reopened.GetProperty("fontSize").GetInt32()==18);
    }
}
