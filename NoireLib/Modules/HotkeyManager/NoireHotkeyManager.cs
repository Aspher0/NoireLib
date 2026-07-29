using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace NoireLib.HotkeyManager;

/// <summary>
/// A module that manages editable hotkeys and triggers callbacks when they activate; hotkey ids are matched
/// ignoring case, so "my.hotkey" and "My.Hotkey" name the same hotkey.<br/>
/// Every callback, CLR event and EventBus publication runs on the framework thread, whether triggered by a
/// consumer call or by the detection timer capturing a rebind, falling back to inline delivery on the calling
/// thread when NoireLib is not initialized. Once the module is disposed, nothing is delivered again.
/// </summary>
public class NoireHotkeyManager : NoireModuleBase<NoireHotkeyManager, HotkeyManagerConfigInstance>
{
    /// <summary>
    /// The rule that decides when two hotkey ids name the same hotkey: case is ignored, matching every lookup,
    /// comparison and stored binding.
    /// </summary>
    private static readonly StringComparer HotkeyIdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, HotkeyEntry> hotkeys = new(HotkeyIdComparer);
    private readonly object hotkeyLock = new();

    /// <summary>
    /// A lock-free snapshot of the registered entries, read by the detection tick and the framework input
    /// blocker without taking <see cref="hotkeyLock"/> or allocating.<br/>
    /// Rebuilt only when the set of entries changes structurally; it holds live entry references, so an option
    /// change and a stale read are both safe without one, and a removed entry is skipped by the same disabled
    /// and unregistered guards that already gate delivery.
    /// </summary>
    private volatile HotkeyEntry[] entriesSnapshot = Array.Empty<HotkeyEntry>();
    private readonly HashSet<int> previousKeysDown = new();
    private readonly HashSet<int> currentKeysDown = new();
    private readonly byte[] rawKeyboardState = new byte[256];
    private const int UpdateIntervalMilliseconds = 16;

    /// <summary>
    /// The most triggers that may wait for the framework thread at once; once reached, the oldest triggers are
    /// dropped so a frozen frame loop cannot grow the queue without bound.
    /// </summary>
    internal const int MaxPendingTriggers = 256;

    private readonly ConcurrentQueue<HotkeyEntry> pendingTriggers = new();
    private readonly object timerLock = new();
    private int pendingTriggerCount;
    private Timer? updateTimer;
    private long lastUpdateTick;
    private int updateInProgress;
    private volatile bool disposed;

    /// <summary>
    /// How long a runtime option change waits before it is written, so a burst of sets on one hotkey coalesces
    /// into a single disk write rather than one per property.
    /// </summary>
    private static readonly TimeSpan OptionPersistDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The debounce key prefix for this manager's option-change persists; unique per instance and combined with
    /// the hotkey id, so pending writes for different hotkeys or different manager instances never collide.
    /// </summary>
    private readonly string optionPersistKeyPrefix = $"NoireLib_HotkeyManager_Persist_{Guid.NewGuid():N}_";

    private IReadOnlyList<int> validKeyCodes = Array.Empty<int>();
    private ListeningSession? listeningSession;
    private string? lastBindingChangedId;

    // The detection tick owns the key buffers and rewrites them every 16ms, so it formats what the binding UI
    // shows and publishes the whole string here for a single read on the framework thread.
    private volatile string listeningKeyboardText = string.Empty;

    private int? lastPressedKey;
    private GamepadButtons? lastPressedGamepadButton;
    private volatile int postListeningBlockKeyCode;

    /// <summary>
    /// One rebind capture session, held as a single immutable value.
    /// </summary>
    /// <remarks>
    /// The hotkey being rebound, the input source, and the captured modifiers are one unit read and written
    /// across threads: a session starts and stops on the caller's thread, advances on the detection timer
    /// thread, and is read by the framework thread drawing the binding UI and blocking input. Every field is
    /// init-only; a session is replaced by reference rather than mutated in place.
    /// </remarks>
    /// <param name="HotkeyId">The identifier of the hotkey being rebound.</param>
    /// <param name="Mode">The input source being watched for the new binding.</param>
    /// <param name="ModifierState">The modifiers that were held when the session last saw a modifier only combination, if any.</param>
    /// <param name="WaitingForModifierRelease">Whether a modifier only combination is waiting to be committed once the modifiers are released.</param>
    internal sealed record ListeningSession(
        string HotkeyId,
        HotkeyListenMode Mode,
        (bool Ctrl, bool Shift, bool Alt)? ModifierState,
        bool WaitingForModifierRelease);

    /// <summary>
    /// The rebind capture session in progress, or null when not listening; read once, so a session replaced or
    /// ended mid-read cannot show up as a mixture of the old and new one.
    /// </summary>
    internal ListeningSession? CurrentListeningSession => Volatile.Read(ref listeningSession);

    /// <summary>
    /// The associated EventBus instance for publishing hotkey events.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireHotkeyManager() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireHotkeyManager"/> module.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="shouldSaveKeybinds">Whether the hotkey manager should save keybinds to configuration.</param>
    /// <param name="eventBus">The optional EventBus instance for publishing hotkey events.</param>
    public NoireHotkeyManager(string? moduleId = null, bool active = true, bool enableLogging = true, bool shouldSaveKeybinds = true, NoireEventBus? eventBus = null)
        : base(moduleId, active, enableLogging, shouldSaveKeybinds, eventBus) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>,
    /// for internal module management only.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireHotkeyManager(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters.</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool shouldSaveKeys)
            shouldSaveKeybinds = shouldSaveKeys;

        if (args.Length > 1 && args[1] is NoireEventBus eventBus)
            EventBus = eventBus;

        RefreshValidKeys();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Hotkey Manager initialized.");
    }

    /// <summary>
    /// Called when the module is activated (<see cref="NoireModuleBase{TModule}.IsActive"/> going from false to true).<br/>
    /// Activating before NoireLib is initialized records the active state but wires nothing, since detection and
    /// delivery need the framework thread; the module stays inert even once NoireLib later initializes, so
    /// activate it again to start detection.
    /// </summary>
    protected override void OnActivated()
    {
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogWarning(this, "Hotkey Manager activated before NoireLib was initialized. No hotkey will be detected until the module is activated again once NoireLib is initialized.");
            return;
        }

        StartUpdateTimer();
        NoireService.Framework.Update += OnFrameworkUpdate;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Hotkey Manager activated.");
    }

    /// <summary>
    /// Called when the module is deactivated (<see cref="NoireModuleBase{TModule}.IsActive"/> going from true to
    /// false); detection stops and anything it had detected but not yet delivered is discarded, whether or not
    /// NoireLib is initialized.
    /// </summary>
    protected override void OnDeactivated()
    {
        // Detaching needs the service; an activation while NoireLib was uninitialized never attached this
        // handler, so there is nothing to detach.
        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnFrameworkUpdate;

        StopUpdateTimer();
        ResetInputState();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Hotkey Manager deactivated.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed || !IsActive || !NoireService.IsInitialized())
            return;

        DrainPendingTriggers();
        BlockListeningInputOnFramework();
        BlockHotkeyInputsOnFramework();
    }

    // Set by the consumer and read by every save path, including detection capturing a rebind from the timer
    // thread; volatile so persistence turned on is seen by the very next tick.
    private volatile bool shouldSaveKeybinds = true;
    /// <summary>
    /// Gets or sets whether the hotkey manager should persist keybinds to configuration.
    /// </summary>
    public bool ShouldSaveKeybinds
    {
        get => shouldSaveKeybinds;
        set
        {
            if (shouldSaveKeybinds == value)
                return;

            shouldSaveKeybinds = value;

            if (shouldSaveKeybinds)
                SaveAllHotkeys();
        }
    }

    /// <summary>
    /// Sets the value of <see cref="ShouldSaveKeybinds"/>.
    /// </summary>
    /// <param name="shouldSaveKeybinds">Whether the hotkey manager should save keybinds to configuration.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHotkeyManager SetShouldSaveKeybinds(bool shouldSaveKeybinds)
    {
        ShouldSaveKeybinds = shouldSaveKeybinds;
        return this;
    }

    /// <summary>
    /// Raised when a hotkey is triggered, with the triggered entry.<br/>
    /// Invoked on the framework thread, so handlers may touch game state directly; falls back to the detecting
    /// thread when NoireLib is not initialized.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyTriggered;

    /// <summary>
    /// Raised when a hotkey binding changes, with the live entry already carrying the new binding.<br/>
    /// Invoked on the framework thread regardless of whether the rebind came from the plugin or from the
    /// detection timer, so handlers may touch game state directly; falls back to the calling thread when
    /// NoireLib is not initialized. A handler may call back into this manager freely, including changing or
    /// unregistering the hotkey it was just told about.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyChanged;


    /// <summary>
    /// Gets a value indicating whether the module is currently listening for a new binding; when the hotkey id
    /// is also wanted, read <see cref="ListeningHotkeyId"/> alone instead, since a capture landing between two
    /// separate reads can leave the second one null.
    /// </summary>
    public bool IsListening => CurrentListeningSession != null;

    /// <summary>
    /// Gets the identifier of the hotkey currently being rebound, or null when nothing is being rebound.
    /// </summary>
    public string? ListeningHotkeyId => CurrentListeningSession?.HotkeyId;

    /// <summary>
    /// Registers a hotkey with the given binding and callback; a blank <see cref="HotkeyEntry.DisplayName"/> is
    /// replaced with the entry's <see cref="HotkeyEntry.Id"/>.
    /// </summary>
    /// <param name="hotkeyDefinition">The hotkey definition containing the id, binding, callback, and other options.</param>
    /// <returns>True if the hotkey was registered successfully; otherwise, false.</returns>
    public bool RegisterHotkey(HotkeyEntry hotkeyDefinition)
    {
        if (hotkeyDefinition == null)
            throw new ArgumentNullException(nameof(hotkeyDefinition));

        if (string.IsNullOrWhiteSpace(hotkeyDefinition.Id))
            throw new ArgumentException("Hotkey id cannot be null or empty.", nameof(hotkeyDefinition));

        if (hotkeyDefinition.Callback == null)
            throw new ArgumentNullException(nameof(hotkeyDefinition.Callback));

        ApplyPersistedHotkey(hotkeyDefinition);

        lock (hotkeyLock)
        {
            if (hotkeys.ContainsKey(hotkeyDefinition.Id))
                return false;

            // The binding UI uses the display name as its button label; a blank one leaves the button showing
            // only the binding.
            if (string.IsNullOrWhiteSpace(hotkeyDefinition.DisplayName))
                hotkeyDefinition.DisplayName = hotkeyDefinition.Id;

            // Cleared so a re-registered entry is deliverable again, rather than staying silenced by the flag
            // its earlier removal left.
            hotkeyDefinition.Unregistered = false;

            // Set last, so the entry's notifying option setters start routing runtime changes back here only
            // once fully registered; the initial persist is covered by the explicit SaveHotkey below.
            hotkeyDefinition.Owner = this;

            hotkeys.Add(hotkeyDefinition.Id, hotkeyDefinition);
            RebuildEntriesSnapshot();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Registered hotkey '{hotkeyDefinition.Id}' with binding {KeybindsHelper.FormatBinding(hotkeyDefinition.Binding)}.");

        SaveHotkey(hotkeyDefinition);

        return true;
    }

    /// <summary>
    /// Updates the callback for an existing hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to update.</param>
    /// <param name="callback">The new callback for the hotkey.</param>
    /// <returns>True if the hotkey was found and updated; otherwise, false.</returns>
    public bool SetHotkeyCallback(string id, Action callback)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return false;

            entry.Callback = callback;
            return true;
        }
    }

    /// <summary>
    /// Sets the keyboard or gamepad binding for a hotkey.<br/>
    /// The binding is written before <see cref="OnHotkeyChanged"/> and <see cref="HotkeyBindingChangedEvent"/>
    /// raise on the framework thread, so a handler reading it back always sees the change that notified it;
    /// those notifications reach handlers after this returns unless the caller is already on that thread.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to update.</param>
    /// <param name="binding">The new binding for the hotkey.</param>
    /// <returns>True if the hotkey was found and updated; otherwise, false.</returns>
    public bool SetHotkeyBinding(string id, HotkeyBinding binding)
    {
        HotkeyEntry changedEntry;
        bool isNewBinding;

        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return false;

            isNewBinding = entry.Binding != binding;

            // Written through the field, not the property: HotkeyEntry.Binding's setter on a registered entry
            // routes back into this method and would recurse.
            entry.SetBindingStorage(binding);

            // Published rather than plainly written: read by the binding UI on the framework thread without
            // this lock, and written here from whichever thread captured the rebind.
            Volatile.Write(ref lastBindingChangedId, id);
            changedEntry = entry;
        }

        // Persisting and notifying both run with the lock released: the save is a disk write, and notifying
        // hands control to consumer code that may call straight back into this manager. Held under the lock, a
        // same-thread handler would reenter (a Monitor is re-entrant) and could mutate the hotkey dictionary
        // mid-operation, while a handler on another thread would simply block for as long as the consumer ran.
        SaveHotkey(changedEntry);

        // IsNewBinding is computed at write time and carried to the notification. The entry is passed live
        // rather than copied, so a consumer's own edits (or a further rebind arriving before delivery) reach
        // the registered entry rather than a snapshot.
        PostToFrameworkThread(() =>
        {
            OnHotkeyChanged?.Invoke(changedEntry);
            PublishEvent(new HotkeyBindingChangedEvent(changedEntry, isNewBinding));
        });

        return true;
    }

    /// <summary>
    /// Clears the binding for a hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to clear.</param>
    /// <returns>True if the hotkey was found and cleared; otherwise, false.</returns>
    public bool ClearHotkeyBinding(string id)
    {
        return SetHotkeyBinding(id, new HotkeyBinding(0));
    }

    /// <summary>
    /// Enables or disables a hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to update.</param>
    /// <param name="enabled">True to enable the hotkey; false to disable it.</param>
    /// <returns>True if the hotkey was found and updated; otherwise, false.</returns>
    public bool SetHotkeyEnabled(string id, bool enabled)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return false;

            entry.Enabled = enabled;
            return true;
        }
    }

    /// <summary>
    /// Removes a hotkey from the manager; a trigger already captured for it but not yet delivered is discarded,
    /// so no callback for it runs after this returns.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to remove.</param>
    /// <returns>True if the hotkey was found and removed; otherwise, false.</returns>
    public bool UnregisterHotkey(string id)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.Remove(id, out var entry))
                return false;

            // Detection runs ahead of delivery, so a trigger for this hotkey can already be queued; delivery
            // cannot consult this dictionary without holding hotkeyLock across a consumer callback, so the
            // entry itself carries the fact of its removal to the drain.
            entry.Unregistered = true;

            // Detaching stops a later option set on the retired entry from persisting through this manager.
            entry.Owner = null;
            RebuildEntriesSnapshot();
        }

        RemoveStoredHotkey(id);
        return true;
    }

    /// <summary>
    /// Tries to get a registered hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to retrieve.</param>
    /// <param name="entry">The hotkey entry if found; otherwise, null.</param>
    /// <returns>True if the hotkey was found; otherwise, false.</returns>
    public bool TryGetHotkey(string id, out HotkeyEntry entry)
    {
        lock (hotkeyLock)
        {
            return hotkeys.TryGetValue(id, out entry!);
        }
    }

    /// <summary>
    /// Gets all registered hotkeys.
    /// </summary>
    /// <returns>A read-only collection of all registered hotkeys.</returns>
    public IReadOnlyCollection<HotkeyEntry> GetHotkeys()
    {
        // Copied from the lock-free snapshot into a new list the caller owns; the snapshot already reflects
        // every structural change, so this needs neither the lock nor a walk of the dictionary.
        return entriesSnapshot.ToList();
    }

    /// <summary>
    /// Starts listening for a new binding for the specified hotkey; <see cref="HotkeyListeningStartedEvent"/>
    /// publishes on the framework thread, reaching subscribers after this returns unless the caller is already
    /// on that thread.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to listen for.</param>
    /// <param name="mode">The input mode for the hotkey.</param>
    /// <returns>True if listening started successfully; otherwise, false.</returns>
    public bool StartListening(string id, HotkeyListenMode mode = HotkeyListenMode.Keyboard)
    {
        if (!TryGetHotkey(id, out _))
            return false;

        // Cleared before the session installs, so the binding UI cannot render the previous session's captured
        // keys against this new one.
        listeningKeyboardText = string.Empty;

        // One write installs the whole session, so a reader that sees this id also sees the mode and state it
        // started with, never a mixture with the session it replaced.
        Volatile.Write(ref listeningSession, new ListeningSession(id, mode, null, false));

        PostToFrameworkThread(() => PublishEvent(new HotkeyListeningStartedEvent(id, mode)));
        return true;
    }

    /// <summary>
    /// Stops listening for a new binding; <see cref="HotkeyListeningStoppedEvent"/> publishes on the framework
    /// thread, reaching subscribers after this returns unless the caller is already on it. Detection may also
    /// stop listening on its own, from the timer thread, once it captures a binding.
    /// </summary>
    /// <param name="wasCancelled">True if the listening was cancelled; otherwise, false.</param>
    public void StopListening(bool wasCancelled = true)
    {
        // Exchanged rather than tested-then-cleared, so exactly one caller ends and announces a given session,
        // even when detection captures a binding at the same moment a consumer cancels it.
        var stopped = Interlocked.Exchange(ref listeningSession, null);
        if (stopped == null)
            return;

        PostToFrameworkThread(() => PublishEvent(new HotkeyListeningStoppedEvent(stopped.HotkeyId, wasCancelled)));
    }

    /// <summary>
    /// Draws a fully managed ImGui button for rebinding a hotkey.
    /// </summary>
    /// <param name="id">The id of the hotkey to bind.</param>
    /// <param name="label">
    /// The label to display on the button; <see langword="string.Empty"/> hides the display name.
    /// </param>
    /// <param name="size">The size of the button.</param>
    /// <param name="mode">The input mode for the hotkey.</param>
    /// <param name="allowClear">Whether right-clicking the button should clear the binding.</param>
    /// <param name="showClearTooltip">Whether to show a tooltip when hovering the button.</param>
    /// <returns>True if the hotkey was successfully drawn, false otherwise.</returns>
    public bool DrawKeybindInputButton(
        string id,
        string? label = null,
        Vector2? size = null,
        HotkeyListenMode mode = HotkeyListenMode.Keyboard,
        bool allowClear = true,
        bool showClearTooltip = true)
    {
        if (!TryGetHotkey(id, out var entry))
        {
            var labelText = label ?? id;
            if (labelText.IsNullOrEmpty())
                labelText = "<LabelNotFound>";
            ImGui.Text($"Hotkey with label '{labelText}' was not found.");
            return false;
        }

        var bindingText = KeybindsHelper.FormatBinding(entry.Binding);
        var showOnlyBinding = label == string.Empty;
        var buttonLabel = showOnlyBinding ? string.Empty : (label ?? entry.DisplayName);
        var isListening = IsListeningFor(id);

        var displayText = isListening
            ? GetListeningDisplayText(mode, buttonLabel, showOnlyBinding)
            : (showOnlyBinding ? bindingText : $"{buttonLabel}: {bindingText}");

        var buttonId = $"##NoireHotkey_{id}";
        var buttonText = $"{displayText}{buttonId}";
        var buttonSize = size ?? Vector2.Zero;

        if (ImGui.Button(buttonText, buttonSize))
            StartListening(id, mode);

        if (isListening && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            StopListening();
            return false;
        }

        if (!isListening && allowClear && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ClearHotkeyBinding(id);

        if (allowClear && showClearTooltip && ImGui.IsItemHovered())
            ImGui.SetTooltip("Right click to unbind");

        if (isListening)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Press Esc to cancel");
        }

        return TryConsumeBindingChanged(id);
    }

    /// <summary>
    /// Reports whether the rebind capture in progress, if any, is the given hotkey's.
    /// </summary>
    /// <remarks>
    /// The session is read once, so the answer describes a single session rather than a field the detection
    /// timer can empty partway through, and the id is matched by the module's case-insensitive rule.
    /// </remarks>
    /// <param name="id">The identifier of the hotkey to test.</param>
    /// <returns>True if a rebind capture is in progress for that hotkey; otherwise, false.</returns>
    internal bool IsListeningFor(string id)
    {
        var session = CurrentListeningSession;
        return session != null && HotkeyIdComparer.Equals(session.HotkeyId, id);
    }

    /// <summary>
    /// Reports whether the given hotkey's binding has changed since this was last asked about it, and consumes
    /// the report so that only the first caller to ask sees it.
    /// </summary>
    /// <remarks>
    /// A rebind is recorded by whichever thread writes the binding (the detection timer for a capture) and read
    /// here on the framework thread. The record is cleared with a compare and swap rather than an unconditional
    /// write, so a different hotkey's rebind landing between the read and the clear survives to be reported to
    /// its own caller instead of being wiped out by this one.
    /// </remarks>
    /// <param name="id">The identifier of the hotkey to report on.</param>
    /// <returns>True if the hotkey's binding changed since the last call; otherwise, false.</returns>
    internal bool TryConsumeBindingChanged(string id)
    {
        var pendingId = Volatile.Read(ref lastBindingChangedId);
        if (!HotkeyIdComparer.Equals(pendingId, id))
            return false;

        // The comparand is the reference just read, never the caller's id: CompareExchange matches a string by
        // reference, so a merely equal id would clear nothing and let the report be handed out twice. The
        // conditional clear leaves another hotkey's rebind standing when it lands after the read.
        Interlocked.CompareExchange(ref lastBindingChangedId, null, pendingId);
        return true;
    }

    #region Private Methods

    private void RefreshValidKeys()
    {
        if (!NoireService.IsInitialized())
        {
            validKeyCodes = Array.Empty<int>();
            return;
        }

        validKeyCodes = NoireService.KeyState.GetValidVirtualKeys().Select(vk => (int)vk).ToArray();
    }

    private void StartUpdateTimer()
    {
        // A system timer is used instead of framework update, which is bound to FPS and would skip hotkeys at
        // low frame rates.
        lock (timerLock)
        {
            if (updateTimer != null || disposed)
                return;

            lastUpdateTick = Environment.TickCount64;
            updateTimer = new Timer(_ => OnSystemUpdate(), null, 0, UpdateIntervalMilliseconds);
        }
    }

    /// <summary>
    /// Stops detection and discards whatever it detected but has not delivered yet; blocks until any running
    /// tick finishes, so no timer thread work can touch the module's state after this returns.
    /// </summary>
    private void StopUpdateTimer()
    {
        Timer? timer;

        lock (timerLock)
        {
            timer = updateTimer;
            updateTimer = null;
        }

        if (timer != null)
        {
            // Timer.Dispose() can return while a tick is still executing, and updateInProgress only serializes
            // ticks against each other, not against teardown; waiting on the notify handle guarantees the tick
            // has finished before state is cleared. Dispose returns false only when the timer was already
            // disposed, in which case nothing signals the handle and waiting would hang.
            using var timerDrained = new ManualResetEvent(false);

            if (timer.Dispose(timerDrained))
                timerDrained.WaitOne();
        }

        // Detection is off and no tick can queue more, so discarding here is race free: a trigger detected
        // before the stop must not reach a consumer after listening has stopped.
        ClearPendingTriggers();
    }

    /// <summary>
    /// Ticks hotkey detection on the system timer, independent of the framework's frame rate.
    /// </summary>
    private void OnSystemUpdate()
    {
        if (Interlocked.Exchange(ref updateInProgress, 1) == 1)
            return;

        try
        {
            var now = Environment.TickCount64;
            if (now - lastUpdateTick < UpdateIntervalMilliseconds)
                return;

            lastUpdateTick = now;

            if (disposed || !IsActive || !NoireService.IsInitialized())
                return;

            var isFocused = WindowHelper.IsGameWindowFocused();
            if (!isFocused && IsListening)
            {
                ResetInputState();
                return;
            }

            if (validKeyCodes.Count == 0)
                RefreshValidKeys();

            UpdateKeyStates();

            // Read once and carried through the tick, so the capture works on a single session rather than a
            // field a consumer may replace mid-tick.
            var session = CurrentListeningSession;
            if (session != null)
            {
                // The binding UI draws on the framework thread and cannot safely read the key buffers this
                // tick is rewriting, so the display text is formatted here while they are still owned.
                listeningKeyboardText = KeybindsHelper.FormatListeningKeyboardInput(rawKeyboardState, currentKeysDown);

                ProcessListening(session);
                return;
            }

            ProcessHotkeys();
        }
        finally
        {
            Interlocked.Exchange(ref updateInProgress, 0);
        }
    }

    private void UpdateKeyStates()
    {
        KeybindsHelper.TryGetRawKeyboardState(rawKeyboardState);
        currentKeysDown.Clear();

        foreach (var keyCode in validKeyCodes)
        {
            if (KeybindsHelper.IsRawKeyDown(rawKeyboardState, keyCode))
                currentKeysDown.Add(keyCode);
        }

        if (currentKeysDown.Count == 0)
            lastPressedKey = null;

        var newlyPressedKey = KeybindsHelper.GetNewlyPressedKey(rawKeyboardState, validKeyCodes, previousKeysDown);
        if (newlyPressedKey.HasValue)
            lastPressedKey = newlyPressedKey;
        lastPressedGamepadButton = NoireService.GamepadState != null
            ? KeybindsHelper.GetPressedGamepadButton(NoireService.GamepadState)
            : null;

        previousKeysDown.Clear();
        foreach (var keyCode in currentKeysDown)
            previousKeysDown.Add(keyCode);
    }

    /// <summary>
    /// Advances a rebind capture by one detection tick; called from the detection timer thread.
    /// </summary>
    /// <param name="session">The session to advance, as read once by the caller.</param>
    private void ProcessListening(ListeningSession session)
    {
        if (session.Mode == HotkeyListenMode.Keyboard)
        {
            var modifierState = KeybindsHelper.GetRawModifierState(rawKeyboardState);
            var hasModifiers = modifierState.Ctrl || modifierState.Shift || modifierState.Alt;
            var activeKeyCode = currentKeysDown.FirstOrDefault(code => !KeybindsHelper.IsModifierKey(code));

            if (activeKeyCode != 0)
            {
                if (activeKeyCode == KeybindsHelper.VkEscape)
                {
                    StopListening();
                    return;
                }

                var binding = new HotkeyBinding(activeKeyCode, modifierState.Ctrl, modifierState.Shift, modifierState.Alt);
                SetHotkeyBinding(session.HotkeyId, binding);
                postListeningBlockKeyCode = activeKeyCode;
                SuppressHotkeyUntilRelease(session.HotkeyId);
                StopListening(false);
                return;
            }

            if (hasModifiers)
            {
                if (session.WaitingForModifierRelease && session.ModifierState.HasValue && HasModifierReleased(session.ModifierState.Value, modifierState))
                {
                    var pending = session.ModifierState.Value;
                    var binding = new HotkeyBinding(0, pending.Ctrl, pending.Shift, pending.Alt);
                    SetHotkeyBinding(session.HotkeyId, binding);
                    SuppressHotkeyUntilRelease(session.HotkeyId);
                    StopListening(false);
                    return;
                }

                // Replaces only the session this tick read; an unconditional write here could resurrect it over
                // a consumer's own stop or new capture made while this tick ran.
                var withModifiers = session with { ModifierState = modifierState, WaitingForModifierRelease = true };
                Interlocked.CompareExchange(ref listeningSession, withModifiers, session);
                return;
            }

            if (session.WaitingForModifierRelease && session.ModifierState.HasValue)
            {
                var pending = session.ModifierState.Value;
                if (pending.Ctrl || pending.Shift || pending.Alt)
                {
                    var binding = new HotkeyBinding(0, pending.Ctrl, pending.Shift, pending.Alt);
                    SetHotkeyBinding(session.HotkeyId, binding);
                    SuppressHotkeyUntilRelease(session.HotkeyId);
                }

                StopListening(false);
            }
        }

        if (session.Mode == HotkeyListenMode.Gamepad)
        {
            if (lastPressedKey == KeybindsHelper.VkEscape)
            {
                StopListening();
                return;
            }

            if (!lastPressedGamepadButton.HasValue)
                return;

            var binding = new HotkeyBinding(lastPressedGamepadButton.Value);
            SetHotkeyBinding(session.HotkeyId, binding);
            SuppressHotkeyUntilRelease(session.HotkeyId);
            StopListening(false);
        }
    }

    private void ProcessHotkeys()
    {
        // Lock-free snapshot, not a locked copy: this runs every 16ms, and taking the lock or allocating each
        // pass would contend with every consumer call for no gain.
        var entries = entriesSnapshot;

        var textInputActive = KeybindsHelper.IsTextInputActive();
        var isFocused = WindowHelper.IsGameWindowFocused();

        // Read once so every entry this tick shares one timestamp, with the clock passed into the activation
        // logic as an argument rather than read directly.
        var now = GetTimestamp();

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.Binding.IsEmpty)
                continue;

            if (entry.RequireGameFocus && !isFocused)
                continue;

            if (entry.BlockedWhileDown)
            {
                var isDown = GetIsDown(entry);
                if (isDown)
                {
                    entry.Activation.CombinationWasActive = true;
                    continue;
                }

                entry.BlockedWhileDown = false;
                ResetEntryState(entry);
            }

            if (entry.BlockWhenTextInputActive && textInputActive)
            {
                entry.BlockedWhileDown = GetIsDown(entry);
                ResetEntryState(entry);
                continue;
            }

            if (entry.Binding.IsGamepadBinding && NoireService.GamepadState != null)
            {
                if (IsGamepadTriggered(entry, now))
                    QueueTrigger(entry);

                continue;
            }

            if (entry.Binding.IsKeyboardBinding)
            {
                var triggered = IsKeyboardTriggered(entry, now);
                if (triggered)
                    QueueTrigger(entry);
            }
        }
    }

    private bool IsKeyboardTriggered(HotkeyEntry entry, long nowMs)
    {
        var binding = entry.Binding;
        var modifierState = KeybindsHelper.GetRawModifierState(rawKeyboardState);
        var modifiersExactMatch = KeybindsHelper.AreExactModifiersDown(modifierState, binding);

        bool mainKeyPhysicallyDown;
        bool combinationActive;

        if (binding.IsModifierOnly)
        {
            mainKeyPhysicallyDown = KeybindsHelper.AreRequiredModifiersDown(modifierState, binding);
            combinationActive = modifiersExactMatch;
        }
        else
        {
            mainKeyPhysicallyDown = KeybindsHelper.IsRawKeyDown(rawKeyboardState, binding.VkCode);
            combinationActive = mainKeyPhysicallyDown && modifiersExactMatch;

            if (combinationActive && (binding.Ctrl || binding.Shift || binding.Alt))
            {
                foreach (var code in currentKeysDown)
                {
                    if (!KeybindsHelper.IsModifierKey(code) && code != binding.VkCode)
                    {
                        combinationActive = false;
                        break;
                    }
                }
            }
        }

        return EvaluateActivation(entry, combinationActive, mainKeyPhysicallyDown, nowMs);
    }

    private bool IsGamepadTriggered(HotkeyEntry entry, long nowMs)
    {
        if (entry.Binding.GamepadButton == null)
            return false;

        var button = entry.Binding.GamepadButton.Value;
        var gamepadState = NoireService.GamepadState;
        if (gamepadState == null)
            return false;

        var isDown = gamepadState.Raw(button) > 0f;
        return EvaluateActivation(entry, isDown, isDown, nowMs);
    }

    /// <summary>
    /// Advances one hotkey's activation state by a single detection tick and reports whether it triggers.<br/>
    /// The clock is passed in rather than read, so the decision is a pure function of the inputs and the
    /// entry's current state.
    /// </summary>
    /// <param name="entry">The hotkey entry whose state is advanced.</param>
    /// <param name="combinationActive">Whether the full binding (key plus exact modifiers, no extra keys) is active this tick.</param>
    /// <param name="mainKeyPhysicallyDown">Whether the binding's main key (or a required modifier, for a modifier-only binding) is physically down this tick.</param>
    /// <param name="nowMs">The timestamp of this tick, in milliseconds.</param>
    /// <returns>True if the hotkey should trigger this tick; otherwise, false.</returns>
    internal bool EvaluateActivation(HotkeyEntry entry, bool combinationActive, bool mainKeyPhysicallyDown, long nowMs)
    {
        ref var state = ref entry.Activation;
        var wasHeld = state.IsHeld;

        if (!mainKeyPhysicallyDown)
        {
            // Key up: a Released hotkey fires here if a press had armed it, and the whole machine resets for the
            // next hold.
            var shouldTriggerRelease = entry.ActivationMode == HotkeyActivationMode.Released && state.Armed;
            state.Reset();
            return shouldTriggerRelease;
        }

        // Key down: leave Idle for Engaged, but keep a HoldFired phase if a Held hotkey already fired this hold.
        if (state.Phase == HotkeyActivationPhase.Idle)
            state.Phase = HotkeyActivationPhase.Engaged;

        if (!combinationActive)
        {
            // The main key is down but the full combination is not satisfied (wrong modifiers, or an extra key).
            state.CombinationWasActive = false;

            if (state.Phase != HotkeyActivationPhase.HoldFired)
                state.HoldStartMs = null;

            return false;
        }

        if (!wasHeld)
        {
            // The physical key just went down with the combination active: arm a release, start the hold clock.
            state.Armed = true;
            state.CombinationWasActive = true;
            state.HoldStartMs = nowMs;
            state.NextRepeatMs = null;

            if (entry.ActivationMode == HotkeyActivationMode.Pressed)
                return true;
        }
        else if (!state.CombinationWasActive)
        {
            // The key was already down and the combination just completed this tick, for instance a modifier
            // arriving after the main key.
            state.CombinationWasActive = true;

            if (state.Phase != HotkeyActivationPhase.HoldFired)
                state.HoldStartMs = nowMs;
        }

        return entry.ActivationMode switch
        {
            HotkeyActivationMode.Held => ShouldTriggerHeld(entry, nowMs),
            HotkeyActivationMode.Repeat => ShouldTriggerRepeat(entry, nowMs),
            HotkeyActivationMode.HoldAndRepeat => ShouldTriggerHoldAndRepeat(entry, nowMs),
            _ => false,
        };
    }

    private bool ShouldTriggerHeld(HotkeyEntry entry, long nowMs)
    {
        ref var state = ref entry.Activation;

        if (state.Phase == HotkeyActivationPhase.HoldFired)
            return false;

        state.HoldStartMs ??= nowMs;

        if (nowMs - state.HoldStartMs.Value >= entry.HoldDelay.TotalMilliseconds)
        {
            state.Phase = HotkeyActivationPhase.HoldFired;
            return true;
        }

        return false;
    }

    private bool ShouldTriggerRepeat(HotkeyEntry entry, long nowMs)
    {
        ref var state = ref entry.Activation;

        if (state.NextRepeatMs == null || nowMs >= state.NextRepeatMs.Value)
        {
            var delay = GetRepeatDelayMilliseconds(entry);
            state.NextRepeatMs = nowMs + (long)delay;
            return true;
        }

        return false;
    }

    private bool ShouldTriggerHoldAndRepeat(HotkeyEntry entry, long nowMs)
    {
        ref var state = ref entry.Activation;

        state.HoldStartMs ??= nowMs;

        // Initial hold gate: nothing fires until the hold delay has elapsed, exactly like Held.
        if (nowMs - state.HoldStartMs.Value < entry.HoldDelay.TotalMilliseconds)
            return false;

        // Past the gate, fire on the same cadence as Repeat: immediately on the first tick through, then each delay.
        if (state.NextRepeatMs == null || nowMs >= state.NextRepeatMs.Value)
        {
            var delay = GetRepeatDelayMilliseconds(entry);
            state.NextRepeatMs = nowMs + (long)delay;
            return true;
        }

        return false;
    }

    private long GetTimestamp()
    {
        return Environment.TickCount64;
    }

    private double GetRepeatDelayMilliseconds(HotkeyEntry entry)
    {
        if (!entry.UseRandomRepeatDelay)
            return Math.Max(0, entry.FixedRepeatDelay.TotalMilliseconds);

        var min = Math.Max(0, entry.RepeatDelayMin.TotalMilliseconds);
        var max = Math.Max(min, entry.RepeatDelayMax.TotalMilliseconds);

        if (max <= min)
            return min;

        return RandomGenerator.GenerateRandomDouble(min, max);
    }

    private void ResetEntryState(HotkeyEntry entry)
    {
        entry.Activation.Reset();
    }

    /// <summary>
    /// Rebuilds and publishes <see cref="entriesSnapshot"/> from the current registered entries; must be called
    /// while holding <see cref="hotkeyLock"/>.<br/>
    /// Called on every structural change (register, unregister, teardown); option changes need no rebuild,
    /// since the snapshot holds live entry references.
    /// </summary>
    private void RebuildEntriesSnapshot()
    {
        entriesSnapshot = hotkeys.Values.ToArray();
    }

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    /// <summary>
    /// Runs a consumer visible notification on the framework thread.
    /// </summary>
    /// <remarks>
    /// Callers must have released <see cref="hotkeyLock"/> first, since the notification runs arbitrary
    /// consumer code of unknown duration that may call back into this manager. The framework thread is the only
    /// one safe to touch game state from, and detection can change bindings from its own timer thread when it
    /// captures a rebind, so marshalling here keeps a handler's thread independent of whichever caller reached
    /// it; a caller already on the framework thread runs the notification inline. Without an initialized
    /// NoireLib there is no framework thread to marshal onto, so the notification runs inline on the calling
    /// thread.
    /// </remarks>
    /// <param name="notification">The notification to run.</param>
    private void PostToFrameworkThread(Action notification)
    {
        if (disposed)
            return;

        if (!NoireService.IsInitialized())
        {
            RunNotification(notification);
            return;
        }

        _ = AsyncHelper.RunOnFrameworkThreadAsync(() =>
        {
            // The module can be torn down between the post and the frame that runs it; a notification
            // delivered then would reach a plugin that is unloading.
            if (disposed)
                return;

            RunNotification(notification);
        });
    }

    /// <summary>
    /// Invokes a notification, containing any exception a consumer handler throws so it neither stops the
    /// notifications behind it nor surfaces in the framework update or detection tick that caused it.
    /// </summary>
    /// <param name="notification">The notification to run.</param>
    private void RunNotification(Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Error while notifying hotkey listeners.");
        }
    }

    private bool HasModifierReleased((bool Ctrl, bool Shift, bool Alt) previous, (bool Ctrl, bool Shift, bool Alt) current)
    {
        return (previous.Ctrl && !current.Ctrl)
            || (previous.Shift && !current.Shift)
            || (previous.Alt && !current.Alt);
    }

    private void SuppressHotkeyUntilRelease(string id)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return;

            entry.BlockedWhileDown = true;
            ResetEntryState(entry);
        }
    }

    private void ApplyPersistedHotkey(HotkeyEntry entry)
    {
        if (!shouldSaveKeybinds)
            return;

        if (HotkeyManagerConfig.Hotkeys.TryGetValue(entry.Id, out var persisted))
            persisted.ApplyTo(entry);
    }

    private void SaveHotkey(HotkeyEntry entry)
    {
        if (!shouldSaveKeybinds)
            return;

        HotkeyManagerConfig.Hotkeys[entry.Id] = PersistedHotkey.FromEntry(entry);
        HotkeyManagerConfig.Save();
    }

    /// <summary>
    /// Persists a hotkey whose option a consumer changed at runtime through the live entry; called by
    /// <see cref="HotkeyEntry"/>'s notifying setters.<br/>
    /// Behavior takes effect regardless, since the detection loop reads the entry's options directly every
    /// tick, so this only carries the change to disk.
    /// </summary>
    /// <param name="entry">The entry whose option changed.</param>
    internal void OnEntryOptionChanged(HotkeyEntry entry)
    {
        if (disposed || !shouldSaveKeybinds)
            return;

        // Without an initialized NoireLib there is no debouncer to schedule onto and no framework loop to
        // coalesce a burst against, so the persist runs inline.
        if (!NoireService.IsInitialized())
        {
            SaveHotkey(entry);
            return;
        }

        // Coalesces a burst of consumer sets into one write; SaveHotkey re-checks shouldSaveKeybinds, so
        // persistence turned off in the meantime is honored.
        _ = DebounceHelper.DebounceAsync(optionPersistKeyPrefix + entry.Id, OptionPersistDebounce, () =>
        {
            // Ownership is re-checked at fire time, not schedule time: an entry unregistered or moved to
            // another manager during the debounce window must not have its persist resurrect a hotkey that is
            // no longer this manager's. Owner is cleared on unregister and on teardown, so this also drops a
            // write scheduled just before dispose; disposed guards the brief window before teardown clears the
            // owners.
            if (disposed || entry.Owner != this)
                return;

            SaveHotkey(entry);
        });
    }

    private void RemoveStoredHotkey(string id)
    {
        if (!shouldSaveKeybinds)
            return;

        if (HotkeyManagerConfig.Hotkeys.Remove(id))
            HotkeyManagerConfig.Save();
    }

    /// <summary>
    /// Writes the binding of every hotkey this instance holds to the stored keybinds.
    /// </summary>
    /// <remarks>
    /// The stored keybinds are keyed by hotkey id alone and shared by every hotkey manager instance in the
    /// plugin, so this updates only the entries it owns and leaves every other id untouched; replacing the
    /// whole dictionary would erase a sibling instance's bindings, or any hotkey registered after this runs.<br/>
    /// Nothing is removed here: a stored id with no registered hotkey cannot be told apart from one belonging
    /// to another instance, an unregistered hotkey, or a retired feature, so there is no way to know it is
    /// stale. Removal is driven by <see cref="UnregisterHotkey"/> instead, which knows the single id it retires.
    /// </remarks>
    private void SaveAllHotkeys()
    {
        if (!shouldSaveKeybinds)
            return;

        lock (hotkeyLock)
        {
            foreach (var entry in hotkeys.Values)
                HotkeyManagerConfig.Hotkeys[entry.Id] = PersistedHotkey.FromEntry(entry);
        }

        HotkeyManagerConfig.Save();
    }

    private bool GetIsDown(HotkeyEntry entry)
    {
        if (entry.Binding.IsGamepadBinding && entry.Binding.GamepadButton.HasValue && NoireService.GamepadState != null)
            return NoireService.GamepadState.Raw(entry.Binding.GamepadButton.Value) > 0f;

        if (!entry.Binding.IsKeyboardBinding)
            return false;

        var modifierState = KeybindsHelper.GetRawModifierState(rawKeyboardState);
        if (entry.Binding.IsModifierOnly)
            return KeybindsHelper.AreRequiredModifiersDown(modifierState, entry.Binding);

        var modifiersDown = KeybindsHelper.AreExactModifiersDown(modifierState, entry.Binding);
        if (!modifiersDown)
            return false;

        return KeybindsHelper.IsRawKeyDown(rawKeyboardState, entry.Binding.VkCode);
    }

    private void BlockListeningInputOnFramework()
    {
        if (!NoireService.IsInitialized())
            return;

        var blockCode = postListeningBlockKeyCode;
        if (blockCode != 0)
        {
            if (KeybindsHelper.IsAsyncKeyDown(blockCode))
                NoireService.KeyState[blockCode] = false;
            else
                postListeningBlockKeyCode = 0;
        }

        // Whether to swallow keys, and which keys, are both answers about one session and read from one
        // reference; reading them separately could block keyboard input for a capture that already moved on to
        // the gamepad.
        var session = CurrentListeningSession;
        if (session == null)
            return;

        if (session.Mode == HotkeyListenMode.Keyboard)
        {
            if (validKeyCodes.Count == 0)
                RefreshValidKeys();

            foreach (var code in validKeyCodes)
            {
                if (!KeybindsHelper.IsModifierKey(code) && KeybindsHelper.IsAsyncKeyDown(code))
                {
                    NoireService.KeyState[code] = false;
                }
            }
        }
    }

    private void BlockHotkeyInputsOnFramework()
    {
        // Reads the same lock-free snapshot as the detection tick, since this runs every framework frame on a
        // second independent clock and must not take the lock or allocate either.
        var entries = entriesSnapshot;

        var isFocused = WindowHelper.IsGameWindowFocused();
        var modifierState = KeybindsHelper.GetModifierState();

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.Binding.IsEmpty || !entry.Binding.IsKeyboardBinding)
                continue;

            // The standing setting and a live suppression both take the key, kept apart so a transient one is
            // never written to disk as the hotkey's own answer.
            if (!entry.BlockGameInput && !entry.IsGameInputSuppressed)
                continue;

            if (entry.RequireGameFocus && !isFocused)
                continue;

            if (entry.Binding.IsModifierOnly)
            {
                if (KeybindsHelper.AreExactModifiersDown(modifierState, entry.Binding))
                    BlockModifierKeys(entry.Binding);
            }
            else if (IsFrameworkKeyDown(entry.Binding, modifierState))
            {
                NoireService.KeyState[entry.Binding.VkCode] = false;
            }
        }
    }

    private void BlockModifierKeys(HotkeyBinding binding)
    {
        if (binding.Ctrl)
        {
            NoireService.KeyState[KeybindsHelper.VkControl] = false;
            NoireService.KeyState[KeybindsHelper.VkLeftControl] = false;
            NoireService.KeyState[KeybindsHelper.VkRightControl] = false;
        }

        if (binding.Shift)
        {
            NoireService.KeyState[KeybindsHelper.VkShift] = false;
            NoireService.KeyState[KeybindsHelper.VkLeftShift] = false;
            NoireService.KeyState[KeybindsHelper.VkRightShift] = false;
        }

        if (binding.Alt)
        {
            NoireService.KeyState[KeybindsHelper.VkAlt] = false;
            NoireService.KeyState[KeybindsHelper.VkLeftAlt] = false;
            NoireService.KeyState[KeybindsHelper.VkRightAlt] = false;
        }
    }

    private bool IsFrameworkKeyDown(HotkeyBinding binding, (bool Ctrl, bool Shift, bool Alt) modifierState)
    {
        if (binding.VkCode == 0)
            return false;

        if (!KeybindsHelper.AreExactModifiersDown(modifierState, binding))
            return false;

        return NoireService.KeyState[binding.VkCode];
    }

    private string GetListeningDisplayText(HotkeyListenMode mode, string buttonLabel, bool showOnlyBinding)
    {
        if (mode == HotkeyListenMode.Gamepad && NoireService.GamepadState != null)
        {
            var listeningText = KeybindsHelper.FormatListeningGamepadInput(NoireService.GamepadState);
            if (string.IsNullOrWhiteSpace(listeningText))
                listeningText = "Press a gamepad button...";

            return showOnlyBinding ? listeningText : $"{buttonLabel}: {listeningText}";
        }

        // Taken from what the detection tick published, not formatted here, since the buffers are cleared and
        // refilled by that tick every 16ms while this runs on the framework thread.
        var keyboardText = listeningKeyboardText;
        if (string.IsNullOrWhiteSpace(keyboardText))
            keyboardText = "Press a key...";

        return showOnlyBinding ? keyboardText : $"{buttonLabel}: {keyboardText}";
    }

    /// <summary>
    /// Hands a detected trigger to the framework thread; called from the detection timer thread.
    /// </summary>
    /// <remarks>
    /// Every detected trigger is queued separately and delivered in order, never coalesced, so a hotkey that
    /// fires more than once between two frames (a Repeat hotkey at its 80ms default does, below roughly 12 FPS)
    /// produces one callback per trigger rather than one. Detection deliberately runs on a 16ms system timer
    /// rather than the framework update so a press is not lost to a low frame rate. The queue is bounded by
    /// <see cref="MaxPendingTriggers"/>: a framework thread that stops pumping drops the oldest triggers rather
    /// than letting the queue grow without limit.
    /// </remarks>
    /// <param name="entry">The hotkey entry whose trigger should be delivered.</param>
    internal void QueueTrigger(HotkeyEntry entry)
    {
        if (disposed)
            return;

        if (Interlocked.Increment(ref pendingTriggerCount) > MaxPendingTriggers)
        {
            if (pendingTriggers.TryDequeue(out _))
                Interlocked.Decrement(ref pendingTriggerCount);

            NoireLogger.LogWarning(this, $"More than {MaxPendingTriggers} hotkey triggers are waiting for the framework thread; the oldest trigger was dropped.");
        }

        pendingTriggers.Enqueue(entry);
    }

    /// <summary>
    /// Delivers every trigger that detection queued before this call, except those whose hotkey has since been
    /// unregistered; framework thread only.
    /// </summary>
    internal void DrainPendingTriggers()
    {
        // Only drains what was already queued; detection keeps running on its own thread while this loop
        // executes, so an unbounded drain could be fed indefinitely and hold the frame open.
        var toDrain = Volatile.Read(ref pendingTriggerCount);

        while (toDrain-- > 0 && pendingTriggers.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref pendingTriggerCount);

            // Registration is re-checked here, not at detection time, since a hotkey removed in between must
            // not reach a consumer that already retired its callback; skipping it in place leaves the
            // surrounding triggers in detection order.
            if (entry.Unregistered)
                continue;

            TriggerHotkey(entry);
        }
    }

    private void ClearPendingTriggers()
    {
        pendingTriggers.Clear();
        Interlocked.Exchange(ref pendingTriggerCount, 0);
    }

    /// <summary>
    /// Invokes the consumer visible surfaces of a triggered hotkey; framework thread only, since the callback,
    /// event handlers and EventBus subscribers may read or write game state.
    /// </summary>
    /// <param name="entry">The hotkey entry that was triggered.</param>
    private void TriggerHotkey(HotkeyEntry entry)
    {
        try
        {
            entry.Callback?.Invoke();
            OnHotkeyTriggered?.Invoke(entry);
            PublishEvent(new HotkeyTriggeredEvent(entry));
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Error while executing hotkey '{entry.Id}'.");
        }
    }

    /// <summary>
    /// Discards everything detection was holding and ends any capture in progress; runs on the detection timer
    /// thread, or during deactivation or teardown on a thread that has already waited for detection to stop, so
    /// nothing else can be writing the key buffers while this clears them.
    /// </summary>
    private void ResetInputState()
    {
        previousKeysDown.Clear();
        currentKeysDown.Clear();
        lastPressedKey = null;
        lastPressedGamepadButton = null;
        listeningKeyboardText = string.Empty;
        postListeningBlockKeyCode = 0;

        // Ends the session as a whole, carrying the modifier state and pending release away with it.
        StopListening();
    }

    /// <summary>
    /// Called when the module is disposed; once this returns, neither the detection timer nor the framework
    /// update can invoke a hotkey callback again.
    /// </summary>
    protected override void DisposeInternal()
    {
        // Latched first so that a tick which is already running stops queueing work on its way out, and so
        // that a framework update racing this teardown delivers nothing.
        disposed = true;

        // A module disposed while still active would otherwise leave this handler attached to the framework,
        // holding the instance alive and draining into a plugin that is unloading.
        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnFrameworkUpdate;

        StopUpdateTimer();
        ResetInputState();

        lock (hotkeyLock)
        {
            // A framework update that entered the drain before the latch was set can still be delivering while
            // this runs; marking the entries stops it there too, the same way an unregister does. The owner is
            // cleared alongside, so an option set on a retired entry no longer persists through a torn-down
            // manager.
            foreach (var entry in hotkeys.Values)
            {
                entry.Unregistered = true;
                entry.Owner = null;
            }

            hotkeys.Clear();
            RebuildEntriesSnapshot();
        }
    }

    #endregion
}
