using NoireLib.Helpers.ObjectExtensions;
using System;
using System.Threading;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Represents a registered hotkey entry.<br/>
/// The entry handed back by <see cref="NoireHotkeyManager.TryGetHotkey"/> is the live entry the detection loop
/// reads: assigning one of its options takes effect on the next detection tick and persists if the owning manager
/// does, coalescing a burst of assignments into a single write while the game is running. Assigning
/// <see cref="Binding"/> routes through <see cref="NoireHotkeyManager.SetHotkeyBinding"/>, raising the
/// binding-changed notifications and persisting exactly as that method does. <see cref="Id"/> and
/// <see cref="Callback"/> are ordinary values; changing the id after registration is not supported.
/// </summary>
public sealed class HotkeyEntry
{
    /// <summary>
    /// The unique identifier for the hotkey, matched ignoring case (so "my.hotkey" and "My.Hotkey" are the same)
    /// and immutable after registration.
    /// </summary>
    public string Id { get; set; }

    private string displayName;

    /// <summary>
    /// The display name for the hotkey, used as the label of the binding UI; a blank name is replaced with
    /// <see cref="Id"/> on registration.
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
    /// The binding for this hotkey; on a registered entry, assigning it routes through
    /// <see cref="NoireHotkeyManager.SetHotkeyBinding"/> (records the change, raises binding-changed
    /// notifications on the framework thread, and persists), otherwise it is a plain value.
    /// </summary>
    public HotkeyBinding Binding
    {
        get => binding;
        set
        {
            // Routed through the manager so this setter behaves like SetHotkeyBinding on a registered entry.
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
    /// Gets or sets whether to block game input when this hotkey is pressed; persisted (so it survives a restart
    /// and overrides the registered value on the next load), unlike the momentary <see cref="SuppressGameInput"/>.
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

    private int gameInputSuppressions;

    /// <summary>
    /// Whether something is holding this hotkey's key away from the game right now: true while any
    /// <see cref="SuppressGameInput"/> is outstanding, checked alongside <see cref="BlockGameInput"/> since
    /// either one takes the key.
    /// </summary>
    public bool IsGameInputSuppressed => Volatile.Read(ref gameInputSuppressions) > 0;

    /// <summary>
    /// Takes this hotkey's key away from the game until <see cref="ReleaseGameInputSuppression"/> gives it back;
    /// the runtime, non-persisted counterpart of <see cref="BlockGameInput"/>, so a forgotten release cannot
    /// outlive the session. Calls nest: the key returns to the game once the last one releases.
    /// </summary>
    public void SuppressGameInput() => Interlocked.Increment(ref gameInputSuppressions);

    /// <summary>
    /// Gives back one <see cref="SuppressGameInput"/>; releasing more than was taken does nothing.
    /// </summary>
    public void ReleaseGameInputSuppression()
    {
        // Floored rather than left to go negative, so an unbalanced release cannot put the count below zero and
        // silently swallow the next genuine suppression.
        while (true)
        {
            var current = Volatile.Read(ref gameInputSuppressions);

            if (current <= 0)
                return;

            if (Interlocked.CompareExchange(ref gameInputSuppressions, current - 1, current) == current)
                return;
        }
    }

    /// <summary>
    /// The runtime state of the activation state machine for this entry, written only on the detection thread.
    /// </summary>
    internal HotkeyActivationState Activation;

    /// <summary>
    /// Whether this hotkey is suppressed until its key is released, set when a rebind capture or a game text
    /// input claims the key while it is down, tracked apart from <see cref="Activation"/>.
    /// </summary>
    internal bool BlockedWhileDown { get; set; }

    private volatile NoireHotkeyManager? owner;

    /// <summary>
    /// The manager currently holding this entry, or null when not registered; set on registration, cleared on
    /// unregister or teardown. The notifying option setters route a runtime change back through it to persist,
    /// falling back to a plain field write when there is no owner; volatile since the registering thread need
    /// not be the one that later reconfigures the entry.
    /// </summary>
    internal NoireHotkeyManager? Owner
    {
        get => owner;
        set => owner = value;
    }

    private volatile bool unregistered;

    /// <summary>
    /// Whether the manager has stopped holding this entry; delivery reads it to discard a trigger queued before
    /// an unregister rather than invoke a retired callback. Volatile, since removal and delivery need not run
    /// on the same thread and delivery reads it without the manager's lock.
    /// </summary>
    internal bool Unregistered
    {
        get => unregistered;
        set => unregistered = value;
    }

    /// <summary>
    /// Writes the binding field directly, bypassing the <see cref="Binding"/> setter's routing; used by
    /// <see cref="NoireHotkeyManager.SetHotkeyBinding"/> itself, to avoid recursing into its own call.
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
