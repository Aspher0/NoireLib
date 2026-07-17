using System;
using System.Threading;

namespace NoireLib.Networker.Internal;

/// <summary>
/// Owns the named election mutex on a dedicated thread (kernel mutexes are thread-affine).<br/>
/// Acquiring the mutex IS becoming the hub; an abandoned mutex (dead hub) counts as acquired.
/// </summary>
internal sealed class ElectionMutex : IDisposable
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HolderJoinTimeout = TimeSpan.FromSeconds(2);

    private readonly object gate = new();
    private readonly string mutexName;
    private ManualResetEventSlim? acquireCompleted;
    private ManualResetEventSlim? releaseRequested;
    private Thread? holderThread;
    private bool disposed;

    public ElectionMutex(string mutexName)
    {
        this.mutexName = mutexName;
    }

    public bool IsHeld { get; private set; }

    /// <summary>
    /// Tries to acquire the election mutex without waiting. Returns true when this instance is now the hub.<br/>
    /// Returns false once disposed, so a caller that outlives its election mutex cannot take the role back.
    /// </summary>
    public bool TryAcquire()
    {
        lock (gate)
        {
            if (disposed)
                return false;

            if (IsHeld)
                return true;
        }

        // The holder thread signals both events for as long as it runs, so neither may be disposed before it has been
        // joined. Disposing one underneath a live thread faults it on a signal it is entitled to make, in a frame
        // where nothing can handle the exception.
        var acquireSignal = new ManualResetEventSlim(false);
        var releaseSignal = new ManualResetEventSlim(false);
        var acquired = false;

        var thread = new Thread(() =>
        {
            Mutex? mutex = null;

            try
            {
                mutex = new Mutex(initiallyOwned: false, mutexName, out _);

                try
                {
                    Volatile.Write(ref acquired, mutex.WaitOne(0));
                }
                catch (AbandonedMutexException)
                {
                    // The previous hub died while holding the mutex - ownership transferred to us.
                    Volatile.Write(ref acquired, true);
                }

                acquireSignal.Set();

                if (!Volatile.Read(ref acquired))
                    return;

                // Hold ownership on this thread until release is requested.
                releaseSignal.Wait();

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
                // Report the failed attempt so the caller stops waiting, without ever throwing out of this frame:
                // an unhandled exception on a background thread terminates the process.
                try
                {
                    acquireSignal.Set();
                }
                catch
                {
                    // Best effort.
                }
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

        // A wait that times out leaves the thread's progress unknown, so the attempt counts as failed and the thread is
        // asked to unwind: any mutex it did take is released as soon as it reaches the request, freeing the role for
        // the next contender.
        if (!acquireSignal.Wait(AcquireTimeout) || !Volatile.Read(ref acquired))
        {
            releaseSignal.Set();
            JoinAndDispose(thread, acquireSignal, releaseSignal);
            return false;
        }

        lock (gate)
        {
            if (!disposed)
            {
                holderThread = thread;
                acquireCompleted = acquireSignal;
                releaseRequested = releaseSignal;
                IsHeld = true;
                return true;
            }
        }

        // Disposal landed while this attempt was in flight. The role was taken but nothing will ever use it, so it is
        // handed straight back rather than published, which is what keeps a disposed election mutex from stranding the
        // role on a thread nobody will ask to release.
        releaseSignal.Set();
        JoinAndDispose(thread, acquireSignal, releaseSignal);
        return false;
    }

    /// <summary>
    /// Releases the mutex if held, allowing another instance to become the hub.
    /// </summary>
    public void Release()
    {
        Thread? thread;
        ManualResetEventSlim? acquireSignal;
        ManualResetEventSlim? releaseSignal;

        // Taking ownership of the holder state under the gate is what makes a release concurrent with an acquire safe:
        // exactly one of the two publishes the thread, and exactly one unwinds it.
        lock (gate)
        {
            if (!IsHeld)
                return;

            IsHeld = false;

            thread = holderThread;
            acquireSignal = acquireCompleted;
            releaseSignal = releaseRequested;

            holderThread = null;
            acquireCompleted = null;
            releaseRequested = null;
        }

        if (thread == null || acquireSignal == null || releaseSignal == null)
            return;

        releaseSignal.Set();
        JoinAndDispose(thread, acquireSignal, releaseSignal);
    }

    /// <summary>
    /// Releases the role and refuses any further acquisition.<br/>
    /// Safe to call while another thread is acquiring: the in-flight attempt observes the disposal and hands the role back.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
        }

        Release();
    }

    /// <summary>
    /// Waits for the holder thread to exit, then disposes the events it signals.<br/>
    /// A thread that outlives the join keeps its events instead: their handles are then reclaimed by finalization,
    /// which costs a collection, where disposing them out from under the thread would fault it.
    /// </summary>
    private static void JoinAndDispose(Thread thread, ManualResetEventSlim acquireSignal, ManualResetEventSlim releaseSignal)
    {
        if (!thread.Join(HolderJoinTimeout))
            return;

        acquireSignal.Dispose();
        releaseSignal.Dispose();
    }
}
