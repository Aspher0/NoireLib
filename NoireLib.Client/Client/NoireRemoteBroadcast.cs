using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// What one call sent to several listeners produced. Every target appears, in the order it was listed, whether it
/// answered or not.<br/>
/// A broadcast is not a transaction. Three clients teleporting and a fourth failing leaves three clients teleported.
/// </summary>
/// <typeparam name="TResult">The type each return value converts into.</typeparam>
public sealed class NoireRemoteBroadcast<TResult> : IReadOnlyList<NoireRemoteOutcome<TResult>>
{
    internal NoireRemoteBroadcast(IReadOnlyList<NoireRemoteOutcome<TResult>> outcomes, TimeSpan elapsed)
    {
        Outcomes = outcomes;
        Elapsed = elapsed;
    }

    /// <summary>
    /// Gets one row per target, in the order the targets were listed.
    /// </summary>
    public IReadOnlyList<NoireRemoteOutcome<TResult>> Outcomes { get; }

    /// <summary>
    /// Gets how long the whole broadcast took.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets how many targets answered.
    /// </summary>
    public int Answered
    {
        get
        {
            var count = 0;

            foreach (var outcome in Outcomes)
            {
                if (outcome.Ok)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Gets how many targets failed.
    /// </summary>
    public int Failed => Outcomes.Count - Answered;

    /// <summary>
    /// Gets whether every target answered.
    /// </summary>
    public bool AllAnswered => Failed == 0;

    /// <summary>
    /// Gets the number of targets.
    /// </summary>
    public int Count => Outcomes.Count;

    /// <summary>
    /// Gets the outcome at an index.
    /// </summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The outcome.</returns>
    public NoireRemoteOutcome<TResult> this[int index] => Outcomes[index];

    /// <summary>
    /// Gets the results of the targets that answered, in order.
    /// </summary>
    public IReadOnlyList<TResult> Values
    {
        get
        {
            var values = new List<TResult>(Outcomes.Count);

            foreach (var outcome in Outcomes)
            {
                if (outcome.Ok)
                    values.Add(outcome.Result!);
            }

            return values;
        }
    }

    /// <summary>
    /// Throws when any target failed, carrying the whole broadcast on the exception.
    /// </summary>
    /// <returns>This broadcast, for chaining.</returns>
    /// <exception cref="NoireRemoteBroadcastException">If at least one target failed.</exception>
    public NoireRemoteBroadcast<TResult> ThrowIfAnyFailed()
    {
        if (AllAnswered)
            return this;

        throw new NoireRemoteBroadcastException(ToString(), this);
    }

    /// <summary>
    /// Returns an enumerator over the outcomes.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<NoireRemoteOutcome<TResult>> GetEnumerator()
        => Outcomes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <summary>
    /// Returns how many targets answered, how long it took and a line per failure.
    /// </summary>
    /// <returns>A description, one line plus one per failure.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();

        text.Append(Answered.ToString(CultureInfo.InvariantCulture));
        text.Append(" of ").Append(Outcomes.Count.ToString(CultureInfo.InvariantCulture));
        text.Append(" answered in ").Append(((long)Elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms");

        foreach (var outcome in Outcomes)
        {
            if (outcome.Ok)
                continue;

            text.Append(Environment.NewLine).Append("  ").Append(outcome.Instance).Append(": ");
            text.Append(outcome.Error?.Message ?? "failed with no message");
        }

        return text.ToString();
    }

    internal NoireRemoteBroadcastWire ToWire()
    {
        var rows = new List<NoireRemoteBroadcastRow>(Outcomes.Count);

        foreach (var outcome in Outcomes)
        {
            rows.Add(new NoireRemoteBroadcastRow
            {
                Instance = outcome.Instance.Id,
                Label = outcome.Instance.Label,
                Machine = outcome.Instance.Machine,
                Address = outcome.Instance.Address,
                Port = outcome.Instance.Port,
                Ok = outcome.Ok,
                ElapsedMs = (long)outcome.Elapsed.TotalMilliseconds,
                Result = outcome.Envelope?.Result,
                Error = outcome.Envelope?.Error ?? (outcome.Error == null ? null : new NoireRemoteError
                {
                    Code = outcome.Error is NoireRemoteException named ? named.Code : NoireRemoteErrorCodes.HandlerFault,
                    Message = outcome.Error.Message,
                }),
            });
        }

        return new NoireRemoteBroadcastWire
        {
            Answered = Answered,
            Total = Outcomes.Count,
            ElapsedMs = (long)Elapsed.TotalMilliseconds,
            Outcomes = rows,
        };
    }
}

/// <summary>
/// What one target of a broadcast did.
/// </summary>
/// <typeparam name="TResult">The type the return value converts into.</typeparam>
public sealed class NoireRemoteOutcome<TResult>
{
    internal NoireRemoteOutcome(NoireRemoteInstance instance, bool ok, TResult? result, NoireRemoteEnvelope? envelope, Exception? error, TimeSpan elapsed)
    {
        Instance = instance;
        Ok = ok;
        Result = result;
        Envelope = envelope;
        Error = error;
        Elapsed = elapsed;
    }

    /// <summary>
    /// Gets the target.
    /// </summary>
    public NoireRemoteInstance Instance { get; }

    /// <summary>
    /// Gets whether the target answered.
    /// </summary>
    public bool Ok { get; }

    /// <summary>
    /// Gets the converted result, or the type's default when the target failed.
    /// </summary>
    public TResult? Result { get; }

    /// <summary>
    /// Gets the whole answer, for a caller reading more than the result.
    /// </summary>
    public NoireRemoteEnvelope? Envelope { get; }

    /// <summary>
    /// Gets what went wrong, or null when the target answered.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Gets how long this target took.
    /// </summary>
    public TimeSpan Elapsed { get; }
}

/// <summary>
/// Raised by <see cref="NoireRemoteBroadcast{TResult}.ThrowIfAnyFailed"/>, carrying the whole broadcast.
/// </summary>
public sealed class NoireRemoteBroadcastException : NoireRemoteException
{
    internal NoireRemoteBroadcastException(string message, object broadcast) : base(message)
    {
        Broadcast = broadcast;
    }

    /// <summary>
    /// Gets the broadcast, as a <see cref="NoireRemoteBroadcast{TResult}"/> of the result type the call asked for.
    /// </summary>
    public object Broadcast { get; }
}

// One bad payload does not fail the other targets.
internal static class NoireRemoteBroadcast
{
    public static async Task<NoireRemoteBroadcast<TResult>> RunAsync<TResult>(
        IReadOnlyList<NoireRemoteInstance> targets,
        string endpointName,
        string member,
        object? args,
        int maxConcurrency,
        CancellationToken cancellationToken,
        TimeSpan? deadline = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcomes = new NoireRemoteOutcome<TResult>[targets.Count];

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (deadline is { } limit && limit > TimeSpan.Zero)
            bound.CancelAfter(limit);

        // Zero means all at once.
        using var slots = new SemaphoreSlim(maxConcurrency <= 0 ? targets.Count == 0 ? 1 : targets.Count : maxConcurrency);

        var running = new Task[targets.Count];

        for (var index = 0; index < targets.Count; index++)
            running[index] = OneAsync(index);

        await Task.WhenAll(running).ConfigureAwait(false);

        return new NoireRemoteBroadcast<TResult>(outcomes, stopwatch.Elapsed);

        async Task OneAsync(int index)
        {
            var target = targets[index];
            var each = Stopwatch.StartNew();

            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var envelope = await new NoireRemoteApi(endpointName, target)
                    .PostToAsync(target, member, args, null, bound.Token).ConfigureAwait(false);

                var result = envelope.ResultAs<TResult>();

                outcomes[index] = new NoireRemoteOutcome<TResult>(target, true, result, envelope, null, each.Elapsed);
            }
            catch (Exception exception)
            {
                outcomes[index] = new NoireRemoteOutcome<TResult>(target, false, default, null, exception, each.Elapsed);
            }
            finally
            {
                slots.Release();
            }
        }
    }
}

/// <summary>
/// A broadcast as the console's fleet route carries it.
/// </summary>
public sealed class NoireRemoteBroadcastWire
{
    /// <summary>
    /// Gets or sets whether the broadcast ran. A target reports its own outcome on its own row.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets how many targets answered.
    /// </summary>
    public int Answered { get; set; }

    /// <summary>
    /// Gets or sets how many targets were called.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets how long the whole broadcast took in milliseconds.
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Gets or sets one row per target, in the order they were listed.
    /// </summary>
    public IReadOnlyList<NoireRemoteBroadcastRow> Outcomes { get; set; } = [];
}

/// <summary>
/// One target's row in a broadcast answer.
/// </summary>
public sealed class NoireRemoteBroadcastRow
{
    /// <summary>
    /// Gets or sets the target's instance id.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the target's label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the machine the target runs on.
    /// </summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target's address.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target's port.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets whether the target answered.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    /// Gets or sets how long this target took in milliseconds.
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Gets or sets what the target returned.
    /// </summary>
    public Newtonsoft.Json.Linq.JToken? Result { get; set; }

    /// <summary>
    /// Gets or sets what went wrong, or null when the target answered.
    /// </summary>
    public NoireRemoteError? Error { get; set; }
}
