namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when an action effect is received from the server (an action resolving on its targets),
/// with fully parsed per-target effects.
/// </summary>
/// <param name="Entry">The captured action-effect entry.</param>
public sealed record ActionEffectEvent(ActionEffectEntry Entry);
