using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// The state behind the Magic polygon tool (decision 0016). Hovering shows the outline of the object under the pointer,
/// a click turns it into a label, dragging a box around the object does the same for things a point cannot pick out
/// (black tape on black cable, cable ties), and Shift+click / right click add points to refine the outline first.
/// When the picture is zoomed in, only the part on screen is analysed, so a small object has more pixels to be seen by.
/// The segmenter is slow, so hover work is latest-wins: while one outline is being worked out, newer pointer positions
/// replace each other and only the last one is computed. Events are posted to the thread that created the controller.
/// </summary>
public sealed class MagicPolygonController
{
    /// <summary>Zoomed in until the screen shows less than this share of the picture, the part on screen is analysed instead of the whole.</summary>
    public const double ZoomThreshold = 0.6;

    private const double RegionMargin = 0.25;
    private static readonly TimeSpan ViewSettleTime = TimeSpan.FromMilliseconds(300);

    private readonly ISmartSegmenter _segmenter;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly object _gate = new();
    private readonly List<SegmentPrompt> _prompts = new();

    private string? _path;
    private bool _active;
    private int _generation;
    private CancellationTokenSource _cancel = new();
    private PixelPoint? _hover;
    private int _hoverVersion;
    private int _viewVersion;
    private PixelRect? _region;
    private bool _ready;
    private bool _working;

    public MagicPolygonController(ISmartSegmenter segmenter)
    {
        _segmenter = segmenter;
    }

    /// <summary>The outline or the prompts or the status changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The user accepted an outline; the editor makes it a label.</summary>
    public event Action<IReadOnlyList<PixelPoint>>? Accepted;

    /// <summary>Null when the tool can run; otherwise why it cannot (model files missing).</summary>
    public string? Unavailable => _segmenter.Unavailable;

    /// <summary>What the tool is doing or why it failed: "Đang phân tích ảnh...", an error; null when idle.</summary>
    public string? Status { get; private set; }

    /// <summary>The outline under the pointer, in master pixels.</summary>
    public IReadOnlyList<PixelPoint>? Preview { get; private set; }

    /// <summary>The part of the picture being analysed, or null for the whole picture.</summary>
    public PixelRect? Region
    {
        get { lock (_gate) return _region; }
    }

    public IReadOnlyList<SegmentPrompt> Prompts
    {
        get { lock (_gate) return _prompts.ToList(); }
    }

    /// <summary>
    /// The part to analyse for what is on screen: the whole picture (null) unless the screen shows well under
    /// <see cref="ZoomThreshold"/> of it. A region that already holds the screen and is not much bigger than needed is kept,
    /// so panning a little never starts a new analysis.
    /// </summary>
    public static PixelRect? ChooseRegion(PixelRect visible, double imageWidth, double imageHeight, PixelRect? current)
    {
        var left = Math.Max(visible.X, 0);
        var top = Math.Max(visible.Y, 0);
        var right = Math.Min(visible.Right, imageWidth);
        var bottom = Math.Min(visible.Bottom, imageHeight);
        if (right <= left || bottom <= top) return current;

        var area = (right - left) * (bottom - top);
        if (area >= ZoomThreshold * imageWidth * imageHeight) return null;

        if (current is { } held
            && held.X <= left && held.Y <= top && held.Right >= right && held.Bottom >= bottom
            && held.Area <= area * 3)
            return held;

        var marginX = (right - left) * RegionMargin;
        var marginY = (bottom - top) * RegionMargin;
        var x = Math.Max(left - marginX, 0);
        var y = Math.Max(top - marginY, 0);
        return new PixelRect(x, y, Math.Min(right + marginX, imageWidth) - x, Math.Min(bottom + marginY, imageHeight) - y);
    }

    /// <summary>The picture the tool works on; null when none is open. Throws away everything about the previous one.</summary>
    public void SetImage(string? path)
    {
        Reset();
        lock (_gate) _path = path;
        if (_active) StartPrepare();
    }

    /// <summary>On while the Magic polygon tool is the current tool. Off, nothing runs and nothing is kept.</summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;

        _active = active;
        if (active) StartPrepare();
        else Reset();
    }

    /// <summary>The part of the picture on screen changed (zoom or pan). Settles for a moment, then may start a new analysis.</summary>
    public void SetView(PixelRect visible, double imageWidth, double imageHeight)
    {
        if (!_active) return;

        int version;
        lock (_gate) version = ++_viewVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(ViewSettleTime).ConfigureAwait(false);
            lock (_gate)
            {
                if (version != _viewVersion || !_active) return;

                var wanted = ChooseRegion(visible, imageWidth, imageHeight, _region);
                if (Equals(wanted, _region)) return;
                _region = wanted;
            }

            StartPrepare();
        });
    }

    /// <summary>The pointer moved over the picture.</summary>
    public void Hover(PixelPoint point)
    {
        if (!_active) return;

        lock (_gate)
        {
            _hover = point;
            _hoverVersion++;
        }

        Pump();
    }

    /// <summary>The pointer left the picture: keep only what the added points describe.</summary>
    public void LeaveHover()
    {
        if (!_active) return;

        lock (_gate)
        {
            _hover = null;
            _hoverVersion++;
        }

        Pump();
    }

    /// <summary>Shift+click or right click: refines the outline before it is accepted.</summary>
    public void AddPrompt(PixelPoint point, bool positive)
    {
        if (!_active) return;

        lock (_gate)
        {
            _prompts.Add(new SegmentPrompt(point, positive));
            _hoverVersion++;
        }

        Raise();
        Pump();
    }

    /// <summary>Takes the last added point back.</summary>
    public void UndoPrompt()
    {
        lock (_gate)
        {
            if (_prompts.Count == 0) return;
            _prompts.RemoveAt(_prompts.Count - 1);
            _hoverVersion++;
        }

        Raise();
        Pump();
    }

    /// <summary>Click: the object at this point (with the points added so far) becomes a label.</summary>
    public Task AcceptAsync(PixelPoint point) => AcceptAsync(point, null);

    /// <summary>A box dragged around the object (with the points added so far) becomes a label.</summary>
    public Task AcceptBoxAsync(PixelRect box) => AcceptAsync(null, box);

    private async Task AcceptAsync(PixelPoint? point, PixelRect? box)
    {
        if (!_active) return;

        string? path;
        List<SegmentPrompt> prompts;
        PixelRect? region;
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            path = _path;
            generation = _generation;
            region = _region;
            token = _cancel.Token;
            prompts = _prompts.ToList();
        }

        if (path is null) return;
        if (point is { } clicked) prompts.Add(new SegmentPrompt(clicked, true));

        try
        {
            var outline = await _segmenter.SegmentAsync(path, region, new SegmentRequest(prompts, box), token).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return;
                _prompts.Clear();
                Preview = null;
                Status = outline is null ? "Không tách được vật thể ở chỗ này. Thử bấm hoặc kéo khung chỗ khác." : null;
            }

            Raise();
            if (outline is not null) Post(() => Accepted?.Invoke(outline));
        }
        catch (OperationCanceledException)
        {
            // The picture or the tool changed meanwhile.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(generation, exception.Message);
        }
    }

    /// <summary>Drops the outline and the added points (Esc, another tool, another picture).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _cancel.Cancel();
            _cancel = new CancellationTokenSource();
            _prompts.Clear();
            _hover = null;
            _hoverVersion++;
            _viewVersion++;
            _region = null;
            _ready = false;
            Preview = null;
            Status = null;
        }

        Raise();
    }

    private void StartPrepare()
    {
        string? path;
        PixelRect? region;
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            path = _path;
            generation = ++_generation;
            region = _region;
            _ready = false;
            Preview = null;
            _cancel.Cancel();
            _cancel = new CancellationTokenSource();
            token = _cancel.Token;
        }

        if (_segmenter.Unavailable is { } why)
        {
            Status = why;
            Raise();
            return;
        }

        if (path is null) return;

        Status = region is null ? "Đang phân tích ảnh..." : "Đang phân tích vùng đang xem...";
        Raise();
        _ = Task.Run(async () =>
        {
            try
            {
                await _segmenter.PrepareAsync(path, region, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (generation != _generation) return;
                    _ready = true;
                    Status = null;
                }

                Raise();
                Pump();
            }
            catch (OperationCanceledException)
            {
                // Superseded.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail(generation, exception.Message);
            }
        }, token);
    }

    /// <summary>Runs the outline worker unless one is already running; it loops until the pointer stops moving.</summary>
    private void Pump()
    {
        lock (_gate)
        {
            if (_working || !_ready || _path is null) return;
            _working = true;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                string path;
                int generation;
                int version;
                PixelRect? region;
                List<SegmentPrompt> prompts;
                CancellationToken token;
                lock (_gate)
                {
                    if (!_ready || _path is null)
                    {
                        _working = false;
                        return;
                    }

                    path = _path;
                    generation = _generation;
                    version = _hoverVersion;
                    region = _region;
                    token = _cancel.Token;
                    prompts = _prompts.ToList();
                    // A pointer outside the analysed part has nothing to say until the view settles on it.
                    if (_hover is { } hover && (region is not { } held || Inside(held, hover)))
                        prompts.Add(new SegmentPrompt(hover, true));
                }

                IReadOnlyList<PixelPoint>? outline = null;
                try
                {
                    if (prompts.Count > 0)
                        outline = await _segmenter.SegmentAsync(path, region, new SegmentRequest(prompts), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (_gate) _working = false;
                    return;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    lock (_gate) _working = false;
                    Fail(generation, exception.Message);
                    return;
                }

                var again = false;
                lock (_gate)
                {
                    if (generation == _generation) Preview = outline;
                    again = generation == _generation && version != _hoverVersion;
                    if (!again) _working = false;
                }

                Raise();
                if (!again) return;
            }
        });
    }

    private static bool Inside(PixelRect region, PixelPoint point) =>
        point.X >= region.X && point.X <= region.Right && point.Y >= region.Y && point.Y <= region.Bottom;

    private void Fail(int generation, string message)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            Status = message;
            Preview = null;
        }

        Raise();
    }

    private void Raise() => Post(() => Changed?.Invoke(this, EventArgs.Empty));

    private void Post(Action action)
    {
        if (_context is null) action();
        else _context.Post(_ => action(), null);
    }
}
