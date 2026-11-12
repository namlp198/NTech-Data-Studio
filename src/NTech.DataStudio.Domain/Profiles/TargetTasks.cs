namespace NTech.DataStudio.Domain.Profiles;

/// <summary>
/// The tasks a project can aim at (decisions 0014 and 0019). A project aims at a set of them (none, one or several).
/// Anomaly is left out: it has no labels to draw.
/// </summary>
public static class TargetTasks
{
    public static IReadOnlyList<TaskKind> All { get; } =
        new[] { TaskKind.Detect, TaskKind.Segment, TaskKind.Obb, TaskKind.Classify };

    public static string Label(TaskKind task) => task switch
    {
        TaskKind.Detect => "Detect (box)",
        TaskKind.Segment => "Segment (polygon)",
        TaskKind.Obb => "OBB (box xoay)",
        TaskKind.Classify => "Classify (tag ảnh)",
        TaskKind.Anomaly => "Anomaly",
        _ => task.ToString()
    };

    /// <summary>The key stored in the project database.</summary>
    public static string Key(TaskKind task) => task.ToString().ToLowerInvariant();

    /// <summary>Reads a stored key; null for a missing, empty or unknown one (the task is then not set).</summary>
    public static TaskKind? Parse(string? key) =>
        Enum.TryParse<TaskKind>(key, ignoreCase: true, out var task) && All.Contains(task) ? task : null;

    /// <summary>The tasks without repeats, only those a project can aim at, in the order of <see cref="All"/>.</summary>
    public static IReadOnlyList<TaskKind> Normalize(IEnumerable<TaskKind>? tasks)
    {
        var wanted = tasks?.ToList();
        return wanted is null ? Array.Empty<TaskKind>() : All.Where(task => wanted.Contains(task)).ToArray();
    }

    /// <summary>The key stored in the project database for a set: the keys joined by commas, empty for none.</summary>
    public static string Key(IEnumerable<TaskKind> tasks) => string.Join(',', Normalize(tasks).Select(Key));

    /// <summary>Reads a stored set; unknown keys are dropped, so a missing or empty one gives no task.</summary>
    public static IReadOnlyList<TaskKind> ParseSet(string? key) =>
        Normalize((key ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse).OfType<TaskKind>().ToList());

    /// <summary>For messages: the labels joined with a plus sign.</summary>
    public static string Label(IEnumerable<TaskKind> tasks) => string.Join(" + ", Normalize(tasks).Select(Label));
}
