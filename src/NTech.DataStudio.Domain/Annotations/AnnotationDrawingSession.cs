using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Annotations;

/// <summary>
/// Pure state machine for one in-progress shape. The presentation layer owns
/// pointer capture; this type owns committed/candidate points and finish
/// validity. Every point is clamped inside the image.
/// </summary>
public sealed class AnnotationDrawingSession
{
    /// <summary>Smaller than this, in pixels, two points count as the same point.</summary>
    public const double MinPointDistance = 0.5;

    private readonly List<PixelPoint> _vertices = new();
    private readonly double _imageWidth;
    private readonly double _imageHeight;

    public AnnotationDrawingSession(ShapeKind kind, PixelPoint first, double imageWidth, double imageHeight)
    {
        if (!first.IsFinite) throw new ArgumentOutOfRangeException(nameof(first));
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

        Kind = kind;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        var clamped = first.ClampTo(imageWidth, imageHeight);
        _vertices.Add(clamped);
        Candidate = clamped;
    }

    public ShapeKind Kind { get; }
    public IReadOnlyList<PixelPoint> Vertices => _vertices;
    public PixelPoint Candidate { get; private set; }

    /// <summary>A shape built by committing points one after another.</summary>
    public bool IsPath => Kind is ShapeKind.Polygon or ShapeKind.RotatedBox;

    /// <summary>What to draw now, including the point under the pointer.</summary>
    public IReadOnlyList<PixelPoint> PreviewPoints
    {
        get
        {
            switch (Kind)
            {
                case ShapeKind.Box:
                    return BoxCorners();
                case ShapeKind.Point:
                    return new[] { _vertices[0] };
                case ShapeKind.RotatedBox when _vertices.Count >= 2:
                    return RotatedCorners();
                default:
                    var points = _vertices.ToList();
                    if (points[^1].DistanceTo(Candidate) > MinPointDistance)
                        points.Add(Candidate);
                    return points;
            }
        }
    }

    public bool IsSelfIntersecting =>
        Kind == ShapeKind.Polygon && PolygonMath.HasSelfIntersection(PreviewPoints, closed: true);

    public IReadOnlyList<PixelPoint> FinalPoints => Kind switch
    {
        ShapeKind.Box => BoxCorners(),
        ShapeKind.RotatedBox => _vertices.Count >= 2 ? RotatedCorners() : Array.Empty<PixelPoint>(),
        _ => _vertices.ToArray()
    };

    public bool CanFinish
    {
        get
        {
            var points = FinalPoints;
            return Kind switch
            {
                ShapeKind.Box or ShapeKind.RotatedBox => points.Count == 4 && PolygonMath.Area(points) >= 1,
                ShapeKind.Polygon => CountDistinct(points) >= 3
                    && PolygonMath.Area(points) >= 1
                    && !PolygonMath.HasSelfIntersection(points, closed: true),
                ShapeKind.Point => points.Count == 1,
                _ => false
            };
        }
    }

    public void UpdateCandidate(PixelPoint point)
    {
        if (!point.IsFinite) return;
        Candidate = point.ClampTo(_imageWidth, _imageHeight);
    }

    /// <summary>Polygon: adds a vertex. RotatedBox: fixes the main axis; afterwards the candidate sets the width.</summary>
    public bool AddCandidateAsVertex()
    {
        if (!IsPath) return false;
        if (Kind == ShapeKind.RotatedBox && _vertices.Count >= 2) return false;
        if (_vertices[^1].DistanceTo(Candidate) <= MinPointDistance) return false;

        _vertices.Add(Candidate);
        return true;
    }

    public bool UndoLastVertex()
    {
        if (!IsPath || _vertices.Count <= 1) return false;

        _vertices.RemoveAt(_vertices.Count - 1);
        Candidate = _vertices[^1];
        return true;
    }

    private IReadOnlyList<PixelPoint> BoxCorners() => PixelRect.FromCorners(_vertices[0], Candidate).Corners();

    private IReadOnlyList<PixelPoint> RotatedCorners()
    {
        var start = _vertices[0];
        var end = _vertices[1];
        var width = Math.Min(
            RotatedRect.WidthThrough(start, end, Candidate),
            RotatedRect.MaxWidthInside(start, end, _imageWidth, _imageHeight));
        return new RotatedRect(start, end, width).Corners();
    }

    private static int CountDistinct(IReadOnlyList<PixelPoint> points)
    {
        var distinct = new List<PixelPoint>();
        foreach (var point in points)
        {
            if (distinct.All(existing => existing.DistanceTo(point) > MinPointDistance))
                distinct.Add(point);
        }

        return distinct.Count;
    }
}
