using System.Windows.Media.Imaging;

namespace NTech.DataStudio.Desktop.Services;

/// <summary>
/// Decodes full-resolution masters off the UI thread and keeps the last two, so
/// stepping to the next image is instant once it has been preloaded.
/// </summary>
public sealed class ImageLoader
{
    private const int Capacity = 2;

    private readonly object _gate = new();
    private readonly Dictionary<string, Task<BitmapSource>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();

    public Task<BitmapSource> LoadAsync(string path)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                _order.Remove(path);
                _order.Add(path);
                return cached;
            }

            var task = Task.Run(() => Decode(path));
            _cache[path] = task;
            _order.Add(path);
            while (_order.Count > Capacity)
            {
                _cache.Remove(_order[0]);
                _order.RemoveAt(0);
            }

            // A failed decode must not be remembered, or the image could never be retried.
            _ = task.ContinueWith(
                _ =>
                {
                    lock (_gate)
                    {
                        if (_cache.TryGetValue(path, out var current) && current == task)
                        {
                            _cache.Remove(path);
                            _order.Remove(path);
                        }
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }
    }

    /// <summary>Starts decoding in the background; a failure surfaces when the image is really opened.</summary>
    public void Preload(string path) => _ = LoadAsync(path).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);

    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _order.Clear();
        }
    }

    private static BitmapSource Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
