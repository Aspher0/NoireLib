using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// One named endpoint on one listening plugin. Resolution happens on the first call. An endpoint can be held
/// before the game is running.
/// </summary>
public sealed class NoireRemoteApi
{
    private readonly NoireRemoteInstance? bound;
    private readonly Func<NoireRemoteInstance, bool>? filter;
    private readonly bool takeAny;

    internal NoireRemoteApi(string name, NoireRemoteInstance? bound = null, Func<NoireRemoteInstance, bool>? filter = null, bool takeAny = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An endpoint name is required.", nameof(name));

        Name = name;
        this.bound = bound;
        this.filter = filter;
        this.takeAny = takeAny;
    }

    /// <summary>
    /// Gets the endpoint name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether a live instance publishes this endpoint. Reading it performs a directory read.
    /// </summary>
    public bool Exists => TryResolve(out _);

    /// <summary>
    /// Narrows resolution to one instance id.
    /// </summary>
    /// <param name="instanceId">The instance id, as printed by an ambiguity message or read from a record.</param>
    /// <returns>A new endpoint with the filter applied.</returns>
    public NoireRemoteApi Instance(Guid instanceId)
        => new(Name, bound, instance => instance.Id == instanceId, takeAny);

    /// <summary>
    /// Narrows resolution to the instances whose label or plugin name contains a piece of text, ignoring case.
    /// </summary>
    /// <param name="label">The text to look for.</param>
    /// <returns>A new endpoint with the filter applied.</returns>
    public NoireRemoteApi Instance(string label)
        => new(Name, bound, instance =>
            instance.Label.Contains(label, StringComparison.OrdinalIgnoreCase)
            || instance.Plugin.Contains(label, StringComparison.OrdinalIgnoreCase), takeAny);

    /// <summary>
    /// Narrows resolution with a predicate of your own.
    /// </summary>
    /// <param name="predicate">The test an instance passes to be considered.</param>
    /// <returns>A new endpoint with the filter applied.</returns>
    public NoireRemoteApi Instance(Func<NoireRemoteInstance, bool> predicate)
        => new(Name, bound, predicate, takeAny);

    /// <summary>
    /// Picks the most recently started instance when several publish the endpoint.
    /// </summary>
    /// <returns>A new endpoint that no longer raises on ambiguity.</returns>
    public NoireRemoteApi Any()
        => new(Name, bound, filter, true);

    /// <summary>
    /// Targets every live instance publishing the endpoint at once.
    /// </summary>
    /// <returns>A fanout over the matching instances.</returns>
    public NoireRemoteFanout All()
        => new(this, filter);

    /// <summary>
    /// Resolves the instance this endpoint would call without calling anything.
    /// </summary>
    /// <param name="instance">Receives the resolved instance, or null when none matched.</param>
    /// <returns>True when exactly one instance matched, or one was taken after <see cref="Any"/>.</returns>
    public bool TryResolve(out NoireRemoteInstance? instance)
    {
        try
        {
            instance = Resolve();
            return true;
        }
        catch (NoireRemoteException)
        {
            instance = null;
            return false;
        }
    }

    /// <summary>
    /// Calls a member and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The type the member's return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, as an anonymous object keyed by parameter name, a dictionary, or an array in declaration order. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The converted result.</returns>
    /// <exception cref="NoireRemoteNotFoundException">If no live instance publishes the endpoint, or the member does not exist.</exception>
    /// <exception cref="NoireRemoteAmbiguousEndpointException">If several instances publish it and none was named.</exception>
    /// <exception cref="NoireRemoteRemoteException">If the member threw.</exception>
    public async Task<TResult> CallAsync<TResult>(string member, object? args = null, CancellationToken cancellationToken = default)
    {
        var envelope = await PostAsync(member, args, null, cancellationToken).ConfigureAwait(false);
        return envelope.ResultAs<TResult>()!;
    }

    /// <summary>
    /// Calls a member and discards its result.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments, as an anonymous object keyed by parameter name, a dictionary, or an array in declaration order. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the member has finished.</returns>
    public async Task CallAsync(string member, object? args = null, CancellationToken cancellationToken = default)
        => await PostAsync(member, args, null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Calls a member and blocks until it answers. Blocking is safe only at a console entry point.
    /// </summary>
    /// <typeparam name="TResult">The type the member's return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <returns>The converted result.</returns>
    public TResult Call<TResult>(string member, object? args = null)
        => CallAsync<TResult>(member, args).GetAwaiter().GetResult();

    /// <summary>
    /// Calls a member and blocks until it answers, discarding the result. Blocking is safe only at a console entry
    /// point.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments. Null means none.</param>
    public void Call(string member, object? args = null)
        => CallAsync(member, args).GetAwaiter().GetResult();

    /// <summary>
    /// Starts a member as a job and returns at once, without waiting for it to finish.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <param name="cancellationToken">Cancels the request that starts the job. The job itself keeps running.</param>
    /// <returns>A handle that polls and cancels the job.</returns>
    /// <exception cref="NoireRemoteProtocolException">If the listener answered without a job.</exception>
    public async Task<NoireRemoteJob> StartJobAsync(string member, object? args = null, CancellationToken cancellationToken = default)
    {
        var instance = Resolve();
        var envelope = await PostToAsync(instance, member, args, "job", cancellationToken).ConfigureAwait(false);

        if (envelope.Job == null)
            throw new NoireRemoteProtocolException("The listener answered a job call without a job.");

        return new NoireRemoteJob(instance, envelope.Job);
    }

    /// <summary>
    /// Reads the published surface of the instance this endpoint resolves to.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The manifest.</returns>
    public async Task<NoireRemoteManifest> ManifestAsync(CancellationToken cancellationToken = default)
    {
        var instance = Resolve();

        return await RemoteWireTransport.SendDocumentAsync<NoireRemoteManifest>(
            instance,
            HttpMethod.Get,
            NoireRemotePaths.Prefix + NoireRemotePaths.Manifest,
            null,
            NoireRemoteClient.Options.CallTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Watches the instance's event stream, yielding each event as it arrives. The enumeration ends when the token
    /// is cancelled.
    /// </summary>
    /// <param name="topics">A comma separated list of topic prefixes, or null for every topic.</param>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    public async IAsyncEnumerable<NoireRemoteEvent> EventsAsync(string? topics = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = Resolve();
        var topicList = ParseTopics(topics);
        long cursor = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var request = new NoireRemoteEventRequest { Topics = topicList, Since = cursor, WaitMs = 25000 };

            var batch = await RemoteWireTransport.SendDocumentAsync<NoireRemoteEventBatch>(
                instance,
                HttpMethod.Post,
                NoireRemotePaths.Prefix + NoireRemotePaths.Events,
                request,
                TimeSpan.FromMilliseconds(35000),
                cancellationToken).ConfigureAwait(false);

            cursor = batch.Cursor;

            foreach (var item in batch.Events)
                yield return item;
        }
    }

    /// <summary>
    /// Holds one connection open and yields every event as it is written. The listener writes one JSON document
    /// per line and the enumeration ends when the token is cancelled or the connection closes.
    /// </summary>
    /// <param name="channels">A comma separated list of channels, or null for the events channel alone.</param>
    /// <param name="topics">A comma separated list of topic prefixes, or null for every topic.</param>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    public async IAsyncEnumerable<NoireRemoteEvent> StreamAsync(
        string? channels = null,
        string? topics = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var instance = Resolve();
        var query = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(channels))
            query.Add("channels=" + Uri.EscapeDataString(channels!));

        if (!string.IsNullOrWhiteSpace(topics))
            query.Add("topics=" + Uri.EscapeDataString(topics!));

        var path = NoireRemotePaths.Prefix + NoireRemotePaths.Stream + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));

        // The event is in the data: field of a server-sent-events line.
        await foreach (var line in RemoteWireTransport.ReadLinesAsync(instance, path, cancellationToken).ConfigureAwait(false))
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var published = NoireRemoteJson.TryRead<NoireRemoteEvent>(line.Substring(5).TrimStart());

            // A notice frame, such as dropped events, has no topic.
            if (published != null && published.Topic.Length > 0)
                yield return published;
        }
    }

    /// <summary>
    /// Builds a typed client over this endpoint, checking the interface against the listener's manifest first.
    /// </summary>
    /// <typeparam name="TInterface">The interface describing the endpoint's members.</typeparam>
    /// <returns>A proxy whose calls go over HTTP.</returns>
    /// <exception cref="NoireRemoteContractException">If the interface names a member or a parameter the listener does not publish.</exception>
    public TInterface As<TInterface>() where TInterface : class
        => NoireRemoteProxy.Create<TInterface>(this);

    internal NoireRemoteInstance Resolve()
        => bound ?? NoireRemoteClient.Resolver.Resolve(NoireRemoteClient.Options, Name, filter, takeAny);

    internal Func<NoireRemoteInstance, bool>? Filter => filter;

    private async Task<NoireRemoteEnvelope> PostAsync(string member, object? args, string? mode, CancellationToken cancellationToken)
    {
        var instance = Resolve();

        try
        {
            return await PostToAsync(instance, member, args, mode, cancellationToken).ConfigureAwait(false);
        }
        catch (NoireRemoteUnauthorizedException) when (bound == null && NoireRemoteClient.Options.RetryOnStaleToken)
        {
            // A plugin reload rotates the credential.
            NoireRemoteClient.Resolver.Invalidate();
            return await PostToAsync(Resolve(), member, args, mode, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<NoireRemoteEnvelope> PostToAsync(NoireRemoteInstance instance, string member, object? args, string? mode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member))
            throw new ArgumentException("A member name is required.", nameof(member));

        await NoireRemoteClient.VerifyAsync(instance, cancellationToken).ConfigureAwait(false);

        var options = NoireRemoteClient.Options;
        var body = new NoireRemoteRequest
        {
            Args = ToArgs(args),
            Mode = mode,
            Protocol = NoireRemotePaths.Protocol,
            TimeoutMs = (int)options.CallTimeout.TotalMilliseconds,
        };

        return await RemoteWireTransport.SendEnvelopeAsync(
            instance,
            HttpMethod.Post,
            NoireRemotePaths.Member(Name, member),
            body,
            options.CallTimeout.Add(TimeSpan.FromSeconds(2)),
            cancellationToken).ConfigureAwait(false);
    }

    internal static JToken? ToArgs(object? args)
    {
        if (args == null)
            return null;

        if (args is JToken token)
            return token;

        return NoireRemoteJson.ToToken(args);
    }

    private static IReadOnlyList<string> ParseTopics(string? topics)
    {
        if (string.IsNullOrWhiteSpace(topics))
            return [];

        var parts = topics!.Split(',');
        var list = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            if (trimmed.Length > 0)
                list.Add(trimmed);
        }

        return list;
    }
}
