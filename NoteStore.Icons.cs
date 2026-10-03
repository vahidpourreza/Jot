using System.IO;

namespace Jot;

internal sealed partial class NoteStore
{
    internal const string DefaultNoteIcon="emoji:📝";
    private static readonly HashSet<string> NoteIconNames=new("notepad-text book-open briefcase-business code-xml lightbulb list-checks calendar-days heart star flag coffee music-2".Split(' '),StringComparer.Ordinal);
    private static readonly Lazy<HashSet<string>> EmojiValues=new(LoadEmojiValues);

    internal static string NormalizeNoteIcon(string? value)=>IsValidNoteIcon(value)?value!:DefaultNoteIcon;
    internal static string ValidateNoteIcon(string? value)=>IsValidNoteIcon(value)?value!:throw new InvalidDataException("Choose a single emoji from the emoji picker.");

    private static bool IsValidNoteIcon(string? value)
    {
        if(value is null)return false;
        // Preserve original IDs in old notes/files; only their UI changes.
        if(value.StartsWith("icon:",StringComparison.Ordinal))return NoteIconNames.Contains(value[5..]);
        return value.StartsWith("emoji:",StringComparison.Ordinal)&&EmojiValues.Value.Contains(value[6..]);
    }

    private static HashSet<string> LoadEmojiValues()
    {
        using var stream=typeof(NoteStore).Assembly.GetManifestResourceStream("Jot.EmojiValues")??throw new InvalidDataException("The bundled emoji catalogue is missing.");
        using var reader=new StreamReader(stream);
        return new HashSet<string>(reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries),StringComparer.Ordinal);
    }
}
