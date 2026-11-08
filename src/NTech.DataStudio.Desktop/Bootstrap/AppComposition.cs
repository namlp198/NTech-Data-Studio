using System.Windows.Media;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;
using NTech.DataStudio.Desktop.ViewModels.Annotate;
using NTech.DataStudio.Desktop.ViewModels.Dataset;
using NTech.DataStudio.Desktop.ViewModels.Export;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Desktop.ViewModels.Project;
using NTech.DataStudio.Desktop.ViewModels.Settings;
using NTech.DataStudio.Desktop.ViewModels.Shared;
using NTech.DataStudio.Exporters;
using NTech.DataStudio.Imaging.Augment;
using NTech.DataStudio.Imaging.Codecs;
using NTech.DataStudio.Imaging.Segmentation;
using NTech.DataStudio.Storage.Profiles;
using NTech.DataStudio.Storage.Settings;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Desktop.Bootstrap;

/// <summary>The manual composition root: every object graph is wired here.</summary>
public static class AppComposition
{
    public static MainWindow CreateMainWindow()
    {
        var projects = new ProjectService(new SqliteProjectStore());
        var sam = new SamSegmenter(SamSegmenter.DefaultFolder());
        var samModels = new SamModelService(sam, new SamModelDownloader(), SamSegmenter.DefaultFolder());
        var settings = new SettingsService(new JsonSettingsStore(JsonSettingsStore.DefaultPath()));
        var folderPicker = new WpfFolderPicker();
        var prompt = new MessageBoxPrompt();
        var codec = new OpenCvImageCodec();
        var engine = new OpenCvAugmentationEngine();
        var profiles = new YamlModelProfileCatalog(Path.Combine(AppContext.BaseDirectory, "profiles"));

        var projectView = new ProjectViewModel(
            projects,
            folderPicker,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        var prepareView = new PrepareViewModel(projects, new (string, Geometry, PrepareStepViewModel)[]
        {
            ("1. Import", Icon("Icon.Plus"), new ImportStepViewModel(projects, new ImportService(codec), folderPicker)),
            ("2. Audit", Icon("Icon.Report"), new AuditStepViewModel(projects)),
            ("3. Làm sạch", Icon("Icon.Search"), new CleanStepViewModel(projects, new AnalyzeService(codec))),
            ("4. Chuẩn hóa", Icon("Icon.FitFrame"), new NormalizeStepViewModel(
                projects, new NormalizeService(codec), codec, profiles, prompt))
        });

        var views = new (string Label, string Icon, object ViewModel)[]
        {
            ("Project", "Icon.Project", projectView),
            ("Prepare", "Icon.Prepare", prepareView),
            ("Annotate", "Icon.Annotate", new AnnotateViewModel(projects, prompt, new ImageLoader(), settings, sam, samModels)),
            ("Dataset", "Icon.Dataset", new DatasetViewModel(
                projects, new DatasetService(projects, engine), prompt)),
            ("Export", "Icon.Export", new ExportViewModel(
                projects,
                new DatasetService(projects, engine),
                new ExportService(projects, engine, DatasetExporters.All()),
                profiles,
                folderPicker,
                new ExplorerFolderOpener())),
            ("Cài đặt", "Icon.Settings", new SettingsViewModel(settings, samModels, new ExplorerFolderOpener()))
        };

        var shell = new ShellViewModel(
            projects,
            views.Select(view => new ViewEntry(view.Label, Icon(view.Icon), view.ViewModel)));

        return new MainWindow { DataContext = shell };
    }

    private static Geometry Icon(string key) => (Geometry)System.Windows.Application.Current.FindResource(key);
}

