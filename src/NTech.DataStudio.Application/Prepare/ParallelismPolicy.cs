namespace NTech.DataStudio.Application.Prepare;

/// <summary>A 20MP image takes about 60 MB decoded, so thread count is limited by RAM, not only by cores.</summary>
public static class ParallelismPolicy
{
    private const double UsableMemoryFraction = 0.5;

    public static int ThreadsFor(long bytesPerImage, long availableBytes, int coreCount)
    {
        if (bytesPerImage <= 0) return Math.Max(1, coreCount);

        var byMemory = (long)(availableBytes * UsableMemoryFraction) / bytesPerImage;
        return (int)Math.Clamp(byMemory, 1, Math.Max(1, coreCount));
    }

    /// <summary>Thread count for this machine right now, sized for the largest image of the batch.</summary>
    public static int ThreadsForCurrentMachine(long bytesPerImage)
    {
        var memory = GC.GetGCMemoryInfo();
        return ThreadsFor(bytesPerImage, memory.TotalAvailableMemoryBytes - memory.MemoryLoadBytes, Environment.ProcessorCount);
    }

    /// <summary>Peak memory for one image while working: the file in memory, its pixels and a working copy.</summary>
    public static long EstimateBytesPerImage(int width, int height, int channels, int bitDepth, long fileSize)
    {
        var decoded = (long)width * height * Math.Max(channels, 3) * Math.Max(bitDepth / 8, 1);
        return fileSize + 2 * decoded;
    }
}
