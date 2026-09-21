using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// Calls one member on every live instance publishing an endpoint. One instance failing never costs another its
/// answer. A failed instance still gets a row in the result.
/// </summary>
public sealed class NoireRemoteFanout
{
    private readonly NoireRemoteApi endpoint;
    private readonly Func<NoireRemoteInstance, bool>? filter;

    internal NoireRemoteFanout(NoireRemoteApi endpoint, Func<NoireRemoteInstance, bool>? filter)
    {
        this.endpoint = endpoint;
        this.filter = filter;
    }

    /// <summary>
    /// Gets or sets how many calls run at once. Zero runs them all at once, the shape a fleet call needs.
    /// </summary>
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// Gets or sets how long the whole broadcast may take. Zero leaves it to the per-call deadline. A wedged
    /// target still gets a timeout row.
    /// </summary>
    public TimeSpan Deadline { get; set; }

    /// <summary>
    /// Gets or sets whether the targets are looked for on the local network as well as on this machine. It needs
    /// <see cref="NoireRemoteClientOptions.NetworkSecret"/>.
    /// </summary>
    public bool IncludeNetwork { get; set; }

    /// <summary>
    /// Calls a member on every matching instance and collects one row per target.
    /// </summary>
    /// <typeparam name="TResult">The type each return value converts into.</typeparam>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>One row per target, in the order the targets were listed.</returns>
    public async Task<NoireRemoteBroadcast<TResult>> CallAsync<TResult>(string member, object? args = null, CancellationToken cancellationToken = default)
    {
        var targets = IncludeNetwork
            ? Filtered(await NoireRemoteClient.DiscoverAsync(endpoint.Name, cancellationToken).ConfigureAwait(false))
            : Targets();

        return await NoireRemoteBroadcast.RunAsync<TResult>(
            targets, endpoint.Name, member, args, MaxConcurrency, cancellationToken, Deadline).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls a member on every matching instance and discards the results.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>One row per target, carrying no result.</returns>
    public Task<NoireRemoteBroadcast<object>> CallAsync(string member, object? args = null, CancellationToken cancellationToken = default)
        => CallAsync<object>(member, args, cancellationToken);

    /// <summary>
    /// Lists the instances this fanout would call.
    /// </summary>
    /// <returns>Every live instance on this machine publishing the endpoint and passing the filter.</returns>
    public IReadOnlyList<NoireRemoteInstance> Targets()
        => Filtered(NoireRemoteClient.Resolver.Discover(NoireRemoteClient.Options, endpoint.Name));

    /// <summary>
    /// Lists the instances this fanout would call, looking on the local network as well.
    /// </summary>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>Every live instance publishing the endpoint and passing the filter.</returns>
    public async Task<IReadOnlyList<NoireRemoteInstance>> TargetsAsync(CancellationToken cancellationToken = default)
        => Filtered(await NoireRemoteClient.DiscoverAsync(endpoint.Name, cancellationToken).ConfigureAwait(false));

    private List<NoireRemoteInstance> Filtered(IReadOnlyList<NoireRemoteInstance> found)
    {
        var matches = new List<NoireRemoteInstance>(found.Count);

        foreach (var instance in found)
        {
            if (filter == null || filter(instance))
                matches.Add(instance);
        }

        return matches;
    }
}
