using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Geometry;

public class TileLayoutTests
{
    [Fact]
    public void Compute_ImageSmallerThanATileGivesOneTileOfTheImageSize()
    {
        Assert.Equal(new[] { new TileRect(0, 0, 300, 200) }, TileLayout.Compute(300, 200, 640, 64));
    }

    [Fact]
    public void Compute_ExactFitGivesOneTile()
    {
        Assert.Equal(new[] { new TileRect(0, 0, 640, 640) }, TileLayout.Compute(640, 640, 640, 64));
    }

    [Theory]
    [InlineData(5472, 3648, 1280, 128)]
    [InlineData(1000, 600, 640, 64)]
    [InlineData(1217, 1217, 640, 63)]
    [InlineData(2000, 300, 640, 0)]
    public void Compute_CoversEveryPixelWithinTheImageAndKeepsTheOverlap(int width, int height, int tile, int overlap)
    {
        var tiles = TileLayout.Compute(width, height, tile, overlap);

        Assert.All(tiles, rect =>
        {
            Assert.InRange(rect.X, 0, width - rect.Width);
            Assert.InRange(rect.Y, 0, height - rect.Height);
            Assert.Equal(Math.Min(tile, width), rect.Width);
            Assert.Equal(Math.Min(tile, height), rect.Height);
        });

        AssertAxis(tiles.Select(rect => rect.X).Distinct().Order().ToList(), Math.Min(tile, width), width, overlap);
        AssertAxis(tiles.Select(rect => rect.Y).Distinct().Order().ToList(), Math.Min(tile, height), height, overlap);
        Assert.Equal(tiles.Count, TileLayout.Count(width, height, tile, overlap));
    }

    private static void AssertAxis(IReadOnlyList<int> starts, int tile, int size, int overlap)
    {
        Assert.Equal(0, starts[0]);
        Assert.Equal(size, starts[^1] + tile);
        for (var index = 1; index < starts.Count; index++)
            Assert.True(starts[index - 1] + tile - starts[index] >= overlap);
    }

    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(100, 100, 64, 64)]
    [InlineData(100, 100, 64, -1)]
    [InlineData(0, 100, 64, 0)]
    public void Compute_RejectsImpossibleArguments(int width, int height, int tile, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileLayout.Compute(width, height, tile, overlap));
    }
}
