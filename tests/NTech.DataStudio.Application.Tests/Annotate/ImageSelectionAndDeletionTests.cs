using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>Select all / clear / invert for the dataset, and taking images out of the project from Annotate (decision 0022).</summary>
public class ImageSelectionAndDeletionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly AnnotateFixture _fixture = new(imageCount: 5);

    public void Dispose() => _fixture.Dispose();

    private static Annotation BoxAt(long imageId, long classId) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Box, new PixelRect(10, 10, 40, 30).Corners(), AnnotationSource.Manual, Now, Now);

    [Fact]
    public void SetInDatasetMany_SetsOnlyTheGivenImages_AndCanClearThemAgain()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);

        service.SetInDatasetMany(new long[] { 1, 3, 4 }, true);

        Assert.Equal(new long[] { 1, 3, 4 }, service.LoadImages().Where(image => image.InDataset).Select(image => image.Id));

        service.SetInDatasetMany(new long[] { 3 }, false);

        Assert.Equal(new long[] { 1, 4 }, service.LoadImages().Where(image => image.InDataset).Select(image => image.Id));
        service.SetInDatasetMany(Array.Empty<long>(), true);
    }

    [Fact]
    public void DeleteImages_RemovesThemFromTheListAtOnce_ButKeepsTheirLabels()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        service.Execute(_fixture.OpenImage(service, 2), EditRequest.Add(new[] { BoxAt(2, classId) }));

        service.DeleteImages(new long[] { 2, 4 });

        Assert.Equal(new long[] { 1, 3, 5 }, service.LoadImages().Select(image => image.Id));
        Assert.Equal(new long[] { 2, 4 }, service.LoadDeletedIds());
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 2"));
    }

    [Fact]
    public void RestoreImages_BringsBackTheStatusTheImageHad()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("connector").Id;
        var document = _fixture.OpenImage(service, 2);
        service.Execute(document, EditRequest.Add(new[] { BoxAt(2, classId) }));
        service.ToggleReviewed(_fixture.OpenImage(service, 2));
        service.ToggleNull(_fixture.OpenImage(service, 3));

        service.DeleteImages(new long[] { 2, 3 });
        service.RestoreImages(service.LoadDeletedIds());

        var images = service.LoadImages().ToDictionary(image => image.Id);
        Assert.Equal(5, images.Count);
        Assert.Equal(ImageStatus.Reviewed, images[2].Status);
        Assert.Equal(ImageStatus.Null, images[3].Status);
        Assert.Empty(service.LoadDeletedIds());
    }

    [Fact]
    public void DeletingTwice_DoesNotLoseTheRememberedStatus()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        service.ToggleNull(_fixture.OpenImage(service, 1));

        service.DeleteImages(new long[] { 1 });
        service.DeleteImages(new long[] { 1 });
        service.RestoreImages(new long[] { 1 });

        Assert.Equal(ImageStatus.Null, service.LoadImages().Single(image => image.Id == 1).Status);
    }

    [Fact]
    public void ADeletedImage_LeavesTheDatasetStore_SoNewVersionsSkipIt()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);

        service.DeleteImages(new long[] { 5 });

        Assert.Equal(4, _fixture.Scalar("SELECT COUNT(*) FROM active_images"));
    }
}
