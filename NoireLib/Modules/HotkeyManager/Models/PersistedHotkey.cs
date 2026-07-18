using System;

namespace NoireLib.HotkeyManager;

/// <summary>
/// The persisted form of a hotkey: its binding plus every option a consumer can configure on a
/// <see cref="HotkeyEntry"/>.<br/>
/// This is a deliberate projection of <see cref="HotkeyEntry"/> rather than the entry itself, because the entry's
/// callback is not serializable and its activation state is runtime-only. A stored record overrides the values a
/// hotkey is registered with, the same way a stored binding already did, so a change a consumer makes at runtime
/// survives a restart. Every field has the same default as the corresponding <see cref="HotkeyEntry"/> property, so
/// a record read from an older file that lacks a field comes up with that default.
/// </summary>
public sealed class PersistedHotkey
{
    /// <summary>
    /// The stored binding. See <see cref="HotkeyEntry.Binding"/>.
    /// </summary>
    public HotkeyBinding Binding { get; set; }

    /// <summary>
    /// The stored display name. See <see cref="HotkeyEntry.DisplayName"/>.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the hotkey is enabled. See <see cref="HotkeyEntry.Enabled"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The activation mode. See <see cref="HotkeyEntry.ActivationMode"/>.
    /// </summary>
    public HotkeyActivationMode ActivationMode { get; set; } = HotkeyActivationMode.Pressed;

    /// <summary>
    /// The initial hold delay. See <see cref="HotkeyEntry.HoldDelay"/>.
    /// </summary>
    public TimeSpan HoldDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The fixed repeat delay. See <see cref="HotkeyEntry.FixedRepeatDelay"/>.
    /// </summary>
    public TimeSpan FixedRepeatDelay { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// The minimum random repeat delay. See <see cref="HotkeyEntry.RepeatDelayMin"/>.
    /// </summary>
    public TimeSpan RepeatDelayMin { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// The maximum random repeat delay. See <see cref="HotkeyEntry.RepeatDelayMax"/>.
    /// </summary>
    public TimeSpan RepeatDelayMax { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Whether the repeat delay is randomized. See <see cref="HotkeyEntry.UseRandomRepeatDelay"/>.
    /// </summary>
    public bool UseRandomRepeatDelay { get; set; }

    /// <summary>
    /// Whether the hotkey is blocked while a game text input is active. See <see cref="HotkeyEntry.BlockWhenTextInputActive"/>.
    /// </summary>
    public bool BlockWhenTextInputActive { get; set; } = true;

    /// <summary>
    /// Whether the hotkey requires the game window to be focused. See <see cref="HotkeyEntry.RequireGameFocus"/>.
    /// </summary>
    public bool RequireGameFocus { get; set; } = true;

    /// <summary>
    /// Whether the hotkey blocks game input while pressed. See <see cref="HotkeyEntry.BlockGameInput"/>.
    /// </summary>
    public bool BlockGameInput { get; set; }

    /// <summary>
    /// Builds a persisted record from a live hotkey entry, capturing its binding and every configurable option.
    /// </summary>
    /// <param name="entry">The entry to capture.</param>
    /// <returns>A record carrying the entry's binding and options.</returns>
    public static PersistedHotkey FromEntry(HotkeyEntry entry) => new()
    {
        Binding = entry.Binding,
        DisplayName = entry.DisplayName,
        Enabled = entry.Enabled,
        ActivationMode = entry.ActivationMode,
        HoldDelay = entry.HoldDelay,
        FixedRepeatDelay = entry.FixedRepeatDelay,
        RepeatDelayMin = entry.RepeatDelayMin,
        RepeatDelayMax = entry.RepeatDelayMax,
        UseRandomRepeatDelay = entry.UseRandomRepeatDelay,
        BlockWhenTextInputActive = entry.BlockWhenTextInputActive,
        RequireGameFocus = entry.RequireGameFocus,
        BlockGameInput = entry.BlockGameInput,
    };

    /// <summary>
    /// Copies this record's binding and options onto a live hotkey entry, overriding the values it was registered
    /// with. The entry's id and callback are left untouched. A blank stored display name is harmless: registration
    /// replaces a blank display name with the id afterwards.
    /// </summary>
    /// <param name="entry">The entry to write onto.</param>
    public void ApplyTo(HotkeyEntry entry)
    {
        entry.Binding = Binding;
        entry.DisplayName = DisplayName;
        entry.Enabled = Enabled;
        entry.ActivationMode = ActivationMode;
        entry.HoldDelay = HoldDelay;
        entry.FixedRepeatDelay = FixedRepeatDelay;
        entry.RepeatDelayMin = RepeatDelayMin;
        entry.RepeatDelayMax = RepeatDelayMax;
        entry.UseRandomRepeatDelay = UseRandomRepeatDelay;
        entry.BlockWhenTextInputActive = BlockWhenTextInputActive;
        entry.RequireGameFocus = RequireGameFocus;
        entry.BlockGameInput = BlockGameInput;
    }
}
