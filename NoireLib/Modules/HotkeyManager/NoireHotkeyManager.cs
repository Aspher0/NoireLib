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
/// Manages editable hotkeys and triggers callbacks when they activate. Ids ignore case. Callbacks and events run on
/// the framework thread, and never after disposal.
/// </summary>
public class NoireHotkeyManager : NoireModuleBase<NoireHotkeyManager, HotkeyManagerConfigInstance>
{
    private static readonly StringComparer HotkeyIdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, HotkeyEntry> hotkeys = new(HotkeyIdComparer);
    private readonly object hotkeyLock = new();

    // Read by the detection tick and the input blocker without the lock. Holds live entries: only structural changes rebuild it.
    private volatile HotkeyEntry[] entriesSnapshot = Array.Empty<HotkeyEntry>();
    private readonly HashSet<int> previousKeysDown = new();
    private readonly HashSet<int> currentKeysDown = new();
    private readonly byte[] rawKeyboardState = new byte[256];
    private const int UpdateIntervalMilliseconds = 16;

    // Beyond this the oldest triggers are dropped: a frozen frame loop cannot grow the queue.
    internal const int MaxPendingTriggers = 256;

    private readonly ConcurrentQueue<HotkeyEntry> pendingTriggers = new();
    private readonly object timerLock = new();
    private int pendingTriggerCount;
    private Timer? updateTimer;
    private long lastUpdateTick;
    private int updateInProgress;
    private Action? detectionTick;
    private volatile bool disposed;

    // A burst of option sets on one hotkey coalesces into one write.
    private static readonly TimeSpan OptionPersistDebounce = TimeSpan.FromMilliseconds(500);

    // Unique per instance: pending writes of different managers never collide.
    private readonly string optionPersistKeyPrefix = $"NoireLib_HotkeyManager_Persist_{Guid.NewGuid():N}_";

    private IReadOnlyList<int> validKeyCodes = Array.Empty<int>();
    private ListeningSession? listeningSession;
    private string? lastBindingChangedId;

    // Formatted by the detection tick, which owns the key buffers, and read once on the framework thread.
    private volatile string listeningKeyboardText = string.Empty;

    private int? lastPressedKey;
    private GamepadButtons? lastPressedGamepadButton;
    private volatile int postListeningBlockKeyCode;

    internal sealed record ListeningSession(
        string HotkeyId,
        HotkeyListenMode Mode,
        (bool Ctrl, bool Shift, bool Alt)? ModifierState,
        bool WaitingForModifierRelease);

    // Read once: a session replaced mid-read never shows as a mix of two.
    internal ListeningSession? CurrentListeningSession => Volatile.Read(ref listeningSession);

    /// <summary>The associated EventBus instance for publishing hotkey events.</summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireHotkeyManager() : base() { }

    /// <summary>Creates a new instance of the <see cref="NoireHotkeyManager"/> module.</summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="shouldSaveKeybinds">Whether the hotkey manager should save keybinds to configuration.</param>
    /// <param name="eventBus">The optional EventBus instance for publishing hotkey events.</param>
    public NoireHotkeyManager(string? moduleId = null, bool active = true, bool enableLogging = true, bool shouldSaveKeybinds = true, NoireEventBus? eventBus = null)
        : base(moduleId, active, enableLogging, shouldSaveKeybinds, eventBus) { }

    internal NoireHotkeyManager(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>Initializes the module with optional initialization parameters.</summary>
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
    /// Starts detection. Activating before NoireLib is initialized wires nothing: activate again once it is.
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

    /// <summary>Stops detection and discards undelivered triggers.</summary>
    protected override void OnDeactivated()
    {
        // An activation made before NoireLib was initialized attached nothing.
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

    // Volatile: the detection timer may save a captured rebind on its own thread.
    private volatile bool shouldSaveKeybinds = true;
    /// <summary>Gets or sets whether the hotkey manager should persist keybinds to configuration.</summary>
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

    /// <summary>Sets the value of <see cref="ShouldSaveKeybinds"/>.</summary>
    /// <param name="shouldSaveKeybinds">Whether the hotkey manager should save keybinds to configuration.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireHotkeyManager SetShouldSaveKeybinds(bool shouldSaveKeybinds)
    {
        ShouldSaveKeybinds = shouldSaveKeybinds;
        return this;
    }

    /// <summary>Raised on the framework thread when a hotkey triggers.</summary>
    public event Action<HotkeyEntry>? OnHotkeyTriggered;

    /// <summary>
    /// Raised on the framework thread when a binding changes, with the entry already carrying it. A handler may call back
    /// into this manager, even to unregister that hotkey.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyChanged;


    /// <summary>Whether a binding capture runs. Read <see cref="ListeningHotkeyId"/> alone when the id is wanted too.</summary>
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

            // The binding UI labels its button with the display name.
            if (string.IsNullOrWhiteSpace(hotkeyDefinition.DisplayName))
                hotkeyDefinition.DisplayName = hotkeyDefinition.Id;

            // Cleared for an entry registered again after a removal.
            hotkeyDefinition.Unregistered = false;

            // Set last: option setters route changes back here only once the entry is registered.
            hotkeyDefinition.Owner = this;

            hotkeys.Add(hotkeyDefinition.Id, hotkeyDefinition);
            RebuildEntriesSnapshot();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Registered hotkey '{hotkeyDefinition.Id}' with binding {KeybindsHelper.FormatBinding(hotkeyDefinition.Binding)}.");

        SaveHotkey(hotkeyDefinition);

        return true;
    }

    /// <summary>Updates the callback for an existing hotkey.</summary>
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

    /// <summary>Sets a hotkey's binding. It is written before the change notifications are raised.</summary>
    /// <param name="id">The hotkey to update.</param>
    /// <param name="binding">The new binding.</param>
    /// <returns>Whether the hotkey was found.</returns>
    public bool SetHotkeyBinding(string id, HotkeyBinding binding)
    {
        HotkeyEntry changedEntry;
        bool isNewBinding;

        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return false;

            isNewBinding = entry.Binding != binding;

            // Through the field: the property's setter routes back into this method.
            entry.SetBindingStorage(binding);

            // Read by the binding UI without the lock, written from whichever thread captured the rebind.
            Volatile.Write(ref lastBindingChangedId, id);
            changedEntry = entry;
        }

        // Outside the lock: saving writes to disk, and a handler may call straight back into this manager.
        SaveHotkey(changedEntry);

        // The live entry, not a copy: edits and later rebinds reach the registered entry.
        PostToFrameworkThread(() =>
        {
            OnHotkeyChanged?.Invoke(changedEntry);
            PublishEvent(new HotkeyBindingChangedEvent(changedEntry, isNewBinding));
        });

        return true;
    }

    /// <summary>Clears the binding for a hotkey.</summary>
    /// <param name="id">The identifier of the hotkey to clear.</param>
    /// <returns>True if the hotkey was found and cleared; otherwise, false.</returns>
    public bool ClearHotkeyBinding(string id)
    {
        return SetHotkeyBinding(id, new HotkeyBinding(0));
    }

    /// <summary>Enables or disables a hotkey.</summary>
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

            // A trigger may already be queued. The entry carries its removal to the drain.
            entry.Unregistered = true;

            // A later option set on the retired entry no longer persists through this manager.
            entry.Owner = null;
            RebuildEntriesSnapshot();
        }

        RemoveStoredHotkey(id);
        return true;
    }

    /// <summary>Tries to get a registered hotkey.</summary>
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

    /// <summary>Gets all registered hotkeys.</summary>
    /// <returns>A read-only collection of all registered hotkeys.</returns>
    public IReadOnlyCollection<HotkeyEntry> GetHotkeys()
    {
        return entriesSnapshot.ToList();
    }

    /// <summary>Starts listening for a new binding of a hotkey.</summary>
    /// <param name="id">The hotkey to rebind.</param>
    /// <param name="mode">The input mode.</param>
    /// <returns>Whether listening started.</returns>
    public bool StartListening(string id, HotkeyListenMode mode = HotkeyListenMode.Keyboard)
    {
        if (!TryGetHotkey(id, out _))
            return false;

        // Cleared first: the UI never shows the previous session's keys against this one.
        listeningKeyboardText = string.Empty;

        // One write: a reader sees this id with the mode and state it started with.
        Volatile.Write(ref listeningSession, new ListeningSession(id, mode, null, false));

        PostToFrameworkThread(() => PublishEvent(new HotkeyListeningStartedEvent(id, mode)));
        return true;
    }

    /// <summary>Stops listening for a new binding.</summary>
    /// <param name="wasCancelled">Whether the capture was cancelled rather than completed.</param>
    public void StopListening(bool wasCancelled = true)
    {
        // Exchanged: exactly one caller ends and announces a session, even when detection captures at the same moment.
        var stopped = Interlocked.Exchange(ref listeningSession, null);
        if (stopped == null)
            return;

        PostToFrameworkThread(() => PublishEvent(new HotkeyListeningStoppedEvent(stopped.HotkeyId, wasCancelled)));
    }

    /// <summary>Draws a fully managed ImGui button for rebinding a hotkey.</summary>
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

    internal bool IsListeningFor(string id)
    {
        var session = CurrentListeningSession;
        return session != null && HotkeyIdComparer.Equals(session.HotkeyId, id);
    }

    // Only the first caller to ask sees the change.
    internal bool TryConsumeBindingChanged(string id)
    {
        var pendingId = Volatile.Read(ref lastBindingChangedId);
        if (!HotkeyIdComparer.Equals(pendingId, id))
            return false;

        // Compared by reference: CompareExchange never matches a merely equal string.
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
        // A system timer: framework update follows the frame rate and would skip hotkeys at low FPS.
        lock (timerLock)
        {
            if (updateTimer != null || disposed)
                return;

            lastUpdateTick = Environment.TickCount64;
            updateTimer = new Timer(_ => OnSystemUpdate(), null, 0, UpdateIntervalMilliseconds);
        }
    }

    // Blocks until a running tick finishes: nothing on the timer thread touches state afterwards.
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
            // Timer.Dispose can return mid-tick. It returns false for a timer already disposed, whose handle never signals.
            using var timerDrained = new ManualResetEvent(false);

            if (timer.Dispose(timerDrained))
                timerDrained.WaitOne();
        }

        // A trigger detected before the stop must not reach a consumer after it.
        ClearPendingTriggers();
    }

    private void OnSystemUpdate() => RunGuardedTick(detectionTick ??= RunDetectionTick);

    // A persistent fault is logged once per run of failing ticks.
    internal bool TickFaultReported { get; private set; }

    // An exception escaping a timer callback terminates the process.
    internal void RunGuardedTick(Action tick)
    {
        if (Interlocked.Exchange(ref updateInProgress, 1) == 1)
            return;

        try
        {
            tick();
            TickFaultReported = false;
        }
        catch (Exception ex)
        {
            if (!TickFaultReported)
            {
                TickFaultReported = true;
                NoireLogger.LogError(this, ex, "Hotkey detection tick failed.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref updateInProgress, 0);
        }
    }

    private void RunDetectionTick()
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

        // Read once: a consumer may replace the session mid-tick.
        var session = CurrentListeningSession;
        if (session != null)
        {
            // Formatted here, while this tick owns the key buffers.
            listeningKeyboardText = KeybindsHelper.FormatListeningKeyboardInput(rawKeyboardState, currentKeysDown);

            ProcessListening(session);
            return;
        }

        ProcessHotkeys();
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

                // Replaces only the session this tick read, never a consumer's newer stop or capture.
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
        // Lock-free: this runs every 16ms.
        var entries = entriesSnapshot;

        var textInputActive = KeybindsHelper.IsTextInputActive();
        var isFocused = WindowHelper.IsGameWindowFocused();

        // One timestamp for every entry this tick.
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

    // The clock is passed in: the decision depends only on the inputs and the entry's state.
    internal bool EvaluateActivation(HotkeyEntry entry, bool combinationActive, bool mainKeyPhysicallyDown, long nowMs)
    {
        ref var state = ref entry.Activation;
        var wasHeld = state.IsHeld;

        if (!mainKeyPhysicallyDown)
        {
            // Key up: a Released hotkey fires if a press armed it, then the machine resets.
            var shouldTriggerRelease = entry.ActivationMode == HotkeyActivationMode.Released && state.Armed;
            state.Reset();
            return shouldTriggerRelease;
        }

        // A Held hotkey that already fired this hold stays HoldFired.
        if (state.Phase == HotkeyActivationPhase.Idle)
            state.Phase = HotkeyActivationPhase.Engaged;

        if (!combinationActive)
        {
            // Wrong modifiers, or an extra key.
            state.CombinationWasActive = false;

            if (state.Phase != HotkeyActivationPhase.HoldFired)
                state.HoldStartMs = null;

            return false;
        }

        if (!wasHeld)
        {
            state.Armed = true;
            state.CombinationWasActive = true;
            state.HoldStartMs = nowMs;
            state.NextRepeatMs = null;

            if (entry.ActivationMode == HotkeyActivationMode.Pressed)
                return true;
        }
        else if (!state.CombinationWasActive)
        {
            // For instance a modifier arriving after the main key.
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

        if (nowMs - state.HoldStartMs.Value < entry.HoldDelay.TotalMilliseconds)
            return false;

        // Fires on the first tick past the gate, then on every delay.
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

    // Caller holds hotkeyLock. Option changes need no rebuild: the snapshot holds live entries.
    private void RebuildEntriesSnapshot()
    {
        entriesSnapshot = hotkeys.Values.ToArray();
    }

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    // Caller must have released hotkeyLock: the notification may call back into this manager.
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
            // Torn down between the post and the frame: the plugin may be unloading.
            if (disposed)
                return;

            RunNotification(notification);
        });
    }

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

    // Only carries the change to disk: detection reads the options every tick.
    internal void OnEntryOptionChanged(HotkeyEntry entry)
    {
        if (disposed || !shouldSaveKeybinds)
            return;

        // No debouncer without NoireLib.
        if (!NoireService.IsInitialized())
        {
            SaveHotkey(entry);
            return;
        }

        // SaveHotkey re-checks shouldSaveKeybinds when the write fires.
        _ = DebounceHelper.DebounceAsync(optionPersistKeyPrefix + entry.Id, OptionPersistDebounce, () =>
        {
            // Checked when the write fires: the entry may have been unregistered, moved or torn down meanwhile.
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

        // Whether and which keys to swallow come from one session read.
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
        // Lock-free and allocation-free: this runs every frame.
        var entries = entriesSnapshot;

        var isFocused = WindowHelper.IsGameWindowFocused();
        var modifierState = KeybindsHelper.GetModifierState();

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.Binding.IsEmpty || !entry.Binding.IsKeyboardBinding)
                continue;

            // Kept apart: a transient suppression is never saved as the hotkey's setting.
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

        // Published by the detection tick, which refills the buffers every 16ms.
        var keyboardText = listeningKeyboardText;
        if (string.IsNullOrWhiteSpace(keyboardText))
            keyboardText = "Press a key...";

        return showOnlyBinding ? keyboardText : $"{buttonLabel}: {keyboardText}";
    }

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

    // Framework thread only. Skips hotkeys unregistered since detection.
    internal void DrainPendingTriggers()
    {
        // Only what was queued: detection keeps feeding the queue and would hold the frame open.
        var toDrain = Volatile.Read(ref pendingTriggerCount);

        while (toDrain-- > 0 && pendingTriggers.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref pendingTriggerCount);

            // Checked here: a hotkey removed since detection must not reach a retired callback.
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

    // Runs only once detection has stopped: nothing else writes the key buffers.
    private void ResetInputState()
    {
        previousKeysDown.Clear();
        currentKeysDown.Clear();
        lastPressedKey = null;
        lastPressedGamepadButton = null;
        listeningKeyboardText = string.Empty;
        postListeningBlockKeyCode = 0;

        StopListening();
    }

    /// <summary>
    /// Called when the module is disposed; once this returns, neither the detection timer nor the framework
    /// update can invoke a hotkey callback again.
    /// </summary>
    protected override void DisposeInternal()
    {
        // Latched first: a running tick stops queueing and a racing framework update delivers nothing.
        disposed = true;

        // A module disposed while active would stay attached to the framework.
        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnFrameworkUpdate;

        StopUpdateTimer();
        ResetInputState();

        lock (hotkeyLock)
        {
            // Stops a drain already under way. A retired entry's option sets no longer persist.
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
