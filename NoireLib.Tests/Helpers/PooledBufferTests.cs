using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the borrowed working buffer: that it is the length asked for, that it goes back exactly once however it
/// leaves scope, and that two buffers held at the same time are genuinely separate.
/// </summary>
/// <remarks>
/// The last two matter more than they look. Returning one array twice puts it in the pool twice, and the next two
/// renters then share it: the symptom is one surface's data appearing inside another's, far from the cause and
/// looking nothing like a pooling problem.
/// </remarks>
public class PooledBufferTests
{
    [Fact]
    public void ABufferIsExactlyTheLengthAskedFor()
    {
        // The pool hands over an array of at least the requested size, often larger. A span that exposed the whole
        // array would let a loop read whatever the previous borrower left past the end.
        using var buffer = PooledBuffer<Vector2>.Rent(3);

        buffer.Span.Length.Should().Be(3);
        buffer.Length.Should().Be(3);
    }

    [Fact]
    public void ABufferHoldsWhatIsWrittenIntoIt()
    {
        using var buffer = PooledBuffer<int>.Rent(4);

        for (var index = 0; index < buffer.Span.Length; index++)
            buffer.Span[index] = index * 2;

        buffer.Span.ToArray().Should().Equal(0, 2, 4, 6);
    }

    [Fact]
    public void AZeroLengthBufferIsAllowed()
    {
        using var buffer = PooledBuffer<Vector2>.Rent(0);

        buffer.Span.IsEmpty.Should().BeTrue();
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void ANegativeLengthIsRejected()
    {
        // Wrapped in a local function rather than a lambda: a ref struct cannot be the return of one.
        static void RentNegative()
        {
            using var buffer = PooledBuffer<Vector2>.Rent(-1);
        }

        var act = RentNegative;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GivingABufferBackTwiceDoesNothingTheSecondTime()
    {
        // The ordinary way to reach this is an explicit Dispose followed by the one `using` makes. If that returned
        // the array twice, the next two renters would share one array.
        var buffer = PooledBuffer<int>.Rent(8);

        buffer.Dispose();
        buffer.Dispose();

        buffer.Length.Should().Be(0);

        // Two fresh buffers must still be separate. They would not be if the pool were now holding the same array
        // under two slots.
        using var first = PooledBuffer<int>.Rent(8);
        using var second = PooledBuffer<int>.Rent(8);

        first.Span.Fill(1);
        second.Span.Fill(2);

        first.Span.ToArray().Should().Equal(1, 1, 1, 1, 1, 1, 1, 1);
    }

    [Fact]
    public void ASpanHeldPastDisposalWritesNowhere()
    {
        var buffer = PooledBuffer<int>.Rent(8);
        buffer.Dispose();

        // Empty rather than still pointing at an array somebody else now owns.
        buffer.Span.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TwoBuffersHeldAtOnceAreSeparate()
    {
        // The single-owner requirement, stated as behaviour: two surfaces drawing in the same frame each hold their
        // own working set, and writing through one must not be visible through the other.
        using var first = PooledBuffer<int>.Rent(4);
        using var second = PooledBuffer<int>.Rent(4);

        first.Span.Fill(1);
        second.Span.Fill(2);

        first.Span.ToArray().Should().Equal(1, 1, 1, 1);
        second.Span.ToArray().Should().Equal(2, 2, 2, 2);
    }

    [Fact]
    public void ABufferGoesBackWhenTheWorkThrows()
    {
        // `using` compiles to a try/finally, so a surface that faults mid-draw does not leak its buffer out of the
        // pool. A pool that leaks is a pool that allocates a fresh array on the next rent, every frame.
        static void Faulting()
        {
            using var buffer = PooledBuffer<int>.Rent(64);

            buffer.Span[0] = 1;

            throw new InvalidOperationException("drawing faulted");
        }

        var act = Faulting;
        act.Should().Throw<InvalidOperationException>();

        // The array is back in the pool, so renting the same size again is served from it rather than allocated.
        var before = GC.GetAllocatedBytesForCurrentThread();

        using (var reused = PooledBuffer<int>.Rent(64))
            reused.Span[0] = 2;

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0L);
    }

    [Fact]
    public void RentingAWarmBufferAllocatesNothing()
    {
        // The whole point. A fresh array per frame is garbage produced on the draw thread; a rented one is not.
        using (var warm = PooledBuffer<Vector2>.Rent(256))
            warm.Span[0] = Vector2.One;

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 256; index++)
        {
            using var buffer = PooledBuffer<Vector2>.Rent(256);
            buffer.Span[0] = new Vector2(index, index);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0L);
    }
}
