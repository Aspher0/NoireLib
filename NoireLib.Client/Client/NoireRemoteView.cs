using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// A client seen through a target, a wire and a waiting window. It carries no state of its own and composes in any
/// order: <c>client.On("role", "tank").Over(NoireRemoteTransport.Websocket).InvokeAsync(...)</c>.
/// </summary>
public readonly struct NoireRemoteView
{
    private readonly NoireRemoteClient client;
    private readonly NoireRemoteTarget? target;
    private readonly NoireRemoteTransport transport;
    private readonly TimeSpan? wait;

    internal NoireRemoteView(NoireRemoteClient client, NoireRemoteTarget? target, NoireRemoteTransport transport, TimeSpan? wait)
    {
        this.client = client;
        this.target = target;
        this.transport = transport;
        this.wait = wait;
    }

    /// <summary>
    /// Aims at a character name, a character and world, or a plugin name.
    /// </summary>
    /// <param name="identity">The text to match.</param>
    /// <returns>The same view, aimed.</returns>
    public NoireRemoteView On(string identity) => new(client, NoireRemoteTarget.Identity(identity), transport, wait);

    /// <summary>
    /// Aims at a tag the instance set on itself.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value it has to hold.</param>
    /// <returns>The same view, aimed.</returns>
    public NoireRemoteView On(string key, string value) => new(client, NoireRemoteTarget.Meta(key, value), transport, wait);

    /// <summary>
    /// Aims at a process id.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The same view, aimed.</returns>
    public NoireRemoteView On(int processId) => new(client, NoireRemoteTarget.Process(processId), transport, wait);

    /// <summary>
    /// Aims at every live instance publishing the surface.
    /// </summary>
    /// <returns>The same view, aimed at all of them.</returns>
    public NoireRemoteView OnAll() => new(client, NoireRemoteTarget.All, transport, wait);

    /// <summary>
    /// Takes the call over a given wire.
    /// </summary>
    /// <param name="over">The wire to use.</param>
    /// <returns>The same view, on that wire.</returns>
    public NoireRemoteView Over(NoireRemoteTransport over) => new(client, target, over, wait);

    /// <summary>
    /// Waits for an absent target to appear before failing.
    /// </summary>
    /// <param name="window">How long to wait.</param>
    /// <returns>The same view, waiting.</returns>
    public NoireRemoteView WaitUpTo(TimeSpan window) => new(client, target, transport, window);

    /// <summary>Calls a member and returns its result.</summary>
    /// <typeparam name="TResult">The type the return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The converted result.</returns>
    /// <exception cref="InvalidOperationException">The view aims at every instance.</exception>
    /// <exception cref="NoireRemoteNotFoundException">Nothing matches the target.</exception>
    public async Task<TResult> InvokeAsync<TResult>(string member, object? args = null, CancellationToken cancellationToken = default)
    {
        var aim = Aim();

        if (aim.IsAll)
        {
            throw new InvalidOperationException(
                "A call aimed at every instance has no single answer. Use InvokeAllAsync, which answers once per instance.");
        }

        var instance = client.Resolve(aim, Waiting());

        if (Wire() == NoireRemoteTransport.Websocket)
        {
            var frame = await client.CallOverSocketAsync(instance, member, args, cancellationToken).ConfigureAwait(false);

            if (frame.Error != null)
            {
                throw new NoireRemoteRemoteException(
                    frame.Error.Message ?? "The member failed.",
                    frame.Error.Code,
                    frame.Error.Message,
                    frame.Error.StackTrace);
            }

            return frame.Payload == null ? default! : frame.Payload.ToObject<TResult>()!;
        }

        var envelope = await client.CallOverHttpAsync(instance, member, args, cancellationToken).ConfigureAwait(false);

        return envelope.ResultAs<TResult>()!;
    }

    /// <summary>
    /// Calls a member and discards its result.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the member has run.</returns>
    public Task InvokeAsync(string member, object? args = null, CancellationToken cancellationToken = default)
        => InvokeAsync<object?>(member, args, cancellationToken);

    /// <summary>
    /// Calls a member on every matching instance and answers once per instance.
    /// </summary>
    /// <typeparam name="TResult">The type each return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>One outcome per instance, carrying the instance, the value and any error.</returns>
    public async Task<IReadOnlyList<NoireRemoteOutcome<TResult>>> InvokeAllAsync<TResult>(
        string member,
        object? args = null,
        CancellationToken cancellationToken = default)
    {
        var instances = client.ResolveAll(Aim());
        var outcomes = new List<NoireRemoteOutcome<TResult>>(instances.Count);

        foreach (var instance in instances)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var envelope = await client.CallOverHttpAsync(instance, member, args, cancellationToken).ConfigureAwait(false);

                outcomes.Add(new NoireRemoteOutcome<TResult>(instance, true, envelope.ResultAs<TResult>(), envelope, null, started.Elapsed));
            }
            catch (Exception exception)
            {
                outcomes.Add(new NoireRemoteOutcome<TResult>(instance, false, default, null, exception, started.Elapsed));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Starts a member that runs as a job and hands back a handle to follow it.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, keyed by parameter name. Null means none.</param>
    /// <param name="cancellationToken">Cancels the request that starts the job. The job itself keeps running.</param>
    /// <returns>A handle that reads the job's state and its result.</returns>
    /// <exception cref="NoireRemoteProtocolException">If the listener answered without a job.</exception>
    public async Task<NoireRemoteJob> StartAsync(string member, object? args = null, CancellationToken cancellationToken = default)
    {
        var instance = client.Resolve(Aim(), Waiting());

        if (Wire() == NoireRemoteTransport.Websocket)
        {
            var frame = await client.CallOverSocketAsync(instance, member, args, cancellationToken).ConfigureAwait(false);

            if (frame.Error != null)
                throw new NoireRemoteRemoteException(frame.Error.Message, frame.Error.Code, frame.Error.Message, frame.Error.StackTrace);

            var status = frame.Payload?.ToObject<NoireRemoteJobStatus>()
                ?? throw new NoireRemoteProtocolException("The listener answered a job call without a job.");

            var job = new NoireRemoteJob(instance, status);
            client.Follow(job);

            return job;
        }

        var envelope = await client.CallOverHttpAsync(instance, member, args, cancellationToken, mode: "job").ConfigureAwait(false);

        if (envelope.Job == null)
            throw new NoireRemoteProtocolException("The listener answered a job call without a job.");

        return new NoireRemoteJob(instance, envelope.Job);
    }

    private NoireRemoteTarget Aim()
        => target ?? NoireRemoteClient.DefaultTarget ?? NoireRemoteTarget.Any;

    private TimeSpan Waiting()
        => wait ?? NoireRemoteClient.DefaultWait;

    // Over, then the constructor argument, then automatic: HTTP for a plain call.
    private NoireRemoteTransport Wire()
    {
        if (transport != NoireRemoteTransport.Inherit)
            return transport;

        return client.Transport == NoireRemoteTransport.Inherit ? NoireRemoteTransport.Http : client.Transport;
    }
}
