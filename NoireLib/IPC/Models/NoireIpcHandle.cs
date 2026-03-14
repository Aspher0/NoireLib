using System;
using System.Threading;

namespace NoireLib.IPC;

/// <summary>
/// Represents a tracked IPC handle created by <see cref="NoireIPC"/>.
/// </summary>
public abstract class NoireIpcHandle : IDisposable
{
    private readonly Action _disposeAction;
    private readonly Action<NoireIpcHandle>? _disposedCallback;
    private int _disposeState;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcHandle"/> class.
    /// </summary>
    /// <param name="fullName">The fully qualified IPC channel name associated with the handle.</param>
    /// <param name="disposeAction">The action executed when the handle is disposed.</param>
    /// <param name="disposedCallback">The callback invoked after the handle has been disposed.</param>
    protected NoireIpcHandle(string fullName, Action disposeAction, Action<NoireIpcHandle>? disposedCallback)
    {
        FullName = fullName;
        _disposeAction = disposeAction;
        _disposedCallback = disposedCallback;
    }

    /// <summary>
    /// Gets the fully qualified IPC channel name associated with the handle.
    /// </summary>
    /// <returns>The fully qualified IPC channel name.</returns>
    public string FullName { get; }

    /// <summary>
    /// Gets a value indicating whether the handle has already been disposed.
    /// </summary>
    /// <returns><see langword="true"/> when the handle has already been disposed; otherwise, <see langword="false"/>.</returns>
    public bool IsDisposed => _disposeState != 0;

    /// <summary>
    /// Disposes the handle and unregisters or unsubscribes it immediately.
    /// </summary>
    /// <remarks>
    /// Calling this method is optional. <see cref="NoireIPC"/> automatically disposes every tracked handle when <see cref="NoireLibMain.Dispose()"/> runs.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            _disposeAction();
        }
        finally
        {
            _disposedCallback?.Invoke(this);
            GC.SuppressFinalize(this);
        }
    }
}
