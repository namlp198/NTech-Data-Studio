using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Annotate;

public class ClassServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly AnnotateFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static Annotation BoxOf(long imageId, long classId, double x = 10) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Box, new PixelRect(x, 10, 30, 30).Corners(), AnnotationSource.Manual, Now, Now);

    [Fact]
    public void Add_TakesPaletteColorsInOrderAndTheLowestFreeHotkey()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);

        var first = classes.Add("connector");
        var second = classes.Add("tape");

        Assert.Equal(("#4A90E2", 1, 0), (first.Color, first.Hotkey, first.Order));
        Assert.Equal(("#E25D5D", 2, 1), (second.Color, second.Hotkey, second.Order));
    }

    [Fact]
    public void Add_ColorsCycleAfterTenAndHotkeysStopAfterNine()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);

        var all = Enumerable.Range(1, 11).Select(index => classes.Add($"class {index}")).ToList();

        Assert.Equal(all[0].Color, all[10].Color);
        Assert.Equal(9, all[8].Hotkey);
        Assert.Null(all[9].Hotkey);
        Assert.Null(all[10].Hotkey);
    }

    [Fact]
    public void Add_ReusesAFreedHotkey()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var first = classes.Add("a");
        classes.Add("b");

        classes.Delete(first.Id);

        Assert.Equal(1, classes.Add("c").Hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_RejectsBlankNames(string name)
    {
        using var store = _fixture.OpenStore();

        Assert.Throws<ProjectException>(() => new ClassService(store).Add(name));
    }

    [Fact]
    public void Add_RejectsDuplicatesIgnoringCaseAndTooLongNames()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        classes.Add("Connector");

        Assert.Throws<ProjectException>(() => classes.Add("  connector "));
        Assert.Throws<ProjectException>(() => classes.Add(new string('x', 65)));
        Assert.Single(classes.Load());
    }

    [Fact]
    public void Rename_KeepsTheClassAndItsLabels()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var service = _fixture.Service(store);
        var cls = classes.Add("connector");
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxOf(1, cls.Id) }));

        classes.Rename(cls.Id, "  Đầu nối ");

        Assert.Equal("Đầu nối", classes.Load().Single().Name);
        Assert.Equal(1, classes.LoadCounts()[cls.Id]);
    }

    [Fact]
    public void Rename_ToAnotherClassNameIsRefusedButToItsOwnNameIsFine()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var first = classes.Add("a");
        classes.Add("b");

        Assert.Throws<ProjectException>(() => classes.Rename(first.Id, "B"));
        classes.Rename(first.Id, "A");
        Assert.Equal("A", classes.Load()[0].Name);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("")]
    public void Recolor_RejectsBadColors(string color)
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var cls = classes.Add("a");

        Assert.Throws<ProjectException>(() => classes.Recolor(cls.Id, color));
        Assert.Equal("#4A90E2", classes.Load().Single().Color);
    }

    [Fact]
    public void Recolor_NormalizesToUpperCase()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var cls = classes.Add("a");

        classes.Recolor(cls.Id, "#12abEF");

        Assert.Equal("#12ABEF", classes.Load().Single().Color);
    }

    [Fact]
    public void Merge_MovesLabelsAndTagsAndRemovesTheSource()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var service = _fixture.Service(store);
        var red = classes.Add("connector red");
        var blue = classes.Add("connector blue");
        var first = _fixture.OpenImage(service, 1);
        service.Execute(first, EditRequest.Add(new[] { BoxOf(1, red.Id), BoxOf(1, blue.Id, 80) }));
        var second = _fixture.OpenImage(service, 2);
        // The second image is tagged with both: the merged class must keep a single tag.
        service.Execute(second, EditRequest.Tags(new[] { red.Id, blue.Id }, Array.Empty<long>()));

        classes.Merge(red.Id, blue.Id);

        var left = classes.Load().Single();
        Assert.Equal(blue.Id, left.Id);
        Assert.Equal(3, classes.LoadCounts()[blue.Id]);
        Assert.All(_fixture.OpenImage(service, 1).Annotations, annotation => Assert.Equal(blue.Id, annotation.ClassId));
        Assert.Equal(new[] { blue.Id }, _fixture.OpenImage(service, 2).Tags);
    }

    [Fact]
    public void Merge_IntoItselfOrAMissingClassIsRefused()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var cls = classes.Add("a");

        Assert.Throws<ProjectException>(() => classes.Merge(cls.Id, cls.Id));
        Assert.Throws<ProjectException>(() => classes.Merge(cls.Id, 99));
    }

    [Fact]
    public void Usage_CountsLabelsAndImages()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var service = _fixture.Service(store);
        var cls = classes.Add("a");
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxOf(1, cls.Id), BoxOf(1, cls.Id, 80) }));
        service.Execute(_fixture.OpenImage(service, 2), EditRequest.Tags(new[] { cls.Id }, Array.Empty<long>()));

        Assert.Equal(new ClassUsage(3, 2), classes.Usage(cls.Id));
    }

    [Fact]
    public void Delete_RemovesItsLabelsAndSendsEmptiedImagesBackToNew()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var service = _fixture.Service(store);
        var doomed = classes.Add("doomed");
        var kept = classes.Add("kept");
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxOf(1, doomed.Id) }));
        service.Execute(_fixture.OpenImage(service, 2), EditRequest.Add(new[] { BoxOf(2, doomed.Id), BoxOf(2, kept.Id, 80) }));
        service.ToggleReviewed(_fixture.OpenImage(service, 1));

        classes.Delete(doomed.Id);

        var images = service.LoadImages();
        Assert.Equal(ImageStatus.New, images[0].Status);
        Assert.Equal(ImageStatus.Labeled, images[1].Status);
        Assert.Equal(kept.Id, _fixture.OpenImage(service, 2).Annotations.Single().ClassId);
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
        Assert.Equal(new[] { kept.Id }, classes.Load().Select(item => item.Id));
    }

    [Fact]
    public void Delete_ALeftOverTempTableDoesNotBreakTheNextDelete()
    {
        using var store = _fixture.OpenStore();
        var classes = new ClassService(store);
        var first = classes.Add("a");
        var second = classes.Add("b");

        classes.Delete(first.Id);
        classes.Delete(second.Id);

        Assert.Empty(classes.Load());
    }
}
