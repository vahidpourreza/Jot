using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jot;

// Local bounded diagnostics. Never record note bodies, clipboard payloads,
// exception messages, URLs, or absolute file paths.
internal sealed class ErrorLog(string root, long maxBytes = 1024 * 1024)
{
    private readonly object gate = new();
    private readonly string runId = Guid.NewGuid().ToString("N");
    public string DirectoryPath { get; } = Path.Combine(root, "logs");
    public string FilePath => Path.Combine(DirectoryPath, "jot-errors.jsonl");
    private static string Token(string? value) => value is not null && Regex.IsMatch(value, "^[a-zA-Z0-9_.:+-]{1,100}$") ? value : "unknown";
    public void Error(string operation, Exception exception, string area = "native")
    {
        var frames = new StackTrace(exception, true).GetFrames()?.Take(16).Select(frame => new {
            method = (frame.GetMethod()?.DeclaringType?.FullName ?? "") + "." + frame.GetMethod()?.Name,
            file = Path.GetFileName(frame.GetFileName()),
            line = frame.GetFileLineNumber()
        });
        Write(new { utc = DateTimeOffset.UtcNow, runId, version = typeof(App).Assembly.GetName().Version?.ToString(),
            severity = "error", operation = Token(operation), area = Token(area),
            exception = exception.GetType().FullName, hresult = $"0x{exception.HResult:X8}", frames });
    }
    public void Event(string operation, string outcome, int attempts = 0) =>
        Write(new { utc = DateTimeOffset.UtcNow, runId, severity = "info",
            operation = Token(operation), outcome = Token(outcome), attempts });
    public void Renderer(JsonElement data)
    {
        string Read(string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? Token(value.GetString()) : "unknown";
        int Number(string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? Math.Clamp(number, 0, 1000000) : 0;
        Write(new { utc = DateTimeOffset.UtcNow, runId, severity = "error", area = "renderer",
            operation = Read("operation"), name = Read("name"), code = Read("code"),
            source = Read("source"), line = Number("line"), column = Number("column") });
    }
    private void Write(object entry)
    {
        try
        {
            var json = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + Encoding.UTF8.GetByteCount(json) > maxBytes)
                {
                    // Only rotate the app's four explicitly named diagnostic files.
                    for (int i = 2; i >= 1; i--)
                    {
                        var from = Path.Combine(DirectoryPath, $"jot-errors.{i}.jsonl");
                        var to = Path.Combine(DirectoryPath, $"jot-errors.{i + 1}.jsonl");
                        if (File.Exists(from)) File.Move(from, to, true);
                    }
                    File.Move(FilePath, Path.Combine(DirectoryPath, "jot-errors.1.jsonl"), true);
                }
                File.AppendAllText(FilePath, json, new UTF8Encoding(false));
            }
        }
        catch { /* A logging failure must never interrupt editing or error recovery. */ }
    }
}
