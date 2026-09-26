using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jot;

internal sealed class NoteStore(string root)
{
    public string Root { get; } = root;
    public string FilePath => Path.Combine(Root, "notes.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly HashSet<string> NoteColors = new("crimson neutral amber blue cyan emerald fuchsia green indigo lime orange pink purple red rose sky teal violet yellow".Split(' '));
    public static JsonObject Defaults() => new() { ["theme"] = "dark", ["fontSize"] = 16, ["accent"] = "neutral", ["iconWeight"] = 1.8, ["coloredIcons"] = false, ["toolbarVisible"] = true, ["language"] = "en" };
    private static JsonObject Empty() => new() { ["version"] = 2, ["activeId"] = null, ["notes"] = new JsonArray(), ["prefs"] = Defaults() };

    public async Task<JsonElement?> Load()
    {
        if (!File.Exists(FilePath)) return null;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        Validate(doc.RootElement);
        var data = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        var prefs = data["prefs"] as JsonObject ?? Defaults();
        var legacyColor = prefs["accent"]?.GetValue<string>() ?? "neutral";
        // Preserve the old visible header color once; later theme changes cannot recolor notes.
        foreach (var note in data["notes"]!.AsArray())
            if (note?["color"] is null) note!["color"] = NoteColors.Contains(legacyColor) ? legacyColor : "neutral";
        prefs["accent"] = "neutral"; prefs["coloredIcons"] = false;
        data["prefs"] = prefs;
        return JsonSerializer.SerializeToElement(data);
    }
    private async Task Write(JsonElement value)
    {
        Validate(value);
        string json = value.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > 64 * 1024 * 1024)
            throw new InvalidOperationException("حجم یادداشت‌ها از ۶۴ مگابایت بیشتر شده است.");
        Directory.CreateDirectory(Root);
        var temporary = FilePath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
            stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
    }
    public async Task Save(JsonElement value)
    {
        await gate.WaitAsync();
        try { await Write(value); }
        finally { gate.Release(); }
    }
    private async Task<JsonElement> Mutate(Action<JsonObject> mutation)
    {
        await gate.WaitAsync();
        try
        {
            var current = await Load();
            var data = current is null ? Empty() : JsonNode.Parse(current.Value.GetRawText())!.AsObject();
            mutation(data);
            var result = JsonSerializer.SerializeToElement(data);
            await Write(result);
            return result;
        }
        finally { gate.Release(); }
    }
    public async Task<string> Create()
    {
        var id = Guid.NewGuid().ToString();
        await Mutate(data => {
            data["notes"]!.AsArray().Insert(0, new JsonObject {
                ["id"] = id, ["color"] = "crimson", ["html"] = "<p dir=\"auto\"><br></p>", ["plain"] = "", ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            data["activeId"] = id;
        });
        return id;
    }
    public Task<JsonElement> SaveNote(JsonElement note) => Mutate(data => {
        var incoming = JsonNode.Parse(note.GetRawText())!;
        var id = incoming["id"]?.GetValue<string>();
        var notes = data["notes"]!.AsArray();
        var existing = notes.FirstOrDefault(item => item?["id"]?.GetValue<string>() == id);
        if (existing is null) throw new InvalidDataException("یادداشت پیدا نشد؛ متن شما در پنجره حفظ شده است.");
        // Content saves must not overwrite titles/groups edited in the index.
        foreach (var field in new[] { "html", "plain", "updatedAt" })
            existing![field] = incoming[field]?.DeepClone();
        data["activeId"] = id;
    });
    public Task<JsonElement> SaveMetadata(JsonElement patch) => Mutate(data => {
        var id = patch.GetProperty("id").GetString();
        var note = data["notes"]!.AsArray().FirstOrDefault(item => item?["id"]?.GetValue<string>() == id)
            ?? throw new InvalidDataException("یادداشت پیدا نشد.");
        var title = (patch.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : note["title"]?.GetValue<string>())?.Trim() ?? "";
        var group = (patch.TryGetProperty("group", out var groupValue) ? groupValue.GetString() : note["group"]?.GetValue<string>())?.Trim() ?? "";
        if (title.Length > 140 || group.Length > 64) throw new InvalidDataException("عنوان یا نام گروه طولانی است.");
        note["title"] = title; note["group"] = group;
        if (patch.TryGetProperty("color", out var color))
        {
            if (!NoteColors.Contains(color.GetString() ?? "")) throw new InvalidDataException("Choose a color from the note palette.");
            note["color"] = color.GetString();
        }
    });
    public Task<JsonElement> SavePreferences(JsonElement patch) => Mutate(data => {
        var prefs = data["prefs"] as JsonObject ?? Defaults();
        foreach (var key in new[] { "theme", "fontSize", "iconWeight", "toolbarVisible", "language" })
            if (patch.TryGetProperty(key, out var value)) prefs[key] = JsonNode.Parse(value.GetRawText());
        prefs["language"] = "en"; prefs["accent"] = "neutral"; prefs["coloredIcons"] = false;
        data["prefs"] = prefs;
    });
    public async Task Delete(string id)
    {
        await gate.WaitAsync();
        try
        {
            var current = await Load() ?? throw new InvalidDataException("Note not found.");
            var data = JsonNode.Parse(current.GetRawText())!.AsObject();
            var notes = data["notes"]!.AsArray();
            var note = notes.FirstOrDefault(item => item?["id"]?.GetValue<string>() == id)
                ?? throw new InvalidDataException("Note not found.");
            // Write a recoverable full-resolution copy BEFORE changing the live store.
            var trash = Path.Combine(Root, "trash");
            Directory.CreateDirectory(trash);
            var archive = JsonSerializer.SerializeToUtf8Bytes(new { deletedAt = DateTimeOffset.UtcNow, note });
            await using (var stream = new FileStream(Path.Combine(trash, $"{id}-{Guid.NewGuid():N}.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(archive);
                stream.Flush(true);
            }
            notes.Remove(note);
            if (data["activeId"]?.GetValue<string>() == id) data["activeId"] = notes.FirstOrDefault()?["id"]?.DeepClone();
            await Write(JsonSerializer.SerializeToElement(data));
        }
        finally { gate.Release(); }
    }
    public async Task Import(JsonElement data)
    {
        await gate.WaitAsync();
        try { if (!File.Exists(FilePath)) await Write(data); }
        finally { gate.Release(); }
    }
    internal static void Validate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("version", out var version) || version.GetInt32() != 2
            || !value.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ساختار فایل یادداشت معتبر نیست. نسخه اصلی حفظ شد.");
        var ids = new HashSet<string>();
        foreach (var note in notes.EnumerateArray())
            if (!note.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out _) || !ids.Add(id.GetString()!)
                || !note.TryGetProperty("html", out var html) || html.ValueKind != JsonValueKind.String
                || !note.TryGetProperty("plain", out var plain) || plain.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("یک یادداشت معتبر نیست. ذخیره متوقف شد.");
    }
}
