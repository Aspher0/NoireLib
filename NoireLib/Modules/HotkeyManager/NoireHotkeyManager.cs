using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace NoireLib.HotkeyManager;

/// <summary>
/// A module that manages editable hotkeys and triggers callbacks when they are activated.
/// </summary>
public class NoireHotkeyManager : NoireModuleBase<NoireHotkeyManager>
{
    private readonly Dictionary<string, HotkeyEntry> hotkeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object hotkeyLock = new();
    private readonly HashSet<int> previousKeysDown = new();
    private readonly HashSet<int> currentKeysDown = new();
    private readonly byte[] rawKeyboardState = new byte[256];
    private const int UpdateIntervalMilliseconds = 16;
    private Timer? updateTimer;
    private long lastUpdateTick;
    private int updateInProgress;

    private IReadOnlyList<int> validKeyCodes = Array.Empty<int>();
    private string? listeningHotkeyId;
    private HotkeyListenMode listeningMode = HotkeyListenMode.Keyboard;
    private string? lastBindingChangedId;
    private int? lastPressedKey;
    private GamepadButtons? lastPressedGamepadButton;
    private (bool Ctrl, bool Shift, bool Alt)? listeningModifierState;
    private bool waitingForModifierRelease;
    private volatile int postListeningBlockKeyCode;
    private HotkeyManagerConfig? hotkeyConfig;

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
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        StartUpdateTimer();
        NoireService.Framework.Update += OnFrameworkUpdate;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Hotkey Manager activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        NoireService.Framework.Update -= OnFrameworkUpdate;
        StopUpdateTimer();
        ResetInputState();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Hotkey Manager deactivated.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsActive || !NoireService.IsInitialized())
            return;

        BlockListeningInputOnFramework();
        BlockHotkeyInputsOnFramework();
        ForwardPendingInputs();
    }

    private bool shouldSaveKeybinds = true;
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
            {
                EnsureConfigLoaded();
                SaveAllKeybinds();
            }
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
    /// Contains the hotkey entry that was triggered.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyTriggered;

    /// <summary>
    /// Raised when a hotkey binding changes.<br/>
    /// Contains the hotkey entry that was changed.
    /// </summary>
    public event Action<HotkeyEntry>? OnHotkeyChanged;


    /// <summary>
    /// Gets a value indicating whether the module is currently listening for a new binding.
    /// </summary>
    public bool IsListening => listeningHotkeyId != null;

    /// <summary>
    /// Gets the identifier of the hotkey currently being rebound.
    /// </summary>
    public string? ListeningHotkeyId => listeningHotkeyId;

    /// <summary>
    /// Registers a hotkey with the given binding and callback.
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

        var displayName = string.IsNullOrWhiteSpace(hotkeyDefinition.DisplayName)
            ? hotkeyDefinition.Id
            : hotkeyDefinition.DisplayName;

        ApplyPersistedBinding(hotkeyDefinition);

        lock (hotkeyLock)
        {
            if (hotkeys.ContainsKey(hotkeyDefinition.Id))
                return false;

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
    /// Sets the keyboard or gamepad binding for a hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to update.</param>
    /// <param name="binding">The new binding for the hotkey.</param>
    /// <returns>True if the hotkey was found and updated; otherwise, false.</returns>
    public bool SetHotkeyBinding(string id, HotkeyBinding binding)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.TryGetValue(id, out var entry))
                return false;

            var isNewBinding = entry.Binding != binding;
            entry.Binding = binding;
            lastBindingChangedId = id;
            OnHotkeyChanged?.Invoke(entry);
            PublishEvent(new HotkeyBindingChangedEvent(entry, isNewBinding));
            SaveHotkeyBinding(id, binding);
            return true;
        }
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
    /// Removes a hotkey from the manager.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to remove.</param>
    /// <returns>True if the hotkey was found and removed; otherwise, false.</returns>
    public bool UnregisterHotkey(string id)
    {
        lock (hotkeyLock)
        {
            if (!hotkeys.Remove(id))
                return false;
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
    /// Starts listening for a new binding for the specified hotkey.
    /// </summary>
    /// <param name="id">The identifier of the hotkey to listen for.</param>
    /// <param name="mode">The input mode for the hotkey.</param>
    /// <returns>True if listening started successfully; otherwise, false.</returns>
    public bool StartListening(string id, HotkeyListenMode mode = HotkeyListenMode.Keyboard)
    {
        if (!TryGetHotkey(id, out _))
            return false;

        listeningHotkeyId = id;
        listeningMode = mode;
        listeningModifierState = null;
        waitingForModifierRelease = false;
        PublishEvent(new HotkeyListeningStartedEvent(id, mode));
        return true;
    }

    /// <summary>
    /// Stops listening for a new binding.
    /// </summary>
    /// <param name="wasCancelled">True if the listening was cancelled; otherwise, false.</param>
    public void StopListening(bool wasCancelled = true)
    {
        if (listeningHotkeyId == null)
            return;

        var previousId = listeningHotkeyId;
        listeningHotkeyId = null;
        listeningModifierState = null;
        waitingForModifierRelease = false;
        PublishEvent(new HotkeyListeningStoppedEvent(previousId, wasCancelled));
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
        var isListening = listeningHotkeyId == id;

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

        var changed = lastBindingChangedId == id;
        if (changed)
            lastBindingChangedId = null;

        return changed;
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
        // On low FPS, hotkeys are skipped
        if (updateTimer != null)
            return;

        lastUpdateTick = Environment.TickCount64;
        updateTimer = new Timer(_ => OnSystemUpdate(), null, 0, UpdateIntervalMilliseconds);
    }

    private void StopUpdateTimer()
    {
        updateTimer?.Dispose();
        updateTimer = null;
        updateInProgress = 0;
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

            if (!IsActive || !NoireService.IsInitialized())
                return;

            var isFocused = WindowHelper.IsGameWindowFocused();
            if (!isFocused && listeningHotkeyId != null)
            {
                ResetInputState();
                return;
            }

            if (validKeyCodes.Count == 0)
                RefreshValidKeys();

            UpdateKeyStates();

            if (listeningHotkeyId != null)
            {
                ProcessListening();
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

    private void ProcessListening()
    {
        if (listeningHotkeyId == null)
            return;

        if (listeningMode == HotkeyListenMode.Keyboard)
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
                SetHotkeyBinding(listeningHotkeyId, binding);
                postListeningBlockKeyCode = activeKeyCode;
                SuppressHotkeyUntilRelease(listeningHotkeyId);
                StopListening(false);
                return;
            }

            if (hasModifiers)
            {
                if (waitingForModifierRelease && listeningModifierState.HasValue && HasModifierReleased(listeningModifierState.Value, modifierState))
                {
                    var pending = listeningModifierState.Value;
                    var binding = new HotkeyBinding(0, pending.Ctrl, pending.Shift, pending.Alt);
                    SetHotkeyBinding(listeningHotkeyId, binding);
                    SuppressHotkeyUntilRelease(listeningHotkeyId);
                    StopListening(false);
                    return;
                }

                listeningModifierState = modifierState;
                waitingForModifierRelease = true;
                return;
            }

            if (waitingForModifierRelease && listeningModifierState.HasValue)
            {
                var pending = listeningModifierState.Value;
                if (pending.Ctrl || pending.Shift || pending.Alt)
                {
                    var binding = new HotkeyBinding(0, pending.Ctrl, pending.Shift, pending.Alt);
                    SetHotkeyBinding(listeningHotkeyId, binding);
                    SuppressHotkeyUntilRelease(listeningHotkeyId);
                }

                StopListening(false);
            }
        }

        if (listeningMode == HotkeyListenMode.Gamepad)
        {
            if (lastPressedKey == KeybindsHelper.VkEscape)
            {
                StopListening();
                return;
            }

            if (!lastPressedGamepadButton.HasValue)
                return;

            var binding = new HotkeyBinding(lastPressedGamepadButton.Value);
            SetHotkeyBinding(listeningHotkeyId, binding);
            SuppressHotkeyUntilRelease(listeningHotkeyId);
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
                    TriggerHotkey(entry);

                continue;
            }

            if (entry.Binding.IsKeyboardBinding)
            {
                var triggered = IsKeyboardTriggered(entry);
                if (triggered)
                    TriggerHotkey(entry);
            }
        }
    }

    private bool IsKeyboardTriggered(HotkeyEntry entry)
    {
        var binding = entry.Binding;
        var modifierState = KeybindsHelper.GetRawModifierState(rawKeyboardState);
        var modifiersExactMatch = AreExactModifiersDown(modifierState, binding);

        bool mainKeyPhysicallyDown;
        bool combinationActive;

        if (binding.IsModifierOnly)
        {
            mainKeyPhysicallyDown = AreRequiredModifiersDown(modifierState, binding);
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
            var holdTriggered = entry.HoldTriggered;

            if (entry.ActivationMode == HotkeyActivationMode.Held
                && entry.BlockGameInput && wasArmed && !holdTriggered)
            {
                entry.NeedsInputForward = true;
            }

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
        entry.NeedsInputForward = false;
    }

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
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

    private void EnsureConfigLoaded()
    {
        hotkeyConfig ??= HotkeyManagerConfig.Instance;

        if (hotkeyConfig == null)
            return;

        if (hotkeyConfig.Keybinds.Comparer != StringComparer.OrdinalIgnoreCase)
            hotkeyConfig.Keybinds = new Dictionary<string, HotkeyBinding>(hotkeyConfig.Keybinds, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyPersistedBinding(HotkeyEntry entry)
    {
        if (!shouldSaveKeybinds)
            return;

        EnsureConfigLoaded();
        if (hotkeyConfig == null)
            return;

        if (hotkeyConfig.Keybinds.TryGetValue(entry.Id, out var binding))
            entry.Binding = binding;
    }

    private void SaveHotkeyBinding(string id, HotkeyBinding binding)
    {
        if (!shouldSaveKeybinds)
            return;

        EnsureConfigLoaded();
        if (hotkeyConfig == null)
            return;

        hotkeyConfig.Keybinds[id] = binding;
        hotkeyConfig.Save();
    }

    private void RemoveHotkeyBinding(string id)
    {
        if (!shouldSaveKeybinds)
            return;

        EnsureConfigLoaded();
        if (hotkeyConfig == null)
            return;

        if (hotkeyConfig.Keybinds.Remove(id))
            hotkeyConfig.Save();
    }

    private void SaveAllKeybinds()
    {
        if (!shouldSaveKeybinds)
            return;

        EnsureConfigLoaded();
        if (hotkeyConfig == null)
            return;

        lock (hotkeyLock)
        {
            hotkeyConfig.Keybinds = hotkeys.Values.ToDictionary(entry => entry.Id, entry => entry.Binding, StringComparer.OrdinalIgnoreCase);
        }

        hotkeyConfig.Save();
    }

    private bool GetIsDown(HotkeyEntry entry)
    {
        if (entry.Binding.IsGamepadBinding && entry.Binding.GamepadButton.HasValue && NoireService.GamepadState != null)
            return NoireService.GamepadState.Raw(entry.Binding.GamepadButton.Value) > 0f;

        if (!entry.Binding.IsKeyboardBinding)
            return false;

        var modifierState = KeybindsHelper.GetRawModifierState(rawKeyboardState);
        if (entry.Binding.IsModifierOnly)
            return AreRequiredModifiersDown(modifierState, entry.Binding);

        var modifiersDown = AreExactModifiersDown(modifierState, entry.Binding);
        if (!modifiersDown)
            return false;

        return KeybindsHelper.IsRawKeyDown(rawKeyboardState, entry.Binding.VkCode);
    }

    private bool AreExactModifiersDown((bool Ctrl, bool Shift, bool Alt) modifierState, HotkeyBinding binding)
    {
        return modifierState.Ctrl == binding.Ctrl
            && modifierState.Shift == binding.Shift
            && modifierState.Alt == binding.Alt;
    }

    private bool AreRequiredModifiersDown((bool Ctrl, bool Shift, bool Alt) modifierState, HotkeyBinding binding)
    {
        if (binding.Ctrl && !modifierState.Ctrl)
            return false;
        if (binding.Shift && !modifierState.Shift)
            return false;
        if (binding.Alt && !modifierState.Alt)
            return false;

        return true;
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

        if (listeningHotkeyId == null)
            return;

        if (listeningMode == HotkeyListenMode.Keyboard)
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
        var modifierState = KeybindsHelper.GetModifierState(NoireService.KeyState);

        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.Binding.IsEmpty || !entry.BlockGameInput || !entry.Binding.IsKeyboardBinding)
                continue;

            if (entry.RequireGameFocus && !isFocused)
                continue;

            if (entry.Binding.IsModifierOnly)
            {
                if (AreExactModifiersDown(modifierState, entry.Binding))
                    BlockModifierKeys(entry.Binding);
            }
            else if (IsFrameworkKeyDown(entry.Binding, modifierState))
            {
                NoireService.KeyState[entry.Binding.VkCode] = false;
            }
        }
    }

    private void ForwardPendingInputs()
    {
        List<HotkeyEntry> entries;
        lock (hotkeyLock)
        {
            entries = hotkeys.Values.ToList();
        }

        foreach (var entry in entries)
        {
            if (!entry.NeedsInputForward)
                continue;

            entry.NeedsInputForward = false;
            var binding = entry.Binding;

            // Not working properly, commented out for now.
            //if (binding.IsModifierOnly)
            //    KeybindsHelper.SendModifierPress(binding.Ctrl, binding.Shift, binding.Alt);
            //else if (binding.Ctrl || binding.Shift || binding.Alt)
            //    KeybindsHelper.SendModifiedKeyPress(binding.VkCode, binding.Ctrl, binding.Shift, binding.Alt);
            //else if (binding.VkCode != 0)
            //    KeybindsHelper.SendKeyPress(binding.VkCode);
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

        if (!AreExactModifiersDown(modifierState, binding))
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

        var keyboardText = KeybindsHelper.FormatListeningKeyboardInput(rawKeyboardState, currentKeysDown);
        if (string.IsNullOrWhiteSpace(keyboardText))
            keyboardText = "Press a key...";

        return showOnlyBinding ? keyboardText : $"{buttonLabel}: {keyboardText}";
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

    private void ResetInputState()
    {
        previousKeysDown.Clear();
        currentKeysDown.Clear();
        lastPressedKey = null;
        lastPressedGamepadButton = null;
        listeningModifierState = null;
        waitingForModifierRelease = false;
        postListeningBlockKeyCode = 0;
        StopListening();
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        StopUpdateTimer();
        ResetInputState();
        hotkeys.Clear();
    }

    #endregion
}
