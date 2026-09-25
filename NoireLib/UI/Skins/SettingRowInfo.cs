namespace NoireLib.UI;

/// <summary>What one settings or form row shows besides its control.</summary>
/// <param name="Id">The row's id, unique in its group.</param>
/// <param name="Name">The label.</param>
/// <param name="Help">The help text, or <see langword="null"/>.</param>
/// <param name="Modified">Whether the value differs from its default.</param>
/// <param name="Disabled">Whether the control is disabled.</param>
/// <param name="DisabledReason">Why it is disabled, shown on hover.</param>
/// <param name="Alarm">A warning about the current value, or <see langword="null"/>.</param>
public readonly record struct SettingRowInfo(string Id, string Name, string? Help = null, bool Modified = false, bool Disabled = false, string? DisabledReason = null, string? Alarm = null);
