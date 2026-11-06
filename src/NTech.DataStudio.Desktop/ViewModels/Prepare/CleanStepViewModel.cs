using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

public sealed record RejectedRow(long Id, string FileName, string ReasonText, string? Detail, string ThumbnailPath, bool CanRestore);

public sealed partial class CleanStepViewModel : PrepareStepViewModel
{
    private readonly AnalyzeService _analyze;

    [ObservableProperty]
    private bool _rejectExactDuplicates = true;

    [ObservableProperty]
    private bool _rejectNearDuplicates = true;

    [ObservableProperty]
    private int _nearDuplicateDistance = 5;

    [ObservableProperty]
    private bool _rejectBlurry;

    [ObservableProperty]
    private double _blurThreshold;

    [ObservableProperty]
    private bool _rejectDark = true;

    [ObservableProperty]
    private double _darkPercent = 90;

    [ObservableProperty]
    private bool _rejectBright = true;

    [ObservableProperty]
    private double _brightPercent = 50;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _hasImages;

    [ObservableProperty]
    private bool _hasAnalysis;

    public CleanStepViewModel(ProjectService projects, AnalyzeService analyze)
        : base(projects)
    {
        _analyze = analyze;
    }

    public ObservableCollection<RejectedRow> Rejected { get; } = new();

    public override Task ReloadAsync() => RunAsync("Đang đọc project", (store, _, _) =>
    {
        var sources = store.LoadSources();
        var thumbs = store.ThumbsFolder;
        return () => Show(sources, thumbs);
    });

    [RelayCommand]
    private async Task AnalyzeAndApplyAsync()
    {
        var options = Options();
        await RunAsync("Đang phân tích", (store, progress, token) =>
        {
            var result = _analyze.Analyze(store, progress, token);
            var sources = CleanService.Apply(store, options);
            var thumbs = store.ThumbsFolder;
            return () =>
            {
                Show(sources, thumbs);
                ResultMessage = $"Đã phân tích {result.Analyzed} ảnh, bỏ qua {result.AlreadyAnalyzed} ảnh đã phân tích, phát hiện {result.Corrupt} file hỏng.";
            };
        });
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var options = Options();
        await RunAsync("Đang áp dụng ngưỡng", (store, _, _) =>
        {
            var sources = CleanService.Apply(store, options);
            var thumbs = store.ThumbsFolder;
            return () => Show(sources, thumbs);
        });
    }

    [RelayCommand]
    private async Task RestoreAsync(RejectedRow row)
    {
        await RunAsync("Đang khôi phục", (store, _, _) =>
        {
            var sources = CleanService.Restore(store, row.Id);
            var thumbs = store.ThumbsFolder;
            return () => Show(sources, thumbs);
        });
    }

    private CleanOptions Options() => new(
        RejectExactDuplicates,
        RejectNearDuplicates,
        NearDuplicateDistance,
        RejectBlurry,
        BlurThreshold,
        RejectDark,
        DarkPercent / 100,
        RejectBright,
        BrightPercent / 100);

    private void Show(IReadOnlyList<SourceImage> sources, string thumbsFolder)
    {
        var analyzed = sources.Count(source => source.IsAnalyzed);
        HasImages = sources.Count > 0;
        HasAnalysis = analyzed > 0;
        if (!RejectBlurry && analyzed > 0)
            BlurThreshold = Math.Round(CleanRules.SuggestBlurThreshold(
                sources.Where(source => source.Reject is null or RejectReason.Blurry)), 1);

        Rejected.Clear();
        foreach (var source in sources.Where(source => source.IsRejected))
        {
            Rejected.Add(new RejectedRow(
                source.Id,
                source.FileName,
                PrepareText.Reason(source.Reject),
                source.RejectDetail,
                Path.Combine(thumbsFolder, AnalyzeService.ThumbnailFileName(source.Id)),
                source.Reject != RejectReason.Corrupt));
        }

        var groups = sources.Where(source => source.IsRejected)
            .GroupBy(source => source.Reject)
            .Select(group => $"{PrepareText.Reason(group.Key)} {group.Count()}");
        var detail = string.Join(" · ", groups);
        SummaryText = $"Giữ {sources.Count - Rejected.Count} / {sources.Count} ảnh, loại {Rejected.Count}"
            + (detail.Length > 0 ? $" ({detail})" : string.Empty);
    }
}
