using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Projects;

namespace NTech.DataStudio.Desktop.Shell;

public sealed record ViewEntry(string Label, Geometry Icon, object ViewModel);

/// <summary>The TopBar: which view is showing and the status of the open project.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ProjectService _projects;

    [ObservableProperty]
    private object? _currentView;

    public ShellViewModel(ProjectService projects, IEnumerable<ViewEntry> views)
    {
        _projects = projects;
        Views = views.Select(view => new NavItemViewModel(view, Select)).ToList();
        Views[0].IsSelected = true;
        _projects.CurrentChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public IReadOnlyList<NavItemViewModel> Views { get; }

    public bool HasProject => _projects.Current is not null;

    public string ProjectName => _projects.Current?.Name ?? "Chưa mở project";

    public string ProgressText => _projects.Current is { } project
        ? $"{project.AnnotatedImageCount} / {project.ImageCount} ảnh đã gán"
        : string.Empty;

    private void Select(NavItemViewModel item)
    {
        foreach (var view in Views)
        {
            if (!ReferenceEquals(view, item))
                view.IsSelected = false;
        }

        if (ReferenceEquals(CurrentView, item.ViewModel)) return;

        (CurrentView as IActivatable)?.Deactivate();
        CurrentView = item.ViewModel;
        if (item.ViewModel is IActivatable activatable)
            _ = activatable.ActivateAsync();
    }
}

public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly Action<NavItemViewModel> _select;

    [ObservableProperty]
    private bool _isSelected;

    public NavItemViewModel(ViewEntry entry, Action<NavItemViewModel> select)
    {
        Label = entry.Label;
        IconGeometry = entry.Icon;
        ViewModel = entry.ViewModel;
        _select = select;
    }

    public string Label { get; }
    public Geometry IconGeometry { get; }
    public object ViewModel { get; }

    // Driven by the property, not a command: a RadioButton can be checked without a click.
    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _select(this);
    }
}
