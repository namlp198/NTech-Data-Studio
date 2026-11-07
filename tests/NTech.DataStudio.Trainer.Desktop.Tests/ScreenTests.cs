using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using NTech.DataStudio.Trainer.Desktop.ViewModels;
using NTech.DataStudio.Trainer.Desktop.Views;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Tests;

public sealed class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>One Avalonia UI thread for the whole assembly; every test that touches a control runs on it.</summary>
internal static class Ui
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() =>
    {
        Logger.Sink = BindingErrors.Sink;
        return HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
    });

    public static Task<T> Run<T>(Func<Task<T>> action) => Session.Value.Dispatch(action, CancellationToken.None);

    public static Task<T> Run<T>(Func<T> action) => Session.Value.Dispatch(action, CancellationToken.None);
}

/// <summary>Collects binding and resource failures, which Avalonia only logs and never throws.</summary>
internal sealed class BindingErrors : ILogSink
{
    public static readonly BindingErrors Sink = new();
    private readonly List<string> _messages = [];

    public static IReadOnlyList<string> Take()
    {
        lock (Sink._messages)
        {
            var copy = Sink._messages.ToList();
            Sink._messages.Clear();
            return copy;
        }
    }

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Add(area, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Add(area, messageTemplate, propertyValues);

    private void Add(string area, string template, object?[] values)
    {
        lock (_messages) _messages.Add($"[{area}] {template} {string.Join(" | ", values)}");
    }
}

internal static class Repo
{
    public static string Root
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "NTech.DataStudio.slnx"))) folder = folder.Parent;
            return folder?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục repo.");
        }
    }

    public static string DesktopProject => Path.Combine(Root, "src", "NTech.DataStudio.Trainer.Desktop");
}

/// <summary>
/// Renders every screen with data on disk, as the app would show it, and saves a PNG per screen
/// (under bin/screens) for a person to look at. Also fails on any binding or resource error.
/// </summary>
public sealed class ScreenTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    public static TheoryData<string> Screens => new() { "machine", "dataset", "config", "train", "results", "empty" };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Screen_RendersWithoutBindingErrors(string screen)
    {
        _world.Containers.ValidateOutput = TestWorld.ValidateJson();
        var workspace = screen == "empty" ? null : _world.CreateWorkspace();
        if (workspace is not null)
        {
            var finished = _world.AddRun(workspace, "yolo11n", new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun(), result: true, export: "passed");
            var running = _world.AddRun(workspace, "yolo26n", new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun(epochs: 2, final: RunStates.Running));
            _world.Containers.Running.Add(running.Id);
            Assert.NotNull(finished);
        }

        BindingErrors.Take();
        var file = await Ui.Run(async () =>
        {
            var shell = _world.Shell(workspace);
            var window = new MainWindow { DataContext = shell, Width = 1360, Height = 1500 };
            window.Show();
            shell.Start();
            await shell.Machine.RefreshCommand.ExecuteAsync(null);
            switch (screen)
            {
                case "dataset":
                    shell.Dataset.IsSelected = true;
                    await shell.Dataset.CheckCommand.ExecuteAsync(null);
                    break;
                case "config":
                    shell.Config.IsSelected = true;
                    shell.Config.SelectedFamily = shell.Config.Families.Single(item => item.Family.Id == "yolo12");
                    break;
                case "train":
                    shell.Train.IsSelected = true;
                    await shell.Train.RefreshAsync();
                    break;
                case "results":
                    shell.Results.IsSelected = true;
                    await shell.Results.RefreshAsync();
                    shell.Results.SelectedRow = shell.Results.Rows.Single(row => row.Summary.Overall is not null);
                    break;
                case "empty":
                    shell.Train.IsSelected = true;
                    break;
            }

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Không chụp được khung hình.");
            var folder = Path.Combine(AppContext.BaseDirectory, "screens");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, screen + ".png");
            frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
            return path;
        });

        Assert.True(new FileInfo(file).Length > 10_000);
        var errors = BindingErrors.Take().Where(message => !message.Contains("[Layout]")).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Distinct()));
    }
}

public sealed class ResourceTests
{
    /// <summary>A missing token only fails at run time (CLAUDE.md), so every key used in a view is looked up here.</summary>
    [Fact]
    public async Task EveryStaticResourceInTheViewsExists()
    {
        var keys = Directory.EnumerateFiles(Repo.DesktopProject, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\{StaticResource ([\w.]+)\}").Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.NotEmpty(keys);
        var missing = await Ui.Run(() => keys.Where(key => Converters.Resource(key) is null).ToList());
        Assert.Empty(missing);
    }

    [Fact]
    public async Task EveryGradeAndStateHasItsColorAndIcon()
    {
        var missing = await Ui.Run(() =>
        {
            var problems = new List<string>();
            foreach (var grade in Enum.GetValues<Grade>())
            {
                if (Converters.GradeBrush.Convert(grade, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is null) problems.Add("brush " + grade);
                if (Converters.GradeIcon.Convert(grade, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is null) problems.Add("icon " + grade);
            }

            foreach (var state in new[] { RunStates.Created, RunStates.Preparing, RunStates.Preflight, RunStates.Running, RunStates.Evaluating, RunStates.Completed, RunStates.Failed, RunStates.Cancelled, RunStates.Interrupted })
                if (Converters.StateBrush.Convert(state, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is null) problems.Add("state " + state);
            return problems;
        });
        Assert.Empty(missing);
    }
}

/// <summary>Design section 12: WPF tokens cannot be used in Avalonia, so the port is compared value by value.</summary>
public sealed class TokenParityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Colors.xaml", "Colors.axaml")]
    [InlineData("Spacing.xaml", "Spacing.axaml")]
    [InlineData("Typography.xaml", "Typography.axaml")]
    [InlineData("Sizing.xaml", "Sizing.axaml")]
    public void SharedKeysHaveTheSameValue(string wpf, string avalonia)
    {
        var source = Values(Path.Combine(Repo.Root, "src", "NTech.DataStudio.Desktop", "Themes", "Tokens", wpf));
        var port = Values(Path.Combine(Repo.DesktopProject, "Themes", "Tokens", avalonia));
        var shared = source.Keys.Intersect(port.Keys).ToList();
        Assert.NotEmpty(shared);
        foreach (var key in shared.Where(key => key is not "Font.Primary" and not "Font.Monospace"))
            Assert.True(source[key] == port[key], $"{key}: WPF {source[key]}, Avalonia {port[key]}");
    }

    [Fact]
    public void EveryWpfColorIsPorted()
    {
        var source = Values(Path.Combine(Repo.Root, "src", "NTech.DataStudio.Desktop", "Themes", "Tokens", "Colors.xaml"));
        var port = Values(Path.Combine(Repo.DesktopProject, "Themes", "Tokens", "Colors.axaml"));
        Assert.Empty(source.Keys.Where(key => key.StartsWith("Color.", StringComparison.Ordinal)).Except(port.Keys));
    }

    [Fact]
    public void SharedIconsKeepTheirShape()
    {
        var source = Values(Path.Combine(Repo.Root, "src", "NTech.DataStudio.Desktop", "Themes", "Tokens", "Icons.xaml"));
        var port = Values(Path.Combine(Repo.DesktopProject, "Themes", "Tokens", "Icons.axaml"));
        foreach (var key in source.Keys.Intersect(port.Keys))
            Assert.Equal(source[key], port[key]);
    }

    /// <summary>Key to the element's text or Color attribute, whitespace removed so "16, 0" equals "16,0".</summary>
    private static Dictionary<string, string> Values(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(element => element.Attribute(X + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(X + "Key")!.Value,
                element => Regex.Replace(element.Attribute("Color")?.Value ?? element.Value, @"\s+", ""));
}
