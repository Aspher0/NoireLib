using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks why a dialog over a disabled window body can be clicked: ImGui gives the hover to a disabled item under the
/// mouse, and releasing it lets the item drawn over it take the hover.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiHoverTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiHoverTests(UiHarness harness) => this.harness = harness;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnItemOverADisabledOne_TakesTheHover_OnlyOnceItIsReleased(bool release, bool expected)
    {
        var hovered = false;

        harness.Draw(() =>
        {
            // The harness window takes no input: the items go in a window of their own.
            var size = new Vector2(80f, 30f);
            ImGui.SetNextWindowPos(new Vector2(40f, 40f), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(200f, 100f), ImGuiCond.Always);

            if (ImGui.Begin("##hovertest", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings))
            {
                var at = ImGui.GetCursorScreenPos();
                ImGui.GetIO().MousePos = at + (size * 0.5f);

                ImGui.BeginDisabled();
                ImGui.InvisibleButton("##under", size);
                ImGui.EndDisabled();

                if (release)
                    UiHover.ReleaseDisabled();

                ImGui.SetCursorScreenPos(at);
                ImGui.InvisibleButton("##over", size);
                hovered = ImGui.GetCurrentContext().HoveredId == ImGuiP.GetItemID();
            }

            ImGui.End();
        }, warmUpFrames: 3, profile: false);

        hovered.Should().Be(expected);
    }
}
