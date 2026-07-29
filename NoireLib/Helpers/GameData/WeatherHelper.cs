using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One weather window: what the weather is, when it starts, and how long it lasts.</summary>
/// <param name="WeatherId">The Weather row id.</param>
/// <param name="TerritoryId">The territory the window was computed for.</param>
/// <param name="Start">The real moment the window begins.</param>
/// <param name="End">The real moment the window ends, which is the next window's start.</param>
/// <param name="Chance">
/// The roll the window was decided by, 0 to 99. Two territories sharing a moment share this number and can still
/// land on different weather, because each has its own rate table.
/// </param>
public readonly record struct WeatherWindow(
    uint WeatherId,
    uint TerritoryId,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Chance)
{
    /// <summary>The window's length, which is always one weather window.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Whether a real moment falls inside this window.</summary>
    /// <param name="realTime">The moment to test.</param>
    /// <returns>True when it falls inside.</returns>
    public bool Contains(DateTimeOffset realTime) => realTime >= Start && realTime < End;
}

/// <summary>
/// What the weather is, and what it will be.<br/>
/// <b>Weather is not stored anywhere; it is computed.</b> The game rolls a number from the moment alone, the same
/// number on every client and every world, and looks it up in the territory's own rate table, so a window weeks
/// away is as readable as the current one.<br/>
/// <see cref="Active"/> reads what the client is actually showing, the one thing here that needs a running game.
/// Everything else is arithmetic over the sheets.
/// </summary>
public static class WeatherHelper
{
    /// <summary>How many weather entries a rate table can list.</summary>
    public const int MaxRateEntries = 8;

    /// <summary>
    /// The weather the client is currently showing, read live from the game. Zero when there is no game behind it.
    /// <br/>
    /// This is the ground truth, and it can differ from <see cref="Current"/> in a territory whose weather is set by
    /// something other than the clock (a duty, a cutscene, a quest phase).
    /// </summary>
    /// <returns>The Weather row id, or zero.</returns>
    public static unsafe byte Active()
    {
        return SafeExecutor.ExecuteSafely<byte>(() =>
        {
            if (!NoireService.IsInitialized())
                return 0;

            var manager = FFXIVClientStructs.FFXIV.Client.Game.WeatherManager.Instance();
            return manager == null ? (byte)0 : manager->GetCurrentWeather();
        }, 0);
    }

    /// <summary>Resolves a weather's name in the client's own language, or empty when it does not resolve.</summary>
    /// <param name="weatherId">The Weather row id.</param>
    /// <returns>The name, or empty.</returns>
    public static string Name(uint weatherId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (weatherId != 0 && ExcelSheetHelper.TryGetRow<Weather>(weatherId, out var row) && row is { } weather)
                return weather.Name.ExtractText() ?? string.Empty;

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// The roll that decides a window's weather, 0 to 99, computed from the moment alone.<br/>
    /// This is the game's own derivation, identical on every client, so a forecast agrees with what a player will
    /// actually see.
    /// </summary>
    /// <param name="realTime">Any real moment; the window containing it is what gets rolled.</param>
    /// <returns>The roll, 0 to 99.</returns>
    public static int ChanceAt(DateTimeOffset realTime)
    {
        var seconds = EorzeaTimeHelper.WeatherWindowStart(realTime).ToUnixTimeSeconds();

        // Eorzean hours since the epoch, snapped to the end of the window it falls in: the game seeds from the
        // boundary rather than the hour, so 16:00 seeds as 0 and midnight as 8.
        var bell = seconds / EorzeaTimeHelper.RealSecondsPerEorzeaHour;
        var increment = (bell + EorzeaTimeHelper.EorzeaHoursPerWeatherWindow
                              - (bell % EorzeaTimeHelper.EorzeaHoursPerWeatherWindow)) % 24;

        var totalDays = (uint)(seconds / EorzeaTimeHelper.RealSecondsPerEorzeaDay);
        var calcBase = (uint)(totalDays * 100 + increment);

        var step1 = (calcBase << 11) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;

        return (int)(step2 % 100);
    }

    /// <summary>
    /// Reads a territory's weather rate table: which weathers it can have and how likely each is.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The weathers and their rates, in sheet order. Empty when the territory names no table.</returns>
    public static IReadOnlyList<(uint WeatherId, byte Rate)> ReadRates(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var rates = new List<(uint, byte)>(MaxRateEntries);

            if (!ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var territoryRow) ||
                territoryRow is not { } territory ||
                territory.WeatherRate.ValueNullable is not { } table)
            {
                return (IReadOnlyList<(uint, byte)>)rates;
            }

            var count = Math.Min(table.Weather.Count, table.Rate.Count);

            for (var i = 0; i < count; i++)
            {
                var weatherId = table.Weather[i].RowId;
                var rate = table.Rate[i];

                if (weatherId != 0 && rate != 0)
                    rates.Add((weatherId, rate));
            }

            return rates;
        }, []) ?? [];
    }

    /// <summary>Every weather a territory can have, which is its rate table without the rates.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Weather row ids.</returns>
    public static IReadOnlySet<uint> PossibleWeathers(uint territoryId)
    {
        var weathers = new HashSet<uint>();

        foreach (var (weatherId, _) in ReadRates(territoryId))
            weathers.Add(weatherId);

        return weathers;
    }

    /// <summary>
    /// The weather a territory has at a real moment.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="realTime">Any real moment; the window containing it is what gets answered.</param>
    /// <returns>The Weather row id, or zero when the territory names no rate table.</returns>
    public static uint WeatherAt(uint territoryId, DateTimeOffset realTime)
        => Resolve(ReadRates(territoryId), ChanceAt(realTime));

    /// <summary>The weather a territory has right now, computed rather than read from the client.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Weather row id, or zero.</returns>
    public static uint Current(uint territoryId) => WeatherAt(territoryId, DateTimeOffset.UtcNow);

    /// <summary>
    /// The next several weather windows for a territory, starting with the one in progress.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="windows">How many windows to produce.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The windows, in order. Empty when the territory names no rate table.</returns>
    public static IReadOnlyList<WeatherWindow> Forecast(uint territoryId, int windows = 10, DateTimeOffset? from = null)
        => Forecast(ReadRates(territoryId), territoryId, windows, from);

    /// <summary>
    /// Forecasts against a rate table the caller already holds, or a hypothetical one.<br/>
    /// Reading the table is the only part of a forecast that needs the game, so this is also the form that runs
    /// without one.
    /// </summary>
    /// <param name="rates">The rate table, from <see cref="ReadRates"/> or built by hand.</param>
    /// <param name="territoryId">The territory to stamp on each window.</param>
    /// <param name="windows">How many windows to produce.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The windows, in order.</returns>
    public static IReadOnlyList<WeatherWindow> Forecast(
        IReadOnlyList<(uint WeatherId, byte Rate)> rates,
        uint territoryId,
        int windows = 10,
        DateTimeOffset? from = null)
    {
        ArgumentNullException.ThrowIfNull(rates);

        if (rates.Count == 0 || windows <= 0)
            return [];

        var results = new List<WeatherWindow>(windows);
        var start = EorzeaTimeHelper.WeatherWindowStart(from ?? DateTimeOffset.UtcNow);

        for (var i = 0; i < windows; i++)
        {
            var windowStart = start.AddSeconds(EorzeaTimeHelper.RealSecondsPerWeatherWindow * i);
            results.Add(BuildWindow(territoryId, rates, windowStart));
        }

        return results;
    }

    /// <summary>
    /// Searches forward for the next window whose weather satisfies a predicate: how a timed node, a fish or a
    /// spawn condition gets waited for.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="matches">Given a Weather row id, whether this window is the one being waited for.</param>
    /// <param name="searchWindows">How many windows to look through before giving up.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The first matching window, or null when none was found inside the search.</returns>
    public static WeatherWindow? FindNext(
        uint territoryId,
        Func<uint, bool> matches,
        int searchWindows = 1000,
        DateTimeOffset? from = null)
    {
        ArgumentNullException.ThrowIfNull(matches);

        return FindNextTransition(territoryId, (_, current) => matches(current), searchWindows, from);
    }

    /// <summary>
    /// Searches forward for the next window whose weather is one of a set.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="weatherIds">The Weather row ids being waited for.</param>
    /// <param name="searchWindows">How many windows to look through before giving up.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The first matching window, or null.</returns>
    public static WeatherWindow? FindNext(
        uint territoryId,
        IReadOnlySet<uint> weatherIds,
        int searchWindows = 1000,
        DateTimeOffset? from = null)
    {
        ArgumentNullException.ThrowIfNull(weatherIds);

        return weatherIds.Count == 0
            ? null
            : FindNextTransition(territoryId, (_, current) => weatherIds.Contains(current), searchWindows, from);
    }

    /// <summary>
    /// Searches forward for the next window matching a predicate over <b>both</b> the previous window's weather and
    /// this one's.<br/>
    /// A great many timed conditions are stated as a transition ("clear skies, after fog"), and a predicate over the
    /// current window alone cannot express one.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="matches">Given the previous and the current Weather row ids, whether this window is the one.</param>
    /// <param name="searchWindows">How many windows to look through before giving up.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The first matching window, or null when none was found inside the search.</returns>
    public static WeatherWindow? FindNextTransition(
        uint territoryId,
        Func<uint, uint, bool> matches,
        int searchWindows = 1000,
        DateTimeOffset? from = null)
        => FindNextTransition(ReadRates(territoryId), territoryId, matches, searchWindows, from);

    /// <summary>
    /// Searches against a rate table the caller already holds, or a hypothetical one.
    /// </summary>
    /// <param name="rates">The rate table, from <see cref="ReadRates"/> or built by hand.</param>
    /// <param name="territoryId">The territory to stamp on the window.</param>
    /// <param name="matches">Given the previous and the current Weather row ids, whether this window is the one.</param>
    /// <param name="searchWindows">How many windows to look through before giving up.</param>
    /// <param name="from">The moment to start from, or null for now.</param>
    /// <returns>The first matching window, or null.</returns>
    public static WeatherWindow? FindNextTransition(
        IReadOnlyList<(uint WeatherId, byte Rate)> rates,
        uint territoryId,
        Func<uint, uint, bool> matches,
        int searchWindows = 1000,
        DateTimeOffset? from = null)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(matches);

        if (rates.Count == 0 || searchWindows <= 0)
            return null;

        var start = EorzeaTimeHelper.WeatherWindowStart(from ?? DateTimeOffset.UtcNow);
        var previous = Resolve(rates, ChanceAt(start.AddSeconds(-EorzeaTimeHelper.RealSecondsPerWeatherWindow)));

        for (var i = 0; i < searchWindows; i++)
        {
            var windowStart = start.AddSeconds(EorzeaTimeHelper.RealSecondsPerWeatherWindow * i);
            var window = BuildWindow(territoryId, rates, windowStart);

            if (matches(previous, window.WeatherId))
                return window;

            previous = window.WeatherId;
        }

        return null;
    }

    /// <summary>
    /// Picks the weather a roll lands on, which is the pure rule over a rate table and needs no game.
    /// </summary>
    /// <param name="rates">The territory's rate table, from <see cref="ReadRates"/>.</param>
    /// <param name="chance">The roll, 0 to 99.</param>
    /// <returns>The Weather row id, or zero for an empty table.</returns>
    public static uint Resolve(IReadOnlyList<(uint WeatherId, byte Rate)> rates, int chance)
    {
        ArgumentNullException.ThrowIfNull(rates);

        var cumulative = 0;

        foreach (var (weatherId, rate) in rates)
        {
            cumulative += rate;

            if (chance < cumulative)
                return weatherId;
        }

        // The rates in a well-formed table sum to a hundred, so this is only reached for a malformed one. The last
        // entry is a better answer than nothing, since it is what the missing share would have belonged to.
        return rates.Count > 0 ? rates[^1].WeatherId : 0;
    }

    private static WeatherWindow BuildWindow(
        uint territoryId,
        IReadOnlyList<(uint WeatherId, byte Rate)> rates,
        DateTimeOffset windowStart)
    {
        var chance = ChanceAt(windowStart);

        return new WeatherWindow(
            Resolve(rates, chance),
            territoryId,
            windowStart,
            windowStart.AddSeconds(EorzeaTimeHelper.RealSecondsPerWeatherWindow),
            chance);
    }
}
