using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Asserts that every drawing surface in <c>UI/</c> is visible to the profiler, and that being measured does not
/// change what reaches the screen.
/// </summary>
/// <remarks>
/// A surface that opens no scope costs what it costs, but that cost lands in whichever scope encloses it and reads as
/// a caller's expense. The failure is silent: a row simply stops appearing in a window nobody had open. These are one
/// assertion per surface, so a surface going quiet is a red test instead.<br/>
/// Several draws are wrapped in <see cref="NoireShapes.On(ImDrawListPtr, Action)"/> to give a shape somewhere real to
/// land, which is what a shape drawn outside a window needs.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiDrawCoverageTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiDrawCoverageTests(UiHarness harness) => this.harness = harness;

    private static readonly Vector2 Min = new(20f, 20f);
    private static readonly Vector2 Max = new(220f, 120f);
    private static readonly Vector2 Centre = new(120f, 70f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Runs a draw redirected onto the window's list, which is what a gated surface needs outside a plugin.
    /// </summary>
    private UiHarnessResult Redirected(Action body)
        => harness.Draw(() => NoireShapes.On(ImGui.GetWindowDrawList(), body, static b => b()));

    [Theory]
    [InlineData("NoireShapes.Fill")]
    [InlineData("NoireShapes.Stroke")]
    [InlineData("NoireShapes.Bevel")]
    [InlineData("NoireShapes.Glow")]
    [InlineData("NoireShapes.FillShaded")]
    [InlineData("NoireShapes.CornerTicks")]
    [InlineData("NoireShapes.FadedLine")]
    [InlineData("NoireShapes.Gradient")]
    [InlineData("NoireShapes.Sunburst")]
    [InlineData("NoireShapes.Guilloche")]
    public void EveryShapeSurface_RegistersItsOwnScope(string scope)
    {
        var result = Redirected(static () =>
        {
            Span<Vector2> path = stackalloc Vector2[NoireShapes.MaxRectPathPoints];
            var count = NoireShapes.RectPath(path, Min, Max, CornerShape.Notched, 12f);

            NoireShapes.Fill(path[..count], White);
            NoireShapes.Stroke(path[..count], White);
            NoireShapes.Bevel(path[..count], White, White, 2f);
            NoireShapes.Glow(Min, Max, White, 8f);
            NoireShapes.GradientRect(Min, Max, White, White);
            NoireShapes.Frame(Min, Max, new FrameStyle { TickLength = 14f });
            NoireShapes.FadedLine(Min, new Vector2(Max.X, Min.Y), White);
            NoireShapes.Gradient(Min, Max, GradientAxis.Vertical, White, White, static () => NoireShapes.Rect(Min, Max, White));
            NoireShapes.Sunburst(Centre, 60f, White);
            NoireShapes.Guilloche(Centre, 60f, White);
        });

        result.Scopes.Should().Contain(scope);
    }

    [Fact]
    public void SunburstGuillocheAndGlow_ReportUnderTheirMethodNames()
    {
        // These three are named for their method rather than their type, so a reader sees what all the sunbursts cost
        // separately from what all the glows did. Renaming any of them silently invalidates every figure recorded
        // against the old name.
        var result = Redirected(static () =>
        {
            NoireShapes.Sunburst(Centre, 60f, White);
            NoireShapes.Guilloche(Centre, 60f, White);
            NoireShapes.Glow(Min, Max, White, 8f);
        });

        result.Scopes.Should().Contain("NoireShapes.Sunburst")
            .And.Contain("NoireShapes.Guilloche")
            .And.Contain("NoireShapes.Glow");
    }

    [Fact]
    public void TrackedText_RegistersUnderTheSameNameAsEveryOtherPieceOfText()
    {
        // NoireText.Tracked.cs is a second file of the same partial class, and it has to report as NoireText rather
        // than as NoireText.Tracked: text is one row by design, and the scope name is derived from the type the file
        // belongs to rather than from the file.
        var result = Redirected(static () => NoireText.Tracked("HEADING"));

        result.Scopes.Should().Contain("NoireText");
        result.Scopes.Should().NotContain("NoireText.Tracked");
    }

    [Fact]
    public void AScopeIsNeverOpenedInsideAScopeOfItsOwnName()
    {
        // The profiler keys a node on its name and its parent, so a surface that opens its own name again inside
        // itself gets a second node rather than one row: the outer row loses the time to a same-named child, and a
        // figure recorded against the single row is no longer comparable with either.
        var result = harness.Draw(static () =>
        {
            NoireButtons.Button("Save", null);

            var number = 3f;
            NoireInputs.Number("Count", ref number, (NumberStyle?)null);

            NoireGauges.Bar(0.6f);
        });

        foreach (var scope in result.Scopes)
            result.Scopes.Should().ContainSingle(other => other == scope, "'{0}' must be one row, not a row nested in itself", scope);
    }

    [Fact]
    public void Buttons_RegisterUnderOneRowForTheWholeFrame()
    {
        var result = harness.Draw(static () =>
        {
            NoireButtons.Button("Save", null);
            NoireButtons.Spinner();

            var on = true;
            NoireButtons.Toggle("Enabled", ref on);
        });

        // One row for every button in the frame, which is the shape a static helper wants: a reader asks what all the
        // buttons cost, not what the eleventh one did.
        result.Scopes.Should().Contain("NoireButtons");
    }

    [Fact]
    public void SlidersAndGauges_RegisterTheirOwnScopes()
    {
        var result = harness.Draw(static () =>
        {
            var value = 0.5f;

            NoireSliders.Float("Volume", ref value, 0f, 1f);
            NoireGauges.Ring(0.6f);
            NoireGauges.Bar(0.6f);
            NoireGauges.Pips(2, 5);
        });

        result.Scopes.Should().Contain("NoireSliders").And.Contain("NoireGauges");
    }

    [Fact]
    public void Inputs_RegisterTheirOwnScope()
    {
        var result = harness.Draw(static () =>
        {
            var number = 3f;
            var duration = TimeSpan.FromMinutes(2);
            var colour = new Vector4(0.2f, 0.4f, 0.8f, 1f);

            NoireInputs.Number("Count", ref number, (NumberStyle?)null);
            NoireInputs.Duration("Delay", ref duration);
            NoireInputs.HexColor("Tint", ref colour);
        });

        result.Scopes.Should().Contain("NoireInputs");
    }

    [Fact]
    public void AnInstanceWidget_KeepsItsKindAndIdScopeIdentity()
    {
        // Instance widgets report under Kind:Id so that two tables on one page are told apart. The gate derives names
        // from the call site, which cannot produce a runtime id, so it must not flatten these into one row per type.
        var table = new NoireTable<string>("people", ["Ada", "Grace"]);

        table.Columns.Add(new TableColumn<string> { Header = "Name", Text = static row => row });

        var result = harness.Draw(() => table.Draw());

        result.Scopes.Should().Contain("NoireTable:people");
    }

    [Fact]
    public void ATooltip_RegistersItsOwnScope()
    {
        var result = harness.Draw(static () => NoireTooltip.Show("A tooltip", null, "coverage"));

        result.Scopes.Should().Contain("NoireTooltip");
    }

    [Fact]
    public void ATooltipKeepsItsWindowIdentityAndPlacement()
    {
        // The tooltip window flag reroutes which style fields ImGui reads for border and background, and it decides
        // placement. Anything opened between the style pushes and the Begin that reads them can move the window or
        // change what it is styled by, so the scope has to sit outside both.
        var first = harness.Draw(static () => NoireTooltip.Show("A tooltip", null, "identity"), warmUpFrames: 3);
        var second = harness.Draw(static () => NoireTooltip.Show("A tooltip", null, "identity"), warmUpFrames: 3);

        // The tooltip parks itself off screen until it has measured, then settles. Two settled frames of the same
        // content must produce the same geometry: a window that moved or changed identity would not.
        second.TotalVtxCount.Should().Be(first.TotalVtxCount);
        second.TotalIdxCount.Should().Be(first.TotalIdxCount);
        second.TotalVtxCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The geometry a notched plate and a ticked frame produce at these exact dimensions.
    /// </summary>
    /// <remarks>
    /// Exact rather than a lower bound, which is the point: measuring a surface must not change what it draws, and
    /// neither must a change made for speed. A change that legitimately alters the tessellation updates these two
    /// numbers alongside the case for the new ones.
    /// </remarks>
    private const int PlateAndFrameVertices = 80;
    private const int PlateAndFrameIndices = 186;

    [Fact]
    public void Splitter_RegistersItsOwnScope()
    {
        var size = 120f;
        var result = harness.Draw(() => NoireLayout.Splitter("splitter", ref size, new SplitterOptions()));

        result.Scopes.Should().Contain("NoireLayout.Splitter");
    }

    [Fact]
    public void Collapsible_RegistersItsOwnScope()
    {
        var result = harness.Draw(static () =>
            NoireLayout.Collapsible("collapsible", "Section", static () => NoireText.Draw("body"), static b => b()));

        result.Scopes.Should().Contain("NoireLayout.Collapsible");
    }

    [Fact]
    public void AKeyCap_RegistersItsOwnScope()
    {
        var result = harness.Draw(static () => new NoireContent().AddKeyCap("Ctrl").Draw());

        result.Scopes.Should().Contain("NoireContent.DrawKeyCap");
    }

    [Fact]
    public void APanel_RegistersItsOwnScope()
    {
        var result = harness.Draw(static () => NoirePanel.Plate(static () => NoireText.Draw("panel")));

        result.Scopes.Should().Contain("NoirePanel");
    }

    [Fact]
    public void WindowChrome_RegistersItsOwnScope()
    {
        var result = harness.Draw(static () => NoireWindowChrome.Draw(static () => NoireText.Draw("chrome")));

        result.Scopes.Should().Contain("NoireWindowChrome");
    }

    [Fact]
    public void APanelLeavesTheDrawListOnTheChannelItFoundIt()
    {
        // NoirePanel splits the window's list into a chrome channel and a content channel, so that chrome painted
        // after the body still lands behind it. A scope opened around a channel switch must not change which channel
        // is current on the way out, and a panel that merged while pointing at the wrong one would put every later
        // item in the frame on the wrong layer.
        var result = harness.Draw(static () =>
        {
            NoirePanel.Plate(static () => NoireText.Draw("inside"));

            // Drawn after the panel has merged its channels. A list left split, or left pointing at a channel that no
            // longer exists, loses this rectangle rather than drawing it.
            using var draw = UiDraw.BeginWindow();
            draw.List.AddRectFilled(new Vector2(300f, 300f), new Vector2(360f, 340f), 0xFFFFFFFFu);
        });

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.Scopes.Should().Contain("NoirePanel");
    }

    [Fact]
    public void NestedPanelsSplitOnlyOnce()
    {
        // A draw list can only be split once at a time. The inner panel must reuse the split its parent made, and the
        // merge must happen exactly once on the way back out.
        var act = () => harness.Draw(static () =>
            NoirePanel.Plate(static () => NoirePanel.Frame(static () => NoireText.Draw("nested"))));

        act.Should().NotThrow();
    }

    [Fact]
    public void APlateAndAFrame_ProduceTheirRecordedGeometry()
    {
        var result = Redirected(static () =>
        {
            NoireShapes.Plate(Min, Max, new PlateStyle { CornerShape = CornerShape.Notched, CornerSize = 12f, BevelSize = 2f });
            NoireShapes.Frame(Min, Max, new FrameStyle { TickLength = 14f });
        });

        result.TotalVtxCount.Should().Be(PlateAndFrameVertices);
        result.TotalIdxCount.Should().Be(PlateAndFrameIndices);
    }
}
