namespace NTech.DataStudio.Domain.Profiles;

/// <summary>
/// The tasks a project can aim at (decision 0014). Anomaly is left out: it has no labels to draw.
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
}
