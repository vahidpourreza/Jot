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
    public static JsonObject Defaults() => new() { ["theme"] = "dark", ["fontSize"] = 16, ["accent"] = "crimson", ["iconWeight"] = 1.8, ["coloredIcons"] = false, ["toolbarVisible"] = true, ["language"] = "en" };
    private static JsonObject Empty() => new() { ["version"] = 2, ["activeId"] = null, ["notes"] = new JsonArray(), ["prefs"] = Defaults() };

    public async Task<JsonElement?> Load()
    {
        if (!File.Exists(FilePath)) return null;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        Validate(doc.RootElement);
        return doc.RootElement.Clone();
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
                ["id"] = id, ["html"] = "<p dir=\"auto\"><br></p>", ["plain"] = "", ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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
        var title = (patch.GetProperty("title").GetString() ?? "").Trim();
        var group = (patch.GetProperty("group").GetString() ?? "").Trim();
        if (title.Length > 140 || group.Length > 64) throw new InvalidDataException("عنوان یا نام گروه طولانی است.");
        note["title"] = title; note["group"] = group;
    });
    public Task<JsonElement> SavePreferences(JsonElement patch) => Mutate(data => {
        var prefs = data["prefs"] as JsonObject ?? Defaults();
        foreach (var key in new[] { "theme", "fontSize", "accent", "iconWeight", "coloredIcons", "toolbarVisible", "language" })
            if (patch.TryGetProperty(key, out var value)) prefs[key] = JsonNode.Parse(value.GetRawText());
        prefs["language"] = "en";
        data["prefs"] = prefs;
    });
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
