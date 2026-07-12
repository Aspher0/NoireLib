using Dalamud.Game.ClientState.Fates;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when a fate appears in the current zone.
/// </summary>
/// <param name="Fate">The fate's first snapshot.</param>
public sealed record FateSpawnedEvent(FateSnapshot Fate);

/// <summary>
/// Fired when a fate disappears from the current zone (completed, failed or expired).
/// </summary>
/// <param name="Fate">The fate's last known snapshot.</param>
public sealed record FateExpiredEvent(FateSnapshot Fate);

/// <summary>
/// Fired when a fate's completion progress changes.
/// </summary>
/// <param name="Fate">The fate's current snapshot.</param>
/// <param name="PreviousProgress">The previous progress (0–100).</param>
public sealed record FateProgressChangedEvent(FateSnapshot Fate, byte PreviousProgress);

/// <summary>
/// Fired when a fate's state changes (preparing, running, ending, …).
/// </summary>
/// <param name="Fate">The fate's current snapshot.</param>
/// <param name="PreviousState">The previous state.</param>
public sealed record FateStateChangedEvent(FateSnapshot Fate, FateState PreviousState);

/// <summary>
/// Fired when the zone weather changes.
/// </summary>
/// <param name="PreviousWeatherId">The previous weather row id.</param>
/// <param name="WeatherId">The new weather row id.</param>
public sealed record WeatherChangedEvent(byte PreviousWeatherId, byte WeatherId);

/// <summary>
/// Fired when the Eorzea hour changes.
/// </summary>
/// <param name="Hour">The new Eorzea hour (0–23).</param>
public sealed record EorzeaHourChangedEvent(int Hour);

/// <summary>
/// Fired when Eorzea transitions between day (6:00–17:59 ET) and night.
/// </summary>
/// <param name="IsNight">Whether it is now night.</param>
public sealed record EorzeaDayNightChangedEvent(bool IsNight);

/// <summary>
/// Fired when a normal toast is shown.
/// </summary>
/// <param name="Message">The toast text.</param>
public sealed record ToastShownEvent(string Message);

/// <summary>
/// Fired when a quest toast is shown.
/// </summary>
/// <param name="Message">The toast text.</param>
public sealed record QuestToastShownEvent(string Message);

/// <summary>
/// Fired when an error toast is shown.
/// </summary>
/// <param name="Message">The toast text.</param>
public sealed record ErrorToastShownEvent(string Message);
