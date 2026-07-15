using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Diffs the fate table at a slow cadence: spawned/expired/progress/state changes.
/// </summary>
internal sealed class FateSource : GameWatcherSource
{
    private readonly Dictionary<ushort, FateSnapshot> baseline = new();

    public FateSource(NoireGameWatcher owner) : base(owner, SourceKind.Fate) { }

    /// <inheritdoc/>
    protected override TimeSpan DefaultPollCadence => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        baseline.Clear();
        SeedBaseline();
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
        => baseline.Clear();

    private void SeedBaseline()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var fate in NoireService.FateTable)
        {
            if (fate != null)
                baseline[fate.FateId] = Capture(fate, now);
        }
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        var survivors = new HashSet<ushort>();

        foreach (var fate in NoireService.FateTable)
        {
            if (fate == null)
                continue;

            var snapshot = Capture(fate, now);
            survivors.Add(snapshot.FateId);

            if (!baseline.TryGetValue(snapshot.FateId, out var previous))
            {
                baseline[snapshot.FateId] = snapshot;
                Owner.DispatchEvent(new FateSpawnedEvent(snapshot));
                continue;
            }

            if (previous.Progress != snapshot.Progress)
                Owner.DispatchEvent(new FateProgressChangedEvent(snapshot, previous.Progress));

            if (previous.State != snapshot.State)
                Owner.DispatchEvent(new FateStateChangedEvent(snapshot, previous.State));

            baseline[snapshot.FateId] = snapshot;
        }

        List<ushort>? gone = null;

        foreach (var fateId in baseline.Keys)
        {
            if (!survivors.Contains(fateId))
                (gone ??= new List<ushort>()).Add(fateId);
        }

        if (gone != null)
        {
            foreach (var fateId in gone)
            {
                Owner.DispatchEvent(new FateExpiredEvent(baseline[fateId]));
                baseline.Remove(fateId);
            }
        }
    }

    /// <summary>Captures a fate snapshot, also used by facade queries.</summary>
    internal static FateSnapshot Capture(Dalamud.Game.ClientState.Fates.IFate fate, DateTimeOffset now) => new()
    {
        FateId = fate.FateId,
        Name = fate.Name.TextValue,
        State = fate.State,
        Progress = fate.Progress,
        Level = fate.Level,
        Position = fate.Position,
        Radius = fate.Radius,
        TimeRemaining = fate.TimeRemaining,
        HasBonus = fate.HasBonus,
        CapturedAt = now,
    };
}

/// <summary>
/// Polls the current zone weather at a slow cadence.
/// </summary>
internal sealed class WeatherSource : GameWatcherSource
{
    private byte lastWeatherId;

    public WeatherSource(NoireGameWatcher owner) : base(owner, SourceKind.Weather) { }

    /// <inheritdoc/>
    protected override TimeSpan DefaultPollCadence => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    protected override void OnActivate()
        => lastWeatherId = ReadCurrentWeather();

    /// <inheritdoc/>
    protected override void OnDeactivate() { }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        var weatherId = ReadCurrentWeather();

        if (weatherId == lastWeatherId)
            return;

        var previous = lastWeatherId;
        lastWeatherId = weatherId;

        if (previous != 0 || weatherId != 0)
            Owner.DispatchEvent(new WeatherChangedEvent(previous, weatherId));
    }

    /// <summary>Reads the current weather row id from game memory (live read).</summary>
    internal static unsafe byte ReadCurrentWeather()
    {
        var manager = FFXIVClientStructs.FFXIV.Client.Game.WeatherManager.Instance();
        return manager == null ? (byte)0 : manager->GetCurrentWeather();
    }
}

/// <summary>
/// The Eorzea clock, computed deterministically from real time (1 Eorzea day = 70 real minutes):
/// hour changes and day/night transitions.
/// </summary>
internal sealed class EorzeaTimeSource : GameWatcherSource
{
    private int lastHour = -1;
    private bool lastIsNight;

    public EorzeaTimeSource(NoireGameWatcher owner) : base(owner, SourceKind.EorzeaTime) { }

    /// <inheritdoc/>
    protected override TimeSpan DefaultPollCadence => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        lastHour = ComputeEorzeaHour(DateTimeOffset.UtcNow);
        lastIsNight = IsNight(lastHour);
    }

    /// <inheritdoc/>
    protected override void OnDeactivate() { }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        var hour = ComputeEorzeaHour(now);

        if (hour == lastHour)
            return;

        lastHour = hour;
        Owner.DispatchEvent(new EorzeaHourChangedEvent(hour));

        var isNight = IsNight(hour);

        if (isNight != lastIsNight)
        {
            lastIsNight = isNight;
            Owner.DispatchEvent(new EorzeaDayNightChangedEvent(isNight));
        }
    }

    /// <summary>
    /// Computes the current Eorzea time of day (hour + minute + second within the 24-hour Eorzea day) from
    /// real time. Pure - unit-testable. Eorzea time runs at 1440/70 the speed of real time.
    /// </summary>
    internal static TimeSpan ComputeEorzeaTimeOfDay(DateTimeOffset realTime)
    {
        var eorzeaSeconds = realTime.ToUnixTimeSeconds() * 1440L / 70L;
        return TimeSpan.FromSeconds(eorzeaSeconds % 86400L);
    }

    /// <summary>Computes the current Eorzea hour (0–23) from real time. Pure - unit-testable.</summary>
    internal static int ComputeEorzeaHour(DateTimeOffset realTime) => ComputeEorzeaTimeOfDay(realTime).Hours;

    /// <summary>Whether an Eorzea hour is night (18:00–5:59 ET). Pure.</summary>
    internal static bool IsNight(int hour) => hour < 6 || hour >= 18;
}

/// <summary>
/// Wraps the native toast events. Event-driven - zero tick cost.
/// </summary>
internal sealed class ToastSource : GameWatcherSource
{
    public ToastSource(NoireGameWatcher owner) : base(owner, SourceKind.Toast) { }

    /// <inheritdoc/>
    public override bool IsPolling => false;

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        NoireService.ToastGui.Toast += OnToast;
        NoireService.ToastGui.QuestToast += OnQuestToast;
        NoireService.ToastGui.ErrorToast += OnErrorToast;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.ToastGui.Toast -= OnToast;
        NoireService.ToastGui.QuestToast -= OnQuestToast;
        NoireService.ToastGui.ErrorToast -= OnErrorToast;
    }

    private void OnToast(ref SeString message, ref ToastOptions toastOptions, ref bool isHandled)
        => Owner.DispatchEvent(new ToastShownEvent(message.TextValue));

    private void OnQuestToast(ref SeString message, ref QuestToastOptions toastOptions, ref bool isHandled)
        => Owner.DispatchEvent(new QuestToastShownEvent(message.TextValue));

    private void OnErrorToast(ref SeString message, ref bool isHandled)
        => Owner.DispatchEvent(new ErrorToastShownEvent(message.TextValue));
}
