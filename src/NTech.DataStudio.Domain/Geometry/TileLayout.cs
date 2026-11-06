namespace NTech.DataStudio.Domain.Geometry;

public readonly record struct TileRect(int X, int Y, int Width, int Height);

public static class TileLayout
{
    /// <summary>
    /// Tiles covering the whole image with at least <paramref name="overlap"/>
    /// pixels shared between neighbours. The last tile on each axis is pushed
    /// back to end on the image edge, so every tile keeps the full size unless
    /// the image itself is smaller.
    /// </summary>
    public static IReadOnlyList<TileRect> Compute(int imageWidth, int imageHeight, int tileSize, int overlap)
    {
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));
        if (tileSize <= 0) throw new ArgumentOutOfRangeException(nameof(tileSize));
        if (overlap < 0 || overlap >= tileSize) throw new ArgumentOutOfRangeException(nameof(overlap));

        var tiles = new List<TileRect>();
        foreach (var y in Starts(imageHeight, tileSize, overlap))
        {
            foreach (var x in Starts(imageWidth, tileSize, overlap))
                tiles.Add(new TileRect(x, y, Math.Min(tileSize, imageWidth), Math.Min(tileSize, imageHeight)));
        }

        return tiles;
    }

    public static int Count(int imageWidth, int imageHeight, int tileSize, int overlap) =>
        Starts(imageWidth, tileSize, overlap).Count * Starts(imageHeight, tileSize, overlap).Count;

    private static List<int> Starts(int size, int tileSize, int overlap)
    {
        if (size <= tileSize) return new List<int> { 0 };

        var step = tileSize - overlap;
        var count = (int)Math.Ceiling((size - tileSize) / (double)step) + 1;
        var starts = new List<int>(count);
        for (var index = 0; index < count - 1; index++)
            starts.Add(index * step);
        starts.Add(size - tileSize);
        return starts;
    }
}
