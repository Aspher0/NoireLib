using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using FluentAssertions;
using NoireLib.Helpers;
using NoireLib.HotkeyManager;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the binding matching rules of <see cref="KeybindsHelper"/>: the strict/permissive modifier split that
/// decides whether a binding is considered held, and the <see cref="HotkeyBinding"/> conversions the rules are fed with.
/// These predicates are shared by <see cref="NoireHotkeyManager"/> and the NoireLib.UI widgets, so they are the single place
/// where "is this binding held" is decided; a divergence here would let a widget activate on a combination the hotkey module rejects.
/// </summary>
[SupportedOSPlatform("windows")]
public class KeybindsHelperTests
{
    #region Exact modifiers

    [Theory]
    [InlineData(false, false, false, false, false, false, true)] // Nothing required, nothing held.
    [InlineData(true, false, false, true, false, false, true)] // Ctrl required, Ctrl held.
    [InlineData(true, false, false, true, true, false, false)] // Ctrl required, Ctrl and Shift held.
    [InlineData(true, true, false, true, true, false, true)] // Ctrl and Shift required and held.
    [InlineData(false, false, false, true, false, false, false)] // Nothing required, Ctrl held.
    [InlineData(true, false, false, false, false, false, false)] // Ctrl required, nothing held.
    public void AreExactModifiersDown_RequiresTheHeldModifiersToMatchExactly(
        bool bindingCtrl, bool bindingShift, bool bindingAlt,
        bool heldCtrl, bool heldShift, bool heldAlt,
        bool expected)
    {
        var binding = new HotkeyBinding(0, bindingCtrl, bindingShift, bindingAlt);

        KeybindsHelper.AreExactModifiersDown((heldCtrl, heldShift, heldAlt), binding).Should().Be(expected);
    }

    #endregion

    #region Required modifiers

    [Theory]
    [InlineData(true, false, false, true, false, false, true)] // Ctrl required, Ctrl held.
    [InlineData(true, false, false, true, true, false, true)] // An extra modifier is tolerated.
    [InlineData(true, true, false, true, false, false, false)] // A missing modifier is not.
    [InlineData(false, false, false, true, true, true, true)] // Nothing required matches anything.
    public void AreRequiredModifiersDown_IgnoresExtraModifiers(
        bool bindingCtrl, bool bindingShift, bool bindingAlt,
        bool heldCtrl, bool heldShift, bool heldAlt,
        bool expected)
    {
        var binding = new HotkeyBinding(0, bindingCtrl, bindingShift, bindingAlt);

        KeybindsHelper.AreRequiredModifiersDown((heldCtrl, heldShift, heldAlt), binding).Should().Be(expected);
    }

    [Fact]
    public void AreExactModifiersDown_AndAreRequiredModifiersDown_DisagreeOnAnExtraModifier()
    {
        var binding = new HotkeyBinding(0, ctrl: true, shift: false, alt: false);
        var held = (Ctrl: true, Shift: true, Alt: false);

        KeybindsHelper.AreExactModifiersDown(held, binding).Should().BeFalse("an activation must not fire while a modifier it does not name is held");
        KeybindsHelper.AreRequiredModifiersDown(held, binding).Should().BeTrue("a modifier-only binding stays held while the user also presses another modifier");
    }

    #endregion

    #region IsBindingHeld

    [Fact]
    public void IsBindingHeld_EmptyBinding_IsNeverHeld()
    {
        KeybindsHelper.IsBindingHeld(default).Should().BeFalse("an empty binding means no requirement, not a satisfied one");
        KeybindsHelper.IsBindingHeld(new HotkeyBinding(0)).Should().BeFalse();
    }

    [Fact]
    public void IsBindingHeld_GamepadBinding_WithoutAGamepadState_IsNotHeld()
    {
        // NoireService is not initialized in tests, so no gamepad state exists to read.
        KeybindsHelper.IsBindingHeld(new HotkeyBinding(GamepadButtons.North)).Should().BeFalse();
    }

    [Fact]
    public void IsBindingHeld_KeyboardBinding_DoesNotThrowWithoutTheGame()
    {
        // Keyboard state comes from the OS rather than from Dalamud, so this stays answerable with no game running.
        var act = () => KeybindsHelper.IsBindingHeld(new HotkeyBinding(VirtualKey.F13, ctrl: true));

        act.Should().NotThrow();
    }

    #endregion

    #region HotkeyBinding conversions

    [Fact]
    public void HotkeyBinding_FromVirtualKey_ConvertsImplicitlyWithoutModifiers()
    {
        HotkeyBinding binding = VirtualKey.CONTROL;

        binding.VkCode.Should().Be((int)VirtualKey.CONTROL);
        binding.Ctrl.Should().BeFalse("the implicit conversion names a key, it does not infer a modifier from it");
        binding.IsKeyboardBinding.Should().BeTrue();
        binding.IsGamepadBinding.Should().BeFalse();
    }

    [Fact]
    public void HotkeyBinding_FromVirtualKey_WithModifiers_NeedsNoCast()
    {
        var binding = new HotkeyBinding(VirtualKey.G, ctrl: true, shift: true);

        binding.Should().Be(new HotkeyBinding((int)VirtualKey.G, ctrl: true, shift: true, alt: false));
    }

    [Fact]
    public void HotkeyBinding_FromInt_StillResolvesToTheIntConstructor()
    {
        // The VirtualKey overload must not capture the literal 0 that clearing a binding relies on.
        var cleared = new HotkeyBinding(0);

        cleared.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FormatBinding_FromAVirtualKeyBinding_ReadsAsTheKeyName()
    {
        KeybindsHelper.FormatBinding(new HotkeyBinding(VirtualKey.G, ctrl: true)).Should().Be("Ctrl + G");
        KeybindsHelper.FormatBinding(default).Should().Be("Unbound");
    }

    #endregion
}
