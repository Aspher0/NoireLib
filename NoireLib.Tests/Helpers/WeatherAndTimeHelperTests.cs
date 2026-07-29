using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for <see cref="EorzeaTimeHelper"/> and <see cref="WeatherHelper"/>.
/// <br/><br/>
/// Both are pure functions of real time, which is the whole reason a forecast is possible, and it is also what makes
/// them testable with no client behind them. Only the rate table has to come from the sheets, so the tests that need
/// one build it by hand and drive the rate-taking overloads.
/// </summary>
[SupportedOSPlatform("windows")]
public class WeatherAndTimeHelperTests
{
    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    #region The Eorzean clock

    [Fact]
    public void The_epoch_is_Eorzean_midnight()
        => EorzeaTimeHelper.TimeOfDayAt(At(0)).Should().Be(TimeSpan.Zero);

    [Fact]
    public void An_Eorzean_hour_is_a_hundred_and_seventy_five_real_seconds()
    {
        EorzeaTimeHelper.HourAt(At(175)).Should().Be(1);
        EorzeaTimeHelper.HourAt(At(175 * 5)).Should().Be(5);
        EorzeaTimeHelper.HourAt(At(175 * 23)).Should().Be(23);
    }

    [Fact]
    public void An_Eorzean_day_is_seventy_real_minutes()
    {
        EorzeaTimeHelper.RealSecondsPerEorzeaDay.Should().Be(70 * 60);
        EorzeaTimeHelper.HourAt(At(EorzeaTimeHelper.RealSecondsPerEorzeaDay)).Should().Be(0);
        EorzeaTimeHelper.TimeOfDayAt(At(EorzeaTimeHelper.RealSecondsPerEorzeaDay)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_clock_round_trips_through_real_time()
    {
        var real = At(1_700_000_000);

        var back = EorzeaTimeHelper.ToReal(EorzeaTimeHelper.ToEorzea(real));

        // Both directions truncate to whole seconds, and a real second is worth twenty Eorzean ones, so the round
        // trip can lose up to a full real second. It cannot lose more than that.
        (back - real).Duration().Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(1));

        // What has to survive exactly is the reading on the clock, which is the only part anything uses.
        EorzeaTimeHelper.ToEorzea(real).TimeOfDay.Should().Be(EorzeaTimeHelper.TimeOfDayAt(real));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(23, true)]
    public void Night_runs_from_eighteen_to_six(int hour, bool expected)
        => EorzeaTimeHelper.IsNightHour(hour).Should().Be(expected);

    #endregion

    #region Weather windows

    [Fact]
    public void A_weather_window_is_eight_Eorzean_hours()
        => EorzeaTimeHelper.RealSecondsPerWeatherWindow.Should().Be(1400);

    [Fact]
    public void A_window_always_starts_at_Eorzean_midnight_eight_or_sixteen()
    {
        foreach (var offset in new long[] { 0, 1, 699, 1399, 1400, 123_456, 1_700_000_000 })
            EorzeaTimeHelper.WeatherWindowHour(At(offset)).Should().BeOneOf(0, 8, 16);
    }

    [Fact]
    public void Every_moment_inside_a_window_resolves_to_the_same_start()
    {
        var start = EorzeaTimeHelper.WeatherWindowStart(At(1_700_000_000));

        EorzeaTimeHelper.WeatherWindowStart(start).Should().Be(start);
        EorzeaTimeHelper.WeatherWindowStart(start.AddSeconds(1)).Should().Be(start);
        EorzeaTimeHelper.WeatherWindowStart(start.AddSeconds(1399)).Should().Be(start);
        EorzeaTimeHelper.WeatherWindowStart(start.AddSeconds(1400)).Should().NotBe(start);
    }

    [Fact]
    public void The_next_window_is_one_window_later()
    {
        var now = At(1_700_000_123);

        (EorzeaTimeHelper.NextWeatherWindow(now) - EorzeaTimeHelper.WeatherWindowStart(now))
            .Should().Be(TimeSpan.FromSeconds(1400));
    }

    [Fact]
    public void Windows_are_produced_in_order_and_evenly_spaced()
    {
        var windows = EorzeaTimeHelper.WeatherWindows(At(1_700_000_123), 5);

        windows.Should().HaveCount(5);
        windows.Should().BeInAscendingOrder();

        for (var i = 1; i < windows.Count; i++)
            (windows[i] - windows[i - 1]).Should().Be(TimeSpan.FromSeconds(1400));
    }

    [Fact]
    public void Asking_for_no_windows_produces_none()
        => EorzeaTimeHelper.WeatherWindows(DateTimeOffset.UtcNow, 0).Should().BeEmpty();

    #endregion

    #region The weather roll

    [Fact]
    public void The_roll_at_the_epoch_is_the_hand_computed_value()
    {
        // bell 0, increment (0 + 8 - 0) % 24 = 8, totalDays 0, so calcBase 8.
        // step1 = (8 << 11) ^ 8 = 16392; step2 = (16392 >> 8) ^ 16392 = 16456; 16456 % 100 = 56.
        WeatherHelper.ChanceAt(At(0)).Should().Be(56);
    }

    [Fact]
    public void The_roll_in_the_second_window_is_the_hand_computed_value()
    {
        // bell 8, increment (8 + 8 - 0) % 24 = 16, totalDays 0, so calcBase 16.
        // step1 = (16 << 11) ^ 16 = 32784; step2 = (32784 >> 8) ^ 32784 = 32912; 32912 % 100 = 12.
        WeatherHelper.ChanceAt(At(1400)).Should().Be(12);
    }

    [Fact]
    public void The_roll_is_constant_across_a_window_and_moves_between_them()
    {
        var start = EorzeaTimeHelper.WeatherWindowStart(At(1_700_000_000));
        var expected = WeatherHelper.ChanceAt(start);

        WeatherHelper.ChanceAt(start.AddSeconds(1)).Should().Be(expected);
        WeatherHelper.ChanceAt(start.AddSeconds(1399)).Should().Be(expected);

        var rolls = new HashSet<int>();
        for (var i = 0; i < 40; i++)
            rolls.Add(WeatherHelper.ChanceAt(start.AddSeconds(1400 * i)));

        rolls.Should().HaveCountGreaterThan(1, "consecutive windows must not all roll the same number");
    }

    [Fact]
    public void The_roll_always_lands_in_range()
    {
        for (var i = 0; i < 500; i++)
            WeatherHelper.ChanceAt(At(1_700_000_000 + 1400 * i)).Should().BeInRange(0, 99);
    }

    [Fact]
    public void The_roll_is_spread_across_the_range()
    {
        var buckets = new HashSet<int>();

        for (var i = 0; i < 2000; i++)
            buckets.Add(WeatherHelper.ChanceAt(At(1400L * i)) / 10);

        buckets.Should().HaveCount(10, "a roll that never reaches a tenth of the range would skew every rate table");
    }

    #endregion

    #region Resolving a rate table

    private static readonly IReadOnlyList<(uint WeatherId, byte Rate)> Rates =
    [
        (1u, (byte)30),
        (2u, (byte)40),
        (3u, (byte)30),
    ];

    [Theory]
    [InlineData(0, 1u)]
    [InlineData(29, 1u)]
    [InlineData(30, 2u)]
    [InlineData(69, 2u)]
    [InlineData(70, 3u)]
    [InlineData(99, 3u)]
    public void A_roll_picks_the_weather_its_share_belongs_to(int chance, uint expected)
        => WeatherHelper.Resolve(Rates, chance).Should().Be(expected);

    [Fact]
    public void An_empty_table_resolves_to_nothing()
        => WeatherHelper.Resolve([], 50).Should().Be(0u);

    [Fact]
    public void A_table_that_does_not_reach_a_hundred_answers_its_last_entry()
        => WeatherHelper.Resolve([(7u, (byte)10)], 90).Should().Be(7u);

    #endregion

    #region Forecasting

    [Fact]
    public void A_forecast_is_contiguous_and_covers_its_own_windows()
    {
        var forecast = WeatherHelper.Forecast(Rates, territoryId: 132, windows: 6, from: At(1_700_000_000));

        forecast.Should().HaveCount(6);
        forecast.Should().OnlyContain(w => w.TerritoryId == 132);
        forecast.Should().OnlyContain(w => w.Duration == TimeSpan.FromSeconds(1400));
        forecast[0].Contains(At(1_700_000_000)).Should().BeTrue();

        for (var i = 1; i < forecast.Count; i++)
            forecast[i].Start.Should().Be(forecast[i - 1].End);
    }

    [Fact]
    public void A_forecast_agrees_with_a_point_lookup()
    {
        var from = At(1_700_000_000);

        foreach (var window in WeatherHelper.Forecast(Rates, 132, 20, from))
            WeatherHelper.Resolve(Rates, WeatherHelper.ChanceAt(window.Start)).Should().Be(window.WeatherId);
    }

    [Fact]
    public void An_empty_rate_table_forecasts_nothing()
        => WeatherHelper.Forecast([], 132, 5, At(0)).Should().BeEmpty();

    [Fact]
    public void The_search_finds_the_first_window_matching_the_predicate()
    {
        var from = At(1_700_000_000);

        var found = WeatherHelper.FindNextTransition(Rates, 132, (_, current) => current == 3u, 500, from);

        found.Should().NotBeNull();
        found!.Value.WeatherId.Should().Be(3u);
        found.Value.Start.Should().BeOnOrAfter(EorzeaTimeHelper.WeatherWindowStart(from));

        // Nothing between the start and the hit may also match, or it was not the first.
        var before = WeatherHelper.Forecast(Rates, 132, 500, from)
            .TakeWhile(w => w.Start < found.Value.Start);

        before.Should().OnlyContain(w => w.WeatherId != 3u);
    }

    [Fact]
    public void The_search_can_match_on_the_window_before()
    {
        var from = At(1_700_000_000);

        var found = WeatherHelper.FindNextTransition(Rates, 132, (previous, current) => previous == 1u && current == 3u, 2000, from);

        found.Should().NotBeNull();
        found!.Value.WeatherId.Should().Be(3u);

        var previousStart = found.Value.Start.AddSeconds(-1400);
        WeatherHelper.Resolve(Rates, WeatherHelper.ChanceAt(previousStart)).Should().Be(1u);
    }

    [Fact]
    public void A_search_that_finds_nothing_answers_null()
        => WeatherHelper.FindNextTransition(Rates, 132, (_, _) => false, 50, At(0)).Should().BeNull();

    [Fact]
    public void A_search_with_no_windows_to_look_through_answers_null()
        => WeatherHelper.FindNextTransition(Rates, 132, (_, _) => true, 0, At(0)).Should().BeNull();

    #endregion
}
