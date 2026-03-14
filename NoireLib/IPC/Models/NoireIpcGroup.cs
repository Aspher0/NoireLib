using System;
using System.Collections;
using System.Collections.Generic;

namespace NoireLib.IPC;

/// <summary>
/// Represents a collection of IPC handles created together.
/// </summary>
public sealed class NoireIpcGroup : IReadOnlyList<NoireIpcHandle>, IDisposable
{
    private readonly IReadOnlyList<NoireIpcHandle> _handles;

    internal NoireIpcGroup(IReadOnlyList<NoireIpcHandle> handles)
    {
        _handles = handles;
    }

    /// <summary>
    /// Gets the number of handles in the group.
    /// </summary>
    /// <returns>The number of handles in the group.</returns>
    public int Count => _handles.Count;

    /// <summary>
    /// Gets the handle at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the handle to retrieve.</param>
    /// <returns>The handle at the specified index.</returns>
    public NoireIpcHandle this[int index] => _handles[index];

    /// <summary>
    /// Returns a generic enumerator for the handles in the group.
    /// </summary>
    /// <returns>An enumerator for the handles in the group.</returns>
    public IEnumerator<NoireIpcHandle> GetEnumerator()
        => _handles.GetEnumerator();

    /// <summary>
    /// Returns a non-generic enumerator for the handles in the group.
    /// </summary>
    /// <returns>A non-generic enumerator for the handles in the group.</returns>
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <summary>
    /// Disposes every handle in the group immediately.
    /// </summary>
    /// <remarks>
    /// Calling this method is optional. <see cref="NoireIPC"/> automatically disposes every tracked handle when <see cref="NoireLibMain.Dispose()"/> runs.
    /// </remarks>
    public void Dispose()
    {
        foreach (var handle in _handles)
            handle.Dispose();
    }
}
