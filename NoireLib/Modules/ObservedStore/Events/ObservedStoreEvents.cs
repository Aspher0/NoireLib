namespace NoireLib.ObservedStore;

/// <summary>Raised when an observation is written down, whether it is new or replaces an older sighting.</summary>
/// <param name="Info">The sighting that was recorded.</param>
/// <param name="Replaced">
/// The sighting it replaced, or null when the store had never seen this key before. Comparing the two is how a
/// consumer tells "this changed" from "this was confirmed unchanged".
/// </param>
public sealed record ObservationRecordedEvent(ObservationInfo Info, ObservationInfo? Replaced);

/// <summary>Raised when a single observation is deliberately forgotten.</summary>
/// <param name="Info">The sighting that was removed.</param>
public sealed record ObservationForgottenEvent(ObservationInfo Info);

/// <summary>Raised when observations are removed in bulk, by prefix, by age, or by clearing.</summary>
/// <param name="Count">How many observations were removed.</param>
/// <param name="Scope">The scope they were removed from.</param>
/// <param name="CharacterId">The character they belonged to, or zero for a shared scope.</param>
/// <param name="Reason">Why they were removed, for a log or a diagnostics view.</param>
public sealed record ObservationsPrunedEvent(int Count, ObservationScope Scope, ulong CharacterId, string Reason);
