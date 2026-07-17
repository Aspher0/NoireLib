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
/// A module that manages editable hotkeys and triggers callbacks when they are activated.<br/>
/// Hotkey ids are matched ignoring case, so a hotkey registered as "my.hotkey" is the same hotkey as one that is
/// looked up, rebound, drawn or persisted as "My.Hotkey".<br/>
/// Every callback, CLR event and EventBus publication this module makes is invoked on the framework thread, so
/// handlers may touch game state directly. This covers the binding and listening events as much as the trigger
/// ones, so a handler's thread never depends on whether a binding was changed by a consumer's own call or by the
/// detection timer capturing a rebind.<br/>
/// When NoireLib is not initialized there is no framework thread to marshal onto, so deliveries run inline on the
/// calling thread instead, which is what makes the module usable without a running game. Once the module is
/// disposed, nothing is delivered again.
/// </summary>
public class NoireHotkeyManager : NoireModuleBase<NoireHotkeyManager, HotkeyManagerConfigInstance>
{
    /// <summary>
    /// The rule that decides when two hotkey ids name the same hotkey.<br/>
    /// Ids are matched ignoring case, so "my.hotkey" and "My.Hotkey" are one hotkey to every lookup, comparison
    /// and stored binding alike. The rule is held in one place because a surface that compared ids by case
    /// instead would treat one hotkey as two while the rest of the module treated them as one, leaving a
    /// consumer with a binding button that never reports the rebind it just captured.
    /// </summary>
    private static readonly StringComparer HotkeyIdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, HotkeyEntry> hotkeys = new(HotkeyIdComparer);
    private readonly object hotkeyLock = new();
    private readonly HashSet<int> previousKeysDown = new();
    private readonly HashSet<int> currentKeysDown = new();
    private readonly byte[] rawKeyboardState = new byte[256];
    private const int UpdateIntervalMilliseconds = 16;

    /// <summary>
    /// The most triggers that may wait for the framework thread at once.<br/>
    /// Reached only when the framework thread stops pumping entirely, at which point the oldest triggers
    /// are dropped so that a frozen frame loop cannot grow this queue without bound.
    /// </summary>
    internal const int MaxPendingTriggers = 256;

    private readonly ConcurrentQueue<HotkeyEntry> pendingTriggers = new();
    private readonly object timerLock = new();
    private int pendingTriggerCount;
    private Timer? updateTimer;
    private long lastUpdateTick;
    private int updateInProgress;
    private volatile bool disposed;

    private IReadOnlyList<int> validKeyCodes = Array.Empty<int>();
    private ListeningSession? listeningSession;
    private string? lastBindingChangedId;

    // The detection tick owns the key buffers and rewrites them every 16ms, so the binding UI cannot read them
    // from the framework thread it draws on. The tick formats what that UI shows and publishes it here instead,
    // as one whole string that a reader takes in a single read.
    private volatile string listeningKeyboardText = string.Empty;

    private int? lastPressedKey;
    private GamepadButtons? lastPressedGamepadButton;
    private volatile int postListeningBlockKeyCode;

    /// <summary>
    /// One rebind capture session, held as a single immutable value.
    /// </summary>
    /// <remarks>
    /// The hotkey being rebound, the input source being watched, and the modifiers captured so far are one
    /// logical unit, and the threads that write them are not the threads that read them: a session starts and
    /// stops on whichever thread the consumer calls from, advances on the detection timer thread, and is read by
    /// the framework thread that draws the binding UI and blocks game input. Keeping the whole unit behind one
    /// reference means a reader takes a single read and gets a session whose parts agree with each other,
    /// instead of assembling one out of separate fields and pairing a newly started session's hotkey id with the
    /// previous session's input mode. Replacing the reference rather than mutating a session in place is what
    /// keeps that true, so every field here is init only by construction.
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
    /// The rebind capture session in progress, or null when the module is not listening.<br/>
    /// Every part of the session a caller goes on to read comes from the one reference this returns, so a
    /// session that is replaced or ended midway through the caller's work cannot show up as a mixture of two.
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
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.
    /// Only used for internal module management.
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
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.<br/>
    /// Activating the module while NoireLib is not initialized records the active state but wires nothing:
    /// detection reads the game's key state and delivery runs on the framework thread, so neither exists yet.
    /// The module stays inert in that state and does not start detecting once NoireLib initializes, since
    /// nothing revisits the decision; activate it again afterwards to start detection.
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
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.<br/>
    /// Detection is stopped and whatever it detected but has not delivered yet is discarded, whether or not
    /// NoireLib is initialized.
    /// </summary>
    protected override void OnDeactivated()
    {
        // Detaching is all that needs the service: an activation that happened while NoireLib was not
        // initialized never attached this handler, and there is no framework to detach it from anyway.
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

    // Set by the consumer, read by every save path, and detection reaches those paths from the timer thread
    // when it captures a rebind. Volatile so that turning persistence on is seen by the tick that captures the
    // next rebind, rather than by whichever tick happens to reload the field.
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
                SaveAllKeybinds();
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
    /// Raised when a hotkey is triggered.<br/>
    /// Contains the hotkey entry that was triggered.<br/>
    /// Handlers are invoked on the framework thread, so they may touch game state directly. Falls back to the
    /// detecting thread when NoireLib is not initialized.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyTriggered;

    /// <summary>
    /// Raised when a hotkey binding changes.<br/>
    /// Contains the live hotkey entry that was changed, already carrying the new binding.<br/>
    /// Handlers are invoked on the framework thread, so they may touch game state directly, and so a rebind
    /// captured by the detection timer notifies on the same thread as one made by the plugin itself. Falls back
    /// to the calling thread when NoireLib is not initialized. A handler may call back into this manager freely,
    /// including changing or unregistering the very hotkey it was told about.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyChanged;


    /// <summary>
    /// Gets a value indicating whether the module is currently listening for a new binding.<br/>
    /// When the hotkey being rebound is also wanted, read <see cref="ListeningHotkeyId"/> alone and test it for
    /// null instead of testing this first: detection stops listening from its own thread the moment it captures
    /// a binding, so a capture landing between the two reads leaves the second one null.
    /// </summary>
    public bool IsListening => CurrentListeningSession != null;

    /// <summary>
    /// Gets the identifier of the hotkey currently being rebound, or null when nothing is being rebound.
    /// </summary>
    public string? ListeningHotkeyId => CurrentListeningSession?.HotkeyId;

    /// <summary>
    /// Registers a hotkey with the given binding and callback.<br/>
    /// A blank <see cref="HotkeyEntry.DisplayName"/> is replaced with the entry's
    /// <see cref="HotkeyEntry.Id"/>, which is the only name a hotkey is guaranteed to have.
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

        ApplyPersistedBinding(hotkeyDefinition);

        lock (hotkeyLock)
        {
            if (hotkeys.ContainsKey(hotkeyDefinition.Id))
                return false;

            // The binding UI renders the display name as its button label, and a blank one leaves the button
            // showing nothing but the binding it is prefixed to.
            if (string.IsNullOrWhiteSpace(hotkeyDefinition.DisplayName))
                hotkeyDefinition.DisplayName = hotkeyDefinition.Id;

            // Cleared so that an entry which was unregistered earlier, and is being registered again, is
            // deliverable rather than permanently silenced by the flag its removal left behind.
            hotkeyDefinition.Unregistered = false;

            hotkeys.Add(hotkeyDefinition.Id, hotkeyDefinition);
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Registered hotkey '{hotkeyDefinition.Id}' with binding {KeybindsHelper.FormatBinding(hotkeyDefinition.Binding)}.");

        SaveHotkeyBinding(hotkeyDefinition.Id, hotkeyDefinition.Binding);

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
    /// <see cref="OnHotkeyChanged"/> and <see cref="HotkeyBindingChangedEvent"/> are raised on the framework
    /// thread once the binding has been written, so they reach handlers after this has returned unless the
    /// caller is already on that thread. The binding is stored before either is raised, so a handler that reads
    /// it back, or that calls straight back into this manager, always sees the change that notified it.
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
            entry.Binding = binding;

            // Published rather than plainly written: the binding UI reads this from the framework thread
            // without taking this lock, and a rebind the detection timer captures is written from its thread.
            Volatile.Write(ref lastBindingChangedId, id);
            changedEntry = entry;
        }

        // Persisting and notifying both run with the lock released. The save is a disk write behind a virtual
        // method, and the notification hands control to consumer code that is free to call straight back into
        // this manager. Under the lock, such a handler would not block, because a Monitor is re-entrant on the
        // thread that already owns it: it would instead reach in and mutate the hotkey dictionary halfway
        // through this operation. A handler on any other thread would block for as long as the consumer ran.
        SaveHotkeyBinding(id, binding);

        // Whether the binding actually differed can only be known at the moment it is written, so it is carried
        // to the notification. The entry itself is passed live rather than copied, matching every other surface
        // that hands one out: consumers act on it (toggling Enabled, re-reading Binding), which a copy would
        // silently discard. A rebind landing before this is delivered therefore shows through, which is the
        // intended reading of "go look at this hotkey" rather than a record of one historical value.
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
    /// Removes a hotkey from the manager.<br/>
    /// A trigger that detection captured for this hotkey but that has not been delivered yet is discarded, so
    /// no callback for it runs after this returns.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to remove.</param>
    /// <returns>True if the hotkey was found and removed; otherwise, false.</returns>
    public bool UnregisterHotkey(string id)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.Remove(id, out var entry))
                return false;

            // Detection runs ahead of the framework thread that delivers, so a trigger for this hotkey can
            // already be queued. The queue holds entry references and delivery cannot consult this dictionary,
            // because that would mean holding hotkeyLock across a consumer callback, so the entry itself
            // carries the fact of its removal to the drain.
            entry.Unregistered = true;
        }

        RemoveHotkeyBinding(id);
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
        lock (hotkeyLock)
        {
            return hotkeys.Values.ToList();
        }
    }

    /// <summary>
    /// Starts listening for a new binding for the specified hotkey.<br/>
    /// <see cref="HotkeyListeningStartedEvent"/> is published on the framework thread, so it reaches subscribers
    /// after this has returned unless the caller is already on that thread.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to listen for.</param>
    /// <param name="mode">The input mode for the hotkey.</param>
    /// <returns>True if listening started successfully; otherwise, false.</returns>
    public bool StartListening(string id, HotkeyListenMode mode = HotkeyListenMode.Keyboard)
    {
        if (!TryGetHotkey(id, out _))
            return false;

        // Cleared before the session is installed, so that the binding UI cannot render the keys the previous
        // session had captured against the session that is only just starting.
        listeningKeyboardText = string.Empty;

        // One write installs the whole session. A reader that sees this id therefore also sees the mode, the
        // modifier state and the release flag that were started alongside it, never a mixture with the session
        // this one replaced.
        Volatile.Write(ref listeningSession, new ListeningSession(id, mode, null, false));

        PostToFrameworkThread(() => PublishEvent(new HotkeyListeningStartedEvent(id, mode)));
        return true;
    }

    /// <summary>
    /// Stops listening for a new binding.<br/>
    /// <see cref="HotkeyListeningStoppedEvent"/> is published on the framework thread, so it reaches subscribers
    /// after this has returned unless the caller is already on that thread. Detection stops listening from its
    /// own timer thread once it has captured a binding, which is why the publication is marshalled rather than
    /// raised wherever the stop happened to originate.
    /// </summary>
    /// <param name="wasCancelled">True if the listening was cancelled; otherwise, false.</param>
    public void StopListening(bool wasCancelled = true)
    {
        // Exchanged rather than tested and then cleared, so that exactly one caller ends any given session and
        // announces it. Detection stops listening from its own thread the instant it captures a binding, which
        // can coincide with a consumer cancelling the very same session, and both testing a session that is
        // already gone and announcing the end of one twice would be wrong.
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
    /// The label to display on the button.<br/>
    /// Set to <see langword="string.Empty"/> to hide the display name.
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
    /// The session is read once, so the answer describes a single session rather than a field the detection timer
    /// can empty partway through. The id is matched by the module's rule rather than by case, so a caller that
    /// spells the id differently from the way the session was started is still told about its own capture.
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
    /// A rebind is recorded by whichever thread writes the binding, which is the detection timer thread when the
    /// change came from a capture, and is read here on the framework thread that draws the binding UI. The record
    /// is cleared with a compare and swap rather than an unconditional write, so that a rebind of a different
    /// hotkey landing between the read and the clear survives to be reported to its own button instead of being
    /// wiped out by this one.<br/>
    /// Whether the record belongs to the caller is a question about hotkey identity and is answered by the
    /// module's id rule, so a button drawn with a different casing than the rebind was made with still receives
    /// its report. Whether the record is still the one that was read is a different question, and the compare and
    /// swap answers it on the reference itself.
    /// </remarks>
    /// <param name="id">The identifier of the hotkey to report on.</param>
    /// <returns>True if the hotkey's binding changed since the last call; otherwise, false.</returns>
    internal bool TryConsumeBindingChanged(string id)
    {
        var pendingId = Volatile.Read(ref lastBindingChangedId);
        if (!HotkeyIdComparer.Equals(pendingId, id))
            return false;

        // The comparand is the reference that was just read, never the caller's id, because
        // Interlocked.CompareExchange matches a string by reference and not by value: handing it an id that is
        // merely equal would clear nothing and let the report be handed out a second time. Matching on the
        // reference is also what makes the clear conditional, which is what leaves another hotkey's rebind
        // standing when it lands after the read.
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
        // We use a system timer instead of the framework update because framework update is bound to FPS.
        // On low FPS, hotkeys are skipped otherwise
        lock (timerLock)
        {
            if (updateTimer != null || disposed)
                return;

            lastUpdateTick = Environment.TickCount64;
            updateTimer = new Timer(_ => OnSystemUpdate(), null, 0, UpdateIntervalMilliseconds);
        }
    }

    /// <summary>
    /// Stops detection and discards whatever it detected but has not delivered yet.<br/>
    /// Blocks until a tick that is already running has finished, so that once this returns no timer thread
    /// work can still touch the module's state or reach a consumer.
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
            // Timer.Dispose() returns while a tick is still mid-execution, and updateInProgress only
            // serializes ticks against each other, never a tick against a teardown. Waiting on the notify
            // handle is what actually guarantees the running tick has finished before the caller goes on to
            // clear state a plugin is unloading from. Dispose returns false only when the timer was already
            // disposed, in which case nothing will ever signal the handle and waiting would hang.
            using var timerDrained = new ManualResetEvent(false);

            if (timer.Dispose(timerDrained))
                timerDrained.WaitOne();
        }

        // Detection is off and no tick can be queueing any more, so discarding is race free here. A trigger
        // detected before the stop must not reach a consumer once the module has stopped listening for it.
        ClearPendingTriggers();
    }

    /// <summary>
    /// We use a system timer instead of the framework update because framework update is bound to FPS.
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

            // Read once and carried through the rest of the tick, so that the capture works on a single session
            // rather than re-reading a field a consumer may replace partway through.
            var session = CurrentListeningSession;
            if (session != null)
            {
                // The binding UI draws on the framework thread and cannot read the key buffers this tick is
                // rewriting, so the text it shows is formatted here from them while they are still owned.
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
    /// Advances a rebind capture by one detection tick.<br/>
    /// Called from the detection timer thread.
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

                // The advanced session replaces the one this tick read, and only that one. A consumer can stop
                // listening or start a different capture while this tick runs, and an unconditional write here
                // would bring the session it read back to life over the consumer's decision.
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
        List<HotkeyEntry> entries;
        lock (hotkeyLock)
        {
            entries = hotkeys.Values.ToList();
        }

        var textInputActive = KeybindsHelper.IsTextInputActive();
        var isFocused = WindowHelper.IsGameWindowFocused();

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
                    entry.WasDown = true;
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
                if (IsGamepadTriggered(entry))
                    QueueTrigger(entry);

                continue;
            }

            if (entry.Binding.IsKeyboardBinding)
            {
                var triggered = IsKeyboardTriggered(entry);
                if (triggered)
                    QueueTrigger(entry);
            }
        }
    }

    private bool IsKeyboardTriggered(HotkeyEntry entry)
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

        return EvaluateActivation(entry, combinationActive, mainKeyPhysicallyDown);
    }

    private bool IsGamepadTriggered(HotkeyEntry entry)
    {
        if (entry.Binding.GamepadButton == null)
            return false;

        var button = entry.Binding.GamepadButton.Value;
        var gamepadState = NoireService.GamepadState;
        if (gamepadState == null)
            return false;

        var isDown = gamepadState.Raw(button) > 0f;
        return EvaluateActivation(entry, isDown, isDown);
    }

    private bool EvaluateActivation(HotkeyEntry entry, bool combinationActive, bool mainKeyPhysicallyDown)
    {
        var wasPhysicallyHeld = entry.PhysicallyHeld;
        entry.PhysicallyHeld = mainKeyPhysicallyDown;

        if (!mainKeyPhysicallyDown)
        {
            var wasArmed = entry.Armed;

            var shouldTriggerRelease = entry.ActivationMode == HotkeyActivationMode.Released && wasArmed;

            entry.Armed = false;
            entry.WasDown = false;
            entry.HoldStartTimestamp = null;
            entry.HoldTriggered = false;
            entry.NextRepeatTimestamp = null;

            return shouldTriggerRelease;
        }

        if (!combinationActive)
        {
            entry.WasDown = false;

            if (!entry.HoldTriggered)
                entry.HoldStartTimestamp = null;

            return false;
        }

        if (!wasPhysicallyHeld)
        {
            entry.Armed = true;
            entry.WasDown = true;
            entry.HoldStartTimestamp = GetTimestamp();
            entry.HoldTriggered = false;
            entry.NextRepeatTimestamp = null;

            if (entry.ActivationMode == HotkeyActivationMode.Pressed)
                return true;
        }
        else if (!entry.WasDown)
        {
            entry.WasDown = true;

            if (!entry.HoldTriggered)
                entry.HoldStartTimestamp = GetTimestamp();
        }

        return entry.ActivationMode switch
        {
            HotkeyActivationMode.Held => ShouldTriggerHeld(entry),
            HotkeyActivationMode.Repeat => ShouldTriggerRepeat(entry),
            _ => false,
        };
    }

    private bool ShouldTriggerHeld(HotkeyEntry entry)
    {
        if (entry.HoldTriggered)
            return false;

        var timestamp = GetTimestamp();
        entry.HoldStartTimestamp ??= timestamp;

        if (timestamp - entry.HoldStartTimestamp.Value >= entry.HoldDelay.TotalMilliseconds)
        {
            entry.HoldTriggered = true;
            return true;
        }

        return false;
    }

    private bool ShouldTriggerRepeat(HotkeyEntry entry)
    {
        var timestamp = GetTimestamp();
        if (entry.NextRepeatTimestamp == null || timestamp >= entry.NextRepeatTimestamp.Value)
        {
            var delay = GetRepeatDelayMilliseconds(entry);
            entry.NextRepeatTimestamp = timestamp + (long)delay;
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
        entry.WasDown = false;
        entry.Armed = false;
        entry.PhysicallyHeld = false;
        entry.HoldStartTimestamp = null;
        entry.HoldTriggered = false;
        entry.NextRepeatTimestamp = null;
    }

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    /// <summary>
    /// Runs a consumer visible notification on the framework thread.
    /// </summary>
    /// <remarks>
    /// Callers must have released <see cref="hotkeyLock"/> first. A notification runs consumer code of unknown
    /// duration that may call back into this manager, and the EventBus invokes its synchronous subscribers on
    /// whatever thread publishes, so anything reached from here has to be treated as arbitrary consumer code.<br/>
    /// The framework thread is where the module's hotkey callbacks already run, and it is the only thread on
    /// which game state is safe to touch. Detection changes bindings from its own timer thread when it captures
    /// a rebind, so marshalling here is what stops a handler's thread from depending on which caller happened to
    /// reach it. A caller already on the framework thread runs the notification inline, so the ordinary case of a
    /// plugin rebinding a hotkey from its own UI keeps notifying before the call returns.<br/>
    /// Without an initialized NoireLib there is no framework thread to marshal onto, so the notification runs
    /// inline on the calling thread.
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
            // The module can be torn down between the post and the frame that runs it, and a notification
            // delivered then would reach a plugin that is unloading.
            if (disposed)
                return;

            RunNotification(notification);
        });
    }

    /// <summary>
    /// Invokes a notification, containing any exception a consumer handler throws.<br/>
    /// A handler that throws must not stop the notifications behind it, and must not surface as an exception in
    /// the framework update or in the detection tick that caused the notification.
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

    private void ApplyPersistedBinding(HotkeyEntry entry)
    {
        if (!shouldSaveKeybinds)
            return;

        if (HotkeyManagerConfig.Keybinds.TryGetValue(entry.Id, out var binding))
            entry.Binding = binding;
    }

    private void SaveHotkeyBinding(string id, HotkeyBinding binding)
    {
        if (!shouldSaveKeybinds)
            return;

        HotkeyManagerConfig.Keybinds[id] = binding;
        HotkeyManagerConfig.Save();
    }

    private void RemoveHotkeyBinding(string id)
    {
        if (!shouldSaveKeybinds)
            return;

        if (HotkeyManagerConfig.Keybinds.Remove(id))
            HotkeyManagerConfig.Save();
    }

    /// <summary>
    /// Writes the binding of every hotkey this instance holds to the stored keybinds.
    /// </summary>
    /// <remarks>
    /// The stored keybinds are keyed by hotkey id alone and are shared by every hotkey manager in the plugin,
    /// since modules support several instances of the same type through their module id. This therefore updates
    /// the entries it owns and leaves every other one untouched: replacing the whole dictionary would erase the
    /// bindings of a sibling instance, and even for a single instance it would erase the bindings of hotkeys
    /// that are registered after this runs.<br/>
    /// Nothing is removed here. An id that is stored but not registered cannot be told apart from one belonging
    /// to another instance, to a hotkey not registered yet, or to a feature an earlier version of the plugin
    /// had, so this has no way to decide that any of them is stale. Removal is driven by
    /// <see cref="UnregisterHotkey"/> instead, which knows the single id it is retiring.
    /// </remarks>
    private void SaveAllKeybinds()
    {
        if (!shouldSaveKeybinds)
            return;

        lock (hotkeyLock)
        {
            foreach (var entry in hotkeys.Values)
                HotkeyManagerConfig.Keybinds[entry.Id] = entry.Binding;
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

        // Whether to swallow keys and which keys to swallow are both answers about one session, so they are read
        // from one reference. Reading them separately could block keyboard input for a capture that had already
        // moved on to the gamepad.
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
        List<HotkeyEntry> entries;
        lock (hotkeyLock)
        {
            entries = hotkeys.Values.ToList();
        }

        var isFocused = WindowHelper.IsGameWindowFocused();
        var modifierState = KeybindsHelper.GetModifierState();

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.Binding.IsEmpty || !entry.BlockGameInput || !entry.Binding.IsKeyboardBinding)
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

        // Taken from what the detection tick published rather than formatted here. The buffers it would be
        // formatted from are cleared and refilled by that tick every 16ms, and this runs on the framework
        // thread, so reading them here means enumerating the held key set while it is being rebuilt.
        var keyboardText = listeningKeyboardText;
        if (string.IsNullOrWhiteSpace(keyboardText))
            keyboardText = "Press a key...";

        return showOnlyBinding ? keyboardText : $"{buttonLabel}: {keyboardText}";
    }

    /// <summary>
    /// Hands a detected trigger to the framework thread.<br/>
    /// Called from the detection timer thread.
    /// </summary>
    /// <remarks>
    /// Every detected trigger is queued separately and none are coalesced, even when a hotkey triggers
    /// several times before the next frame drains. Detection deliberately runs on a 16ms system timer rather
    /// than on the framework update so that input is not lost to the frame rate, and a hotkey really can
    /// fire more than once between two frames: a Repeat hotkey at its 80ms default does so below roughly
    /// 12 FPS, and two distinct presses can land in one long frame. Collapsing those back into a single
    /// delivery would reimpose exactly the frame rate ceiling the system timer exists to escape, and would
    /// quietly cut a repeat rate precisely when the game is already struggling. Consumers therefore get one
    /// callback per detected trigger, in detection order.<br/>
    /// The queue is bounded by <see cref="MaxPendingTriggers"/>: a framework thread that stops pumping drops
    /// the oldest triggers rather than letting the queue grow without limit.
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
    /// unregistered.<br/>
    /// Framework thread only.
    /// </summary>
    internal void DrainPendingTriggers()
    {
        // Only drain what was already queued: detection keeps running on its own thread while this loop
        // executes, so an unbounded drain could be fed indefinitely and hold the frame open.
        var toDrain = Volatile.Read(ref pendingTriggerCount);

        while (toDrain-- > 0 && pendingTriggers.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref pendingTriggerCount);

            // Registration is re-checked here rather than at detection time, because a hotkey removed in the
            // interval between the two must not reach a consumer that has already retired its callback.
            // Skipping the entry in place leaves the triggers around it in detection order.
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
    /// Invokes the consumer visible surfaces of a triggered hotkey.<br/>
    /// Framework thread only: the callback, the event handlers and the EventBus subscribers are all consumer
    /// code that may read or write game state, which is only safe on that thread.
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
    /// Discards everything detection was holding and ends any capture in progress.<br/>
    /// Runs either on the detection timer thread or, during a deactivation or a teardown, on a thread that has
    /// already waited for detection to stop, so nothing else can be writing the key buffers while this clears
    /// them.
    /// </summary>
    private void ResetInputState()
    {
        previousKeysDown.Clear();
        currentKeysDown.Clear();
        lastPressedKey = null;
        lastPressedGamepadButton = null;
        listeningKeyboardText = string.Empty;
        postListeningBlockKeyCode = 0;

        // Ends the session as a whole, which is what carries the modifier state and the pending release away
        // with it.
        StopListening();
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.<br/>
    /// Once this returns, neither the detection timer nor the framework update can invoke a hotkey callback
    /// any more.
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
            // A framework update that entered the drain before the latch above was set can still be delivering
            // while this runs. Marking the entries stops it there too, for the same reason an unregister does.
            foreach (var entry in hotkeys.Values)
                entry.Unregistered = true;

            hotkeys.Clear();
        }
    }

    #endregion
}
