using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the contracts of the NoireUI foundations whose failure is silent rather than loud: the effective-AutoDraw
/// truth table, the frame stamp that stops a manual draw being doubled, the transient state store (isolation by type,
/// refresh on read, pruning), and the draw queue's ordering and drop-oldest policy.
/// </summary>
/// <remarks>
/// These share process-wide static state, so the whole NoireUI suite runs in one collection rather than in parallel.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public class NoireUiFoundationsTests : IDisposable
{
    private readonly bool originalAutoDraw = NoireUI.AutoDraw;
    private readonly int originalPruneAfter = UiFrameState.PruneAfterFrames;
    private readonly int originalPruneInterval = UiFrameState.PruneIntervalFrames;

    private int frame;

    public NoireUiFoundationsTests()
    {
        NoireUI.FrameOverride = () => frame;
        UiFrameState.Clear();
    }

    public void Dispose()
    {
        NoireUI.FrameOverride = null;
        NoireUI.AutoDraw = originalAutoDraw;
        UiFrameState.PruneAfterFrames = originalPruneAfter;
        UiFrameState.PruneIntervalFrames = originalPruneInterval;
        UiFrameState.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>A drawable that never registers with the hub, so it can be exercised without an initialized NoireLib.</summary>
    private sealed class TestDrawable : NoireDrawable
    {
        public TestDrawable(string id = "test")
            : base(id, "TestDrawable")
        {
        }

        public int Draws { get; private set; }

        public Action? OnDraw { get; set; }

        /// <summary>Exposes the protected persist-key guard so the refusal rule can be tested directly.</summary>
        public bool TryGetKey(string subKey, out string key) => TryGetPersistKey(subKey, out key);

        protected override void DrawCore()
        {
            Draws++;
            OnDraw?.Invoke();
        }
    }

    private struct Progress
    {
        public float Held;

        public int Clicks;
    }

    #region Effective AutoDraw

    [Theory]
    [InlineData(false, null, false)]  // Out of the box, nothing draws itself.
    [InlineData(false, true, true)]   // An explicit opt-in stands on its own, even against an off master.
    [InlineData(false, false, false)]
    [InlineData(true, null, true)]    // The master default is inherited.
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]  // An explicit opt-out wins over an on master.
    public void EffectiveAutoDraw_ResolvesAgainstTheMasterDefault(bool master, bool? perObject, bool expected)
    {
        NoireUI.AutoDraw = master;

        var drawable = new TestDrawable { AutoDraw = perObject };

        drawable.EffectiveAutoDraw.Should().Be(expected);
    }

    [Fact]
    public void EffectiveAutoDraw_FollowsTheMasterFlippedAtRuntime()
    {
        var drawable = new TestDrawable();

        NoireUI.AutoDraw = false;
        drawable.EffectiveAutoDraw.Should().BeFalse();

        NoireUI.AutoDraw = true;
        drawable.EffectiveAutoDraw.Should().BeTrue("a drawable that never decided for itself follows the master with no bookkeeping");
    }

    [Fact]
    public void TryAutoDraw_SkipsSomethingAlreadyDrawnManuallyThisFrame()
    {
        NoireUI.AutoDraw = true;
        var drawable = new TestDrawable();

        drawable.Draw();
        drawable.TryAutoDraw().Should().BeFalse("the frame stamp is what stops manual and automatic drawing doubling up");
        drawable.Draws.Should().Be(1);

        frame++;
        drawable.TryAutoDraw().Should().BeTrue();
        drawable.Draws.Should().Be(2);
    }

    [Fact]
    public void Draw_AlwaysWorks_EvenWithAutoDrawOff()
    {
        NoireUI.AutoDraw = false;
        var drawable = new TestDrawable { AutoDraw = false };

        drawable.Draw();

        drawable.Draws.Should().Be(1, "manual drawing is never gated by the automatic-drawing policy");
    }

    [Fact]
    public void Draw_AfterDispose_DoesNothing()
    {
        var drawable = new TestDrawable();
        drawable.Dispose();

        drawable.Draw();

        drawable.IsDisposed.Should().BeTrue();
        drawable.Draws.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithoutAnId_GeneratesOneAndFlagsIt()
    {
        var drawable = new TestDrawable(null!);

        drawable.HasGeneratedId.Should().BeTrue("a generated id changes every session, so nothing keyed on it may be persisted");
        drawable.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryGetPersistKey_WithAStableId_BuildsANamespacedKey()
    {
        new TestDrawable("main").TryGetKey("position", out var key).Should().BeTrue();

        key.Should().Be("TestDrawable.main.position");
    }

    [Fact]
    public void TryGetPersistKey_WithAGeneratedId_IsRefused()
    {
        var drawable = new TestDrawable(null!);

        drawable.TryGetKey("position", out var key)
            .Should().BeFalse("a generated id is a new GUID every session, so the entry could never be read back and the file would grow forever");
        key.Should().BeEmpty();
    }

    #endregion

    #region UiFrameState

    [Fact]
    public void Get_WithNoEntry_ReturnsTheFallback()
    {
        UiFrameState.Get("widget", "hover", 0.5f).Should().Be(0.5f);
        UiFrameState.TryGet<float>("widget", "hover", out _).Should().BeFalse();
    }

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        UiFrameState.Set("widget", "hover", 0.75f);

        UiFrameState.Get<float>("widget", "hover").Should().Be(0.75f);
    }

    [Fact]
    public void Entries_OfDifferentTypes_DoNotCollideOnTheSameKey()
    {
        UiFrameState.Set("widget", "state", 3);
        UiFrameState.Set("widget", "state", 9.5f);

        UiFrameState.Get<int>("widget", "state").Should().Be(3);
        UiFrameState.Get<float>("widget", "state").Should().Be(9.5f);
    }

    [Fact]
    public void Entries_OfDifferentSubKeys_AreSeparate()
    {
        UiFrameState.Set("widget", "hover", 1f);
        UiFrameState.Set("widget", "press", 2f);

        UiFrameState.Get<float>("widget", "hover").Should().Be(1f);
        UiFrameState.Get<float>("widget", "press").Should().Be(2f);
    }

    [Fact]
    public void Update_MutatesInPlaceAndWritesBack()
    {
        UiFrameState.Set("hold", "progress", new Progress { Held = 0.2f, Clicks = 1 });

        var updated = UiFrameState.Update<Progress>("hold", "progress", (ref Progress p) =>
        {
            p.Held += 0.3f;
            p.Clicks++;
        });

        updated.Held.Should().BeApproximately(0.5f, 1e-5f);
        UiFrameState.Get<Progress>("hold", "progress").Clicks.Should().Be(2);
    }

    [Fact]
    public void GetOrAdd_CallsTheFactoryOnlyOnce()
    {
        var calls = 0;

        UiFrameState.GetOrAdd("widget", "seed", () => { calls++; return 42; }).Should().Be(42);
        UiFrameState.GetOrAdd("widget", "seed", () => { calls++; return 99; }).Should().Be(42);

        calls.Should().Be(1);
    }

    [Fact]
    public void Remove_DropsTheEntry()
    {
        UiFrameState.Set("widget", "hover", 1f);

        UiFrameState.Remove<float>("widget", "hover").Should().BeTrue();
        UiFrameState.TryGet<float>("widget", "hover", out _).Should().BeFalse();
    }

    [Fact]
    public void Tick_PrunesEntriesLeftUntouched()
    {
        UiFrameState.PruneAfterFrames = 2;
        UiFrameState.PruneIntervalFrames = 0;

        UiFrameState.Set("stale", "value", 1f);

        frame = 10;
        UiFrameState.Tick(frame);

        UiFrameState.TryGet<float>("stale", "value", out _).Should().BeFalse("a widget that stopped drawing must not leave state behind");
    }

    [Fact]
    public void Tick_KeepsAnEntryThatWasRead()
    {
        UiFrameState.PruneAfterFrames = 2;
        UiFrameState.PruneIntervalFrames = 0;

        UiFrameState.Set("live", "value", 1f);

        frame = 10;
        UiFrameState.Get<float>("live", "value").Should().Be(1f);
        UiFrameState.Tick(frame);

        UiFrameState.TryGet<float>("live", "value", out var kept).Should().BeTrue("reading marks an entry as still in use");
        kept.Should().Be(1f);
    }

    [Fact]
    public void Clear_EmptiesEveryTypedStore()
    {
        UiFrameState.Set("a", "x", 1);
        UiFrameState.Set("b", "y", 1f);

        UiFrameState.Clear();

        UiFrameState.Count.Should().Be(0);
    }

    #endregion

    #region Draw queue

    [Fact]
    public void Post_WithoutADrawThread_RunsInline()
    {
        var pump = new UiDrawPump();
        var ran = false;

        pump.Post(() => ran = true);

        pump.InlineMode.Should().BeTrue();
        ran.Should().BeTrue("with no frame to marshal onto, running inline is the only option that runs the work at all");
    }

    [Fact]
    public void Drain_RunsQueuedActionsInOrder()
    {
        var pump = new UiDrawPump { ForceQueuedDelivery = true };
        var order = new List<int>();

        pump.Post(() => order.Add(1));
        pump.Post(() => order.Add(2));
        pump.Post(() => order.Add(3));

        order.Should().BeEmpty("nothing runs until the frame does");
        pump.Drain().Should().Be(3);
        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Drain_DoesNotRunWorkPostedWhileDraining()
    {
        var pump = new UiDrawPump { ForceQueuedDelivery = true };
        var runs = 0;

        pump.Post(() =>
        {
            runs++;
            pump.Post(() => runs++);
        });

        pump.Drain().Should().Be(1, "draining only the count snapshotted at entry is what stops a self-posting action stretching the frame forever");
        runs.Should().Be(1);

        pump.Drain().Should().Be(1);
        runs.Should().Be(2);
    }

    [Fact]
    public void Drain_KeepsGoingAfterAnActionThrows()
    {
        var pump = new UiDrawPump { ForceQueuedDelivery = true };
        var ran = false;

        pump.Post(() => throw new InvalidOperationException("boom"));
        pump.Post(() => ran = true);

        pump.Drain().Should().Be(2);
        ran.Should().BeTrue("one broken caller must not take the rest of the queue with it");
    }

    [Fact]
    public void Post_OverCapacity_DropsTheOldest()
    {
        var pump = new UiDrawPump { ForceQueuedDelivery = true, Capacity = 2 };
        var order = new List<int>();

        pump.Post(() => order.Add(1));
        pump.Post(() => order.Add(2));
        pump.Post(() => order.Add(3));

        pump.Drain();

        order.Should().Equal(new[] { 2, 3 }, "the queue is bounded, so the oldest work is what gives way");
        pump.DroppedCount.Should().Be(1);
    }

    #endregion
}
