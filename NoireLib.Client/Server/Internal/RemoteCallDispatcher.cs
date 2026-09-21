using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// The readiness gate, the thread, and the two bounds that keep a listener from stalling a game process.
internal sealed class RemoteCallDispatcher : IDisposable
{
    private readonly SemaphoreSlim slots;
    private readonly INoireRemoteHost host;
    private readonly int maxQueued;
    private int waiting;

    public RemoteCallDispatcher(NoireRemoteOptions options, INoireRemoteHost host)
    {
        this.host = host;
        slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentCalls));
        maxQueued = Math.Max(1, options.MaxQueuedCalls);
    }

    public static HttpFailure? ToFailure(NoireRemoteReadinessResult result)
        => result.IsReady
            ? null
            : new HttpFailure(503, NoireRemoteErrorCodes.NotReady, result.Reason) { RetryAfterSeconds = result.RetryAfterSeconds };

    public async Task<object?> InvokeAsync(NoireRemoteMemberInfo member, object?[] arguments, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref waiting) > maxQueued)
        {
            Interlocked.Decrement(ref waiting);
            throw new HttpBusyException();
        }

        try
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref waiting);
            throw;
        }

        Interlocked.Decrement(ref waiting);

        try
        {
            return await RunAsync(member, arguments, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A member running at dispose releases a semaphore that is gone. An escaping exception on a pool thread kills the process.
            try
            {
                slots.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task<object?> RunAsync(NoireRemoteMemberInfo member, object?[] arguments, CancellationToken cancellationToken)
    {
        // With no host thread the member runs on the calling thread, like every call on a standalone host.
        var hop = member.Thread == NoireRemoteThread.Framework && host.HasHostThread;

        if (hop)
        {
            // The gate's answer is only true on the thread the member runs on.
            return await host.RunOnHostThreadAsync(async () =>
            {
                Guard(member);
                return await member.Invoker(arguments, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        if (member.Requires != NoireRemoteReadiness.None && host.HasHostThread)
        {
            // Only the host thread can answer readiness.
            var failure = await host.RunOnHostThreadAsync(() =>
                Task.FromResult(ToFailure(host.CheckReadiness(member.Requires, member.Route)))).ConfigureAwait(false);

            if (failure != null)
                throw new HttpNotReadyException(failure);
        }
        else
        {
            Guard(member);
        }

        return await member.Invoker(arguments, cancellationToken).ConfigureAwait(false);
    }

    private void Guard(NoireRemoteMemberInfo member)
    {
        var failure = ToFailure(host.CheckReadiness(member.Requires, member.Route));

        if (failure != null)
            throw new HttpNotReadyException(failure);
    }

    public void Dispose()
        => slots.Dispose();
}

internal sealed class HttpBusyException : Exception
{
    public HttpBusyException() : base("The call queue is full.")
    {
    }
}

// The gate is decided on the host thread.
internal sealed class HttpNotReadyException(HttpFailure failure) : Exception(failure.Message)
{
    public HttpFailure Failure { get; } = failure;
}
