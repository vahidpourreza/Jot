using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;
internal sealed partial class JotSession
{
    private async Task<JsonElement> SaveAppPreferences(JsonElement patch)
    {
        var previous=await Store.LoadPreferences();
        var configureKeys=patch.TryGetProperty("globalShortcuts",out var keys)||patch.TryGetProperty("shortcutBindings",out _);
        if(configureKeys)
        {
            var overrides=patch.TryGetProperty("shortcutBindings",out var custom)?ShortcutBindings.ValidateOverrides(custom):ShortcutBindings.Overrides(previous);
            var enabled=keys.ValueKind is JsonValueKind.True or JsonValueKind.False?keys.GetBoolean():previous.GetProperty("globalShortcuts").GetBoolean();
            ConfigureGlobalShortcuts(enabled,overrides);
        }
        JsonElement result;
        try{result=await Store.SavePreferences(patch);}
        catch{if(configureKeys)ConfigureGlobalShortcuts(previous.GetProperty("globalShortcuts").GetBoolean(),ShortcutBindings.Overrides(previous));throw;}
        if(patch.TryGetProperty("autoSaveFiles",out _)||patch.TryGetProperty("fontSize",out _)||patch.TryGetProperty("lineHeight",out _))await ConfigureFileAutoSave(retryFailures:patch.TryGetProperty("autoSaveFiles",out _));
        return result;
    }
}
