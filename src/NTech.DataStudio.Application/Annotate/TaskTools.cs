using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// Which tools the target tasks of a project allow (decisions 0014 and 0019). Select and Pan always work; a drawing
/// tool works when it makes the shape that task trains on. A project with no task set allows everything.
/// </summary>
public static class TaskTools
{
    /// <summary>A tool works when any task of the project trains on the shape it draws; a project with no task allows everything.</summary>
    public static bool Allows(IReadOnlyList<TaskKind>? targets, AnnotateTool tool) =>
        targets is null || targets.Count == 0 || tool is AnnotateTool.Select or AnnotateTool.Pan || targets.Any(task => Allows(task, tool));

    public static bool Allows(TaskKind? target, AnnotateTool tool) => tool switch
    {
        AnnotateTool.Select or AnnotateTool.Pan => true,
        _ when target is null => true,
        AnnotateTool.Box => target == TaskKind.Detect,
        AnnotateTool.Polygon or AnnotateTool.MagicPolygon => target == TaskKind.Segment,
        AnnotateTool.RotatedBox => target == TaskKind.Obb,
        AnnotateTool.Tag => target == TaskKind.Classify,
        _ => true
    };

    /// <summary>Why a tool is off, for its tooltip; null when it is allowed.</summary>
    public static string? WhyOff(IReadOnlyList<TaskKind>? targets, AnnotateTool tool) =>
        Allows(targets, tool) || targets is null ? null : $"tắt vì project nhắm {TargetTasks.Label(targets)}";

    /// <summary>Why a tool is off for one task; null when it is allowed.</summary>
    public static string? WhyOff(TaskKind? target, AnnotateTool tool) =>
        Allows(target, tool) || target is not { } task ? null : $"tắt vì project nhắm {TargetTasks.Label(task)}";
}
