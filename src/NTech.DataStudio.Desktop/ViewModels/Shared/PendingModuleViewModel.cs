namespace NTech.DataStudio.Desktop.ViewModels.Shared;

/// <summary>Empty state for a module that is not built yet; says so plainly instead of showing sample data.</summary>
public sealed record PendingModuleViewModel(string Title, string Description);
