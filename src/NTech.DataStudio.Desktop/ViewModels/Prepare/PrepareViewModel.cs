using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Projects;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

/// <summary>A step in the wizard's selector; the ViewSelector.Item style reads Label and IconGeometry.</summary>
public sealed partial class WizardStepItem : ObservableObject
{
    private readonly Action<WizardStepItem> _selected;

    [ObservableProperty]
    private bool _isSelected;

    public WizardStepItem(string label, Geometry icon, PrepareStepViewModel step, Action<WizardStepItem> selected)
    {
        Label = label;
        IconGeometry = icon;
        Step = step;
        _selected = selected;
    }

    public string Label { get; }
    public Geometry IconGeometry { get; }
    public PrepareStepViewModel Step { get; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _selected(this);
    }
}

public sealed partial class PrepareViewModel : ObservableObject
{
    private readonly ProjectService _projects;

    [ObservableProperty]
    private PrepareStepViewModel? _currentStep;

    public PrepareViewModel(ProjectService projects, IEnumerable<(string Label, Geometry Icon, PrepareStepViewModel Step)> steps)
    {
        _projects = projects;
        Steps = steps.Select(step => new WizardStepItem(step.Label, step.Icon, step.Step, Select)).ToList();
        Steps[0].IsSelected = true;
        _projects.CurrentChanged += OnProjectChanged;
    }

    public IReadOnlyList<WizardStepItem> Steps { get; }

    public bool HasProject => _projects.Current is not null;

    private string? _loadedRoot;

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasProject));

        // RefreshAsync also lands here after a step finishes; only a different project restarts the wizard.
        var root = _projects.Current?.RootPath;
        if (root == _loadedRoot) return;
        _loadedRoot = root;
        if (root is not null)
            Steps[0].IsSelected = true;
        if (root is not null && CurrentStep is { IsBusy: false } step)
            _ = step.ReloadAsync();
    }

    private void Select(WizardStepItem item)
    {
        foreach (var step in Steps)
        {
            if (!ReferenceEquals(step, item))
                step.IsSelected = false;
        }

        CurrentStep = item.Step;
        if (HasProject && !item.Step.IsBusy)
            _ = item.Step.ReloadAsync();
    }
}
