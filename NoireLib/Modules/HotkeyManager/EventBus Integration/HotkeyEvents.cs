namespace NoireLib.HotkeyManager;

/// <summary>
/// Event fired when a hotkey is triggered.<br/>
/// Published on the framework thread, so subscribers may touch game state directly.
/// </summary>
/// <param name="Hotkey">The triggered hotkey entry.</param>
public record HotkeyTriggeredEvent(HotkeyEntry Hotkey);

/// <summary>
/// Event fired when a hotkey binding changes.<br/>
/// Published on the framework thread, so subscribers may touch game state directly, whether the rebind came from
/// the plugin or from detection capturing one.
/// </summary>
/// <param name="Hotkey">
/// The live hotkey entry carrying the new binding, not a snapshot: a further rebind before delivery shows
/// through, and a subscriber write applies to the registered entry.
/// </param>
/// <param name="IsNewBinding">
/// Whether the binding differed from the previous one at the moment it was written.
/// </param>
public record HotkeyBindingChangedEvent(HotkeyEntry Hotkey, bool IsNewBinding);

/// <summary>
/// Event fired when hotkey listening starts.<br/>
/// Published on the framework thread, so subscribers may touch game state directly.
/// </summary>
/// <param name="HotkeyId">The hotkey identifier.</param>
/// <param name="Mode">The input mode used for listening.</param>
public record HotkeyListeningStartedEvent(string HotkeyId, HotkeyListenMode Mode);

/// <summary>
/// Event fired when hotkey listening stops.<br/>
/// Published on the framework thread, so subscribers may touch game state directly, whether the stop came from
/// the plugin or from detection capturing a binding.
/// </summary>
/// <param name="HotkeyId">The hotkey identifier.</param>
/// <param name="WasCancelled">Whether listening was cancelled without binding.</param>
public record HotkeyListeningStoppedEvent(string HotkeyId, bool WasCancelled);
