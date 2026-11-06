using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Annotations;

public enum ShapeKind
{
    Box,
    Polygon,
    RotatedBox,
    Point
}

public enum AnnotationSource
{
    Manual,
    Model
}

/// <summary>
/// One label on one master image, in master pixels. Exporters normalize to
/// 0..1 when a format asks for it. Box and RotatedBox hold their 4 corners in
/// drawing order; Polygon its vertices; Point a single point.
/// </summary>
public sealed record Annotation
{
    public Annotation(
        Guid id,
        long imageId,
        long classId,
        ShapeKind kind,
        IReadOnlyList<PixelPoint> points,
        AnnotationSource source,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var expected = kind switch
        {
            ShapeKind.Box or ShapeKind.RotatedBox => points.Count == 4,
            ShapeKind.Polygon => points.Count >= 3,
            ShapeKind.Point => points.Count == 1,
            _ => false
        };
        if (!expected)
            throw new ArgumentException($"{kind} cannot have {points.Count} point(s).", nameof(points));
        if (points.Any(point => !point.IsFinite))
            throw new ArgumentException("Points must be finite.", nameof(points));

        Id = id;
        ImageId = imageId;
        ClassId = classId;
        Kind = kind;
        Points = points;
        Source = source;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public long ImageId { get; }
    public long ClassId { get; }
    public ShapeKind Kind { get; }
    public IReadOnlyList<PixelPoint> Points { get; }
    public AnnotationSource Source { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public Annotation WithPoints(IReadOnlyList<PixelPoint> points, DateTimeOffset now) =>
        new(Id, ImageId, ClassId, Kind, points, Source, CreatedAt, now);

    public Annotation WithClass(long classId, DateTimeOffset now) =>
        new(Id, ImageId, classId, Kind, Points, Source, CreatedAt, now);

    /// <summary>A new annotation with its own id, for paste and copy from another image.</summary>
    public Annotation CopyTo(long imageId, IReadOnlyList<PixelPoint> points, DateTimeOffset now) =>
        new(Guid.NewGuid(), imageId, ClassId, Kind, points, Source, now, now);
}
