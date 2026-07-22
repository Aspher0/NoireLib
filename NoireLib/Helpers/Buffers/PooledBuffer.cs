using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace NoireLib.Helpers;

/// <summary>
/// A working buffer borrowed from the runtime's array pool and given back when it leaves scope, for the per-frame
/// working sets that are sized by data rather than by a constant.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public ref struct PooledBuffer<T>
{
    /// <summary>
    /// The array the pool handed over, or <see langword="null"/> once it has been given back.
    /// </summary>
    /// <remarks>
    /// Cleared on return, which is what makes a second <see cref="Dispose"/> do nothing rather than return the same
    /// array again.
    /// </remarks>
    private T[]? rented;

    private readonly int length;

    private PooledBuffer(T[] rented, int length)
    {
        this.rented = rented;
        this.length = length;
    }

    /// <summary>
    /// Borrows a buffer of at least the requested length.
    /// </summary>
    /// <param name="length">How many elements are needed. Zero is allowed and yields an empty buffer.</param>
    /// <returns>The borrowed buffer. Dispose it to give the array back.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is negative.</exception>
    public static PooledBuffer<T> Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        return length == 0
            ? new PooledBuffer<T>(Array.Empty<T>(), 0)
            : new PooledBuffer<T>(ArrayPool<T>.Shared.Rent(length), length);
    }

    /// <summary>
    /// The buffer, exactly as long as was asked for.
    /// </summary>
    public readonly Span<T> Span => rented == null ? Span<T>.Empty : rented.AsSpan(0, length);

    /// <summary>
    /// How many elements the buffer holds.
    /// </summary>
    public readonly int Length => rented == null ? 0 : length;

    /// <summary>
    /// Gives the array back. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// A buffer holding references is cleared on the way back, so the pool does not keep the last frame's objects
    /// alive until the array is rented again. A buffer of plain values is not, since clearing it would be work spent
    /// on data the next renter overwrites.
    /// </remarks>
    public void Dispose()
    {
        var array = rented;

        if (array == null)
            return;

        rented = null;

        if (array.Length > 0)
            ArrayPool<T>.Shared.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }
}
