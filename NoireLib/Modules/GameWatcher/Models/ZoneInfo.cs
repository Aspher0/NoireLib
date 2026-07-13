using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The current zone state returned by <c>watcher.Zone.Current</c>.
/// </summary>
public sealed record ZoneInfo
{
    /// <summary>The territory row id.</summary>
    public required uint TerritoryId { get; init; }

    /// <summary>The map row id.</summary>
    public required uint MapId { get; init; }

    /// <summary>The public instance number (0 when the zone is not instanced).</summary>
    public required uint Instance { get; init; }

    /// <summary>Whether the local player is inside a housing interior.</summary>
    public required bool IsInHousingInterior { get; init; }

    /// <summary>The current weather row id, or 0 when unavailable.</summary>
    public required byte WeatherId { get; init; }

    /// <summary>The current Eorzea hour (0–23).</summary>
    public required int EorzeaHour { get; init; }

    /// <summary>The current Eorzea minute (0–59).</summary>
    public required int EorzeaMinute { get; init; }

    /// <summary>The current Eorzea second (0–59).</summary>
    public required int EorzeaSecond { get; init; }

    /// <summary>The exact Eorzea time of day (hour + minute + second within the Eorzea day).</summary>
    public TimeSpan EorzeaTimeOfDay => new(EorzeaHour, EorzeaMinute, EorzeaSecond);

    /// <summary>Whether it is currently night in Eorzea (18:00–5:59 ET).</summary>
    public bool IsEorzeaNight => EorzeaHour < 6 || EorzeaHour >= 18;

    /// <summary>The UTC timestamp when the info was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
