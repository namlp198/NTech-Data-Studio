using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <param name="Boxes">The boxes that go away.</param>
/// <param name="Polygons">What takes their place, one per box and in the same order, with the same class.</param>
/// <param name="CutOut">How many polygons SAM cut out of the picture.</param>
/// <param name="RectangleOnly">How many boxes SAM could not cut out: they become a four-corner polygon so the image holds no box.</param>
public sealed record BoxConversion(IReadOnlyList<Annotation> Boxes, IReadOnlyList<Annotation> Polygons, int CutOut, int RectangleOnly);

/// <summary>
/// Turns the boxes of an image into polygons with SAM (decision 0022): each box is the prompt that tells SAM which object to cut out.
/// No detect model is involved, so the boxes (drawn or found earlier by a detect model) are what is converted. A box SAM cannot cut
/// out becomes its own rectangle as a polygon, never stays a box, so the image ends up with one kind of shape.
/// </summary>
public sealed class BoxToPolygonConverter
{
    private readonly ISmartSegmenter _segmenter;

    public BoxToPolygonConverter(ISmartSegmenter segmenter)
    {
        _segmenter = segmenter;
    }

    /// <summary>Null when SAM is ready; otherwise what is missing, in words for the user.</summary>
    public string? Unavailable => _segmenter.Unavailable;

    /// <exception cref="InvalidOperationException">SAM is not available or cannot read the picture.</exception>
    public async Task<BoxConversion> ConvertAsync(
        string imagePath,
        int imageWidth,
        int imageHeight,
        IReadOnlyList<Annotation> boxes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (Unavailable is { } why) throw new InvalidOperationException(why);
        if (boxes.Count == 0) return new BoxConversion(boxes, Array.Empty<Annotation>(), 0, 0);

        var rectangles = boxes.ToDictionary(box => box.Id, box => PolygonMath.BoundingBox(box.Points));
        var planned = boxes.Select(box => new DetectedBox(box.Id.ToString("N"), 1, rectangles[box.Id])).ToList();

        var outlines = new Dictionary<string, IReadOnlyList<PixelPoint>?>();
        foreach (var group in SamRegionPlanner.Plan(imageWidth, imageHeight, planned))
        {
            foreach (var found in group.Boxes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outlines[found.ClassName] = await BoxCutter.CutOutAsync(_segmenter, imagePath, group.Region, found.Box, cancellationToken);
            }
        }

        var polygons = new List<Annotation>(boxes.Count);
        var cut = 0;
        foreach (var box in boxes)
        {
            var outline = outlines.GetValueOrDefault(box.Id.ToString("N"));
            if (outline is not null)
            {
                cut++;
                polygons.Add(new Annotation(Guid.NewGuid(), box.ImageId, box.ClassId, ShapeKind.Polygon, outline, AnnotationSource.Model, now, now));
            }
            else
            {
                polygons.Add(new Annotation(Guid.NewGuid(), box.ImageId, box.ClassId, ShapeKind.Polygon, rectangles[box.Id].Corners(), box.Source, now, now));
            }
        }

        return new BoxConversion(boxes, polygons, cut, boxes.Count - cut);
    }
}
