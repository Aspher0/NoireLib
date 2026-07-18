using NoireLib.Helpers.ObjectExtensions;
using System;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Represents a registered hotkey entry.<br/>
/// The entry handed back by <see cref="NoireHotkeyManager.TryGetHotkey"/> is the live entry the detection loop
/// reads, so assigning one of its options takes effect on the next detection tick, and, when the owning manager
/// persists its hotkeys, the change is saved as well. This is the supported way to reconfigure a hotkey at
/// runtime; there is no need to remove and re-add it. A burst of assignments coalesces into a single write while
/// the game is running.<br/>
/// Assigning <see cref="Binding"/> routes through <see cref="NoireHotkeyManager.SetHotkeyBinding"/>, so it raises
/// the binding-changed notifications and persists exactly as that method does. <see cref="Id"/> and
/// <see cref="Callback"/> are ordinary values; changing the id after registration is not supported.
/// </summary>
public sealed class HotkeyEntry
{
    /// <summary>
    /// The unique identifier for the hotkey.<br/>
    /// Ids are matched ignoring case, so "my.hotkey" and "My.Hotkey" name one hotkey, and either spelling
    /// reaches it from every surface that takes an id. Changing the id after registration is not supported.
    /// </summary>
    public string Id { get; set; }

    private string displayName;

    /// <summary>
    /// The display name for the hotkey, used as the label of the binding UI.<br/>
    /// Registering an entry whose display name is blank replaces it with <see cref="Id"/>, so that the binding
    /// UI always has a name to render.
    /// </summary>
    public string DisplayName
    {
        get => displayName;
        set
        {
            if (displayName == value)
                return;

            displayName = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private HotkeyBinding binding;

    /// <summary>
    /// The binding for this hotkey.<br/>
    /// Assigning this on a registered entry is equivalent to calling
    /// <see cref="NoireHotkeyManager.SetHotkeyBinding"/>: the manager records the change, raises its
    /// binding-changed notifications on the framework thread, and persists it. On an entry that is not registered
    /// with a manager it is a plain value.
    /// </summary>
    public HotkeyBinding Binding
    {
        get => binding;
        set
        {
            // A registered entry's binding has a first-class path that notifies and persists; routing through it
            // rather than writing the field is what makes assigning Binding here behave like SetHotkeyBinding.
            var owner = Owner;
            if (owner != null)
            {
                owner.SetHotkeyBinding(Id, value);
                return;
            }

            binding = value;
        }
    }

    /// <summary>
    /// The action to invoke when the hotkey is triggered.
    /// </summary>
    public Action? Callback { get; set; }

    private bool enabled = true;

    /// <summary>
    /// Gets or sets whether this hotkey is enabled.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;

            enabled = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private HotkeyActivationMode activationMode = HotkeyActivationMode.Pressed;

    /// <summary>
    /// Gets or sets the activation mode for this hotkey.
    /// </summary>
    public HotkeyActivationMode ActivationMode
    {
        get => activationMode;
        set
        {
            if (activationMode == value)
                return;

            activationMode = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private TimeSpan holdDelay = 400.Milliseconds();

    /// <summary>
    /// Gets or sets the delay required to trigger held hotkeys.
    /// </summary>
    public TimeSpan HoldDelay
    {
        get => holdDelay;
        set
        {
            if (holdDelay == value)
                return;

            holdDelay = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private TimeSpan fixedRepeatDelay = 80.Milliseconds();

    /// <summary>
    /// Gets or sets the fixed repeat delay for repeat hotkeys.
    /// </summary>
    public TimeSpan FixedRepeatDelay
    {
        get => fixedRepeatDelay;
        set
        {
            if (fixedRepeatDelay == value)
                return;

            fixedRepeatDelay = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private TimeSpan repeatDelayMin = 80.Milliseconds();

    /// <summary>
    /// Gets or sets the minimum repeat delay for repeat hotkeys.
    /// </summary>
    public TimeSpan RepeatDelayMin
    {
        get => repeatDelayMin;
        set
        {
            if (repeatDelayMin == value)
                return;

            repeatDelayMin = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private TimeSpan repeatDelayMax = 80.Milliseconds();

    /// <summary>
    /// Gets or sets the maximum repeat delay for repeat hotkeys.
    /// </summary>
    public TimeSpan RepeatDelayMax
    {
        get => repeatDelayMax;
        set
        {
            if (repeatDelayMax == value)
                return;

            repeatDelayMax = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private bool useRandomRepeatDelay;

    /// <summary>
    /// Gets or sets whether to randomize repeat delay between the minimum and maximum values.
    /// </summary>
    public bool UseRandomRepeatDelay
    {
        get => useRandomRepeatDelay;
        set
        {
            if (useRandomRepeatDelay == value)
                return;

            useRandomRepeatDelay = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private bool blockWhenTextInputActive = true;

    /// <summary>
    /// Gets or sets whether to block this hotkey when a game text input is active.
    /// </summary>
    public bool BlockWhenTextInputActive
    {
        get => blockWhenTextInputActive;
        set
        {
            if (blockWhenTextInputActive == value)
                return;

            blockWhenTextInputActive = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private bool requireGameFocus = true;

    /// <summary>
    /// Gets or sets whether this hotkey should only trigger when the game window is focused.
    /// </summary>
    public bool RequireGameFocus
    {
        get => requireGameFocus;
        set
        {
            if (requireGameFocus == value)
                return;

            requireGameFocus = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    private bool blockGameInput;

    /// <summary>
    /// Gets or sets whether to block game input when this hotkey is pressed.
    /// </summary>
    public bool BlockGameInput
    {
        get => blockGameInput;
        set
        {
            if (blockGameInput == value)
                return;

            blockGameInput = value;
            Owner?.OnEntryOptionChanged(this);
        }
    }

    /// <summary>
    /// The runtime state of the activation state machine for this entry, written only on the detection thread.
    /// </summary>
    internal HotkeyActivationState Activation;

    /// <summary>
    /// Whether this hotkey is suppressed until its key is released, set when a rebind capture or a game text input
    /// claimed the key while it was down. Tracked apart from <see cref="Activation"/> because it is decided against
    /// live game state in the detection loop rather than by the pure activation machine.
    /// </summary>
    internal bool BlockedWhileDown { get; set; }

    private volatile NoireHotkeyManager? owner;

    /// <summary>
    /// The manager currently holding this entry, or null when it is not registered.<br/>
    /// Set on registration and cleared on unregister or teardown. The notifying option setters read it to route a
    /// runtime change back to the manager that persists it; a change made on an entry with no owner is a plain
    /// field write. Volatile because the thread that registers an entry need not be the thread that later
    /// reconfigures it.
    /// </summary>
    internal NoireHotkeyManager? Owner
    {
        get => owner;
        set => owner = value;
    }

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
    /// Writes the binding field directly, bypassing the routing the <see cref="Binding"/> setter does for a
    /// registered entry.<br/>
    /// Used by <see cref="NoireHotkeyManager.SetHotkeyBinding"/>, which is the routed path itself and would
    /// otherwise recurse into its own call.
    /// </summary>
    /// <param name="value">The binding to store.</param>
    internal void SetBindingStorage(HotkeyBinding value) => binding = value;

    /// <summary>
    /// Creates a new hotkey entry.
    /// </summary>
    public HotkeyEntry(string id, string displayName, HotkeyBinding binding, Action? callback, bool enabled, HotkeyActivationMode activationMode)
    {
        Id = id;
        this.displayName = displayName;
        this.binding = binding;
        Callback = callback;
        this.enabled = enabled;
        this.activationMode = activationMode;
    }

    /// <summary>
    /// Creates a new hotkey entry with default values.
    /// </summary>
    public HotkeyEntry()
    {
        Id = string.Empty;
        displayName = string.Empty;
    }
}
