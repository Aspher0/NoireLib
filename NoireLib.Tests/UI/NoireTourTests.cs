using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the tour's step machine and the arithmetic that places its card: a skipped step is passed over in both
/// directions, the end of the tour completes it exactly once, and a card never leaves the viewport.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTourTests : IDisposable
{
    private static readonly Vector2 ViewMin = new(0f, 0f);

    private static readonly Vector2 ViewMax = new(1920f, 1080f);

    private static readonly Vector2 CardSize = new(320f, 140f);

    public NoireTourTests()
    {
        NoireTour.Stop();
        NoireTour.ClearTargets();
    }

    public void Dispose()
    {
        NoireTour.Stop();
        NoireTour.ClearTargets();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Start_RunsTheFirstStep()
    {
        NoireTour.Start("test", Steps(3));

        NoireTour.IsRunning.Should().BeTrue();
        NoireTour.Id.Should().Be("test");
        NoireTour.StepIndex.Should().Be(0);
        NoireTour.StepCount.Should().Be(3);
    }

    [Fact]
    public void Start_WithoutStepsRunsNothing()
    {
        NoireTour.Start("test", Array.Empty<TourStep>());

        NoireTour.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Next_PastTheLastStepCompletesTheTourOnce()
    {
        var completed = 0;
        NoireTour.Start("test", Steps(2), new TourOptions { OnCompleted = () => completed++ });

        NoireTour.Next().Should().BeTrue();
        NoireTour.Next().Should().BeFalse();

        completed.Should().Be(1);
        NoireTour.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Previous_StaysOnTheFirstStep()
    {
        NoireTour.Start("test", Steps(2));

        NoireTour.Previous().Should().BeFalse();
        NoireTour.StepIndex.Should().Be(0);
        NoireTour.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Stop_EndsTheTourWithoutCompletingIt()
    {
        var completed = false;
        var stopped = false;
        NoireTour.Start("test", Steps(2), new TourOptions { OnCompleted = () => completed = true, OnStopped = () => stopped = true });

        NoireTour.Stop();

        stopped.Should().BeTrue();
        completed.Should().BeFalse();
        NoireTour.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void SkippedSteps_ArePassedOverGoingForward()
    {
        var steps = Steps(3);
        steps[1].IsSkipped = static () => true;

        NoireTour.Start("test", steps);
        NoireTour.Next();

        NoireTour.StepIndex.Should().Be(2);
    }

    [Fact]
    public void SkippedSteps_ArePassedOverGoingBack()
    {
        var steps = Steps(3);
        steps[1].IsSkipped = static () => true;

        NoireTour.Start("test", steps);
        NoireTour.Next();
        NoireTour.Previous();

        NoireTour.StepIndex.Should().Be(0);
    }

    [Fact]
    public void AFirstStepThatIsSkipped_StartsOnTheNextOne()
    {
        var steps = Steps(2);
        steps[0].IsSkipped = static () => true;

        NoireTour.Start("test", steps);

        NoireTour.StepIndex.Should().Be(1);
    }

    [Fact]
    public void StepChanged_ReportsEveryStepTheTourLandsOn()
    {
        var seen = new List<int>();
        NoireTour.Start("test", Steps(3), new TourOptions { OnStepChanged = seen.Add });

        NoireTour.Next();
        NoireTour.Next();

        seen.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Mark_IsReadBackAsAScreenRectangle()
    {
        NoireTour.Mark("field", new Vector2(10f, 20f), new Vector2(110f, 60f));

        NoireTour.TryGetTarget("field", out var rect).Should().BeTrue();
        rect.Should().Be(new Vector4(10f, 20f, 110f, 60f));
    }

    [Fact]
    public void AnUnmarkedTarget_IsNotFound()
        => NoireTour.TryGetTarget("nothing", out _).Should().BeFalse();

    [Fact]
    public void Place_PutsTheCardUnderTheWidgetWhenThereIsRoom()
    {
        var card = TourLayout.Place(ViewMin, ViewMax, new Vector4(800f, 400f, 900f, 430f), true, CardSize, TourPlacement.Auto, 12f);

        card.Y.Should().Be(442f);
        card.X.Should().BeApproximately(690f, 0.01f);
    }

    [Fact]
    public void Place_PutsTheCardAboveTheWidgetWhenBelowIsFull()
    {
        var card = TourLayout.Place(ViewMin, ViewMax, new Vector4(800f, 1000f, 900f, 1040f), true, CardSize, TourPlacement.Auto, 12f);

        card.W.Should().BeLessThan(1000f);
    }

    [Fact]
    public void Place_KeepsTheCardInsideTheViewport()
    {
        var card = TourLayout.Place(ViewMin, ViewMax, new Vector4(0f, 0f, 20f, 20f), true, CardSize, TourPlacement.Left, 12f);

        card.X.Should().Be(0f);
        card.Z.Should().BeLessThanOrEqualTo(ViewMax.X);
    }

    [Fact]
    public void Place_CentresTheCardWithoutAWidget()
    {
        var card = TourLayout.Place(ViewMin, ViewMax, default, false, CardSize, TourPlacement.Auto, 12f);

        card.X.Should().Be(800f);
        card.Y.Should().Be(470f);
    }

    [Fact]
    public void Intersect_KeepsTheVisiblePartOfAWidget()
        => TourLayout.Intersect(new Vector4(10f, 10f, 40f, 40f), new Vector4(0f, 0f, 100f, 25f))
            .Should().Be(new Vector4(10f, 10f, 40f, 25f));

    [Fact]
    public void Intersect_LeavesTheWidgetAloneWithoutAClip()
        => TourLayout.Intersect(new Vector4(10f, 10f, 40f, 40f), default)
            .Should().Be(new Vector4(10f, 10f, 40f, 40f));

    [Fact]
    public void AWidgetScrolledOutOfView_IntersectsToNothing()
        => TourLayout.IsEmpty(TourLayout.Intersect(new Vector4(10f, 200f, 40f, 240f), new Vector4(0f, 0f, 100f, 120f)))
            .Should().BeTrue();

    [Theory]
    [InlineData(200f, 240f, TourDirection.Down)]
    [InlineData(-80f, -40f, TourDirection.Up)]
    public void DirectionTo_PointsTheWayTheWidgetWentOut(float top, float bottom, TourDirection expected)
        => TourLayout.DirectionTo(new Vector4(10f, top, 40f, bottom), new Vector4(0f, 0f, 100f, 120f))
            .Should().Be(expected);

    [Fact]
    public void EdgeOf_IsTheStripAlongTheSideTheWidgetWentOut()
        => TourLayout.EdgeOf(new Vector4(0f, 0f, 100f, 120f), TourDirection.Down, 30f)
            .Should().Be(new Vector4(0f, 90f, 100f, 120f));

    [Fact]
    public void IsInteractive_AnswersForTheStepTargetAndItsCompanionsOnly()
    {
        var steps = Steps(1);
        steps[0].Target = "field";
        steps[0].AlsoInteractive = ["tabs"];

        NoireTour.Start("test", steps);

        NoireTour.IsInteractive("field").Should().BeTrue();
        NoireTour.IsInteractive("tabs").Should().BeTrue();
        NoireTour.IsInteractive("open").Should().BeFalse();
    }

    [Fact]
    public void IsInteractive_AnswersTrueWithoutATour()
        => NoireTour.IsInteractive("anything").Should().BeTrue();

    [Fact]
    public void IsInteractive_AnswersTrueWhenBlockingIsOff()
    {
        var steps = Steps(1);
        steps[0].Target = "field";

        NoireTour.Start("test", steps, new TourOptions { BlockOtherWidgets = false });

        NoireTour.IsInteractive("open").Should().BeTrue();
    }

    [Fact]
    public void Contains_AnswersForTheEdgesToo()
    {
        var rect = new Vector4(10f, 10f, 20f, 20f);

        TourLayout.Contains(rect, new Vector2(10f, 20f)).Should().BeTrue();
        TourLayout.Contains(rect, new Vector2(21f, 15f)).Should().BeFalse();
    }

    [Fact]
    public void Bands_CoverEveryPixelOutsideTheLitRectanglesExactlyOnce()
    {
        var holes = new[] { new Vector4(4f, 3f, 10f, 7f), new Vector4(20f, 12f, 30f, 18f) };
        var bands = new Vector4[TourDim.MaxBands];
        var count = TourDim.Bands(bands, Vector2.Zero, new Vector2(40f, 20f), holes, holes.Length);

        var covered = new int[40, 20];

        for (var index = 0; index < count; index++)
        {
            var band = bands[index];

            for (var x = (int)band.X; x < (int)band.Z; x++)
            {
                for (var y = (int)band.Y; y < (int)band.W; y++)
                    covered[x, y]++;
            }
        }

        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                var lit = IsInside(holes, x + 0.5f, y + 0.5f);
                covered[x, y].Should().Be(lit ? 0 : 1, $"the pixel at {x},{y} is {(lit ? "lit" : "dimmed")}");
            }
        }
    }

    [Fact]
    public void Bands_DimTheWholeViewportWithoutALitRectangle()
    {
        var bands = new Vector4[TourDim.MaxBands];
        var count = TourDim.Bands(bands, Vector2.Zero, new Vector2(40f, 20f), Array.Empty<Vector4>(), 0);

        count.Should().Be(1);
        bands[0].Should().Be(new Vector4(0f, 0f, 40f, 20f));
    }

    [Fact]
    public void ASettlingStep_FillsItsBarAsTheConditionHolds()
    {
        var step = new TourStep { Advance = TourAdvance.WhenReady, IsReady = static () => true, SettleSeconds = 2f };
        var run = new TourRun("test", [step], new TourOptions());

        WithClock(0f, () => NoireTourHost.UpdateSettling(run, step));
        WithClock(1f, () => NoireTourHost.UpdateSettling(run, step));
        run.SettleFraction.Should().BeApproximately(0.5f, 0.001f);

        WithClock(2f, () => NoireTourHost.UpdateSettling(run, step));
        run.SettleFraction.Should().Be(1f);
    }

    [Fact]
    public void ASettlingStep_StartsOverWhenTheWatchedValueChanges()
    {
        var typed = 1;
        var step = new TourStep
        {
            Advance = TourAdvance.WhenReady,
            IsReady = static () => true,
            SettleSeconds = 2f,
            SettleStamp = () => typed,
        };

        var run = new TourRun("test", [step], new TourOptions());

        WithClock(0f, () => NoireTourHost.UpdateSettling(run, step));
        WithClock(1.5f, () => NoireTourHost.UpdateSettling(run, step));
        run.SettleFraction.Should().BeApproximately(0.75f, 0.001f);

        typed = 2;
        WithClock(1.5f, () => NoireTourHost.UpdateSettling(run, step));
        run.SettleFraction.Should().Be(0f);
    }

    [Fact]
    public void ASettlingStep_HoldsAtNothingWhileTheConditionIsFalse()
    {
        var step = new TourStep { Advance = TourAdvance.WhenReady, IsReady = static () => false, SettleSeconds = 2f };
        var run = new TourRun("test", [step], new TourOptions());

        WithClock(0f, () => NoireTourHost.UpdateSettling(run, step));
        WithClock(5f, () => NoireTourHost.UpdateSettling(run, step));

        run.SettleFraction.Should().Be(0f);
    }

    private static void WithClock(float seconds, Action body)
    {
        NoireUI.TimeOverride = () => seconds;

        try
        {
            body();
        }
        finally
        {
            NoireUI.TimeOverride = null;
        }
    }

    private static bool IsInside(Vector4[] holes, float x, float y)
    {
        foreach (var hole in holes)
        {
            if (x > hole.X && x < hole.Z && y > hole.Y && y < hole.W)
                return true;
        }

        return false;
    }

    private static TourStep[] Steps(int count)
    {
        var steps = new TourStep[count];

        for (var index = 0; index < count; index++)
            steps[index] = new TourStep { Title = "Step", Body = "Body" };

        return steps;
    }
}
