using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// Which tools a project's target task allows (decision 0014). Select and Pan always work; a drawing
/// tool works when it makes the shape that task trains on. A project with no task set allows everything.
/// </summary>
public static class TaskTools
{
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
    public static string? WhyOff(TaskKind? target, AnnotateTool tool) =>
        Allows(target, tool) || target is not { } task ? null : $"tắt vì project nhắm {TargetTasks.Label(task)}";
}
