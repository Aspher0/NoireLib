using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// The Eorzean clock, and the eight-hour windows the weather is decided in.<br/>
/// Right now comes from <c>Framework.ClientTime.EorzeaTime</c>, so a frozen or overridden clock still reads
/// correctly; any other moment is computed from real time, which <see cref="WeatherHelper"/> forecasts on.
/// </summary>
public static unsafe class EorzeaTimeHelper
{
    /// <summary>
    /// How many real seconds one Eorzean day lasts: seventy minutes.
    /// </summary>
    public const long RealSecondsPerEorzeaDay = 4200;

    /// <summary>How many real seconds one Eorzean hour lasts.</summary>
    public const long RealSecondsPerEorzeaHour = 175;

    /// <summary>How many Eorzean hours one weather window lasts.</summary>
    public const int EorzeaHoursPerWeatherWindow = 8;

    /// <summary>How many real seconds one weather window lasts, being just over twenty-three minutes.</summary>
    public const long RealSecondsPerWeatherWindow = RealSecondsPerEorzeaHour * EorzeaHoursPerWeatherWindow;

    /// <summary>The first Eorzean hour of the night. Night runs from here to <see cref="DawnHour"/>.</summary>
    public const int DuskHour = 18;

    /// <summary>The first Eorzean hour of the day.</summary>
    public const int DawnHour = 6;

    /// <summary>
    /// How many Eorzean seconds have passed since the epoch according to the client itself, which is the reading the
    /// game is actually drawing the sky from.
    /// </summary>
    /// <returns>The Eorzean seconds, or null when the client is not running.</returns>
    public static long? ClientEorzeaSeconds()
    {
        return SafeExecutor.ExecuteSafely<long?>(() =>
        {
            var framework = Framework.Instance();
            return framework == null ? null : framework->ClientTime.EorzeaTime;
        }, null);
    }

    /// <summary>
    /// Whether the client is running an overridden clock rather than the world one, as a zone that freezes
    /// the time of day does. While it is, a computed answer for the current moment will disagree with the sky.
    /// </summary>
    /// <returns>True when the clock is overridden.</returns>
    public static bool IsClientTimeOverridden()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var framework = Framework.Instance();
            return framework != null && framework->ClientTime.IsEorzeaTimeOverridden;
        });
    }

    /// <summary>The Eorzean time of day right now, from the client's own clock when it is running.</summary>
    public static TimeSpan TimeOfDay
        => ClientEorzeaSeconds() is { } seconds
            ? TimeSpan.FromSeconds(Modulo(seconds, 86400L))
            : TimeOfDayAt(DateTimeOffset.UtcNow);

    /// <summary>The Eorzean hour right now, 0 to 23.</summary>
    public static int Hour => TimeOfDay.Hours;

    /// <summary>Whether it is night in Eorzea right now.</summary>
    public static bool IsNight => IsNightHour(Hour);

    /// <summary>
    /// How many Eorzean seconds have passed since the Unix epoch at a given real moment.
    /// </summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The Eorzean seconds.</returns>
    public static long ToEorzeaSeconds(DateTimeOffset realTime)
        => realTime.ToUnixTimeSeconds() * 1440L / 70L;

    /// <summary>
    /// The Eorzean clock at a real moment, as a <see cref="DateTimeOffset"/> whose time of day is the Eorzean one.
    /// <br/>
    /// The <b>date</b> part is not a date in Eorzea and means nothing: it is simply where the Eorzean second count
    /// lands on a calendar that counts twenty times too fast. Read <see cref="DateTimeOffset.TimeOfDay"/> and the
    /// hour from it, not the day.
    /// </summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The Eorzean clock.</returns>
    public static DateTimeOffset ToEorzea(DateTimeOffset realTime)
        => DateTimeOffset.FromUnixTimeSeconds(ToEorzeaSeconds(realTime));

    /// <summary>
    /// The real moment an Eorzean clock reading corresponds to, being the inverse of <see cref="ToEorzea"/>.
    /// </summary>
    /// <param name="eorzeaTime">The Eorzean clock, as <see cref="ToEorzea"/> returns it.</param>
    /// <returns>The real moment.</returns>
    public static DateTimeOffset ToReal(DateTimeOffset eorzeaTime)
        => DateTimeOffset.FromUnixTimeSeconds(eorzeaTime.ToUnixTimeSeconds() * 70L / 1440L);

    /// <summary>The Eorzean time of day at a real moment.</summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The time of day, always under twenty-four hours.</returns>
    public static TimeSpan TimeOfDayAt(DateTimeOffset realTime)
        => TimeSpan.FromSeconds(ToEorzeaSeconds(realTime) % 86400L);

    /// <summary>The Eorzean hour at a real moment, 0 to 23.</summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The hour.</returns>
    public static int HourAt(DateTimeOffset realTime) => TimeOfDayAt(realTime).Hours;

    /// <summary>Whether it is night in Eorzea at a real moment.</summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>True at night.</returns>
    public static bool IsNightAt(DateTimeOffset realTime) => IsNightHour(HourAt(realTime));

    /// <summary>Whether an Eorzean hour falls at night, which is the rule with no clock behind it.</summary>
    /// <param name="hour">The Eorzean hour, 0 to 23.</param>
    /// <returns>True at night.</returns>
    public static bool IsNightHour(int hour) => hour < DawnHour || hour >= DuskHour;

    /// <summary>
    /// The real moment the weather window containing a real moment began.<br/>
    /// Weather changes every eight Eorzean hours, so a window always starts at Eorzean 00:00, 08:00 or 16:00.
    /// </summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The real moment the window began.</returns>
    public static DateTimeOffset WeatherWindowStart(DateTimeOffset realTime)
    {
        var seconds = realTime.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(seconds - Modulo(seconds, RealSecondsPerWeatherWindow));
    }

    /// <summary>The real moment the next weather window begins.</summary>
    /// <param name="realTime">The real moment.</param>
    /// <returns>The real moment the next window begins.</returns>
    public static DateTimeOffset NextWeatherWindow(DateTimeOffset realTime)
        => WeatherWindowStart(realTime).AddSeconds(RealSecondsPerWeatherWindow);

    /// <summary>The Eorzean hour a weather window starts at: 0, 8 or 16.</summary>
    /// <param name="realTime">Any real moment inside the window.</param>
    /// <returns>The window's starting Eorzean hour.</returns>
    public static int WeatherWindowHour(DateTimeOffset realTime) => HourAt(WeatherWindowStart(realTime));

    /// <summary>
    /// The starts of consecutive weather windows, beginning with the one containing the given moment.
    /// </summary>
    /// <param name="from">The real moment to start from.</param>
    /// <param name="count">How many windows to produce.</param>
    /// <returns>The window start times, in order.</returns>
    public static IReadOnlyList<DateTimeOffset> WeatherWindows(DateTimeOffset from, int count)
    {
        if (count <= 0)
            return [];

        var windows = new List<DateTimeOffset>(count);
        var start = WeatherWindowStart(from);

        for (var i = 0; i < count; i++)
            windows.Add(start.AddSeconds(RealSecondsPerWeatherWindow * i));

        return windows;
    }

    /// <summary>
    /// The Eorzean hour a territory's clock is frozen at, for the zones that do not run the world clock at all
    /// (many instanced and cutscene territories). Null when the territory follows the normal clock.
    /// <br/>
    /// This reflects the sheet only; for what the client is doing right now, which also catches a cutscene or quest
    /// phase overriding the clock in a zone whose row says nothing, ask <see cref="IsClientTimeOverridden"/>.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The frozen time of day, or null.</returns>
    public static TimeSpan? FixedTimeOfDay(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely<TimeSpan?>(() =>
        {
            if (!ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) || row is not { } territory)
                return null;

            // The sheet stores minutes past Eorzean midnight, and uses a negative value for "not frozen".
            return territory.FixedTime < 0 ? null : TimeSpan.FromMinutes(territory.FixedTime % 1440);
        }, null);
    }

    /// <summary>A modulo that stays non-negative for moments before the Unix epoch.</summary>
    private static long Modulo(long value, long divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
