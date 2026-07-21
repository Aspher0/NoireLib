using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace NoireLib.Helpers;

/// <summary>
/// A working buffer borrowed from the runtime's array pool and given back when it leaves scope, for the per-frame
/// working sets that are sized by data rather than by a constant.
/// </summary>
/// <remarks>
/// The stack is the first answer for a working set, and most of the drawing code takes it: a path buffer with a known
/// maximum is <c>stackalloc</c> and costs nothing. That fails as soon as the size comes from the data, because a table
/// with a thousand rows or a combo with a long option list would overflow the stack. Those rent instead, and renting
/// beats allocating for the same reason the stack does: a fresh array per frame is garbage produced on the one thread
/// a plugin cannot afford a collection on.<br/>
/// A <see langword="ref struct"/>, which is doing two jobs. It cannot be boxed or captured, and it cannot be stored in
/// a field, so a rented buffer <b>cannot</b> be retained past the frame that rented it: that is a compile error rather
/// than a rule to remember. It also pairs with <see langword="using"/>, so the buffer goes back even when the drawing
/// throws.<br/>
/// <b>Give a buffer back exactly once.</b> Returning the same array twice puts it in the pool twice, and the next two
/// renters then share one array, which is a corruption bug that surfaces far from its cause and does not look like a
/// pooling problem. <see cref="Dispose"/> is idempotent so that the ordinary mistake, an explicit call followed by the
/// one <see langword="using"/> makes, cannot do it.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
/// <example>
/// <code>
/// using var buffer = PooledBuffer&lt;Vector2&gt;.Rent(rows.Count);
/// var positions = buffer.Span;
///
/// for (var index = 0; index &lt; rows.Count; index++)
///     positions[index] = Place(rows[index]);
/// </code>
/// </example>
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
    /// <remarks>
    /// The pool may hand over a longer array than asked for, which is why <see cref="Span"/> is the one to write
    /// through: it is exactly the requested length, so a loop bounded by the span cannot read whatever the previous
    /// borrower left past the end.
    /// </remarks>
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
    /// <remarks>
    /// Empty once the buffer has been given back, so writing through a span held past disposal writes nowhere rather
    /// than into an array somebody else is now using.
    /// </remarks>
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
