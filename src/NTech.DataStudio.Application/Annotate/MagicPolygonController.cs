using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// The state behind the Magic polygon tool (decision 0016). Hovering shows the outline of the object under the pointer,
/// a click turns it into a label, Shift+click and right click add points to refine it first. The segmenter is slow, so
/// hover work is latest-wins: while one outline is being worked out, newer pointer positions replace each other and only
/// the last one is computed. Events are posted to the thread that created the controller (the UI thread).
/// </summary>
public sealed class MagicPolygonController
{
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

    public IReadOnlyList<SegmentPrompt> Prompts
    {
        get { lock (_gate) return _prompts.ToList(); }
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
    public async Task AcceptAsync(PixelPoint point)
    {
        if (!_active) return;

        string? path;
        List<SegmentPrompt> prompts;
        int generation;
        lock (_gate)
        {
            path = _path;
            generation = _generation;
            prompts = _prompts.ToList();
        }

        if (path is null) return;
        prompts.Add(new SegmentPrompt(point, true));

        try
        {
            var outline = await _segmenter.SegmentAsync(path, prompts, _cancel.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return;
                _prompts.Clear();
                Preview = null;
                Status = outline is null ? "Không tách được vật thể ở chỗ này. Thử bấm chỗ khác." : null;
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
            _ready = false;
            Preview = null;
            Status = null;
        }

        Raise();
    }

    private void StartPrepare()
    {
        string? path;
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            path = _path;
            generation = _generation;
            token = _cancel.Token;
        }

        if (_segmenter.Unavailable is { } why)
        {
            Status = why;
            Raise();
            return;
        }

        if (path is null) return;

        Status = "Đang phân tích ảnh...";
        Raise();
        _ = Task.Run(async () =>
        {
            try
            {
                await _segmenter.PrepareAsync(path, token).ConfigureAwait(false);
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
                    token = _cancel.Token;
                    prompts = _prompts.ToList();
                    if (_hover is { } hover) prompts.Add(new SegmentPrompt(hover, true));
                }

                IReadOnlyList<PixelPoint>? outline = null;
                try
                {
                    if (prompts.Count > 0)
                        outline = await _segmenter.SegmentAsync(path, prompts, token).ConfigureAwait(false);
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
