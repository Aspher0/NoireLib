using System;
using System.Threading;

namespace NoireLib.Networker.Internal;

/// <summary>
/// Owns the named election mutex on a dedicated thread (kernel mutexes are thread-affine).<br/>
/// Acquiring the mutex IS becoming the hub; an abandoned mutex (dead hub) counts as acquired.
/// </summary>
internal sealed class ElectionMutex : IDisposable
{
    private readonly string mutexName;
    private ManualResetEventSlim? releaseRequested;
    private Thread? holderThread;

    public ElectionMutex(string mutexName)
    {
        this.mutexName = mutexName;
    }

    public bool IsHeld { get; private set; }

    /// <summary>
    /// Tries to acquire the election mutex without waiting. Returns true when this instance is now the hub.
    /// </summary>
    public bool TryAcquire()
    {
        if (IsHeld)
            return true;

        using var acquireResult = new ManualResetEventSlim(false);
        var acquired = false;
        var release = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            Mutex? mutex = null;

            try
            {
                mutex = new Mutex(initiallyOwned: false, mutexName, out _);

                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // The previous hub died while holding the mutex - ownership transferred to us.
                    acquired = true;
                }

                acquireResult.Set();

                if (!acquired)
                    return;

                // Hold ownership on this thread until release is requested.
                release.Wait();

                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                    // Best effort.
                }
            }
            catch
            {
                acquireResult.Set();
            }
            finally
            {
                mutex?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "NoireNetworker.ElectionMutex",
        };

        thread.Start();
        acquireResult.Wait(TimeSpan.FromSeconds(5));

        if (!acquired)
        {
            release.Set();
            release.Dispose();
            return false;
        }

        holderThread = thread;
        releaseRequested = release;
        IsHeld = true;
        return true;
    }

    /// <summary>
    /// Releases the mutex if held, allowing another instance to become the hub.
    /// </summary>
    public void Release()
    {
        if (!IsHeld)
            return;

        IsHeld = false;
        releaseRequested?.Set();
        holderThread?.Join(TimeSpan.FromSeconds(2));
        releaseRequested?.Dispose();
        releaseRequested = null;
        holderThread = null;
    }

    public void Dispose()
        => Release();
}
