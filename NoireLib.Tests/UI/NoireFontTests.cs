using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.IO;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="NoireFont"/> to CSS sizing (an em size read through the face's own metrics), CSS weight matching,
/// and a draw path that allocates nothing whether the size is built or still standing in.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireFontTests : IClassFixture<UiHarness>, IDisposable
{
    private const string Label = "Support me on Ko-fi";

    private static readonly string FontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");

    private static NoireFont? face;
    private static ImFontPtr builtFont;

    private readonly UiHarness harness;

    public NoireFontTests(UiHarness harness)
    {
        this.harness = harness;
        face ??= File.Exists(FontPath) ? NoireFont.FromFile(FontPath) : null;
    }

    public void Dispose()
    {
        NoireFont.BuiltFontOverride = null;
        NoireUI.ScaleOverride = null;
    }

    [Fact]
    public void ReadMetrics_ReadsHeadAndHhea()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        face!.UnitsPerEm.Should().BeGreaterThan(0);
        face.Ascender.Should().BeGreaterThan(0);
        face.Descender.Should().BeLessThan(0);
        face.LineRatio.Should().BeGreaterThan(1f, "a face's ascender to descender span is taller than its em");
    }

    [Fact]
    public void ReadMetrics_RejectsSomethingThatIsNotAFont()
    {
        var act = () => NoireFont.FromMemory(new byte[64], "empty");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(new[] { 400, 500, 600, 700, 800 }, 500, 500)]
    [InlineData(new[] { 300, 600, 700 }, 400, 300)]
    [InlineData(new[] { 300, 450, 700 }, 400, 450)]
    [InlineData(new[] { 600, 700 }, 500, 600)]
    [InlineData(new[] { 400, 700 }, 600, 700)]
    [InlineData(new[] { 400, 500 }, 800, 500)]
    [InlineData(new[] { 400, 700 }, 300, 400)]
    [InlineData(new[] { 100, 400 }, 300, 100)]
    public void MatchWeight_FollowsCssFontMatching(int[] available, int desired, int expected)
        => NoireFontFamily.MatchWeight(available, desired).Should().Be(expected);

    [Fact]
    public void EmPixels_AppliesScaleAndStep()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        NoireUI.ScaleOverride = static () => 1.5f;

        face!.EmPixels(13f).Should().Be(19.5f);
        face.EmPixels(12.9f).Should().Be(19.5f, "sizes within half a step share one built size");
        face.LineHeight(13f).Should().BeApproximately(19.5f * face.LineRatio, 0.001f);
    }

    [Fact]
    public void HalfLeading_CentresTheTextInACssLineBox()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        NoireUI.ScaleOverride = static () => 1f;

        var box = 13f * 1.4f;
        var expected = (box - face!.LineHeight(13f)) * 0.5f;

        face.HalfLeading(13f, 1.4f).Should().BeApproximately(expected, 0.001f);
    }

    [Fact]
    public void Draw_StandIn_AllocatesNothing()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        var result = harness.Draw(static () => DrawAll(), warmUpFrames: 2);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Draw_BuiltSize_AllocatesNothing()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        UseBuiltFont();

        var result = harness.Draw(static () => DrawAll(), warmUpFrames: 2);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void CalcSize_BuiltSize_TrackingAddsBetweenCharactersOnly()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        UseBuiltFont();

        var plain = Vector2.Zero;
        var tracked = Vector2.Zero;

        harness.Draw(() =>
        {
            plain = face!.CalcSize(Label, 13f);
            tracked = face.CalcSize(Label, 13f, 1f);
        });

        tracked.X.Should().BeApproximately(plain.X + (Label.Length - 1), 0.01f);
    }

    [Fact]
    public void CalcSize_BuiltSize_EllipsisFitsTheWidth()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        UseBuiltFont();

        var full = Vector2.Zero;
        var cut = Vector2.Zero;
        var truncated = false;

        harness.Draw(() =>
        {
            full = face!.CalcSize(Label, 13f);
            cut = face.CalcSize(Label, 13f, 0f, full.X * 0.5f);
            truncated = face.IsTruncated(Label, 13f, 0f, full.X * 0.5f);
        });

        truncated.Should().BeTrue();
        cut.X.Should().BeLessThanOrEqualTo(full.X * 0.5f);
        cut.X.Should().BeGreaterThan(full.X * 0.25f, "the cut keeps as much of the text as fits beside the ellipsis");
    }

    [Fact]
    public void CalcSize_Kerning_PullsAKernedPairTogether()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        UseBuiltFont();

        var kerned = Vector2.Zero;
        var apart = Vector2.Zero;
        var unkerned = Vector2.Zero;

        harness.Draw(() =>
        {
            kerned = face!.CalcSize("AV", 20f);
            apart = face.CalcSize("A", 20f) + face.CalcSize("V", 20f);

            face.Kerning = false;
            unkerned = face.CalcSize("AV", 20f);
            face.Kerning = true;
        });

        kerned.X.Should().BeLessThan(apart.X, "the face kerns this pair, and a browser applies that");
        unkerned.X.Should().BeApproximately(apart.X, 0.01f);
    }

    [Fact]
    public void SyntheticBoldPixels_FollowsTheBrowsersInterpolation()
    {
        NoireFont.SyntheticBoldPixels(9f).Should().BeApproximately(9f / 24f, 0.0001f);
        NoireFont.SyntheticBoldPixels(36f).Should().BeApproximately(36f / 32f, 0.0001f);
        NoireFont.SyntheticBoldPixels(12f).Should().BeApproximately(0.486f, 0.002f);
    }

    [Fact]
    public void Pick_SynthesisesABoldTheFamilyDoesNotCarry()
    {
        Assert.SkipWhen(face == null, "No system font to read.");

        var family = new NoireFontFamily("Probe").Add(400, face!).Add(500, face!);

        family.Pick(500, 12f).SyntheticBoldPx.Should().Be(0f);
        family.Pick(800, 12f).Weight.Should().Be(500);
        family.Pick(800, 12f).SyntheticBoldPx.Should().BeApproximately(NoireFont.SyntheticBoldPixels(12f), 0.0001f);

        var heavy = new NoireFontFamily("Probe").Add(400, face!).Add(700, face!);

        heavy.Pick(800, 12f).SyntheticBoldPx.Should().Be(0f, "the family carries a face heavy enough");
    }

    private static void DrawAll()
    {
        var list = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        face!.Draw(list, origin, 0xFFFFFFFFu, Label, 13f);
        face.Draw(list, origin + new Vector2(0f, 20f), 0xFFFFFFFFu, Label, 11f, 1f);
        face.Draw(list, origin + new Vector2(0f, 40f), 0xFFFFFFFFu, Label, 13f, 0f, 60f);
        face.Draw(list, origin + new Vector2(0f, 60f), 0xFFFFFFFFu, Label, 13f, 1.4f, 60f);
        face.CalcSize(Label, 15f, -0.3f);

        using (face.Push(12f))
            ImGui.TextUnformatted("Pushed"u8);
    }

    // The harness atlas stands in for the Dalamud one: the face is added once at the size it would be built at.
    private static unsafe void UseBuiltFont()
    {
        if (builtFont.IsNull)
        {
            var io = ImGui.GetIO();
            builtFont = io.Fonts.AddFontFromFileTTF(FontPath, face!.LineHeight(13f));
            io.Fonts.Build();
        }

        NoireFont.BuiltFontOverride = static (_, _) => builtFont;
    }
}
