using System.Text;
using System.Text.Json;

namespace NTech.DataStudio.Trainer.Runs;

public sealed record TrainerEvent(long Id, string Timestamp, string Type, JsonElement Data)
{
    public string? Text(string name) =>
        Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public double? Number(string name) =>
        Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}

/// <summary>
/// Reads events.jsonl from a saved offset. Only whole lines count: the worker may be writing the
/// last one, or may have been killed in the middle of it.
/// </summary>
public sealed class EventLogReader(string path)
{
    public const int SupportedSchema = 1;

    public long Offset { get; private set; }

    public IReadOnlyList<TrainerEvent> ReadNew()
    {
        var events = new List<TrainerEvent>();
        if (!File.Exists(path)) return events;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= Offset) return events;
        stream.Seek(Offset, SeekOrigin.Begin);
        var bytes = new byte[stream.Length - Offset];
        stream.ReadExactly(bytes);

        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0) return events;

        foreach (var line in Encoding.UTF8.GetString(bytes, 0, lastNewline).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = Parse(line);
            if (parsed is not null) events.Add(parsed);
        }

        Offset += lastNewline + 1;
        return events;
    }

    private static TrainerEvent? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetInt32() > SupportedSchema) return null;
            return new TrainerEvent(
                root.GetProperty("id").GetInt64(),
                root.GetProperty("ts").GetString() ?? "",
                root.GetProperty("type").GetString() ?? "",
                root.GetProperty("data").Clone());
        }
        catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null; // a torn line left by a killed worker
        }
    }
}

public static class RunStates
{
    public const string Created = "Created";
    public const string Preparing = "Preparing";
    public const string Preflight = "Preflight";
    public const string Running = "Running";
    public const string Evaluating = "Evaluating";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Interrupted = "Interrupted";

    public static bool IsFinal(string state) => state is Completed or Failed or Cancelled;

    /// <summary>
    /// The last state event is the truth. A state that has not ended with no container left means
    /// the worker vanished without telling anyone: power loss, a killed container, Docker stopping.
    /// </summary>
    public static string Derive(string? lastEventState, bool containerRunning)
    {
        var state = lastEventState ?? Created;
        if (IsFinal(state) || containerRunning) return state;
        return lastEventState is null ? Created : Interrupted;
    }

    public static bool CanResume(string state, bool hasCheckpoint) =>
        hasCheckpoint && state is Interrupted or Cancelled;
}
