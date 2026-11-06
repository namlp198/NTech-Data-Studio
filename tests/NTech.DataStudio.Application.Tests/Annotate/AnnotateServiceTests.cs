using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Annotate;

public class AnnotateServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly AnnotateFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static Annotation BoxAt(long imageId, long classId, double x = 10, double y = 10) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Box,
            new PixelRect(x, y, 40, 30).Corners(), AnnotationSource.Manual, Now, Now);

    private long NewClass(AnnotateService service, string name = "connector")
    {
        using var store = _fixture.OpenStore();
        return new ClassService(store).Add(name).Id;
    }

    [Fact]
    public void Execute_SavesBeforeReturningSoACrashLosesNothing()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        var box = BoxAt(1, classId);

        service.Execute(document, EditRequest.Add(new[] { box }));

        // Read through a second connection while the first is still open and was never closed cleanly.
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
        Assert.Equal("labeled", _fixture.Text("SELECT status FROM images WHERE id = 1"));
    }

    [Fact]
    public void Reopening_RestoresPointsClassAndTimestamps()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var box = BoxAt(1, classId);
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { box }));
        store.Dispose();

        using var again = _fixture.OpenStore();
        var loaded = _fixture.OpenImage(_fixture.Service(again), 1).Annotations.Single();

        Assert.Equal(box.Id, loaded.Id);
        Assert.Equal(box.Points, loaded.Points);
        Assert.Equal((classId, ShapeKind.Box, AnnotationSource.Manual), (loaded.ClassId, loaded.Kind, loaded.Source));
        Assert.Equal((Now, Now), (loaded.CreatedAt, loaded.UpdatedAt));
    }

    [Fact]
    public void Reopening_KeepsFractionalCoordinatesExactly()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var points = new PixelPoint[] { new(10.123456789012345, 20.5), new(60.25, 20.5), new(35.000000001, 70.75) };
        var polygon = new Annotation(Guid.NewGuid(), 1, classId, ShapeKind.Polygon, points, AnnotationSource.Manual, Now, Now);
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { polygon }));
        store.Dispose();

        using var again = _fixture.OpenStore();

        Assert.Equal(points, _fixture.OpenImage(_fixture.Service(again), 1).Annotations.Single().Points);
    }

    [Fact]
    public void Undo_RemovesAnAddedLabelAndRedoBringsItBackWithTheSameId()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        var box = BoxAt(1, classId);
        service.Execute(document, EditRequest.Add(new[] { box }));

        service.Undo(document);
        Assert.Empty(document.Annotations);
        Assert.Equal(ImageStatus.New, document.Status);
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
        Assert.Equal("new", _fixture.Text("SELECT status FROM images WHERE id = 1"));

        service.Redo(document);
        Assert.Equal(box.Id, document.Annotations.Single().Id);
        Assert.Equal(ImageStatus.Labeled, document.Status);
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
    }

    [Fact]
    public void Undo_RestoresAMovedLabelAndADeletedOne()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        var box = BoxAt(1, classId);
        service.Execute(document, EditRequest.Add(new[] { box }));
        var moved = box.WithPoints(new PixelRect(50, 40, 40, 30).Corners(), Now);

        service.Execute(document, EditRequest.Replace(box, moved));
        Assert.Equal(moved.Points, document.Annotations.Single().Points);

        service.Execute(document, EditRequest.Remove(new[] { moved }));
        Assert.Empty(document.Annotations);

        service.Undo(document);
        Assert.Equal(moved.Points, document.Annotations.Single().Points);
        service.Undo(document);
        Assert.Equal(box.Points, document.Annotations.Single().Points);
        Assert.Equal(box.Points, _fixture.OpenImage(service, 1).Annotations.Single().Points);
    }

    [Fact]
    public void NewEdit_ClearsTheRedoStack()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        service.Execute(document, EditRequest.Add(new[] { BoxAt(1, classId) }));
        service.Undo(document);
        Assert.True(service.CanRedo(1));

        service.Execute(document, EditRequest.Add(new[] { BoxAt(1, classId, 80) }));

        Assert.False(service.CanRedo(1));
    }

    [Fact]
    public void History_IsSeparatePerImage()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var first = _fixture.OpenImage(service, 1);
        var second = _fixture.OpenImage(service, 2);
        service.Execute(first, EditRequest.Add(new[] { BoxAt(1, classId) }));

        Assert.True(service.CanUndo(1));
        Assert.False(service.CanUndo(2));
        Assert.Null(service.Undo(second));
        Assert.Single(first.Annotations);
    }

    [Fact]
    public void History_SurvivesLeavingAnImageAndComingBack()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId) }));

        var back = _fixture.OpenImage(service, 1);
        service.Undo(back);

        Assert.Empty(back.Annotations);
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
    }

    [Fact]
    public void Execute_WithNothingToChangeIsIgnored()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var document = _fixture.OpenImage(service, 1);

        Assert.Null(service.Execute(document, EditRequest.Add(Array.Empty<Annotation>())));
        Assert.False(service.CanUndo(1));
    }

    [Fact]
    public void Execute_FailedWriteLeavesDocumentAndHistoryUntouched()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var document = _fixture.OpenImage(service, 1);

        // Class 99 does not exist, so the foreign key rejects the insert.
        Assert.ThrowsAny<Exception>(() => service.Execute(document, EditRequest.Add(new[] { BoxAt(1, 99) })));

        Assert.Empty(document.Annotations);
        Assert.Equal(ImageStatus.New, document.Status);
        Assert.False(service.CanUndo(1));
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
    }

    [Fact]
    public void Status_FollowsTheLabels()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        var box = BoxAt(1, classId);

        service.Execute(document, EditRequest.Add(new[] { box }));
        Assert.Equal(ImageStatus.Labeled, document.Status);

        service.ToggleReviewed(document);
        Assert.Equal(ImageStatus.Reviewed, document.Status);

        // Changing a reviewed image asks for a new review.
        service.Execute(document, EditRequest.Replace(box, box.WithClass(classId, Now)));
        Assert.Equal(ImageStatus.Labeled, document.Status);

        service.Execute(document, EditRequest.Remove(new[] { box }));
        Assert.Equal(ImageStatus.New, document.Status);
    }

    [Fact]
    public void Null_CanOnlyBeSetWithoutLabelsAndUndoRestoresIt()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);

        Assert.NotNull(service.ToggleNull(document));
        Assert.Equal(ImageStatus.Null, document.Status);
        Assert.Equal("null", _fixture.Text("SELECT status FROM images WHERE id = 1"));

        // Labeling a Null image makes it Labeled.
        service.Execute(document, EditRequest.Add(new[] { BoxAt(1, classId) }));
        Assert.Equal(ImageStatus.Labeled, document.Status);
        Assert.Null(service.ToggleNull(document));

        service.Undo(document);
        Assert.Equal(ImageStatus.Null, document.Status);
        service.Undo(document);
        Assert.Equal(ImageStatus.New, document.Status);
    }

    [Fact]
    public void Review_NeedsSomethingToReview()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var document = _fixture.OpenImage(service, 1);

        Assert.Null(service.ToggleReviewed(document));

        service.ToggleNull(document);
        service.ToggleReviewed(document);
        Assert.Equal(ImageStatus.Reviewed, document.Status);
        service.ToggleReviewed(document);
        Assert.Equal(ImageStatus.Null, document.Status);
    }

    [Fact]
    public void Tags_AreSavedAndMakeTheImageLabeled()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("ok").Id;
        var document = _fixture.OpenImage(service, 1);

        service.Execute(document, EditRequest.Tags(new[] { classId }, Array.Empty<long>()));
        Assert.Equal(ImageStatus.Labeled, document.Status);
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM image_tags"));

        // Adding the same tag again changes nothing.
        Assert.Null(service.Execute(document, EditRequest.Tags(new[] { classId }, Array.Empty<long>())));

        service.Execute(document, EditRequest.Tags(Array.Empty<long>(), new[] { classId }));
        Assert.Equal(ImageStatus.New, document.Status);
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM image_tags"));
    }

    [Fact]
    public void ProjectCounts_ReadTheLabeledImages()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId) }));
        service.ToggleNull(_fixture.OpenImage(service, 2));

        var images = service.LoadImages();

        Assert.Equal(
            new[] { (1L, ImageStatus.Labeled, 1), (2L, ImageStatus.Null, 0), (3L, ImageStatus.New, 0) },
            images.Select(image => (image.Id, image.Status, image.LabelCount)));
        Assert.Contains(classId, images[0].ClassIds);
    }

    [Fact]
    public void UpdatedAt_ChangesOnEditButCreatedAtDoesNot()
    {
        var store = _fixture.OpenStore();
        var clock = Now;
        var service = new AnnotateService(store, () => clock);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 1);
        var box = BoxAt(1, classId);
        service.Execute(document, EditRequest.Add(new[] { box }));

        clock = Now.AddMinutes(5);
        service.Execute(document, EditRequest.Replace(box, box.WithPoints(new PixelRect(1, 1, 20, 20).Corners(), clock)));

        var saved = _fixture.OpenImage(service, 1).Annotations.Single();
        Assert.Equal((Now, Now.AddMinutes(5)), (saved.CreatedAt, saved.UpdatedAt));
    }
}
