using NoireLib.Helpers.ObjectExtensions;
using System;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Represents a registered hotkey entry.
/// </summary>
public sealed class HotkeyEntry
{
    /// <summary>
    /// The unique identifier for the hotkey.<br/>
    /// Ids are matched ignoring case, so "my.hotkey" and "My.Hotkey" name one hotkey, and either spelling
    /// reaches it from every surface that takes an id.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The display name for the hotkey, used as the label of the binding UI.<br/>
    /// Registering an entry whose display name is blank replaces it with <see cref="Id"/>, so that the binding
    /// UI always has a name to render.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// The binding for this hotkey.
    /// </summary>
    public HotkeyBinding Binding { get; set; }

    /// <summary>
    /// The action to invoke when the hotkey is triggered.
    /// </summary>
    public Action? Callback { get; set; }

    /// <summary>
    /// Gets or sets whether this hotkey is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the activation mode for this hotkey.
    /// </summary>
    public HotkeyActivationMode ActivationMode { get; set; } = HotkeyActivationMode.Pressed;

    /// <summary>
    /// Gets or sets the delay required to trigger held hotkeys.
    /// </summary>
    public TimeSpan HoldDelay { get; set; } = 400.Milliseconds();

    /// <summary>
    /// Gets or sets the fixed repeat delay for repeat hotkeys.
    /// </summary>
    public TimeSpan FixedRepeatDelay { get; set; } = 80.Milliseconds();

    /// <summary>
    /// Gets or sets the minimum repeat delay for repeat hotkeys.<br/>
    /// </summary>
    public TimeSpan RepeatDelayMin { get; set; } = 80.Milliseconds();

    /// <summary>
    /// Gets or sets the maximum repeat delay for repeat hotkeys.<br/>
    /// </summary>
    public TimeSpan RepeatDelayMax { get; set; } = 80.Milliseconds();

    /// <summary>
    /// Gets or sets whether to randomize repeat delay between the minimum and maximum values.
    /// </summary>
    public bool UseRandomRepeatDelay { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to block this hotkey when a game text input is active.
    /// </summary>
    public bool BlockWhenTextInputActive { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this hotkey should only trigger when the game window is focused.
    /// </summary>
    public bool RequireGameFocus { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to block game input when this hotkey is pressed.
    /// </summary>
    public bool BlockGameInput { get; set; } = false;

    internal bool WasDown { get; set; }
    internal bool Armed { get; set; }
    internal bool PhysicallyHeld { get; set; }
    internal long? HoldStartTimestamp { get; set; }
    internal bool HoldTriggered { get; set; }
    internal long? NextRepeatTimestamp { get; set; }
    internal bool BlockedWhileDown { get; set; }

    private volatile bool unregistered;

    /// <summary>
    /// Whether the manager has stopped holding this entry.<br/>
    /// Detection queues a trigger ahead of the framework thread that delivers it, so an entry can be removed
    /// while one of its triggers is still waiting. Delivery reads this to discard such a trigger rather than
    /// invoke a callback the consumer has already retired. Volatile because the removal and the delivery need
    /// not run on the same thread, and the delivery deliberately reads it without taking the manager's lock.
    /// </summary>
    internal bool Unregistered
    {
        get => unregistered;
        set => unregistered = value;
    }

    /// <summary>
    /// Creates a new hotkey entry.
    /// </summary>
    public HotkeyEntry(string id, string displayName, HotkeyBinding binding, Action? callback, bool enabled, HotkeyActivationMode activationMode)
    {
        Id = id;
        DisplayName = displayName;
        Binding = binding;
        Callback = callback;
        Enabled = enabled;
        ActivationMode = activationMode;
    }

    /// <summary>
    /// Creates a new hotkey entry with default values.
    /// </summary>
    public HotkeyEntry()
    {
        Id = string.Empty;
        DisplayName = string.Empty;
    }
}
