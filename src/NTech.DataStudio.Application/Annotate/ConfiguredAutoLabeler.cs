using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// The automatic labelers the settings describe (decisions 0018 and 0019). Nothing runs while the advanced option is off.
/// Detect labels with boxes from the detect model; Segment labels with polygons, by the segment model or by the detect model
/// whose boxes SAM cuts out, as <see cref="AutoLabelMode"/> says. Reads the settings on every call, so a change takes effect
/// without a restart.
/// </summary>
public sealed class ConfiguredAutoLabeler
{
    public const string Disabled = "Gán nhãn tự động đang tắt. Bật ở Cài đặt > Gán nhãn tự động.";

    private readonly SettingsService _settings;

    private sealed class ForTask : IAutoLabeler
    {
        private readonly ConfiguredAutoLabeler _owner;
        private readonly TaskKind _task;

        public ForTask(ConfiguredAutoLabeler owner, TaskKind task)
        {
            _owner = owner;
            _task = task;
        }

        private IAutoLabeler Active => _owner.Active(_task);

        public string? Unavailable => _owner._settings.AutoLabelEnabled ? Active.Unavailable : Disabled;

        public IReadOnlyList<string> ClassNames => _owner._settings.AutoLabelEnabled ? Active.ClassNames : Array.Empty<string>();

        public Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default) =>
            _owner._settings.AutoLabelEnabled
                ? Active.DetectAsync(imagePath, minScore, cancellationToken)
                : throw new InvalidOperationException(Disabled);
    }

    public ConfiguredAutoLabeler(SettingsService settings, IAutoLabeler segment, IAutoLabeler detectAndSegment, IAutoLabeler detect)
    {
        _settings = settings;
        Segment = segment;
        DetectAndSegment = detectAndSegment;
        Detect = detect;
    }

    /// <summary>Segment by a segment model, whatever the current mode.</summary>
    public IAutoLabeler Segment { get; }

    /// <summary>Segment by a detect model and SAM, whatever the current mode.</summary>
    public IAutoLabeler DetectAndSegment { get; }

    /// <summary>Detect by a detect model.</summary>
    public IAutoLabeler Detect { get; }

    /// <summary>The labeler that does a task, by the settings; it refuses to run while the advanced option is off.</summary>
    public IAutoLabeler For(TaskKind task) => new ForTask(this, task);

    /// <summary>The labeler of a task by the current mode, without the check of the advanced option (for showing the state of the models).</summary>
    public IAutoLabeler Active(TaskKind task) => task switch
    {
        TaskKind.Detect => Detect,
        _ => _settings.AutoLabelMode == AutoLabelMode.DetectSam ? DetectAndSegment : Segment
    };
}
