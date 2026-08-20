using NoireLib.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// A <see cref="TimingHelperBase"/> whose interval is a number of game frames, counted off
/// <see cref="FrameClock"/>. This is the base of <see cref="FrameThrottler"/> and <see cref="FrameDebouncer"/>.
/// </summary>
public abstract class FrameTimingHelperBase : TimingHelperBase
{
    /// <summary>
    /// The interval in game frames.
    /// </summary>
    protected long _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameTimingHelperBase"/> class.
    /// </summary>
    /// <param name="frames">The interval in game frames.</param>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    protected FrameTimingHelperBase(long frames)
    {
        if (frames < 1)
            throw new ArgumentException("Frame count must be at least one.", nameof(frames));

        _frames = frames;
    }

    /// <inheritdoc/>
    protected override long CurrentTick => FrameClock.Current;

    /// <inheritdoc/>
    protected override long IntervalTicks => _frames;

    /// <summary>
    /// Gets the current interval in game frames.
    /// </summary>
    /// <returns>The interval in frames.</returns>
    public long GetFrames()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _frames;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets a new interval in game frames. This does not affect any currently running operation.
    /// </summary>
    /// <param name="frames">The new interval in game frames.</param>
    /// <exception cref="ArgumentException">Thrown when the frame count is less than one.</exception>
    public void SetFrames(long frames)
    {
        ThrowIfDisposed();

        if (frames < 1)
            throw new ArgumentException("Frame count must be at least one.", nameof(frames));

        _lock.Wait();
        try
        {
            _frames = frames;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    protected override async Task<bool> TryWaitAsync(CancellationTokenSource cts)
    {
        var target = FrameClock.Current + _frames;

        // Reading Current above attaches the clock, so this answers whether a game update is behind it. Without
        // one the count never moves, and waiting on it would never return; the wait resolves inline instead,
        // which is what lets a frame helper be driven from a test.
        if (!FrameClock.IsRunning)
            return !cts.IsCancellationRequested;

        try
        {
            // Asking for the whole remainder rather than one frame at a time, then re-reading in case the clock
            // moved further while the continuation was queued.
            while (true)
            {
                var remaining = target - FrameClock.Current;

                if (remaining <= 0)
                    return true;

                await AsyncHelper.DelayFramesAsync((int)Math.Min(remaining, int.MaxValue), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
