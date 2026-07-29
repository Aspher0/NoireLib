using System;

namespace NoireLib.ObservedStore;

/// <summary>
/// Everything about a sighting that is not the value itself. Every property is optional: leaving one null takes the
/// store's own default, so <c>store.Record(key, value)</c> needs none of this.
/// </summary>
public sealed class RecordOptions
{
    /// <summary>
    /// Whether the observation belongs to one character or to every character alike.<br/>
    /// Default: <c>null</c>, which takes <see cref="ObservedStoreOptions.DefaultScope"/>.
    /// </summary>
    public ObservationScope? Scope { get; set; }

    /// <summary>
    /// The content id of the character the observation is about. Set this to record something learned about a
    /// character other than the one logged in, as an import from a file does.<br/>
    /// Default: <c>null</c>, which takes the logged-in character. Ignored for a
    /// <see cref="ObservationScope.Shared"/> observation, which belongs to nobody in particular.
    /// </summary>
    public ulong? CharacterId { get; set; }

    /// <summary>
    /// Where the sighting came from, in whatever vocabulary the plugin uses.<br/>
    /// Default: <c>null</c>, which takes <see cref="ObservedStoreOptions.DefaultSource"/>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// When the sighting actually happened. Set this when writing down something seen earlier, so the age the store
    /// reports is the age of the sighting rather than the age of the write.<br/>
    /// Default: <c>null</c>, which is now.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; set; }

    /// <summary>
    /// How long this particular observation stays good for, overriding the store's default. Pass
    /// <see cref="TimeSpan.Zero"/> or a negative span to mark it as never expiring even when the store has a
    /// default.<br/>
    /// Default: <c>null</c>, which takes <see cref="ObservedStoreOptions.DefaultExpiresAfter"/>.
    /// </summary>
    public TimeSpan? ExpiresAfter { get; set; }

    /// <summary>Creates a copy of the options.</summary>
    /// <returns>The copy.</returns>
    public RecordOptions Clone() => new()
    {
        Scope = Scope,
        CharacterId = CharacterId,
        Source = Source,
        ObservedAt = ObservedAt,
        ExpiresAfter = ExpiresAfter,
    };
}
