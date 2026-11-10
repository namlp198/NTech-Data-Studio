using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>Decision 0017: what a model finds becomes Model-source polygons of the project's classes, matched by name.</summary>
public class AutoLabelServiceTests
{
    private sealed class FakeLabeler : IAutoLabeler
    {
        public string? ModelPath => "m.onnx";
        public string? Unavailable { get; set; }
        public IReadOnlyList<string> ClassNames { get; set; } = new[] { "Wire", "Clip" };
        public List<AutoLabel> Found { get; } = new();
        public double? LastMinScore { get; private set; }
        public void SetModel(string? onnxPath) { }

        public Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default)
        {
            LastMinScore = minScore;
            return Task.FromResult<IReadOnlyList<AutoLabel>>(Found.ToList());
        }
    }

    private static readonly PixelPoint[] Triangle = { new(0, 0), new(10, 0), new(5, 8) };

    private static readonly IReadOnlyList<LabelClass> Classes = new[]
    {
        new LabelClass(7, "wire", "#FF0000", null, 0),
        new LabelClass(8, "Other", "#00FF00", null, 1)
    };

    [Fact]
    public async Task Label_MapsClassesByNameIgnoringCaseAndMarksTheLabelsAsModel()
    {
        var labeler = new FakeLabeler();
        labeler.Found.Add(new AutoLabel("Wire", 0.9, Triangle));
        var service = new AutoLabelService(labeler);

        var result = await service.LabelAsync("a.jpg", 3, Classes, 0.4);

        var annotation = Assert.Single(result.Annotations);
        Assert.Equal(7, annotation.ClassId);
        Assert.Equal(3, annotation.ImageId);
        Assert.Equal(ShapeKind.Polygon, annotation.Kind);
        Assert.Equal(AnnotationSource.Model, annotation.Source);
        Assert.Equal(0.4, labeler.LastMinScore);
    }

    [Fact]
    public async Task Label_LeavesOutClassesTheProjectDoesNotHaveAndSaysWhich()
    {
        var labeler = new FakeLabeler();
        labeler.Found.Add(new AutoLabel("Wire", 0.9, Triangle));
        labeler.Found.Add(new AutoLabel("Clip", 0.8, Triangle));
        var service = new AutoLabelService(labeler);

        var result = await service.LabelAsync("a.jpg", 1, Classes, 0.4);

        Assert.Single(result.Annotations);
        Assert.Equal(new[] { "Clip" }, result.SkippedClasses);
    }

    [Fact]
    public void Check_RefusesWhenNoClassNameMatches()
    {
        var labeler = new FakeLabeler { ClassNames = new[] { "Clip" } };
        var service = new AutoLabelService(labeler);

        Assert.Contains("không class nào", service.Check(Classes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Label_WhenTheModelIsUnavailable_Throws()
    {
        var service = new AutoLabelService(new FakeLabeler { Unavailable = "Chưa chọn model" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LabelAsync("a.jpg", 1, Classes, 0.4));
    }
}
