using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Tests.Annotate;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Projects;

/// <summary>Decision 0014: the project's target task.</summary>
public class TargetTaskTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectService _service = new(new SqliteProjectStore());

    public void Dispose() => _temp.Dispose();

    private static readonly TaskKind[] Segment = { TaskKind.Segment };

    [Fact]
    public async Task Create_StoresTheTaskAndOpenReadsItBack()
    {
        var created = await _service.CreateAsync(_temp.Path, "p", Segment);
        Assert.Equal(Segment, created.Tasks);

        _service.Close();
        var reopened = await _service.OpenAsync(created.RootPath);
        Assert.Equal(Segment, reopened.Tasks);
    }

    [Fact]
    public async Task ProjectWithoutATask_HasNone()
    {
        var created = await _service.CreateAsync(_temp.Path, "p");
        Assert.Empty(created.Tasks);
    }

    [Fact]
    public async Task AProjectCanAimAtSeveralTasks_KeptInTheOrderOfTheCatalogWithoutRepeats()
    {
        var created = await _service.CreateAsync(_temp.Path, "p", new[] { TaskKind.Segment, TaskKind.Detect, TaskKind.Segment });
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Segment }, created.Tasks);

        _service.Close();
        var reopened = await _service.OpenAsync(created.RootPath);
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Segment }, reopened.Tasks);
    }

    [Fact]
    public async Task AProjectOfBeforeSeveralTasks_ReadsItsOneTaskFromTheOldKey()
    {
        var created = await _service.CreateAsync(_temp.Path, "p", new[] { TaskKind.Detect, TaskKind.Segment });
        _service.Close();

        // Put the project back as an older build left it: only the single key, no set.
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(created.RootPath, "project.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM project_meta WHERE key = 'target_tasks'; UPDATE project_meta SET value = 'obb' WHERE key = 'target_task';";
            command.ExecuteNonQuery();
        }

        var reopened = await _service.OpenAsync(created.RootPath);
        Assert.Equal(new[] { TaskKind.Obb }, reopened.Tasks);
    }

    [Fact]
    public async Task SetTargetTasks_ChangesAndClearsThemAndRaisesCurrentChanged()
    {
        await _service.CreateAsync(_temp.Path, "p", new[] { TaskKind.Detect });
        var raised = 0;
        _service.CurrentChanged += (_, _) => raised++;

        await _service.SetTargetTasksAsync(new[] { TaskKind.Detect, TaskKind.Obb });
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Obb }, _service.Current!.Tasks);
        Assert.Equal(1, raised);

        await _service.SetTargetTasksAsync(new[] { TaskKind.Obb, TaskKind.Detect });
        Assert.Equal(1, raised);

        await _service.SetTargetTasksAsync(Array.Empty<TaskKind>());
        Assert.Empty(_service.Current!.Tasks);

        var reopened = await _service.OpenAsync(_service.Current.RootPath);
        Assert.Empty(reopened.Tasks);
    }

    [Fact]
    public async Task SetTargetTasks_RefusesAnomalyAndAClosedProject()
    {
        await Assert.ThrowsAsync<ProjectException>(() => _service.SetTargetTasksAsync(new[] { TaskKind.Detect }));

        await _service.CreateAsync(_temp.Path, "p");
        await Assert.ThrowsAsync<ProjectException>(() => _service.SetTargetTasksAsync(new[] { TaskKind.Detect, TaskKind.Anomaly }));
    }

    [Theory]
    [InlineData(null, AnnotateTool.Box, true)]
    [InlineData(null, AnnotateTool.Tag, true)]
    [InlineData(TaskKind.Detect, AnnotateTool.Box, true)]
    [InlineData(TaskKind.Detect, AnnotateTool.Polygon, false)]
    [InlineData(TaskKind.Segment, AnnotateTool.Polygon, true)]
    [InlineData(TaskKind.Segment, AnnotateTool.Box, false)]
    [InlineData(TaskKind.Obb, AnnotateTool.RotatedBox, true)]
    [InlineData(TaskKind.Obb, AnnotateTool.Tag, false)]
    [InlineData(TaskKind.Classify, AnnotateTool.Tag, true)]
    [InlineData(TaskKind.Classify, AnnotateTool.Box, false)]
    [InlineData(TaskKind.Segment, AnnotateTool.Select, true)]
    [InlineData(TaskKind.Segment, AnnotateTool.Pan, true)]
    public void TaskTools_AllowOnlyTheShapeTheTaskTrainsOn(TaskKind? target, AnnotateTool tool, bool allowed)
    {
        Assert.Equal(allowed, TaskTools.Allows(target, tool));
        Assert.Equal(allowed, TaskTools.WhyOff(target, tool) is null);
    }

    [Theory]
    [InlineData(AnnotateTool.Box, true)]
    [InlineData(AnnotateTool.Polygon, true)]
    [InlineData(AnnotateTool.MagicPolygon, true)]
    [InlineData(AnnotateTool.RotatedBox, false)]
    [InlineData(AnnotateTool.Tag, false)]
    [InlineData(AnnotateTool.Select, true)]
    public void SeveralTasks_AllowTheToolsOfAnyOfThem(AnnotateTool tool, bool allowed)
    {
        var tasks = new[] { TaskKind.Detect, TaskKind.Segment };

        Assert.Equal(allowed, TaskTools.Allows(tasks, tool));
        Assert.Equal(allowed, TaskTools.WhyOff(tasks, tool) is null);
    }

    [Fact]
    public void WhyOff_NamesEveryTaskOfTheProject()
    {
        var why = TaskTools.WhyOff(new[] { TaskKind.Detect, TaskKind.Segment }, AnnotateTool.Tag);

        Assert.Contains("Detect", why);
        Assert.Contains("Segment", why);
    }

    [Fact]
    public void Editor_RefusesAToolTheTaskDoesNotAllow_AndCyclingSkipsIt()
    {
        using var fixture = new AnnotateFixture();
        var store = fixture.OpenStore();
        var service = fixture.Service(store);
        var editor = new AnnotationEditor(service, new AnnotationClipboard()) { TargetTasks = Segment };
        editor.Open(fixture.OpenImage(service, 1));

        editor.SetTool(AnnotateTool.Box);
        Assert.Equal(AnnotateTool.Select, editor.Tool);
        Assert.Contains("Segment", editor.Message);

        var seen = new List<AnnotateTool>();
        for (var step = 0; step < 6; step++)
        {
            editor.CycleTool(1);
            seen.Add(editor.Tool);
        }

        Assert.DoesNotContain(AnnotateTool.Box, seen);
        Assert.DoesNotContain(AnnotateTool.RotatedBox, seen);
        Assert.DoesNotContain(AnnotateTool.Tag, seen);
        Assert.Contains(AnnotateTool.Polygon, seen);
    }

    [Fact]
    public void Editor_LeavesATool_WhenTheTaskChangesUnderIt()
    {
        using var fixture = new AnnotateFixture();
        var store = fixture.OpenStore();
        var service = fixture.Service(store);
        var editor = new AnnotationEditor(service, new AnnotationClipboard());
        editor.Open(fixture.OpenImage(service, 1));

        editor.SetTool(AnnotateTool.Box);
        Assert.Equal(AnnotateTool.Box, editor.Tool);

        editor.TargetTasks = Segment;
        Assert.Equal(AnnotateTool.Select, editor.Tool);
    }

    private static ValidationReport Validate(ModelProfile profile, params TaskKind[] target)
    {
        var image = new DatasetImage(1, 1, null, "master/m1.jpg", 1000, 800, ImageStatus.Labeled,
            new Dictionary<long, int>(), new HashSet<long>());
        var now = DateTimeOffset.UtcNow;
        var polygon = new Annotation(Guid.NewGuid(), 1, 1, ShapeKind.Polygon,
            new[] { new PixelPoint(100, 100), new PixelPoint(300, 100), new PixelPoint(200, 300) },
            AnnotationSource.Manual, now, now);
        return DatasetValidator.Validate(new ValidationInput(
            new[] { image },
            new Dictionary<long, SplitKind> { [1] = SplitKind.Train },
            _ => new[] { polygon },
            new[] { new LabelClass(1, "red", "#E25D5D", 1, 0) },
            new DatasetConfig(),
            profile,
            640,
            _ => true,
            target));
    }

    private static readonly ModelProfile DetectProfile =
        new("yolo26-detect", "ultralytics-yolo", TaskKind.Detect, 640, new[] { 640 }, 32, 12, new[] { "yolo" });

    [Fact]
    public void Validator_WarnsWhenTheProfileTaskDiffersFromTheProjectsButDoesNotBlock()
    {
        var report = Validate(DetectProfile, TaskKind.Segment);

        var issue = Assert.Single(report.Issues, item => item.Message.Contains("Project nhắm"));
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("Segment", issue.Message);
    }

    [Fact]
    public void Validator_SaysNothingWhenTheProfileTaskIsOneOfTheProjectsOrNoneIsSet()
    {
        Assert.DoesNotContain(Validate(DetectProfile, TaskKind.Detect).Issues, item => item.Message.Contains("Project nhắm"));
        Assert.DoesNotContain(Validate(DetectProfile, TaskKind.Detect, TaskKind.Segment).Issues, item => item.Message.Contains("Project nhắm"));
        Assert.DoesNotContain(Validate(DetectProfile).Issues, item => item.Message.Contains("Project nhắm"));
    }

    [Fact]
    public void Validator_NamesEveryTaskOfAProjectThatDoesNotMatch()
    {
        var report = Validate(DetectProfile, TaskKind.Segment, TaskKind.Obb);

        var issue = Assert.Single(report.Issues, item => item.Message.Contains("Project nhắm"));
        Assert.Contains("Segment", issue.Message);
        Assert.Contains("OBB", issue.Message);
    }
}
