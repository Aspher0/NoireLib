namespace NoireLib.TweakManager;

/// <summary>
/// Event fired when a tweak is enabled.
/// </summary>
/// <param name="InternalKey">The internal key of the tweak that was enabled.</param>
/// <param name="Name">The display name of the tweak.</param>
public record TweakEnabledEvent(string InternalKey, string Name);

/// <summary>
/// Event fired when a tweak is disabled.
/// </summary>
/// <param name="InternalKey">The internal key of the tweak that was disabled.</param>
/// <param name="Name">The display name of the tweak.</param>
public record TweakDisabledEvent(string InternalKey, string Name);

/// <summary>
/// Event fired when a tweak encounters an error.
/// </summary>
/// <param name="InternalKey">The internal key of the tweak that errored.</param>
/// <param name="Name">The display name of the tweak.</param>
/// <param name="Error">The exception that occurred.</param>
public record TweakErrorEvent(string InternalKey, string Name, System.Exception Error);

/// <summary>
/// Event fired when a tweak is registered with the manager.
/// </summary>
/// <param name="InternalKey">The internal key of the registered tweak.</param>
/// <param name="Name">The display name of the tweak.</param>
public record TweakRegisteredEvent(string InternalKey, string Name);

/// <summary>
/// Event fired when a tweak is unregistered from the manager.
/// </summary>
/// <param name="InternalKey">The internal key of the unregistered tweak.</param>
/// <param name="Name">The display name of the tweak.</param>
public record TweakUnregisteredEvent(string InternalKey, string Name);

/// <summary>
/// Event fired when the tweak manager window is opened.
/// </summary>
public record TweakWindowOpenedEvent();

/// <summary>
/// Event fired when the tweak manager window is closed.
/// </summary>
public record TweakWindowClosedEvent();

/// <summary>
/// Event fired when a tweak is selected in the UI.
/// </summary>
/// <param name="InternalKey">The internal key of the selected tweak.</param>
/// <param name="Name">The display name of the selected tweak.</param>
public record TweakSelectedEvent(string InternalKey, string Name);

/// <summary>
/// Event fired when all tweaks are cleared from the manager.
/// </summary>
public record TweaksClearedEvent();

/// <summary>
/// Event fired when a tweak's configuration is saved.
/// </summary>
/// <param name="InternalKey">The internal key of the tweak whose configuration was saved.</param>
public record TweakConfigSavedEvent(string InternalKey);

/// <summary>
/// Event fired when tweak key migrations are executed.
/// </summary>
/// <param name="MigratedCount">The number of keys that were migrated.</param>
public record TweakKeyMigrationsExecutedEvent(int MigratedCount);
