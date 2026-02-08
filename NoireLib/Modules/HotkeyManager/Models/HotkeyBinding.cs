using Dalamud.Game.ClientState.GamePad;
using System;

namespace NoireLib.HotkeyManager;

/// <summary>
/// Represents a hotkey binding.
/// </summary>
public struct HotkeyBinding : IEquatable<HotkeyBinding>
{
    /// <summary>
    /// The virtual key code used for keyboard bindings.
    /// </summary>
    public int VkCode { get; set; }

    /// <summary>
    /// Whether Ctrl must be held for this binding.
    /// </summary>
    public bool Ctrl { get; set; }

    /// <summary>
    /// Whether Shift must be held for this binding.
    /// </summary>
    public bool Shift { get; set; }

    /// <summary>
    /// Whether Alt must be held for this binding.
    /// </summary>
    public bool Alt { get; set; }

    /// <summary>
    /// Optional gamepad button binding.
    /// </summary>
    public GamepadButtons? GamepadButton { get; set; }

    /// <summary>
    /// Gets a value indicating whether this binding is empty.
    /// </summary>
    public bool IsEmpty => VkCode == 0 && GamepadButton == null && !Ctrl && !Shift && !Alt;

    /// <summary>
    /// Gets a value indicating whether this binding uses the keyboard.
    /// </summary>
    public bool IsKeyboardBinding => GamepadButton == null && (VkCode != 0 || Ctrl || Shift || Alt);

    /// <summary>
    /// Gets a value indicating whether this binding uses only modifier keys.
    /// </summary>
    public bool IsModifierOnly => GamepadButton == null && VkCode == 0 && (Ctrl || Shift || Alt);

    /// <summary>
    /// Gets a value indicating whether this binding uses the gamepad.
    /// </summary>
    public bool IsGamepadBinding => GamepadButton != null;

    /// <summary>
    /// Creates a keyboard binding.
    /// </summary>
    public HotkeyBinding(int vkCode, bool ctrl = false, bool shift = false, bool alt = false)
    {
        VkCode = vkCode;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
        GamepadButton = null;
    }

    /// <summary>
    /// Creates a gamepad binding.
    /// </summary>
    public HotkeyBinding(GamepadButtons gamepadButton)
    {
        VkCode = 0;
        Ctrl = false;
        Shift = false;
        Alt = false;
        GamepadButton = gamepadButton;
    }

    /// <summary>
    /// Creates a binding with both keyboard and gamepad configuration.
    /// </summary>
    public HotkeyBinding(int vkCode, bool ctrl, bool shift, bool alt, GamepadButtons? gamepadButton)
    {
        VkCode = vkCode;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
        GamepadButton = gamepadButton;
    }

    public readonly bool Equals(HotkeyBinding other)
    {
        return VkCode == other.VkCode
            && Ctrl == other.Ctrl
            && Shift == other.Shift
            && Alt == other.Alt
            && GamepadButton == other.GamepadButton;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is HotkeyBinding other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(VkCode, Ctrl, Shift, Alt, GamepadButton);
    }

    public static bool operator ==(HotkeyBinding left, HotkeyBinding right) => left.Equals(right);

    public static bool operator !=(HotkeyBinding left, HotkeyBinding right) => !left.Equals(right);

    public static implicit operator HotkeyBinding(int vkCode) => new(vkCode);

    public static implicit operator HotkeyBinding(GamepadButtons gamepadButton) => new(gamepadButton);
}
