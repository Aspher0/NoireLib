using FluentAssertions;
using NoireLib.Draw3D.Core;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the camera-phase trace's load analysis: the frame-time banding and the after-stop settle
/// classification that separates ordinary under-motion swim from drift that continues once the camera
/// has stopped.
/// </summary>
public class Draw3DSwimAnalysisTests
{
    // ---------------------------------------------------------------- frame-time bands

    [Theory]
    [InlineData(4f, 0)]      // 250 fps
    [InlineData(8.3f, 0)]    // just under the 8.5 ms bound
    [InlineData(8.5f, 1)]    // bound itself belongs to the next band
    [InlineData(16.6f, 1)]   // 60 fps
    [InlineData(20f, 2)]
    [InlineData(30f, 3)]
    [InlineData(50f, 4)]
    [InlineData(70f, 5)]
    [InlineData(500f, 5)]
    public void BandOf_MapsFrameTimesToTheirBand(float frameMs, int expected)
        => CameraSwimAnalysis.BandOf(frameMs).Should().Be(expected);

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void BandOf_DegenerateInputs_LandInBandZero(float frameMs)
        => CameraSwimAnalysis.BandOf(frameMs).Should().Be(0);

    [Fact]
    public void BandLabel_CoversEveryBand()
    {
        for (var b = 0; b < CameraSwimAnalysis.BandCount; b++)
            CameraSwimAnalysis.BandLabel(b).Should().NotBeNullOrWhiteSpace();

        CameraSwimAnalysis.BandLabel(-1).Should().Be("-1");
        CameraSwimAnalysis.BandLabel(CameraSwimAnalysis.BandCount).Should().Be(CameraSwimAnalysis.BandCount.ToString());
    }

    // ---------------------------------------------------------------- settle classification

    [Fact]
    public void Advance_DriftRightAfterMotionStops_IsOneSettleEvent()
    {
        var tracker = new SettleTracker();

        tracker.Advance(cameraMoving: true, overlayDrifting: true).Should().Be(SettleFrame.Moving);
        tracker.Advance(false, true).Should().Be(SettleFrame.Settle);
        tracker.Advance(false, true).Should().Be(SettleFrame.Settle);
        tracker.Advance(false, true).Should().Be(SettleFrame.Settle);
        tracker.Advance(false, false).Should().Be(SettleFrame.QuietClean);

        tracker.Events.Should().Be(1);
        tracker.SettleFrames.Should().Be(3);
        tracker.LongestRun.Should().Be(3);
        tracker.MovingFrames.Should().Be(1);
        tracker.QuietCleanFrames.Should().Be(1);
        tracker.LateDriftFrames.Should().Be(0);
    }

    [Fact]
    public void Advance_DriftWhileMoving_IsNeverASettle()
    {
        var tracker = new SettleTracker();

        for (var i = 0; i < 10; i++)
            tracker.Advance(cameraMoving: true, overlayDrifting: true).Should().Be(SettleFrame.Moving);

        tracker.Events.Should().Be(0);
        tracker.SettleFrames.Should().Be(0);
        tracker.MovingFrames.Should().Be(10);
    }

    [Fact]
    public void Advance_DriftAtTraceStart_CountsAsLateDrift()
    {
        // No motion has been seen yet, so drift on the very first frames is a persistent offset,
        // not a settle after a stop.
        var tracker = new SettleTracker();

        tracker.Advance(false, true).Should().Be(SettleFrame.LateDrift);
        tracker.Advance(false, true).Should().Be(SettleFrame.LateDrift);

        tracker.Events.Should().Be(0);
        tracker.LateDriftFrames.Should().Be(2);
    }

    [Fact]
    public void Advance_DriftLongAfterMotion_CountsAsLateDrift()
    {
        var tracker = new SettleTracker();

        tracker.Advance(true, false);
        for (var i = 0; i < SettleTracker.WindowFrames; i++)
            tracker.Advance(false, false);

        tracker.Advance(false, true).Should().Be(SettleFrame.LateDrift);
        tracker.Events.Should().Be(0);
    }

    [Fact]
    public void Advance_RunStartedInsideTheWindow_KeepsCountingPastIt()
    {
        // A settle that begins right after the stop but outlasts the window is still one settle run;
        // only drift that STARTS late is a persistent offset.
        var tracker = new SettleTracker();

        tracker.Advance(true, false);
        for (var i = 0; i < SettleTracker.WindowFrames - 1; i++)
            tracker.Advance(false, false);

        for (var i = 0; i < 10; i++)
            tracker.Advance(false, true).Should().Be(SettleFrame.Settle);

        tracker.Events.Should().Be(1);
        tracker.SettleFrames.Should().Be(10);
        tracker.LongestRun.Should().Be(10);
        tracker.LateDriftFrames.Should().Be(0);
    }

    [Fact]
    public void Advance_TwoStopsWithDrift_AreTwoEvents()
    {
        var tracker = new SettleTracker();

        tracker.Advance(true, false);
        tracker.Advance(false, true);
        tracker.Advance(false, true);
        tracker.Advance(true, false);
        tracker.Advance(false, true);

        tracker.Events.Should().Be(2);
        tracker.SettleFrames.Should().Be(3);
        tracker.LongestRun.Should().Be(2);
    }

    [Fact]
    public void Advance_CleanGapSplitsARunIntoTwoEvents()
    {
        var tracker = new SettleTracker();

        tracker.Advance(true, false);
        tracker.Advance(false, true);
        tracker.Advance(false, false);
        tracker.Advance(false, true);

        tracker.Events.Should().Be(2);
        tracker.SettleFrames.Should().Be(2);
        tracker.LongestRun.Should().Be(1);
    }
}
