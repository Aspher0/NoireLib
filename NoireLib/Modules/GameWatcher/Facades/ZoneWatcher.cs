using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Zone facts: territory, map and instance changes, housing-interior transitions, weather and Eorzea time.
/// </summary>
public sealed class ZoneWatcher : GameWatcherFacade
{
    internal ZoneWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to territory changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnTerritoryChanged(Action<TerritoryChangedEvent> handler, NoireSubscriptionOptions<TerritoryChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnTerritoryChanged));

    /// <inheritdoc cref="OnTerritoryChanged(Action{TerritoryChangedEvent}, NoireSubscriptionOptions{TerritoryChangedEvent}?)"/>
    public NoireSubscriptionToken OnTerritoryChangedAsync(Func<TerritoryChangedEvent, Task> handler, NoireSubscriptionOptions<TerritoryChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnTerritoryChanged));

    /// <summary>
    /// Subscribes to map changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMapChanged(Action<MapChangedEvent> handler, NoireSubscriptionOptions<MapChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnMapChanged));

    /// <inheritdoc cref="OnMapChanged(Action{MapChangedEvent}, NoireSubscriptionOptions{MapChangedEvent}?)"/>
    public NoireSubscriptionToken OnMapChangedAsync(Func<MapChangedEvent, Task> handler, NoireSubscriptionOptions<MapChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnMapChanged));

    /// <summary>
    /// Subscribes to public-instance number changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnInstanceChanged(Action<InstanceChangedEvent> handler, NoireSubscriptionOptions<InstanceChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnInstanceChanged));

    /// <inheritdoc cref="OnInstanceChanged(Action{InstanceChangedEvent}, NoireSubscriptionOptions{InstanceChangedEvent}?)"/>
    public NoireSubscriptionToken OnInstanceChangedAsync(Func<InstanceChangedEvent, Task> handler, NoireSubscriptionOptions<InstanceChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnInstanceChanged));

    /// <summary>
    /// Subscribes to housing-interior entries.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnHousingInteriorEntered(Action<HousingInteriorEnteredEvent> handler, NoireSubscriptionOptions<HousingInteriorEnteredEvent>? options = null)
        => On(handler, null, options, nameof(OnHousingInteriorEntered));

    /// <inheritdoc cref="OnHousingInteriorEntered(Action{HousingInteriorEnteredEvent}, NoireSubscriptionOptions{HousingInteriorEnteredEvent}?)"/>
    public NoireSubscriptionToken OnHousingInteriorEnteredAsync(Func<HousingInteriorEnteredEvent, Task> handler, NoireSubscriptionOptions<HousingInteriorEnteredEvent>? options = null)
        => On(null, handler, options, nameof(OnHousingInteriorEntered));

    /// <summary>
    /// Subscribes to housing-interior exits.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnHousingInteriorLeft(Action<HousingInteriorLeftEvent> handler, NoireSubscriptionOptions<HousingInteriorLeftEvent>? options = null)
        => On(handler, null, options, nameof(OnHousingInteriorLeft));

    /// <inheritdoc cref="OnHousingInteriorLeft(Action{HousingInteriorLeftEvent}, NoireSubscriptionOptions{HousingInteriorLeftEvent}?)"/>
    public NoireSubscriptionToken OnHousingInteriorLeftAsync(Func<HousingInteriorLeftEvent, Task> handler, NoireSubscriptionOptions<HousingInteriorLeftEvent>? options = null)
        => On(null, handler, options, nameof(OnHousingInteriorLeft));

    /// <summary>
    /// Subscribes to weather changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnWeatherChanged(Action<WeatherChangedEvent> handler, NoireSubscriptionOptions<WeatherChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnWeatherChanged));

    /// <inheritdoc cref="OnWeatherChanged(Action{WeatherChangedEvent}, NoireSubscriptionOptions{WeatherChangedEvent}?)"/>
    public NoireSubscriptionToken OnWeatherChangedAsync(Func<WeatherChangedEvent, Task> handler, NoireSubscriptionOptions<WeatherChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnWeatherChanged));

    /// <summary>
    /// Subscribes to Eorzea hour changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEorzeaHourChanged(Action<EorzeaHourChangedEvent> handler, NoireSubscriptionOptions<EorzeaHourChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnEorzeaHourChanged));

    /// <inheritdoc cref="OnEorzeaHourChanged(Action{EorzeaHourChangedEvent}, NoireSubscriptionOptions{EorzeaHourChangedEvent}?)"/>
    public NoireSubscriptionToken OnEorzeaHourChangedAsync(Func<EorzeaHourChangedEvent, Task> handler, NoireSubscriptionOptions<EorzeaHourChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnEorzeaHourChanged));

    /// <summary>
    /// Subscribes to Eorzea day/night transitions.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnDayNightChanged(Action<EorzeaDayNightChangedEvent> handler, NoireSubscriptionOptions<EorzeaDayNightChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnDayNightChanged));

    /// <inheritdoc cref="OnDayNightChanged(Action{EorzeaDayNightChangedEvent}, NoireSubscriptionOptions{EorzeaDayNightChangedEvent}?)"/>
    public NoireSubscriptionToken OnDayNightChangedAsync(Func<EorzeaDayNightChangedEvent, Task> handler, NoireSubscriptionOptions<EorzeaDayNightChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnDayNightChanged));

    /// <summary>
    /// The current zone state: territory, map, instance, housing, weather and Eorzea time.
    /// Live read (framework thread only); never activates anything.
    /// </summary>
    public ZoneInfo Current
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();

            var now = DateTimeOffset.UtcNow;
            var hour = EorzeaTimeSource.ComputeEorzeaHour(now);

            return new ZoneInfo
            {
                TerritoryId = NoireService.ClientState.TerritoryType,
                MapId = NoireService.ClientState.MapId,
                Instance = NoireService.ClientState.Instance,
                IsInHousingInterior = SessionSource.ReadIsInside(),
                WeatherId = WeatherSource.ReadCurrentWeather(),
                EorzeaHour = hour,
                CapturedAt = now,
            };
        }
    }

    /// <summary>The current territory row id. Live read (framework thread only).</summary>
    public uint TerritoryId
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.ClientState.TerritoryType;
        }
    }

    /// <summary>Whether the local player is logged in. Live read (framework thread only).</summary>
    public bool IsLoggedIn
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.ClientState.IsLoggedIn;
        }
    }
}
