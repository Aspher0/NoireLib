using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// Calls a plugin's published surface from a program that is not a plugin.<br/>
/// The address, the port and the credential come from the record the listening plugin writes. The static members
/// find and address instances. An instance of this class names one surface and holds a transport default.
/// </summary>
public sealed partial class NoireRemoteClient
{
    internal static readonly NoireRemoteResolver Resolver = new();

    // A stale record can name a port another program now holds.
    private static readonly HashSet<Guid> VerifiedInstances = [];

    /// <summary>
    /// Gets the settings every call uses. Assigning a property on it changes the behavior of later calls.
    /// </summary>
    public static NoireRemoteClientOptions Options { get; } = new();

    /// <summary>
    /// Targets an endpoint by name, resolving which running game client publishes it on the first call.
    /// </summary>
    /// <param name="name">The endpoint name, as the plugin declared it.</param>
    /// <returns>An endpoint ready to call.</returns>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public static NoireRemoteApi Endpoint(string name)
        => new(name);

    /// <summary>
    /// Targets a listener by address, skipping discovery. This is how a second machine is reached.
    /// </summary>
    /// <param name="address">The host and port, as <c>host:port</c>.</param>
    /// <param name="secret">The shared secret the listener was configured with. Required off the local machine.</param>
    /// <returns>An instance the endpoints of which can be called.</returns>
    /// <exception cref="ArgumentException">If the address carries no port or the port is not a number.</exception>
    public static NoireRemoteInstance At(string address, string? secret = null)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("An address is required.", nameof(address));

        var separator = address.LastIndexOf(':');

        if (separator <= 0 || !int.TryParse(address.Substring(separator + 1), out var port))
            throw new ArgumentException("An address reads as host:port.", nameof(address));

        return At(address.Substring(0, separator), port, secret);
    }

    /// <summary>
    /// Targets a listener by host and port, skipping discovery.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The port the listener is bound to.</param>
    /// <param name="secret">The shared secret the listener was configured with. Required off the local machine.</param>
    /// <returns>An instance the endpoints of which can be called.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the port is outside the valid range.</exception>
    public static NoireRemoteInstance At(string host, int port, string? secret = null)
    {
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "A port is between 1 and 65535.");

        return new NoireRemoteInstance(host.Trim('[', ']'), port, secret);
    }

    /// <summary>
    /// Lists the live instances on this machine.
    /// </summary>
    /// <param name="endpointName">Only list the instances publishing this endpoint. Null lists them all.</param>
    /// <returns>The instances found, in no particular order.</returns>
    public static IReadOnlyList<NoireRemoteInstance> Discover(string? endpointName = null)
        => Resolver.Discover(Options, endpointName);

    /// <summary>
    /// Lists the live instances on this machine and, with <see cref="NoireRemoteClientOptions.NetworkSecret"/> set, on
    /// the local network. Nothing on the network answers an unsigned probe. A host that does not share the secret
    /// is never listed.
    /// </summary>
    /// <param name="endpointName">Only list the instances publishing this endpoint. Null lists them all.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <param name="options">The settings to search with. Null uses <see cref="Options"/>.</param>
    /// <returns>The instances found, local ones first, then by machine, label and port.</returns>
    public static Task<IReadOnlyList<NoireRemoteInstance>> DiscoverAsync(
        string? endpointName = null,
        CancellationToken cancellationToken = default,
        NoireRemoteClientOptions? options = null)
        => NoireRemoteFleet.DiscoverAsync(options ?? Options, endpointName, cancellationToken);

    /// <summary>
    /// Drops the pooled connections this assembly holds. A plugin that has called out must do this before it
    /// unloads, because the pool's timer runs outside the plugin's load context. A call in flight fails.
    /// </summary>
    public static void Shutdown()
    {
        RemoteWireTransport.Shutdown();
        Refresh();
    }

    /// <summary>
    /// Forgets the cached directory read. The next call enumerates the folder again.
    /// </summary>
    public static void Refresh()
    {
        Resolver.Invalidate();

        lock (VerifiedInstances)
            VerifiedInstances.Clear();
    }

    internal static async Task VerifyAsync(NoireRemoteInstance instance, CancellationToken cancellationToken)
    {
        if (!Options.VerifyWithPing || instance.Id == Guid.Empty)
            return;

        lock (VerifiedInstances)
        {
            if (VerifiedInstances.Contains(instance.Id))
                return;
        }

        var answer = await instance.PingAsync(cancellationToken).ConfigureAwait(false);

        if (answer.Instance != instance.Id)
            throw new NoireRemoteNotFoundException(
                "Port " + instance.Port + " answers as instance " + answer.Instance.ToString("D")
                + ". The record naming a different instance is stale.");

        lock (VerifiedInstances)
            VerifiedInstances.Add(instance.Id);
    }

    /// <summary>
    /// Calls a member named by its full route and returns its result.
    /// </summary>
    /// <param name="route">The route, as <c>Endpoint/Member</c>.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <typeparam name="TResult">The type the member's return value converts into.</typeparam>
    /// <returns>The converted result.</returns>
    /// <exception cref="ArgumentException">If the route carries no slash.</exception>
    public static Task<TResult> CallAsync<TResult>(string route, object? args = null, CancellationToken cancellationToken = default)
    {
        Split(route, out var endpoint, out var member);
        return Endpoint(endpoint).CallAsync<TResult>(member, args, cancellationToken);
    }

    /// <summary>
    /// Calls a member named by its full route and discards its result.
    /// </summary>
    /// <param name="route">The route, as <c>Endpoint/Member</c>.</param>
    /// <param name="args">The arguments. Null means none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the member has finished.</returns>
    /// <exception cref="ArgumentException">If the route carries no slash.</exception>
    public static Task CallAsync(string route, object? args = null, CancellationToken cancellationToken = default)
    {
        Split(route, out var endpoint, out var member);
        return Endpoint(endpoint).CallAsync(member, args, cancellationToken);
    }

    /// <summary>
    /// Builds a typed client from an interface carrying <see cref="NoireRemoteClassAttribute"/>, taking the endpoint
    /// name from the attribute.
    /// </summary>
    /// <typeparam name="TInterface">The interface describing the endpoint's members.</typeparam>
    /// <returns>A proxy whose calls go over HTTP.</returns>
    /// <exception cref="ArgumentException">If the interface carries no endpoint attribute and its name yields none.</exception>
    /// <exception cref="NoireRemoteContractException">If the interface disagrees with the published surface.</exception>
    public static TInterface Connect<TInterface>() where TInterface : class
    {
        var attribute = typeof(TInterface).GetCustomAttribute<NoireRemoteClassAttribute>();
        var name = attribute?.Name;

        if (string.IsNullOrWhiteSpace(name))
            name = DefaultEndpointName(typeof(TInterface));

        return Endpoint(name!).As<TInterface>();
    }

    internal static string DefaultEndpointName(Type contract)
    {
        var name = contract.Name;

        if (contract.IsInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name.Substring(1);

        return name;
    }

    private static void Split(string route, out string endpoint, out string member)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("A route is required.", nameof(route));

        var separator = route.LastIndexOf('/');

        if (separator <= 0 || separator == route.Length - 1)
            throw new ArgumentException("A route reads as Endpoint/Member.", nameof(route));

        endpoint = route.Substring(0, separator);
        member = route.Substring(separator + 1);
    }
}
