namespace NTech.DataStudio.Domain.Classes;

/// <summary>A label class. <see cref="Color"/> is #RRGGBB; <see cref="Hotkey"/> is 1-9 or none.</summary>
public sealed record LabelClass(long Id, string Name, string Color, int? Hotkey, int Order);
