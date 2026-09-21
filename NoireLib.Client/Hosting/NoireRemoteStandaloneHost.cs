using System;
using System.Reflection;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// The host a program that is not a plugin gets. It runs every member on the thread that read the request, gates
/// nothing and writes nowhere. Derive from it to override one part.
/// </summary>
public class NoireRemoteStandaloneHost : INoireRemoteHost
{
    private static readonly AssemblyName Entry = Assembly.GetEntryAssembly()?.GetName() ?? new AssemblyName("NoireRemote");

    private static readonly string Library =
        typeof(NoireRemoteStandaloneHost).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Gets or sets the name the instance record and the manifest carry. Defaults to the entry assembly's name.
    /// </summary>
    public string Name { get; set; } = Entry.Name ?? "NoireRemote";

    /// <summary>
    /// Gets or sets the version the instance record and the manifest carry. Defaults to the entry assembly's version.
    /// </summary>
    public string Version { get; set; } = Entry.Version?.ToString() ?? "0.0.0";

    /// <summary>
    /// Gets or sets the label telling this host from another in a caller's listing.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets where the listener's own lines go. Null discards them.
    /// </summary>
    public Action<NoireRemoteLogLevel, string, Exception?>? LogSink { get; set; }

    /// <inheritdoc/>
    public virtual NoireRemoteIdentity Identity => new(Name, Version, Library);

    /// <inheritdoc/>
    public virtual NoireRemoteThread DefaultThread => NoireRemoteThread.Background;

    /// <inheritdoc/>
    public virtual bool HasHostThread => false;

    /// <inheritdoc/>
    public virtual Task<TResult> RunOnHostThreadAsync<TResult>(Func<Task<TResult>> work)
        => work();

    /// <summary>
    /// Answers ready to every gate, because the state a gate names belongs to a game that is not running here.
    /// </summary>
    /// <param name="requires">The state the member declared.</param>
    /// <param name="route">The route asking.</param>
    /// <returns>Ready.</returns>
    public virtual NoireRemoteReadinessResult CheckReadiness(NoireRemoteReadiness requires, string route)
        => NoireRemoteReadinessResult.Ready;

    /// <inheritdoc/>
    public virtual void RefreshLabel(Action<string> setLabel)
        => setLabel?.Invoke(Label);

    /// <inheritdoc/>
    public virtual void Log(NoireRemoteLogLevel level, string message, Exception? exception)
        => LogSink?.Invoke(level, message, exception);
}
