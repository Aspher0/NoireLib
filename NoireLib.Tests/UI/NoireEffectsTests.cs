using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.Helpers;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks gradient sampling, motion measurement and effect scopes: the layout never moves and nothing allocates once warm.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireEffectsTests : IClassFixture<UiHarness>
{
    private const int Warm = 3;

    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Green = new(0f, 1f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    private static readonly NoireMotion Everything = NoireMotion.Create()
        .Scaling().Rocking().Squashing().Floating().Swaying().Bouncing().Orbiting().Leaning()
        .Shaking().Trembling().Glitching(every: 0f).WavingGlyphs().WobblingGlyphs().Revealing();

    private static readonly NoireGradient Animated = NoireGradient.Linear(GradientDirection.LeftToRight, Red, Green, Blue)
        .Scrolling().HueCycling().Pulsing(Vector4.One).Breathing().Shimmering(Vector4.One).Waving().Sparkling(Vector4.One).Blinking();

    private readonly UiHarness harness;

    public NoireEffectsTests(UiHarness harness) => this.harness = harness;

    #region Sampling

    [Fact]
    public void A_two_color_gradient_runs_from_the_first_to_the_last()
    {
        var gradient = NoireGradient.Of(Red, Blue);

        gradient.Sample(0f).Should().Be(Red);
        gradient.Sample(1f).Should().Be(Blue);
        gradient.Sample(0.5f).X.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Colors_without_a_position_are_spread_evenly()
        => NoireGradient.Of(Red, Green, Blue).Sample(0.5f).Should().Be(Green);

    [Fact]
    public void A_band_holds_its_color_across_its_width()
    {
        var gradient = NoireGradient.Of(Red, GradientStop.Band(Green, 0.3f, 0.6f), Blue);

        gradient.Sample(0.35f).Should().Be(Green);
        gradient.Sample(0.55f).Should().Be(Green);
    }

    [Fact]
    public void The_midpoint_moves_where_the_transition_is_half_done()
    {
        var gradient = NoireGradient.Of(new GradientStop(Red, Midpoint: 0.2f), Blue);

        gradient.Sample(0.2f).X.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void A_hard_transition_jumps_halfway()
    {
        var gradient = NoireGradient.Of(Red, Blue).WithEasing(GradientEasing.Hard);

        gradient.Sample(0.45f).Should().Be(Red);
        gradient.Sample(0.55f).Should().Be(Blue);
    }

    [Fact]
    public void Steps_cut_the_gradient_into_flat_bands()
    {
        var gradient = NoireGradient.Of(Red, Blue).WithSteps(2);

        gradient.Sample(0.2f).Should().Be(Red);
        gradient.Sample(0.8f).Should().Be(Blue);
    }

    [Fact]
    public void Repeating_starts_over_and_mirroring_runs_back()
    {
        var repeat = NoireGradient.Of(Red, Blue).WithRepeat(GradientRepeat.Repeat);
        var mirror = NoireGradient.Of(Red, Blue).WithRepeat(GradientRepeat.Mirror);

        repeat.Sample(1.25f).Should().Be(repeat.Sample(0.25f));
        mirror.Sample(1.25f).Should().Be(mirror.Sample(0.75f));
    }

    [Fact]
    public void Reversing_swaps_the_ends()
        => NoireGradient.Of(Red, Blue).Reversed().Sample(0f).Should().Be(Blue);

    [Fact]
    public void Every_color_space_keeps_the_stops_themselves()
    {
        foreach (var space in Enum.GetValues<GradientColorSpace>())
        {
            var gradient = NoireGradient.Of(Red, Blue).WithSpace(space);

            gradient.Sample(0f).X.Should().BeApproximately(1f, 0.01f, space.ToString());
            gradient.Sample(1f).Z.Should().BeApproximately(1f, 0.01f, space.ToString());
        }
    }

    [Fact]
    public void The_long_way_round_the_hue_wheel_passes_other_hues()
    {
        var shortest = NoireGradient.Of(Red, Blue).WithSpace(GradientColorSpace.HsvShortest).Sample(0.5f);
        var longest = NoireGradient.Of(Red, Blue).WithSpace(GradientColorSpace.HsvLongest).Sample(0.5f);

        shortest.Y.Should().BeApproximately(0f, 0.01f, "red to blue the short way passes magenta");
        longest.Y.Should().BeGreaterThan(0.9f, "the long way passes through green");
    }

    [Fact]
    public void A_gradient_needs_a_color()
    {
        var build = () => NoireGradient.Of();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Oklab_and_hsv_round_trip()
    {
        var color = new Vector4(0.2f, 0.6f, 0.9f, 1f);
        var (hue, saturation, value) = ColorHelper.ToHsv(color);

        ColorHelper.FromHsv(hue, saturation, value).X.Should().BeApproximately(color.X, 0.001f);
        ColorHelper.FromOklab(ColorHelper.ToOklab(color)).Y.Should().BeApproximately(color.Y, 0.002f);
    }

    [Theory]
    [InlineData(EffectWave.Sine)]
    [InlineData(EffectWave.Triangle)]
    [InlineData(EffectWave.Sawtooth)]
    public void Waves_rest_at_zero_and_stay_between_zero_and_one(EffectWave wave)
    {
        NoireEffects.Wave(wave, 0f).Should().BeApproximately(0f, 0.001f);

        for (var i = 0; i < 20; i++)
            NoireEffects.Wave(wave, i / 20f).Should().BeInRange(0f, 1f);
    }

    #endregion

    #region Measuring

    [Fact]
    public void A_motion_that_does_nothing_measures_the_box_it_was_given()
    {
        var (min, max) = NoireMotion.Create().Measure(new Vector2(10f, 20f), new Vector2(110f, 40f));

        min.Should().Be(new Vector2(10f, 20f));
        max.Should().Be(new Vector2(110f, 40f));
    }

    [Fact]
    public void A_quarter_turn_swaps_the_width_and_the_height()
    {
        var size = NoireMotion.Create().Rotated(90f).Measure(new Vector2(100f, 20f));

        size.X.Should().BeApproximately(20f, 0.01f);
        size.Y.Should().BeApproximately(100f, 0.01f);
    }

    [Fact]
    public void The_envelope_holds_every_moment_of_the_motion()
    {
        var motion = NoireMotion.Create().Rocking(10f).Scaling(0.9f, 1.2f).Floating(4f);
        var envelope = motion.Envelope(new Vector2(100f, 20f));

        envelope.X.Should().BeGreaterThanOrEqualTo(120f);
        envelope.Y.Should().BeGreaterThan(24f);
    }

    [Fact]
    public void A_spinning_motion_reserves_its_whole_circle()
    {
        var envelope = NoireMotion.Create().Spinning().Envelope(new Vector2(100f, 20f));
        var diagonal = new Vector2(100f, 20f).Length();

        envelope.X.Should().BeApproximately(diagonal, 0.5f);
        envelope.Y.Should().BeApproximately(diagonal, 0.5f);
    }

    #endregion

    #region Scopes

    [Fact]
    public void A_motion_never_moves_the_layout()
    {
        var plain = Vector2.Zero;
        var moved = Vector2.Zero;

        harness.Draw(() =>
        {
            ImGui.TextUnformatted("Before");
            plain = ImGui.GetCursorScreenPos();
        });

        harness.Draw(() =>
        {
            using (NoireMotion.Create().Scaled(3f).Offset(40f, 40f).Begin())
                ImGui.TextUnformatted("Before");

            moved = ImGui.GetCursorScreenPos();
        });

        moved.Should().Be(plain);
    }

    [Fact]
    public void Ending_a_scope_says_where_the_drawing_showed()
    {
        EffectResult result = default;

        harness.Draw(() =>
        {
            var scope = NoireMotion.Create().Offset(12f, 0f).Begin();
            ImGui.TextUnformatted("Shifted");
            result = scope.End();
        });

        result.IsEmpty.Should().BeFalse();
        result.Shift.X.Should().BeApproximately(12f, 0.01f);
        result.Size.Should().Be(result.LayoutSize);
    }

    [Fact]
    public void Closing_a_scope_twice_does_nothing_the_second_time()
    {
        var depthAfter = -1;

        harness.Draw(() =>
        {
            var scope = NoireGradient.Rainbow.Begin();
            ImGui.TextUnformatted("Once");
            scope.Dispose();
            scope.Dispose();
            scope.End().IsEmpty.Should().BeTrue();
            depthAfter = NoireEffects.Depth;
        });

        depthAfter.Should().Be(0);
    }

    [Fact]
    public void Closing_an_outer_scope_closes_the_ones_left_open_inside()
    {
        var depthAfter = -1;

        harness.Draw(() =>
        {
            var outer = NoireMotion.Create().Floating().Begin();
            NoireGradient.Rainbow.Begin();
            NoireMotion.Create().Rocking().Begin();
            ImGui.TextUnformatted("Nested");
            outer.Dispose();
            depthAfter = NoireEffects.Depth;
        });

        depthAfter.Should().Be(0);
    }

    [Fact]
    public void A_gradient_recolors_the_text_it_covers()
    {
        uint first = 0;
        uint last = 0;

        harness.Draw(() =>
        {
            var list = ImGui.GetWindowDrawList();
            var start = list.VtxBuffer.Size;

            // The host window can still be narrow from an earlier test, and its clip would cut the line short.
            ImGui.PushClipRect(Vector2.Zero, new Vector2(4096f, 4096f), false);

            using (NoireGradient.Linear(GradientDirection.LeftToRight, Red, Blue).Begin())
                ImGui.TextUnformatted("WWWWWWWWWW");

            ImGui.PopClipRect();

            var vertices = list.VtxBuffer.AsSpan()[start..];
            first = vertices[0].Col;
            last = vertices[^1].Col;
        }, warmUpFrames: Warm);

        (first & 0xFFu).Should().BeGreaterThan(200u, "the left end is red");
        ((last >> 16) & 0xFFu).Should().BeGreaterThan(200u, "the right end is blue");
    }

    [Fact]
    public void A_shape_under_a_gradient_is_cut_into_cells()
    {
        var plain = harness.Draw(() => NoireShapes.Rect(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + new Vector2(120f, 60f), Vector4.One));
        var cut = harness.Draw(() => NoireGradient.Radial(Red, Green, Blue).FillRect(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + new Vector2(120f, 60f), 8f));

        cut.TotalVtxCount.Should().BeGreaterThan(plain.TotalVtxCount + 100);
    }

    #endregion

    #region Allocation

    [Fact]
    public void An_animated_gradient_draws_without_allocating()
    {
        var result = harness.Draw(static () => Animated.Text("An animated gradient on text"), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Every_gradient_shape_draws_without_allocating()
    {
        foreach (var shape in Enum.GetValues<GradientShape>())
        {
            var gradient = shape == GradientShape.Custom
                ? NoireGradient.Custom(static p => p.X * p.Y, Red, Blue)
                : NoireGradient.Of(Red, Green, Blue).WithShape(shape);

            var result = harness.Draw(() => gradient.Text("Every shape"), warmUpFrames: Warm);

            result.AllocatedBytes.Should().Be(0L, shape.ToString());
        }
    }

    [Fact]
    public void Every_movement_at_once_draws_without_allocating()
    {
        var result = harness.Draw(static () => Everything.TextWrapped("Every movement at once, across several words"), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void A_gradient_and_a_motion_together_draw_without_allocating()
    {
        var result = harness.Draw(static () => NoireEffects.Text("Both", NoireGradient.Rainbow, Everything), warmUpFrames: Warm);

        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void A_cut_shape_draws_without_allocating()
    {
        var result = harness.Draw(static () => NoireGradient.Lava.FillRect(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + new Vector2(200f, 80f), 10f), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    #endregion
}
