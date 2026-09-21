using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// Issue, await, dispatch, re-issue. The poll and the wait are delegates, testable without a server or a clock.
internal sealed class LongPollLoop
{
    private readonly Func<string?, CancellationToken, Task<NoireLongPollResponse>> poll;
    private readonly Func<NoireLongPollResponse, string?> readCursor;
    private readonly Action<NoireLongPollResponse> deliver;
    private readonly Action<Exception> fail;
    private readonly Action<NoireSocketState> setState;
    private readonly Func<TimeSpan, CancellationToken, Task> wait;
    private readonly NoireRetryPolicy retry;
    private readonly TimeSpan emptyPollDelay;

    public LongPollLoop(
        NoireRetryPolicy retry,
        TimeSpan emptyPollDelay,
        string? initialCursor,
        Func<string?, CancellationToken, Task<NoireLongPollResponse>> poll,
        Func<NoireLongPollResponse, string?> readCursor,
        Action<NoireLongPollResponse> deliver,
        Action<Exception> fail,
        Action<NoireSocketState> setState,
        Func<TimeSpan, CancellationToken, Task> wait)
    {
        this.retry = retry;
        this.emptyPollDelay = emptyPollDelay;
        this.poll = poll;
        this.readCursor = readCursor;
        this.deliver = deliver;
        this.fail = fail;
        this.setState = setState;
        this.wait = wait;

        Cursor = initialCursor;
    }

    public string? Cursor { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = new BackoffClock(retry);

        while (!cancellationToken.IsCancellationRequested)
        {
            NoireLongPollResponse response;

            try
            {
                response = await poll(Cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!await BackoffAsync(backoff, exception, cancellationToken).ConfigureAwait(false))
                    return;

                continue;
            }

            if (IsFatal(response.Status))
            {
                // A rejected credential, an unknown route or a refused cursor does not improve with another poll.
                fail(Refused(response));
                setState(NoireSocketState.Faulted);
                return;
            }

            if (response.Status >= 400)
            {
                if (!await BackoffAsync(backoff, Refused(response), cancellationToken).ConfigureAwait(false))
                    return;

                continue;
            }

            var next = readCursor(response);

            if (next != null)
                Cursor = next;

            setState(NoireSocketState.Connected);
            deliver(response);

            if (response.IsEmpty)
            {
                // An empty answer leaves the failure schedule where it is.
                if (emptyPollDelay > TimeSpan.Zero && !await WaitAsync(emptyPollDelay, cancellationToken).ConfigureAwait(false))
                    return;
            }
            else
            {
                backoff.Reset();
            }
        }
    }

    // 408 and 429 ask to be retried. Every other 4xx is a refusal.
    private static bool IsFatal(int status)
        => status is >= 400 and < 500 and not 408 and not 429;

    private static NoireSocketConnectException Refused(NoireLongPollResponse response)
        => new("The poll was refused with status " + response.Status + ".", (HttpStatusCode)response.Status, response.Headers);

    private async Task<bool> BackoffAsync(BackoffClock backoff, Exception exception, CancellationToken cancellationToken)
    {
        fail(exception);

        if (!backoff.ShouldRetry)
        {
            setState(NoireSocketState.Faulted);
            return false;
        }

        setState(NoireSocketState.Reconnecting);
        return await WaitAsync(backoff.Next(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await wait(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return !cancellationToken.IsCancellationRequested;
    }
}
