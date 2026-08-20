using FluentAssertions;
using NoireLib.Hooking;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the one thing that keeps the diagnostics window free for a plugin that does not want it: the window is
/// not constructed, not registered with any window system and never drawn until something asks to open it.
/// Reading its state must not be what brings it into existence.
/// </summary>
public sealed class HookWindowTests
{
    [Fact]
    public void ReadingWindowState_DoesNotConstructTheWindow()
    {
        NoireHook.IsWindowOpen.Should().BeFalse();

        NoireHookWindow.HasSharedInstance.Should().BeFalse(
            "a plugin that only creates hooks must not carry a window it never opened");
    }

    [Fact]
    public void HidingAWindowThatWasNeverOpened_DoesNotConstructIt()
    {
        NoireHook.HideWindow();

        NoireHookWindow.HasSharedInstance.Should().BeFalse();
        NoireHook.IsWindowOpen.Should().BeFalse();
    }
}
