using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// A <see cref="TimingHelperBase"/> whose interval is a <see cref="TimeSpan"/>, counted in milliseconds off the
/// process clock. This is the base of <see cref="NoireLib.Helpers.Throttler"/> and
/// <see cref="NoireLib.Helpers.Debouncer"/>.
/// </summary>
public abstract class TimeTimingHelperBase : TimingHelperBase
{
    /// <summary>
    /// The TimeSpan delay associated with the timing helper.
    /// </summary>
    protected TimeSpan _delay;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeTimingHelperBase"/> class.
    /// </summary>
    /// <param name="delay">The delay as a <see cref="TimeSpan"/>.</param>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    protected TimeTimingHelperBase(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delay));

        _delay = delay;
    }

    /// <inheritdoc/>
    protected override long CurrentTick => Environment.TickCount64;

    /// <inheritdoc/>
    protected override long IntervalTicks => (long)_delay.TotalMilliseconds;

    /// <summary>
    /// Gets the current delay.
    /// </summary>
    /// <returns>The current delay as a <see cref="TimeSpan"/>.</returns>
    public TimeSpan GetDelay()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _delay;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets a new delay. This does not affect any currently running operation.
    /// </summary>
    /// <param name="delay">The new delay as a <see cref="TimeSpan"/>.</param>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public void SetDelay(TimeSpan delay)
    {
        ThrowIfDisposed();

        if (delay <= TimeSpan.Zero)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delay));

        _lock.Wait();
        try
        {
            _delay = delay;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    protected override async Task<bool> TryWaitAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay((int)_delay.TotalMilliseconds, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
