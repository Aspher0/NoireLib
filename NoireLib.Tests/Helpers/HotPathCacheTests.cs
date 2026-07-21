using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the cache the draw path reads: that it answers, that it forgets when it should, that it stays bounded, and
/// that a hit costs nothing.
/// </summary>
/// <remarks>
/// The last of those is the whole reason this type exists rather than one of the other two cache helpers, so it is
/// asserted as behaviour rather than assumed from the implementation.
/// </remarks>
public class HotPathCacheTests
{
    /// <summary>
    /// A key of the shape this is built for: everything the value depends on, in one struct the compiler writes
    /// equality for.
    /// </summary>
    private readonly record struct LayoutKey(string Text, float Width, float Scale, int Generation);

    private static HotPathCache<LayoutKey, Vector2> Cache(int capacity = HotPathCache<LayoutKey, Vector2>.DefaultCapacity)
        => new(capacity);

    private static LayoutKey Key(string text = "Save", float width = 120f, float scale = 1f, int generation = 0)
        => new(text, width, scale, generation);

    [Fact]
    public void AValueThatWasStored_IsFoundAgain()
    {
        var cache = Cache();
        var key = Key();

        cache.Set(key, new Vector2(40f, 16f));

        cache.TryGet(key, out var size).Should().BeTrue();
        size.Should().Be(new Vector2(40f, 16f));
    }

    [Fact]
    public void AValueThatWasNeverStored_IsAMiss()
    {
        var cache = Cache();

        cache.TryGet(Key(), out var size).Should().BeFalse();
        size.Should().Be(default(Vector2));
    }

    [Fact]
    public void KeysDifferingInAnyField_AreDifferentEntries()
    {
        // The point of a composite key is that every input is part of it. A key that ignored one would serve a value
        // measured under a different scale, theme or font, which reads as a layout bug rather than a caching one.
        var cache = Cache();

        cache.Set(Key(), Vector2.One);

        cache.TryGet(Key(text: "Cancel"), out _).Should().BeFalse();
        cache.TryGet(Key(width: 121f), out _).Should().BeFalse();
        cache.TryGet(Key(scale: 1.25f), out _).Should().BeFalse();
        cache.TryGet(Key(generation: 1), out _).Should().BeFalse();
    }

    [Fact]
    public void StoringTwiceUnderOneKey_KeepsTheSecondValue()
    {
        var cache = Cache();

        cache.Set(Key(), Vector2.One);
        cache.Set(Key(), Vector2.Zero);

        cache.TryGet(Key(), out var size).Should().BeTrue();
        size.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void ReachingTheBound_StartsOverRatherThanGrowing()
    {
        var cache = Cache(capacity: 8);

        for (var index = 0; index < 64; index++)
            cache.Set(Key(text: index.ToString()), Vector2.One);

        // The bound is a bound, not a budget: what it protects against is a key that differs every frame, which would
        // otherwise grow the dictionary forever without ever hitting.
        cache.Count.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public void TheInvalidationToken_ForgetsEverythingWhenItMoves()
    {
        var cache = Cache();

        cache.InvalidateIfChanged(1);
        cache.Set(Key(), Vector2.One);

        cache.InvalidateIfChanged(2).Should().BeTrue();
        cache.TryGet(Key(), out _).Should().BeFalse();
    }

    [Fact]
    public void TheInvalidationToken_KeepsEverythingWhenItHoldsStill()
    {
        var cache = Cache();

        cache.InvalidateIfChanged(1);
        cache.Set(Key(), Vector2.One);

        cache.InvalidateIfChanged(1).Should().BeFalse();
        cache.TryGet(Key(), out _).Should().BeTrue();
    }

    [Fact]
    public void TheFirstInvalidationCall_RecordsTheTokenWithoutReportingAChange()
    {
        // Nothing has been stored under an earlier token, so there is nothing stale to drop and nothing to report.
        var cache = Cache();

        cache.InvalidateIfChanged(7).Should().BeFalse();

        cache.Set(Key(), Vector2.One);
        cache.InvalidateIfChanged(7).Should().BeFalse();
        cache.TryGet(Key(), out _).Should().BeTrue();
    }

    [Fact]
    public void ClearingDoesNotMakeTheNextInvalidationReportAChangeThatDidNotHappen()
    {
        var cache = Cache();

        cache.InvalidateIfChanged(3);
        cache.Set(Key(), Vector2.One);
        cache.Clear();

        cache.InvalidateIfChanged(3).Should().BeFalse();
    }

    [Fact]
    public void RemovingOneEntry_LeavesTheRest()
    {
        var cache = Cache();

        cache.Set(Key(text: "Save"), Vector2.One);
        cache.Set(Key(text: "Cancel"), Vector2.Zero);

        cache.Remove(Key(text: "Save")).Should().BeTrue();
        cache.Remove(Key(text: "Save")).Should().BeFalse();

        cache.TryGet(Key(text: "Cancel"), out _).Should().BeTrue();
    }

    [Fact]
    public void HitsAndMissesAreCounted()
    {
        var cache = Cache();

        cache.TryGet(Key(), out _);
        cache.Set(Key(), Vector2.One);
        cache.TryGet(Key(), out _);
        cache.TryGet(Key(), out _);

        cache.Hits.Should().Be(2L);
        cache.Misses.Should().Be(1L);
    }

    [Fact]
    public void ANonPositiveCapacityIsRejected()
    {
        var zero = () => new HotPathCache<LayoutKey, Vector2>(0);
        var negative = () => new HotPathCache<LayoutKey, Vector2>(-1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AHitAllocatesNothing()
    {
        var cache = Cache();
        var key = Key();

        cache.Set(key, new Vector2(40f, 16f));

        // Warmed first, so the dictionary's own growth and any jitting are outside the measurement.
        for (var index = 0; index < 128; index++)
            cache.TryGet(key, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1024; index++)
            cache.TryGet(key, out _);

        var after = GC.GetAllocatedBytesForCurrentThread();

        // This is the reason the type exists. A string-keyed cache would allocate a key per lookup and an object-valued
        // one would box the Vector2 on the way in, and either would put those bytes on the draw thread every frame.
        (after - before).Should().Be(0L);
    }

    [Fact]
    public void AStoreAllocatesNothingOnceTheDictionaryHasRoom()
    {
        var cache = Cache();
        var key = Key();

        cache.Set(key, Vector2.One);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1024; index++)
            cache.Set(key, new Vector2(index, index));

        var after = GC.GetAllocatedBytesForCurrentThread();

        // Replacing a value must not box it. Growth is not measured here: a cache that is still growing is one that has
        // not filled yet, and filling once is the expected shape.
        (after - before).Should().Be(0L);
    }
}
