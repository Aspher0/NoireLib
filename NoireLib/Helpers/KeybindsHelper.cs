using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using NoireLib.HotkeyManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NoireLib.Helpers;

/// <summary>
/// Helper utilities for keybind management.
/// </summary>
public static class KeybindsHelper
{
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkAlt = 0x12;
    public const int VkEscape = 0x1B;
    public const int VkLeftShift = 0xA0;
    public const int VkRightShift = 0xA1;
    public const int VkLeftControl = 0xA2;
    public const int VkRightControl = 0xA3;
    public const int VkLeftAlt = 0xA4;
    public const int VkRightAlt = 0xA5;

    private static readonly GamepadButtons[] SingleGamepadButtons = Enum.GetValues<GamepadButtons>()
        .Where(value => value != 0 && IsSingleFlag(value))
        .ToArray();

    private const int InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Returns whether a key is currently pressed using the Win32 async key state.
    /// This is thread-independent and always reflects the physical key state.
    /// </summary>
    /// <param name="vkCode">The virtual key code.</param>
    /// <returns>True if the key is currently pressed, false otherwise.</returns>
    public static bool IsAsyncKeyDown(int vkCode)
    {
        return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Gets a display name for a virtual key code.
    /// </summary>
    /// <param name="vkCode">The virtual key code.</param>
    /// <returns>The display name of the key.</returns>
    public static string GetKeyName(int vkCode)
    {
        var key = (VirtualKey)vkCode;
        return Enum.IsDefined(key) ? key.ToString() : $"VK_{vkCode:X2}";
    }

    /// <summary>
    /// Formats a hotkey binding into a readable string.
    /// </summary>
    /// <param name="binding">The hotkey binding to format.</param>
    /// <returns>A readable string representation of the hotkey binding.</returns>
    public static string FormatBinding(HotkeyBinding binding)
    {
        if (binding.IsEmpty)
            return "Unbound";

        if (binding.GamepadButton != null)
            return $"Gamepad: {binding.GamepadButton}";

        var parts = FormatModifierParts(binding.Ctrl, binding.Shift, binding.Alt);

        if (binding.VkCode != 0)
            parts.Add(GetKeyName(binding.VkCode));

        return parts.Count == 0 ? "Unbound" : string.Join(" + ", parts);
    }

    /// <summary>
    /// Formats the currently held keyboard input while listening.
    /// </summary>
    /// <param name="keyState">The current key state.</param>
    /// <param name="keysDown">The currently pressed keys.</param>
    /// <returns>A formatted string representing the currently held keyboard input.</returns>
    public static string FormatListeningKeyboardInput(IKeyState keyState, IReadOnlyCollection<int> keysDown)
    {
        var modifierState = GetModifierState(keyState);
        var parts = FormatModifierParts(modifierState.Ctrl, modifierState.Shift, modifierState.Alt);
        var keyCode = keysDown.FirstOrDefault(code => !IsModifierKey(code));

        if (keyCode != 0)
            parts.Add(GetKeyName(keyCode));

        return parts.Count == 0 ? string.Empty : string.Join(" + ", parts);
    }

    /// <summary>
    /// Formats the currently held keyboard input while listening using raw keyboard state.
    /// </summary>
    /// <param name="keyState">The raw keyboard state.</param>
    /// <param name="keysDown">The currently pressed keys.</param>
    /// <returns>A formatted string representing the currently held keyboard input.</returns>
    public static string FormatListeningKeyboardInput(byte[] keyState, IReadOnlyCollection<int> keysDown)
    {
        var modifierState = GetRawModifierState(keyState);
        var parts = FormatModifierParts(modifierState.Ctrl, modifierState.Shift, modifierState.Alt);
        var keyCode = keysDown.FirstOrDefault(code => !IsModifierKey(code));

        if (keyCode != 0)
            parts.Add(GetKeyName(keyCode));

        return parts.Count == 0 ? string.Empty : string.Join(" + ", parts);
    }

    /// <summary>
    /// Formats the currently held gamepad input while listening.
    /// </summary>
    /// <param name="gamepadState">The current gamepad state.</param>
    /// <returns>A formatted string representing the currently held gamepad input.</returns>
    public static string FormatListeningGamepadInput(IGamepadState gamepadState)
    {
        var button = GetActiveGamepadButton(gamepadState);
        return button.HasValue ? $"Gamepad: {button.Value}" : string.Empty;
    }

    /// <summary>
    /// Returns whether a game text input is focused.
    /// </summary>
    /// <returns>True if a game text input is focused, false otherwise.</returns>
    public static bool IsTextInputActive()
    {
        try
        {
            unsafe
            {
                var module = RaptureAtkModule.Instance();
                return module != null && module->AtkModule.IsTextInputActive();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns whether the key code is a modifier key.
    /// </summary>
    /// <param name="vkCode">The virtual key code.</param>
    /// <returns>True if the key code is a modifier key, false otherwise.</returns>
    public static bool IsModifierKey(int vkCode)
    {
        return vkCode is VkShift or VkControl or VkAlt or VkLeftShift or VkRightShift or VkLeftControl or VkRightControl or VkLeftAlt or VkRightAlt;
    }

    /// <summary>
    /// Returns the current modifier key state.
    /// </summary>
    /// <param name="keyState">The current key state.</param>
    /// <returns>The current modifier key state.</returns>
    public static (bool Ctrl, bool Shift, bool Alt) GetModifierState(IKeyState keyState)
    {
        return (IsCtrlDown(keyState), IsShiftDown(keyState), IsAltDown(keyState));
    }

    /// <summary>
    /// Returns the current modifier key state from raw keyboard data.
    /// </summary>
    /// <param name="keyState">The raw keyboard state.</param>
    /// <returns>The current modifier key state.</returns>
    public static (bool Ctrl, bool Shift, bool Alt) GetRawModifierState(byte[] keyState)
    {
        return (IsRawCtrlDown(keyState), IsRawShiftDown(keyState), IsRawAltDown(keyState));
    }

    /// <summary>
    /// Updates the raw keyboard state.
    /// </summary>
    /// <param name="keyState">The buffer to fill with key state data.</param>
    /// <returns>True if the state was updated; otherwise, false.</returns>
    public static bool TryGetRawKeyboardState(byte[] keyState)
    {
        if (keyState == null || keyState.Length < 256)
            return false;

        Array.Clear(keyState, 0, keyState.Length);
        for (var keyCode = 0; keyCode < 256; keyCode++)
        {
            var state = GetAsyncKeyState(keyCode);
            if ((state & 0x8000) != 0)
                keyState[keyCode] = 0x80;
        }

        return true;
    }

    /// <summary>
    /// Returns whether the raw key state indicates a pressed key.
    /// </summary>
    /// <param name="keyState">The raw keyboard state.</param>
    /// <param name="vkCode">The virtual key code.</param>
    /// <returns>True if the key is down, false otherwise.</returns>
    public static bool IsRawKeyDown(byte[] keyState, int vkCode)
    {
        return vkCode >= 0 && vkCode < keyState.Length && (keyState[vkCode] & 0x80) != 0;
    }

    /// <summary>
    /// Returns the first newly pressed key from raw keyboard data.
    /// </summary>
    /// <param name="keyState">The raw keyboard state.</param>
    /// <param name="validKeys">The valid key codes to check.</param>
    /// <param name="previousKeysDown">The previously pressed keys.</param>
    /// <returns>The first newly pressed key code, or null if none found.</returns>
    public static int? GetNewlyPressedKey(byte[] keyState, IEnumerable<int> validKeys, IReadOnlySet<int> previousKeysDown)
    {
        foreach (var keyCode in validKeys)
        {
            if (IsRawKeyDown(keyState, keyCode) && !previousKeysDown.Contains(keyCode))
                return keyCode;
        }

        return null;
    }

    /// <summary>
    /// Sends a key press for a single key.
    /// </summary>
    /// <param name="vkCode">The virtual key code to press.</param>
    public static void SendKeyPress(int vkCode)
    {
        SendInputs(CreateKeyInputs(vkCode));
    }

    /// <summary>
    /// Sends a modifier-only key press.
    /// </summary>
    /// <param name="ctrl">Whether to press Ctrl.</param>
    /// <param name="shift">Whether to press Shift.</param>
    /// <param name="alt">Whether to press Alt.</param>
    public static void SendModifierPress(bool ctrl, bool shift, bool alt)
    {
        SendInputs(CreateModifierInputs(ctrl, shift, alt).ToArray());
    }

    /// <summary>
    /// Sends a key press with modifiers.
    /// </summary>
    /// <param name="vkCode">The virtual key code to press.</param>
    /// <param name="ctrl">Whether to press Ctrl.</param>
    /// <param name="shift">Whether to press Shift.</param>
    /// <param name="alt">Whether to press Alt.</param>
    public static void SendModifiedKeyPress(int vkCode, bool ctrl, bool shift, bool alt)
    {
        var inputs = new List<INPUT>();
        inputs.AddRange(CreateModifierInputs(ctrl, shift, alt, false));
        inputs.AddRange(CreateKeyInputs(vkCode));
        inputs.AddRange(CreateModifierInputs(ctrl, shift, alt, true));
        SendInputs(inputs.ToArray());
    }

    /// <summary>
    /// Returns whether the required modifiers for a binding are pressed.
    /// </summary>
    /// <param name="keyState">The current key state.</param>
    /// <param name="binding">The hotkey binding to check.</param>
    /// <returns>True if the required modifiers are pressed, false otherwise.</returns>
    public static bool AreModifiersDown(IKeyState keyState, HotkeyBinding binding)
    {
        if (binding.Ctrl && !IsCtrlDown(keyState))
            return false;

        if (binding.Shift && !IsShiftDown(keyState))
            return false;

        if (binding.Alt && !IsAltDown(keyState))
            return false;

        return true;
    }

    /// <summary>
    /// Gets the first newly pressed key code.
    /// </summary>
    /// <param name="keyState">The current key state.</param>
    /// <param name="validKeys">The valid key codes to check.</param>
    /// <param name="previousKeysDown">The previously pressed keys.</param>
    /// <returns>The first newly pressed key code, or null if none found.</returns>
    public static int? GetNewlyPressedKey(IKeyState keyState, IEnumerable<int> validKeys, IReadOnlySet<int> previousKeysDown)
    {
        foreach (var keyCode in validKeys)
        {
            if (keyState[keyCode] && !previousKeysDown.Contains(keyCode))
                return keyCode;
        }

        return null;
    }

    /// <summary>
    /// Gets the first pressed gamepad button, if any.
    /// </summary>
    /// <param name="gamepadState">The current gamepad state.</param>
    /// <returns>The first pressed gamepad button, or null if none found.</returns>
    public static GamepadButtons? GetPressedGamepadButton(IGamepadState gamepadState)
    {
        foreach (var button in SingleGamepadButtons)
        {
            if (gamepadState.Pressed(button) > 0f)
                return button;
        }

        return null;
    }

    /// <summary>
    /// Gets the first currently held gamepad button, if any.
    /// </summary>
    /// <param name="gamepadState">The current gamepad state.</param>
    /// <returns>The first currently held gamepad button, or null if none found.</returns>
    public static GamepadButtons? GetActiveGamepadButton(IGamepadState gamepadState)
    {
        foreach (var button in SingleGamepadButtons)
        {
            if (gamepadState.Raw(button) > 0f)
                return button;
        }

        return null;
    }

    private static bool IsCtrlDown(IKeyState keyState)
    {
        return keyState[VkControl] || keyState[VkLeftControl] || keyState[VkRightControl];
    }

    private static bool IsShiftDown(IKeyState keyState)
    {
        return keyState[VkShift] || keyState[VkLeftShift] || keyState[VkRightShift];
    }

    private static bool IsAltDown(IKeyState keyState)
    {
        return keyState[VkAlt] || keyState[VkLeftAlt] || keyState[VkRightAlt];
    }

    private static List<string> FormatModifierParts(bool ctrl, bool shift, bool alt)
    {
        var parts = new List<string>();

        if (ctrl)
            parts.Add("Ctrl");
        if (shift)
            parts.Add("Shift");
        if (alt)
            parts.Add("Alt");

        return parts;
    }

    private static bool IsSingleFlag(GamepadButtons button)
    {
        var value = (long)button;
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static bool IsRawCtrlDown(byte[] keyState)
    {
        return IsRawKeyDown(keyState, VkControl)
            || IsRawKeyDown(keyState, VkLeftControl)
            || IsRawKeyDown(keyState, VkRightControl);
    }

    private static bool IsRawShiftDown(byte[] keyState)
    {
        return IsRawKeyDown(keyState, VkShift)
            || IsRawKeyDown(keyState, VkLeftShift)
            || IsRawKeyDown(keyState, VkRightShift);
    }

    private static bool IsRawAltDown(byte[] keyState)
    {
        return IsRawKeyDown(keyState, VkAlt)
            || IsRawKeyDown(keyState, VkLeftAlt)
            || IsRawKeyDown(keyState, VkRightAlt);
    }

    private static INPUT[] CreateKeyInputs(int vkCode)
    {
        return
        [
            CreateKeyboardInput(vkCode, false),
            CreateKeyboardInput(vkCode, true),
        ];
    }

    private static IEnumerable<INPUT> CreateModifierInputs(bool ctrl, bool shift, bool alt, bool keyUp = false)
    {
        if (ctrl)
            yield return CreateKeyboardInput(VkControl, keyUp);
        if (shift)
            yield return CreateKeyboardInput(VkShift, keyUp);
        if (alt)
            yield return CreateKeyboardInput(VkAlt, keyUp);
    }

    private static INPUT CreateKeyboardInput(int vkCode, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vkCode,
                    dwFlags = keyUp ? KeyEventKeyUp : 0u,
                },
            },
        };
    }

    private static void SendInputs(INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
