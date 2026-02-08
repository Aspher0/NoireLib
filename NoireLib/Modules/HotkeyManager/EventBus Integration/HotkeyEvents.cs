namespace NoireLib.HotkeyManager;

/// <summary>
/// Event fired when a hotkey is triggered.
/// </summary>
/// <param name="Hotkey">The triggered hotkey entry.</param>
public record HotkeyTriggeredEvent(HotkeyEntry Hotkey);

/// <summary>
/// Event fired when a hotkey binding changes.
/// </summary>
/// <param name="Hotkey">The updated hotkey entry.</param>
/// <param name="IsNewBinding">Whether the change is a new binding or a removal.</param>
public record HotkeyBindingChangedEvent(HotkeyEntry Hotkey, bool IsNewBinding);

/// <summary>
/// Event fired when hotkey listening starts.
/// </summary>
/// <param name="HotkeyId">The hotkey identifier.</param>
/// <param name="Mode">The input mode used for listening.</param>
public record HotkeyListeningStartedEvent(string HotkeyId, HotkeyListenMode Mode);

/// <summary>
/// Event fired when hotkey listening stops.
/// </summary>
/// <param name="HotkeyId">The hotkey identifier.</param>
/// <param name="WasCancelled">Whether listening was cancelled without binding.</param>
public record HotkeyListeningStoppedEvent(string HotkeyId, bool WasCancelled);
