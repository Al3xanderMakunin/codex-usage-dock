using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodexUsage;

/// <summary>Reads the main Codex quota from token_count events in local rollout logs.</summary>
internal sealed class CodexSessionLogs(string directory)
{
    private static readonly EnumerationOptions SearchOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private Dictionary<string, CachedFile> _cache = new();

    private sealed record CachedFile(long Length, DateTime Modified, UsageSnapshot? Snapshot);

    public UsageSnapshot? ReadLatest()
    {
        var cache = new Dictionary<string, CachedFile>();
        UsageSnapshot? latest = null;
        if (Directory.Exists(directory))
        {
            var cutoff = DateTime.UtcNow.AddDays(-8);
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("rollout-*.jsonl", SearchOptions))
            {
                try
                {
                    var modified = file.LastWriteTimeUtc;
                    if (modified < cutoff)
                    {
                        continue;
                    }

                    var length = file.Length;
                    if (!_cache.TryGetValue(file.FullName, out var entry) ||
                        entry.Modified != modified || entry.Length != length)
                    {
                        entry = new CachedFile(length, modified, ReadFile(file.FullName));
                    }

                    cache.Add(file.FullName, entry);
                    latest = Newer(latest, entry.Snapshot);
                }
                catch (IOException)
                {
                    // A session may be moved or deleted while Codex is running.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        _cache = cache;
        return latest;
    }

    private static UsageSnapshot? ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        UsageSnapshot? latest = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("\"token_count\"", StringComparison.Ordinal) &&
                line.Contains("\"rate_limits\"", StringComparison.Ordinal))
            {
                latest = Newer(latest, Parse(line));
            }
        }

        return latest;
    }

    private static UsageSnapshot? Newer(UsageSnapshot? current, UsageSnapshot? candidate) =>
        candidate is not null && (current is null || candidate.UpdatedAt >= current.UpdatedAt) ? candidate : current;

    private static UsageSnapshot? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var payload = Property(root, "payload");
            var limits = Property(payload, "rate_limits");
            var timestamp = Property(root, "timestamp");
            var id = Property(limits, "limit_id");
            if (IsString(Property(root, "type"), "event_msg") &&
                IsString(Property(payload, "type"), "token_count") &&
                limits.ValueKind == JsonValueKind.Object &&
                (id.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || IsString(id, "codex")) &&
                timestamp.ValueKind == JsonValueKind.String && timestamp.TryGetDateTimeOffset(out var updated))
            {
                var primary = Property(limits, "primary");
                var secondary = Property(limits, "secondary");
                var fiveHour = ParseWindow(primary, 300) ?? ParseWindow(secondary, 300);
                var weekly = ParseWindow(primary, 10080) ?? ParseWindow(secondary, 10080);
                if (fiveHour is not null || weekly is not null)
                {
                    return new UsageSnapshot(fiveHour, weekly, updated);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore incomplete or invalid records.
        }

        return null;
    }

    private static UsageWindow? ParseWindow(JsonElement window, int expectedMinutes)
    {
        var duration = Property(window, "window_minutes");
        var used = Property(window, "used_percent");
        if (duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes) ||
            minutes != expectedMinutes || used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent < 0)
        {
            return null;
        }

        var reset = Property(window, "resets_at");
        DateTimeOffset? resetsAt = reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var seconds) &&
            seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return new UsageWindow(percent, resetsAt);
    }

    private static bool IsString(JsonElement element, string value) =>
        element.ValueKind == JsonValueKind.String && element.ValueEquals(value);

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}
