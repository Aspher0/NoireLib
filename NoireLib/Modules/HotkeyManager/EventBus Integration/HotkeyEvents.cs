namespace NoireLib.HotkeyManager;

/// <summary>
/// Event fired when a hotkey is triggered.<br/>
/// Published on the framework thread, so subscribers may touch game state directly.
/// </summary>
/// <param name="Hotkey">The triggered hotkey entry.</param>
public record HotkeyTriggeredEvent(HotkeyEntry Hotkey);

/// <summary>
/// Event fired when a hotkey binding changes.<br/>
/// Published on the framework thread, so subscribers may touch game state directly, and so a rebind captured by
/// the detection timer reaches subscribers on the same thread as one made by the plugin itself.
/// </summary>
/// <param name="Hotkey">
/// The live hotkey entry, already carrying the new binding. It is not a snapshot: a further rebind before this
/// event is delivered shows through, and a subscriber that writes to the entry writes to the registered one.
/// </param>
/// <param name="IsNewBinding">
/// Whether the binding actually differed from the one the hotkey already had, as of the moment it was written.
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
/// Published on the framework thread, so subscribers may touch game state directly, and so a stop that detection
/// performs from its own timer thread after capturing a binding reaches subscribers on the same thread as one the
/// plugin requests itself.
/// </summary>
/// <param name="HotkeyId">The hotkey identifier.</param>
/// <param name="WasCancelled">Whether listening was cancelled without binding.</param>
public record HotkeyListeningStoppedEvent(string HotkeyId, bool WasCancelled);
