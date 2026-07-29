using Newtonsoft.Json;
using NoireLib.EventBus;
using System;

namespace NoireLib.ObservedStore;

/// <summary>
/// Options for a <see cref="NoireObservedStore"/> instance.<br/>
/// Options are snapshotted when the module activates; changes made while active require a deactivate/activate
/// cycle to apply, which <see cref="NoireObservedStore.SetOptions"/> performs for you.
/// </summary>
public sealed class ObservedStoreOptions
{
    /// <summary>
    /// The name of the database file the observations live in, under the plugin's own <c>Databases</c> directory.
    /// Two stores given different names keep entirely separate sets of observations.<br/>
    /// Default: <c>"NoireObservedStore"</c>.
    /// </summary>
    public string DatabaseName { get; set; } = NoireObservedStore.DefaultDatabaseName;

    /// <summary>
    /// The scope a record or read takes when it does not name one.<br/>
    /// Default: <see cref="ObservationScope.Character"/>.
    /// </summary>
    public ObservationScope DefaultScope { get; set; } = ObservationScope.Character;

    /// <summary>
    /// The source a record takes when it does not name one. Naming the plugin or the window that does the observing
    /// helps explain a stale entry later.<br/>
    /// Default: <c>"unspecified"</c>.
    /// </summary>
    public string DefaultSource { get; set; } = "unspecified";

    /// <summary>
    /// How long an observation stays good for when the record does not say; null means it never expires on its
    /// own, leaving staleness to the consumer's judgment rather than the store's.<br/>
    /// Default: <c>null</c>.
    /// </summary>
    public TimeSpan? DefaultExpiresAfter { get; set; }

    /// <summary>
    /// Whether expired observations are deleted when the module activates. Expired entries are never returned by a
    /// read either way; this only decides whether they keep taking up space.<br/>
    /// Default: <c>true</c>.
    /// </summary>
    public bool PruneExpiredOnActivate { get; set; } = true;

    /// <summary>
    /// An optional EventBus to publish store events on. Null keeps them to the module's own subscription surface.<br/>
    /// Default: <c>null</c>.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// Whether store events are published on <see cref="EventBus"/> when one is attached.<br/>
    /// Default: <c>true</c>.
    /// </summary>
    public bool PublishModuleEvents { get; set; } = true;

    /// <summary>
    /// The Newtonsoft settings used to serialize and deserialize recorded values. Null uses the library defaults.
    /// <br/>
    /// <b><see cref="JsonSerializerSettings.TypeNameHandling"/> is always forced back to
    /// <see cref="TypeNameHandling.None"/> whatever is set here.</b> A stored payload that names its own type is a
    /// remote code execution vector, and the store deserializes into the type the caller asked for rather than the
    /// type the payload claims.<br/>
    /// Default: <c>null</c>.
    /// </summary>
    public JsonSerializerSettings? SerializerSettings { get; set; }

    /// <summary>Creates a copy of the options.</summary>
    /// <returns>The copy.</returns>
    public ObservedStoreOptions Clone() => new()
    {
        DatabaseName = DatabaseName,
        DefaultScope = DefaultScope,
        DefaultSource = DefaultSource,
        DefaultExpiresAfter = DefaultExpiresAfter,
        PruneExpiredOnActivate = PruneExpiredOnActivate,
        EventBus = EventBus,
        PublishModuleEvents = PublishModuleEvents,
        SerializerSettings = SerializerSettings,
    };
}
