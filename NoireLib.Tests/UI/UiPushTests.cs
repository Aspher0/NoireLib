using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives <see cref="UiPush"/> through a real ImGui frame. Two properties matter and both are frame properties: a scope
/// costs the frame nothing, and it leaves the ImGui style stacks exactly as deep as it found them.
/// </summary>
/// <remarks>
/// The stack depths are read from the live context rather than inferred, which is the same source
/// <c>UiStackSnapshot</c> reads. An unbalanced scope does not fail where it happens; it changes the colour of
/// everything drawn afterwards, in an unrelated widget, for the rest of the frame.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiPushTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiPushTests(UiHarness harness) => this.harness = harness;

    private static int ColorDepth => ImGui.GetCurrentContext().ColorStack.Size;

    private static int StyleVarDepth => ImGui.GetCurrentContext().StyleVarStack.Size;

    [Fact]
    public void Color_OneColour_AllocatesNothing()
    {
        var result = harness.Draw(static () =>
        {
            using var pushed = UiPush.Color(ImGuiCol.Text, Vector4.One);
            ImGui.TextUnformatted("pushed"u8);
        });

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Push_ManyOfEveryKind_AllocatesNothing()
    {
        var result = harness.Draw(static () =>
        {
            using var pushed = UiPush.Color(ImGuiCol.Text, Vector4.One);

            pushed.Push(ImGuiCol.Border, Vector4.One);
            pushed.Push(ImGuiStyleVar.Alpha, 1f);
            pushed.Push(ImGuiStyleVar.ItemSpacing, Vector2.One);
            pushed.PushFont(ImGui.GetFont());
            pushed.PushDisabled();

            ImGui.TextUnformatted("pushed"u8);
        });

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Dispose_AfterPushing_LeavesTheStacksAsItFoundThem()
    {
        harness.Draw(static () =>
        {
            var colorsBefore = ColorDepth;
            var styleVarsBefore = StyleVarDepth;

            using (var pushed = UiPush.Color(ImGuiCol.Text, Vector4.One))
            {
                pushed.Push(ImGuiCol.Border, Vector4.One);
                pushed.Push(ImGuiStyleVar.Alpha, 1f);

                ColorDepth.Should().Be(colorsBefore + 2);
                StyleVarDepth.Should().Be(styleVarsBefore + 1);
            }

            ColorDepth.Should().Be(colorsBefore);
            StyleVarDepth.Should().Be(styleVarsBefore);
        });
    }

    [Fact]
    public void Push_WhenConditionIsFalse_PushesNothing()
    {
        harness.Draw(static () =>
        {
            var colorsBefore = ColorDepth;
            var styleVarsBefore = StyleVarDepth;

            using (var pushed = UiPush.Color(ImGuiCol.Text, Vector4.One, when: false))
            {
                pushed.Push(ImGuiStyleVar.Alpha, 1f, when: false);

                ColorDepth.Should().Be(colorsBefore);
                StyleVarDepth.Should().Be(styleVarsBefore);
            }

            ColorDepth.Should().Be(colorsBefore);
            StyleVarDepth.Should().Be(styleVarsBefore);
        });
    }

    [Fact]
    public void Dispose_CalledTwice_PopsOnlyOnce()
    {
        harness.Draw(static () =>
        {
            var colorsBefore = ColorDepth;

            // The surrounding push stands in for whatever the caller had on the stack. A second Dispose that popped
            // again would take this one off, and the symptom would appear in an unrelated widget later in the frame.
            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);

            var pushed = UiPush.Color(ImGuiCol.Border, Vector4.One);

            pushed.Dispose();
            pushed.Dispose();

            ColorDepth.Should().Be(colorsBefore + 1);

            ImGui.PopStyleColor();
        });
    }

    /// <summary>
    /// Being disabled is not a stack the way the others are: ImGui multiplies the style's alpha when a disabled scope
    /// opens and writes the displaced value straight back when it closes. A style variable that straddles that, in
    /// either direction, is what these two cover.
    /// </summary>
    /// <remarks>
    /// The symptom of getting it wrong is not a failure where it happens. The alpha stays at the disabled value, so
    /// every window drawn afterwards is faded, for the rest of the frame and every frame after it.
    /// </remarks>
    [Fact]
    public void Dispose_StyleVarPushedInsideADisabledScope_RestoresTheAlpha()
    {
        harness.Draw(static () =>
        {
            var style = ImGui.GetStyle();
            var before = style.Alpha;

            using (var pushed = UiPush.Disabled())
            {
                pushed.Push(ImGuiStyleVar.Alpha, 0.5f);
            }

            style.Alpha.Should().Be(before);
        });
    }

    [Fact]
    public void Dispose_DisabledScopeOpenedAfterAStyleVar_RestoresTheAlpha()
    {
        harness.Draw(static () =>
        {
            var style = ImGui.GetStyle();
            var before = style.Alpha;

            using (var pushed = UiPush.Style(ImGuiStyleVar.Alpha, 0.5f))
            {
                pushed.PushDisabled();
            }

            style.Alpha.Should().Be(before);
        });
    }

    [Fact]
    public void Dispose_FontAndDisabledScope_LeavesNoneOfThemOpen()
    {
        harness.Draw(static () =>
        {
            var style = ImGui.GetStyle();
            var before = style.Alpha;
            var colorsBefore = ColorDepth;
            var styleVarsBefore = StyleVarDepth;

            using (var pushed = UiPush.Font(ImGui.GetFont()))
            {
                pushed.PushDisabled();
                pushed.Push(ImGuiCol.Text, Vector4.One);
                pushed.Push(ImGuiStyleVar.ItemSpacing, Vector2.One);
            }

            style.Alpha.Should().Be(before);
            ColorDepth.Should().Be(colorsBefore);
            StyleVarDepth.Should().Be(styleVarsBefore);
        });
    }

    [Fact]
    public void TextWrapPos_AllocatesNothing()
    {
        var result = harness.Draw(static () =>
        {
            using var pushed = UiPush.TextWrapPos(120f);
            ImGui.TextUnformatted("wrapped at a hundred and twenty"u8);
        });

        // The 24 bytes ImRaii.TextWrapPos costs are what this exists to remove.
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TextWrapPos_Dispose_RestoresThePreviousPosition()
    {
        // Read into locals and asserted outside the draw, so the assertions' own allocation stays out of any frame a
        // byte count is taken from.
        var before = 0f;
        var during = 0f;
        var after = 0f;

        harness.Draw(() =>
        {
            before = WrapPos;

            using (UiPush.TextWrapPos(120f))
                during = WrapPos;

            after = WrapPos;
        });

        during.Should().Be(120f);
        after.Should().Be(before);
    }

    /// <summary>
    /// The live wrap position, read from the context the way the stack depths are.
    /// </summary>
    private static float WrapPos => ImGui.GetCurrentContext().CurrentWindow.DC.TextWrapPos;

    [Fact]
    public void Dispose_DefaultScope_PopsNothing()
    {
        harness.Draw(static () =>
        {
            var colorsBefore = ColorDepth;
            var styleVarsBefore = StyleVarDepth;

            // What a method returns when it turns out to have no style to apply.
            using (default(UiPush)) { }

            ColorDepth.Should().Be(colorsBefore);
            StyleVarDepth.Should().Be(styleVarsBefore);
        });
    }
}
