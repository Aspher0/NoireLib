using Dalamud.Game.Text.SeStringHandling;

namespace NoireLib.Helpers;

/// <summary>One of the states an entry cycles through when it is clicked.</summary>
/// <param name="Name">The state's name.</param>
/// <param name="Text">The text the entry shows in this state.</param>
/// <param name="Tooltip">The tooltip the entry shows in this state, or null to keep the entry's own.</param>
public readonly record struct DtrEntryState(string Name, SeString Text, SeString? Tooltip = null);
