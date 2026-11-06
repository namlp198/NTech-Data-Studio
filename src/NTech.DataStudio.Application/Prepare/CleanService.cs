using System.Text;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

/// <summary>
/// Rejecting never deletes or moves a file. The database holds the decision and
/// rejected/rejected.csv mirrors it, so the reasons stay readable outside the app.
/// </summary>
public static class CleanService
{
    public const string ReportFileName = "rejected.csv";

    public static IReadOnlyList<SourceImage> Apply(IPrepareStore store, CleanOptions options)
    {
        store.ReplaceRejections(CleanRules.Evaluate(store.LoadSources(), options));
        return WriteReport(store);
    }

    public static IReadOnlyList<SourceImage> Restore(IPrepareStore store, long sourceId)
    {
        store.Restore(sourceId);
        return WriteReport(store);
    }

    private static IReadOnlyList<SourceImage> WriteReport(IPrepareStore store)
    {
        var sources = store.LoadSources();
        var report = new StringBuilder("id,file,reason,detail\r\n");
        foreach (var source in sources.Where(source => source.IsRejected))
            report.Append(source.Id).Append(',')
                .Append(Csv(source.OriginalPath)).Append(',')
                .Append(source.Reject).Append(',')
                .Append(Csv(source.RejectDetail)).Append("\r\n");

        Directory.CreateDirectory(store.RejectedFolder);
        // With a BOM, so Excel shows Vietnamese correctly.
        File.WriteAllText(Path.Combine(store.RejectedFolder, ReportFileName), report.ToString(), new UTF8Encoding(true));
        return sources;
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
