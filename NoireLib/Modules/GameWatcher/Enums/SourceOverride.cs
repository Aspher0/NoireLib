namespace NoireLib.GameWatcher;

/// <summary>
/// Per-source configuration override for <see cref="GameWatcherOptions.Sources"/>.<br/>
/// Sources are normally demand-activated: the first subscription touching a source spins it up,
/// disposing the last token shuts it down. Overrides change that default.<br/>
/// Precedence: <see cref="Disabled"/> beats everything, including the always-on implied by a configured
/// history capacity - that contradiction is logged rather than guessed at.
/// </summary>
public enum SourceOverride
{
    /// <summary>Demand-driven activation (the default): active while at least one subscription needs the source.</summary>
    Default,

    /// <summary>Keep the source running whenever the module is active, even with no subscribers (e.g. to collect history).</summary>
    AlwaysOn,

    /// <summary>Hard off. Subscriptions to the source's events log a warning and never fire - visible in diagnostics.</summary>
    Disabled,
}
