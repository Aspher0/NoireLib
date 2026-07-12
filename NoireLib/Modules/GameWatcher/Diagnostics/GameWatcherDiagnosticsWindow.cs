using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NoireLib.Core.Modules;
using System;
using System.Linq;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// The watcher's diagnostics window: per-source state (running/refcount/override/failure), interest masks,
/// event counters and last-tick durations, live subscriptions, active waits, custom-event publish counters
/// and a bounded recent-event log. When a watch "doesn't fire", this window answers why.
/// </summary>
public class GameWatcherDiagnosticsWindow : NoireModuleWindowBase<NoireGameWatcher>
{
    /// <inheritdoc/>
    public override string DisplayWindowName { get; set; } = "GameWatcher Diagnostics";

    /// <summary>
    /// Initializes the diagnostics window.
    /// </summary>
    /// <param name="watcher">The parent watcher module.</param>
    public GameWatcherDiagnosticsWindow(NoireGameWatcher watcher)
        : base(watcher, ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        UpdateTitleBarButtons();
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        ImGui.TextDisabled($"Module: {(ParentModule.IsActive ? "active" : "inactive")} | Subscriptions: {ParentModule.SubscriptionCount} | Value watchers: {ParentModule.ValueWatcherCount} | Active waits: {GameConditionPump.ActiveWaiterCount}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Sources", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSources();

        if (ImGui.CollapsingHeader("Subscriptions"))
            DrawSubscriptions();

        if (ImGui.CollapsingHeader("Event counters"))
            DrawEventCounters();

        if (ImGui.CollapsingHeader("Custom publishes"))
            DrawCustomPublishes();

        if (ImGui.CollapsingHeader("Recent events"))
            DrawRecentEvents();
    }

    private void DrawSources()
    {
        using var table = ImRaii.Table("GameWatcherSources", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);

        if (!table)
            return;

        ImGui.TableSetupColumn("Source");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("Refs");
        ImGui.TableSetupColumn("Override");
        ImGui.TableSetupColumn("Last tick");
        ImGui.TableSetupColumn("Detail");
        ImGui.TableHeadersRow();

        foreach (var (kind, source) in ParentModule.SourcesView.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(kind.ToString());

            ImGui.TableNextColumn();

            if (source.HasFailed)
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "FAILED");
            else if (source.IsRunning)
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "running");
            else
                ImGui.TextDisabled("stopped");

            ImGui.TableNextColumn();
            ImGui.Text(source.RefCount.ToString());

            ImGui.TableNextColumn();
            var configured = ParentModule.ActiveOptions.Sources.TryGetValue(kind, out var value) ? value : SourceOverride.Default;
            ImGui.Text(configured.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(source.IsPolling ? $"{source.LastTickDuration.TotalMilliseconds:0.00} ms" : "event-driven");

            ImGui.TableNextColumn();

            if (source.HasFailed)
                ImGui.TextWrapped(source.FailureMessage ?? string.Empty);
            else if (kind == SourceKind.Characters && source is CharacterSource characterSource)
                ImGui.Text($"mask: {characterSource.UnionMask}, iteration: {characterSource.UnionIteration}");
            else
                ImGui.TextDisabled("-");
        }
    }

    private void DrawSubscriptions()
    {
        var ledger = ParentModule.LedgerSnapshot();

        ImGui.TextDisabled($"{ledger.Length} live subscription(s)");

        using var table = ImRaii.Table("GameWatcherLedger", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, 200));

        if (!table)
            return;

        ImGui.TableSetupColumn("Description");
        ImGui.TableSetupColumn("Event");
        ImGui.TableSetupColumn("Key");
        ImGui.TableSetupColumn("Source");
        ImGui.TableHeadersRow();

        foreach (var (description, eventType, key, interest) in ledger)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(description);

            ImGui.TableNextColumn();
            ImGui.Text(eventType);

            ImGui.TableNextColumn();
            ImGui.Text(key ?? "-");

            ImGui.TableNextColumn();
            ImGui.Text(interest?.ToString() ?? "-");
        }
    }

    private void DrawEventCounters()
    {
        using var table = ImRaii.Table("GameWatcherEventCounters", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, 200));

        if (!table)
            return;

        ImGui.TableSetupColumn("Event type");
        ImGui.TableSetupColumn("Dispatched");
        ImGui.TableHeadersRow();

        foreach (var (type, count) in ParentModule.EventCounters.OrderByDescending(pair => pair.Value))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(type.Name);

            ImGui.TableNextColumn();
            ImGui.Text(count.ToString());
        }
    }

    private void DrawCustomPublishes()
    {
        if (ParentModule.CustomPublishCounters.IsEmpty())
        {
            ImGui.TextDisabled("No custom events published.");
            return;
        }

        using var table = ImRaii.Table("GameWatcherCustomPublishes", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);

        if (!table)
            return;

        ImGui.TableSetupColumn("Event type");
        ImGui.TableSetupColumn("Published");
        ImGui.TableHeadersRow();

        foreach (var (type, count) in ParentModule.CustomPublishCounters.OrderByDescending(pair => pair.Value))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(type.Name);

            ImGui.TableNextColumn();
            ImGui.Text(count.ToString());
        }
    }

    private void DrawRecentEvents()
    {
        var events = ParentModule.RecentEventsSnapshot();

        if (events.Length == 0)
        {
            ImGui.TextDisabled("No events recorded (capacity may be 0).");
            return;
        }

        using var child = ImRaii.Child("GameWatcherRecentEvents", new Vector2(0, 200), true);

        if (!child)
            return;

        for (var i = events.Length - 1; i >= 0; i--)
            ImGui.Text($"{events[i].At:HH:mm:ss.fff}  {events[i].Description}");
    }

    /// <inheritdoc/>
    public override void Dispose() { }
}

internal static class DiagnosticsExtensions
{
    /// <summary>Whether a read-only dictionary is empty (helper for the diagnostics window).</summary>
    public static bool IsEmpty<TKey, TValue>(this System.Collections.Generic.IReadOnlyDictionary<TKey, TValue> dictionary)
        => dictionary.Count == 0;
}
