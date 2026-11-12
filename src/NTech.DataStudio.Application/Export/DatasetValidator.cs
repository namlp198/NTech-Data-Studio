using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Export;

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(IssueSeverity Severity, string Message);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    public static ValidationReport Empty { get; } = new(Array.Empty<ValidationIssue>());

    public int ErrorCount => Issues.Count(issue => issue.Severity == IssueSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == IssueSeverity.Warning);
    public bool HasErrors => ErrorCount > 0;
}

/// <summary>What the validator looks at: the images of one version, their labels as drawn, and the target.</summary>
/// <param name="Annotations">Labels of one image, in master pixels.</param>
/// <param name="MasterExists">Whether a master path (relative to the project) is on disk.</param>
public sealed record ValidationInput(
    IReadOnlyList<DatasetImage> Images,
    IReadOnlyDictionary<long, SplitKind> Split,
    Func<long, IReadOnlyList<Annotation>> Annotations,
    IReadOnlyList<LabelClass> Classes,
    DatasetConfig Config,
    ModelProfile Profile,
    int Imgsz,
    Func<string, bool> MasterExists,
    IReadOnlyList<TaskKind>? TargetTasks = null);

/// <summary>
/// Checks a version against a profile before anything is written (docs/DESIGN_DOC.md, Module 4).
/// Errors would make a trainer reject or misread the dataset and block the export; warnings only
/// say that the result may train poorly. Works on the labels as drawn on the masters: preprocessing
/// and augmentation only move them, and the label transformer already drops what falls out.
/// </summary>
public static class DatasetValidator
{
    /// <summary>Fewer labels of a class than this is thin for a model to learn from.</summary>
    public const int MinLabelsPerClass = 50;

    private const double OutsideTolerancePx = 0.5;
    private const int ExamplesShown = 3;

    public static ValidationReport Validate(ValidationInput input)
    {
        var issues = new List<ValidationIssue>();
        if (ExportKinds.For(input.Profile) is not { } kind)
        {
            issues.Add(Error($"Profile {input.Profile.Id} không có định dạng xuất (anomaly chỉ train trên ảnh tốt, chưa hỗ trợ xuất)."));
            return new ValidationReport(issues);
        }

        if (input.Images.Count == 0)
        {
            issues.Add(Error("Version không có ảnh nào."));
            return new ValidationReport(issues);
        }

        var trainCount = input.Images.Count(image => input.Split.TryGetValue(image.Id, out var split) && split == SplitKind.Train);
        if (trainCount == 0)
            issues.Add(Error("Version không có ảnh train."));
        if (!input.Images.Any(image => input.Split.TryGetValue(image.Id, out var split) && split == SplitKind.Val))
            issues.Add(Warning("Version không có ảnh val: trainer không có gì để đánh giá."));

        var mapper = new ClassMapper(input.Config.Preprocess.ClassMap);
        var known = input.Classes.ToDictionary(item => item.Id, item => item.Name);
        var names = known.ToDictionary(pair => pair.Key, pair => pair.Value);

        var problems = new Problems();
        // Labels (or tagged images, for classification) per class and split.
        var counts = new Dictionary<long, int[]>();
        var scale = ObjectScale(input);

        foreach (var image in input.Images)
        {
            var split = input.Split.TryGetValue(image.Id, out var assigned) ? assigned : SplitKind.Train;
            var name = Path.GetFileName(image.MasterPath);

            if (!input.MasterExists(image.MasterPath))
                problems.Add("missing", name);
            if (image.Status == ImageStatus.New)
                problems.Add("new", name);

            if (kind.IsClassify())
            {
                CheckTags(image, mapper, known, counts, split, problems, name);
                continue;
            }

            var shapes = 0;
            var polygons = 0;
            foreach (var annotation in input.Annotations(image.Id))
            {
                if (!known.ContainsKey(annotation.ClassId))
                {
                    problems.Add("unknownClass", name);
                    continue;
                }

                var classId = mapper.Resolve(annotation.ClassId);
                if (classId is not { } mapped) continue;
                if (!known.ContainsKey(mapped))
                {
                    problems.Add("unknownClass", name);
                    continue;
                }

                if (annotation.Kind == ShapeKind.Point)
                {
                    problems.Add("point", name);
                    continue;
                }

                if (!CheckShape(annotation, image, problems, name)) continue;

                shapes++;
                if (annotation.Kind is ShapeKind.Polygon or ShapeKind.RotatedBox) polygons++;
                Count(counts, mapped, split);

                switch (kind)
                {
                    case ExportKind.YoloDetect or ExportKind.CocoDetect when annotation.Kind != ShapeKind.Box:
                        problems.Add("toBox", name);
                        break;
                    case ExportKind.YoloSegment or ExportKind.CocoSegment when annotation.Kind == ShapeKind.Box:
                        problems.Add("toPolygon", name);
                        break;
                    case ExportKind.YoloObb when annotation.Kind == ShapeKind.Polygon:
                        problems.Add("polygonToObb", name);
                        break;
                }

                if (input.Profile.MinObjectPx > 0 && scale > 0)
                {
                    var box = PolygonMath.BoundingBox(annotation.Points);
                    if (Math.Min(box.Width, box.Height) * scale < input.Profile.MinObjectPx)
                        problems.Add("small", name);
                }
            }

            if (kind.NeedsPolygons() && shapes > 0 && polygons == 0)
                problems.Add("onlyBoxes", name);
        }

        AddProblemIssues(issues, problems, input, kind);
        AddCountIssues(issues, counts, names, input, kind);
        AddProfileIssues(issues, input);
        AddTargetIssue(issues, input);
        return new ValidationReport(issues);
    }

    private static void CheckTags(
        DatasetImage image,
        ClassMapper mapper,
        Dictionary<long, string> known,
        Dictionary<long, int[]> counts,
        SplitKind split,
        Problems problems,
        string name)
    {
        var tags = image.TagClassIds
            .Select(mapper.Resolve)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (tags.Any(id => !known.ContainsKey(id)))
        {
            problems.Add("unknownClass", name);
            return;
        }

        switch (tags.Count)
        {
            case 0:
                problems.Add("noTag", name);
                break;
            case 1:
                Count(counts, tags[0], split);
                break;
            default:
                problems.Add("manyTags", name);
                break;
        }
    }

    /// <summary>Whether the label is fit to write; records what is wrong with it when it is not.</summary>
    private static bool CheckShape(Annotation annotation, DatasetImage image, Problems problems, string name)
    {
        // First, because a bow-tie has area 0 and "tự cắt" says what is actually wrong.
        if (annotation.Kind == ShapeKind.Polygon && PolygonMath.HasSelfIntersection(annotation.Points, true))
        {
            problems.Add("selfIntersect", name);
            return false;
        }

        if (PolygonMath.Area(annotation.Points) < 1e-9)
        {
            problems.Add("zeroArea", name);
            return false;
        }

        var outside = annotation.Points.Any(point =>
            point.X < -OutsideTolerancePx || point.Y < -OutsideTolerancePx
            || point.X > image.Width + OutsideTolerancePx || point.Y > image.Height + OutsideTolerancePx);
        if (outside)
        {
            problems.Add("outside", name);
            return false;
        }

        return true;
    }

    private static void AddProblemIssues(List<ValidationIssue> issues, Problems problems, ValidationInput input, ExportKind kind)
    {
        Report(issues, problems, "missing", IssueSeverity.Error, "ảnh master không còn trên đĩa");
        Report(issues, problems, "unknownClass", IssueSeverity.Error, "ảnh có nhãn thuộc class không tồn tại");
        Report(issues, problems, "zeroArea", IssueSeverity.Error, "ảnh có nhãn diện tích bằng 0");
        Report(issues, problems, "outside", IssueSeverity.Error, "ảnh có nhãn nằm ngoài ảnh");
        Report(issues, problems, "selfIntersect", IssueSeverity.Error, "ảnh có polygon tự cắt");
        Report(issues, problems, "onlyBoxes", IssueSeverity.Error,
            "ảnh chỉ có box mà profile là Segment (cần polygon; vẽ lại bằng polygon hoặc dùng profile Detect)");
        Report(issues, problems, "noTag", IssueSeverity.Error, "ảnh chưa có tag class (phân loại cần đúng một tag mỗi ảnh)");
        Report(issues, problems, "manyTags", IssueSeverity.Error, "ảnh có nhiều tag (phân loại cần đúng một tag mỗi ảnh)");

        Report(issues, problems, "new", IssueSeverity.Warning,
            "ảnh chưa gán nhãn (New) nằm trong version và sẽ được xuất như ảnh không có vật thể");
        Report(issues, problems, "point", IssueSeverity.Warning, "là point, định dạng này không chứa được nên bị bỏ", "nhãn");
        Report(issues, problems, "toBox", IssueSeverity.Warning, "polygon hoặc rotated box sẽ xuất thành box bao quanh", "nhãn");
        Report(issues, problems, "toPolygon", IssueSeverity.Warning, "box sẽ xuất thành polygon 4 đỉnh", "nhãn");
        Report(issues, problems, "polygonToObb", IssueSeverity.Warning,
            "polygon sẽ xuất thành hình chữ nhật diện tích nhỏ nhất bao quanh", "nhãn");
        Report(issues, problems, "small", IssueSeverity.Warning,
            $"nhỏ hơn {input.Profile.MinObjectPx} px ở imgsz {input.Imgsz}, trainer khó học được", "nhãn");
    }

    private static void AddCountIssues(
        List<ValidationIssue> issues,
        Dictionary<long, int[]> counts,
        Dictionary<long, string> names,
        ValidationInput input,
        ExportKind kind)
    {
        var unit = kind.IsClassify() ? "ảnh" : "nhãn";

        var thin = input.Classes
            .Where(item => counts.TryGetValue(item.Id, out var byClass) && byClass.Sum() is > 0 and < MinLabelsPerClass)
            .Select(item => $"{item.Name} ({counts[item.Id].Sum()})")
            .ToList();
        if (thin.Count > 0)
            issues.Add(Warning($"Class có dưới {MinLabelsPerClass} {unit}: {string.Join(", ", thin)}."));

        var hasVal = input.Split.Values.Any(split => split == SplitKind.Val);
        if (!hasVal) return;

        var missingInVal = counts
            .Where(pair => pair.Value[(int)SplitKind.Train] > 0 && pair.Value[(int)SplitKind.Val] == 0)
            .Select(pair => names.TryGetValue(pair.Key, out var name) ? name : pair.Key.ToString())
            .OrderBy(name => name)
            .ToList();
        if (missingInVal.Count > 0)
            issues.Add(Warning($"Tập val thiếu class: {string.Join(", ", missingInVal)}."));
    }

    /// <summary>Decisions 0014 and 0019: a profile of a task the project does not aim at is allowed (polygons give boxes) but worth saying.</summary>
    private static void AddTargetIssue(List<ValidationIssue> issues, ValidationInput input)
    {
        if (input.TargetTasks is not { Count: > 0 } targets || targets.Contains(input.Profile.Task)) return;

        issues.Add(Warning(
            $"Project nhắm {TargetTasks.Label(targets)} nhưng profile {input.Profile.Id} là {TargetTasks.Label(input.Profile.Task)}; "
            + "nhãn được chuyển sang hình của profile khi xuất."));
    }

    private static void AddProfileIssues(List<ValidationIssue> issues, ValidationInput input)
    {
        var profile = input.Profile;
        if (profile.Stride > 0 && input.Imgsz % profile.Stride != 0)
            issues.Add(Warning($"imgsz {input.Imgsz} không chia hết cho stride {profile.Stride} của profile; trainer sẽ làm tròn."));
        if (input.Imgsz <= 0)
            issues.Add(Error("imgsz phải lớn hơn 0."));
    }

    /// <summary>Master pixels to training pixels: the preprocessing resize, then the framework's resize to imgsz.</summary>
    private static double ObjectScale(ValidationInput input)
    {
        if (input.Imgsz <= 0 || input.Images.Count == 0) return 0;

        var preprocess = input.Config.Preprocess;
        var widest = input.Images.Max(image => Math.Max(image.Width, image.Height));
        if (preprocess.Resize == ResizeMode.None)
            return (double)input.Imgsz / widest;

        // The images differ in size, so judge the object size by the typical (median) image.
        var sorted = input.Images.OrderBy(image => image.Width * (long)image.Height).ToList();
        var typical = sorted[sorted.Count / 2];
        var sx = (double)preprocess.Width / typical.Width;
        var sy = (double)preprocess.Height / typical.Height;
        var resize = preprocess.Resize == ResizeMode.Fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
        return resize * input.Imgsz / Math.Max(preprocess.Width, preprocess.Height);
    }

    private static void Count(Dictionary<long, int[]> counts, long classId, SplitKind split)
    {
        if (!counts.TryGetValue(classId, out var byClass))
            counts[classId] = byClass = new int[3];
        byClass[(int)split]++;
    }

    private static void Report(
        List<ValidationIssue> issues,
        Problems problems,
        string code,
        IssueSeverity severity,
        string what,
        string unit = "")
    {
        if (!problems.TryGet(code, out var total, out var images, out var examples)) return;

        var head = unit.Length > 0
            ? $"{total} {unit} trong {images} ảnh: {what}"
            : $"{images} {what}";
        issues.Add(new ValidationIssue(severity, $"{head} (ví dụ: {string.Join(", ", examples)})."));
    }

    private static ValidationIssue Error(string message) => new(IssueSeverity.Error, message);
    private static ValidationIssue Warning(string message) => new(IssueSeverity.Warning, message);

    /// <summary>Tallies one kind of problem: how often, in how many images, and the first few file names.</summary>
    private sealed class Problems
    {
        private readonly Dictionary<string, (int Total, HashSet<string> Images, List<string> Examples)> _byCode = new();

        public void Add(string code, string image)
        {
            if (!_byCode.TryGetValue(code, out var entry))
                _byCode[code] = entry = (0, new HashSet<string>(), new List<string>());

            entry.Images.Add(image);
            if (entry.Examples.Count < ExamplesShown && !entry.Examples.Contains(image))
                entry.Examples.Add(image);
            _byCode[code] = (entry.Total + 1, entry.Images, entry.Examples);
        }

        public bool TryGet(string code, out int total, out int images, out IReadOnlyList<string> examples)
        {
            if (_byCode.TryGetValue(code, out var entry))
            {
                total = entry.Total;
                images = entry.Images.Count;
                examples = entry.Examples;
                return true;
            }

            total = images = 0;
            examples = Array.Empty<string>();
            return false;
        }
    }
}
