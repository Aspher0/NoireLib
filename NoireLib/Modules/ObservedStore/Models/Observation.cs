using System;

namespace NoireLib.ObservedStore;

/// <summary>
/// Everything the store knows about one observation except its value: what it is about, who saw it, where the
/// sighting came from, when it happened, and how long it stays good for.<br/>
/// Reading this costs no deserialization, so it is the cheap way to ask "do I still trust what I have".
/// </summary>
/// <param name="Key">The key the value was recorded under.</param>
/// <param name="Scope">Whether the observation belongs to one character or to every character alike.</param>
/// <param name="CharacterId">
/// The content id of the character the observation is about, or zero for a <see cref="ObservationScope.Shared"/> one.
/// </param>
/// <param name="Source">
/// Where the sighting came from, as the recorder named it. Free text, so a consumer can tell an observation made by
/// opening a window from one imported out of a file and decide which it trusts.
/// </param>
/// <param name="ObservedAt">When the sighting happened, which is not necessarily when it was written down.</param>
/// <param name="ExpiresAfter">
/// How long after <see cref="ObservedAt"/> the observation should stop being trusted, or null when it never expires
/// on its own.
/// </param>
public readonly record struct ObservationInfo(
    string Key,
    ObservationScope Scope,
    ulong CharacterId,
    string Source,
    DateTimeOffset ObservedAt,
    TimeSpan? ExpiresAfter)
{
    /// <summary>How long ago the sighting happened.</summary>
    public TimeSpan Age => AgeAt(DateTimeOffset.UtcNow);

    /// <summary>Whether the observation has outlived its own <see cref="ExpiresAfter"/>.</summary>
    public bool IsExpired => IsExpiredAt(DateTimeOffset.UtcNow);

    /// <summary>How long before the sighting the given moment was, never negative.</summary>
    /// <param name="now">The moment to measure from.</param>
    /// <returns>The age at that moment.</returns>
    public TimeSpan AgeAt(DateTimeOffset now)
    {
        var age = now - ObservedAt;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    /// <summary>Whether the sighting is older than the given span.</summary>
    /// <param name="maxAge">The oldest age still considered good.</param>
    /// <returns>True when the observation is older than that.</returns>
    public bool IsOlderThan(TimeSpan maxAge) => IsOlderThan(maxAge, DateTimeOffset.UtcNow);

    /// <inheritdoc cref="IsOlderThan(TimeSpan)"/>
    /// <param name="maxAge">The oldest age still considered good.</param>
    /// <param name="now">The moment to measure from, so the rule is testable without a clock.</param>
    /// <returns>True when the observation is older than that.</returns>
    public bool IsOlderThan(TimeSpan maxAge, DateTimeOffset now) => AgeAt(now) > maxAge;

    /// <inheritdoc cref="IsExpired"/>
    /// <param name="now">The moment to measure from, so the rule is testable without a clock.</param>
    /// <returns>True when the observation has expired.</returns>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAfter is { } lifetime && IsOlderThan(lifetime, now);
}

/// <summary>
/// One observation: a value the client was seen to hold, together with everything known about the sighting.
/// </summary>
/// <typeparam name="T">The recorded value's type.</typeparam>
/// <param name="Info">The sighting's metadata.</param>
/// <param name="Value">The value as it was seen.</param>
public sealed record Observation<T>(ObservationInfo Info, T Value)
{
    /// <inheritdoc cref="ObservationInfo.Key"/>
    public string Key => Info.Key;

    /// <inheritdoc cref="ObservationInfo.Source"/>
    public string Source => Info.Source;

    /// <inheritdoc cref="ObservationInfo.ObservedAt"/>
    public DateTimeOffset ObservedAt => Info.ObservedAt;

    /// <inheritdoc cref="ObservationInfo.Age"/>
    public TimeSpan Age => Info.Age;

    /// <inheritdoc cref="ObservationInfo.IsExpired"/>
    public bool IsExpired => Info.IsExpired;

    /// <inheritdoc cref="ObservationInfo.IsOlderThan(TimeSpan)"/>
    /// <param name="maxAge">The oldest age still considered good.</param>
    /// <returns>True when the observation is older than that.</returns>
    public bool IsOlderThan(TimeSpan maxAge) => Info.IsOlderThan(maxAge);
}
