using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Remote;

/// <summary>
/// The handle a publication hands back. Disposing it removes the members it published.
/// </summary>
public sealed class NoireRemotePublication : IDisposable
{
    private readonly Action<NoireRemotePublication> disposeAction;
    private int disposeState;

    internal NoireRemotePublication(
        string name,
        IReadOnlyList<NoireRemoteMemberInfo> members,
        Action<NoireRemotePublication> disposeAction,
        IReadOnlyList<NoireRemoteEventInfo>? events = null)
    {
        Name = name;
        Members = members;
        Events = events;
        this.disposeAction = disposeAction;
    }

    /// <summary>
    /// Gets the endpoint name the members were published under.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the members this publication added.
    /// </summary>
    public IReadOnlyList<NoireRemoteMemberInfo> Members { get; }

    /// <summary>
    /// Gets the events this publication attached, or null when the type declared none.
    /// </summary>
    public IReadOnlyList<NoireRemoteEventInfo>? Events { get; }

    /// <summary>
    /// Gets whether the publication has already been disposed.
    /// </summary>
    public bool IsDisposed => disposeState != 0;

    /// <summary>
    /// Removes the published members. Calling it is optional: NoireRemote removes every tracked publication when the
    /// library disposes.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        if (Events != null)
        {
            foreach (var declared in Events)
                declared.Detach();
        }

        disposeAction(this);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The publications made by one call, such as a whole assembly scan.
/// </summary>
public sealed class NoireRemoteGroup : IReadOnlyList<NoireRemotePublication>, IDisposable
{
    private readonly IReadOnlyList<NoireRemotePublication> publications;

    internal NoireRemoteGroup(IReadOnlyList<NoireRemotePublication> publications)
    {
        this.publications = publications;
    }

    /// <summary>
    /// Gets the number of publications in the group.
    /// </summary>
    public int Count => publications.Count;

    /// <summary>
    /// Gets the publication at an index.
    /// </summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The publication at that index.</returns>
    public NoireRemotePublication this[int index] => publications[index];

    /// <summary>
    /// Returns an enumerator over the publications.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<NoireRemotePublication> GetEnumerator()
        => publications.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <summary>
    /// Disposes every publication in the group.
    /// </summary>
    public void Dispose()
    {
        foreach (var publication in publications)
            publication.Dispose();
    }
}
