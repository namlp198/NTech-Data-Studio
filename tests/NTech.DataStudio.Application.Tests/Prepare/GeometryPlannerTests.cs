using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Tests.Prepare;

public class GeometryPlannerTests
{
    private static readonly ModelProfile Detect = new(
        "yolo26-detect", "ultralytics-yolo", TaskKind.Detect, 640, new[] { 640, 960, 1280 }, 32, 12, new[] { "yolo" });

    private static readonly ModelProfile Classify = new(
        "yolo26-classify", "ultralytics-yolo", TaskKind.Classify, 224, new[] { 224, 320, 640 }, 32, 0, new[] { "yolo" });

    [Fact]
    public void BigObject_StaysAtTheDefaultSizeWithoutWarnings()
    {
        var proposal = GeometryPlanner.Propose(5472, 200, Detect);

        Assert.Equal(GeometryMethod.Downscale, proposal.Plan.Method);
        Assert.Equal(640, proposal.RecommendedImgsz);
        Assert.Empty(proposal.Warnings);
    }

    [Fact]
    public void MediumObject_RecommendsALargerSizeAndSaysWhy()
    {
        // 80 px on a 5120 px image: 10 px at 640, 15 px at 960.
        var proposal = GeometryPlanner.Propose(5120, 80, Detect);

        Assert.Equal(GeometryMethod.Downscale, proposal.Plan.Method);
        Assert.Equal(960, proposal.RecommendedImgsz);
        Assert.Contains("Ở imgsz 640, vật thể 80 px còn 10 px. Nên dùng imgsz 960 hoặc tile.", proposal.Warnings);
    }

    [Fact]
    public void SmallObject_ProposesTiles()
    {
        // The example in the design: 40 px on 5120 is 5 px at 640 and still only 10 px at 1280.
        var proposal = GeometryPlanner.Propose(5120, 40, Detect);

        Assert.Equal(GeometryMethod.Tile, proposal.Plan.Method);
        Assert.Equal(1280, proposal.Plan.TileSize);
        Assert.Equal(1280, proposal.RecommendedImgsz);
        Assert.True(proposal.Plan.TileOverlap >= 60, "the overlap must hold a whole object");
        Assert.Contains(proposal.Warnings, warning => warning.StartsWith("Ở imgsz 640, vật thể 40 px còn 5 px."));
    }

    [Fact]
    public void ObjectBelowTheModelMinimum_WarnsThatResolutionCannotHelp()
    {
        var proposal = GeometryPlanner.Propose(5120, 8, Detect);

        Assert.Contains(proposal.Warnings, warning => warning.Contains("ngay trên ảnh gốc"));
    }

    [Fact]
    public void MasterEdge_GrowsWhenTheObjectWouldBeHardToAnnotate()
    {
        // At 2048 the 100 px object keeps about 20 px, under the margin of 24; at 4096 it keeps 40.
        var proposal = GeometryPlanner.Propose(10000, 100, Detect);

        Assert.Equal(4096, proposal.Plan.MaxLongEdge);
    }

    [Fact]
    public void MasterEdge_DefaultsTo2048()
    {
        Assert.Equal(2048, GeometryPlanner.Propose(5472, null, Detect).Plan.MaxLongEdge);
        Assert.Equal(2048, GeometryPlanner.Propose(5472, 500, Detect).Plan.MaxLongEdge);
    }

    [Fact]
    public void UnknownObjectSize_GivesNoWarnings()
    {
        Assert.Empty(GeometryPlanner.Propose(5472, null, Detect).Warnings);
    }

    [Fact]
    public void ProfileWithoutObjects_IgnoresTheObjectSize()
    {
        var proposal = GeometryPlanner.Propose(5472, 3, Classify);

        Assert.Equal(GeometryMethod.Downscale, proposal.Plan.Method);
        Assert.Equal(224, proposal.RecommendedImgsz);
        Assert.Empty(proposal.Warnings);
    }

    [Fact]
    public void Roi_IsKeptAndMeasuredInsteadOfTheWholeImage()
    {
        var roi = new PixelRect(100, 100, 1280, 800);

        // 40 px is too small on 5120 px, but fine in a 1280 px crop at imgsz 640 (20 px).
        var proposal = GeometryPlanner.Propose(5120, 40, Detect, roi);

        Assert.Equal(GeometryMethod.RoiCrop, proposal.Plan.Method);
        Assert.Equal(roi, proposal.Plan.Roi);
        Assert.Equal(640, proposal.RecommendedImgsz);
        Assert.Empty(proposal.Warnings);
    }

    [Fact]
    public void Roi_StillTooSmall_WarnsAndKeepsTheCrop()
    {
        var proposal = GeometryPlanner.Propose(5120, 40, Detect, new PixelRect(0, 0, 5000, 3000));

        Assert.Equal(GeometryMethod.RoiCrop, proposal.Plan.Method);
        Assert.NotEmpty(proposal.Warnings);
    }

    [Fact]
    public void ImageSmallerThanEverySize_IsNeverShrunkByThePlanner()
    {
        var proposal = GeometryPlanner.Propose(500, 20, Detect);

        Assert.Equal(640, proposal.RecommendedImgsz);
        Assert.Empty(proposal.Warnings);
    }
}
