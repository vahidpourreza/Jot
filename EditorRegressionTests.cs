using System.IO;
using System.Text.Json;

namespace Jot;

public partial class MainWindow
{
    // The retired contenteditable engine's fixtures manipulate DOM snapshots
    // and private history arrays. The current default suite instead exercises
    // the real editorcn/ProseMirror engine plus native storage/library flows.
    private async Task RunCurrentEditorRegressionTests()
    {
        var checks=new List<object>();
        try
        {
            await VerifyEditorFormatCompatibility(checks);
            await VerifyEditorMigration(checks);
            await VerifySqliteStorage(checks);
            await VerifyTrash(checks);
            await VerifyLibraryRedesign(checks);
            await VerifyShortcutsSettings(checks);
            await VerifyCustomShortcuts(checks);
            var json=JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true});
            await File.WriteAllTextAsync(Path.Combine(testOutput,"results.json"),json);
            using var report=JsonDocument.Parse(json);
            var passed=report.RootElement.EnumerateArray().All(check=>check.GetProperty("passed").GetBoolean());
            ClosePermanently();System.Windows.Application.Current.Shutdown(passed?0:1);
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(testOutput,"partial-results.json"),JsonSerializer.Serialize(checks));
            throw;
        }
    }
}
