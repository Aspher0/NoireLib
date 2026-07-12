using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Addon facts, lifecycle-driven: per-addon listeners registered on demand, shown/hidden transitions, and
/// node watchers re-evaluated on refresh events plus a low-frequency safety poll
/// (<see cref="GameWatcherOptions.AddonSafetyPollInterval"/>) for addons that mutate nodes without a refresh.
/// Lifecycle events themselves are exact.
/// </summary>
internal sealed class AddonSource : GameWatcherSource
{
    internal sealed class NodeWatcherRegistration
    {
        public required string AddonName { get; init; }
        public required uint NodeId { get; init; }
        public required Action<string?, string?> OnChanged { get; init; }
        public string? LastValue { get; set; }
        public bool Seeded { get; set; }
    }

    private static readonly AddonEvent[] ListenedEvents =
    {
        AddonEvent.PostSetup,
        AddonEvent.PostRefresh,
        AddonEvent.PostRequestedUpdate,
        AddonEvent.PreFinalize,
    };

    private readonly Dictionary<string, int> nameInterest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> visibility = new(StringComparer.Ordinal);
    private readonly List<NodeWatcherRegistration> nodeWatchers = new();
    private DateTimeOffset nextSafetyPoll = DateTimeOffset.MinValue;
    private bool listening;

    public AddonSource(NoireGameWatcher owner) : base(owner, SourceKind.Addons) { }

    /// <summary>Registers interest in an addon name (refcounted) and returns the removal action.</summary>
    internal Action AddAddonInterest(string addonName)
    {
        lock (nameInterest)
        {
            nameInterest.TryGetValue(addonName, out var count);
            nameInterest[addonName] = count + 1;

            if (IsRunning && count == 0)
                RegisterListeners(addonName);
        }

        return () =>
        {
            lock (nameInterest)
            {
                if (!nameInterest.TryGetValue(addonName, out var count))
                    return;

                if (count <= 1)
                {
                    nameInterest.Remove(addonName);
                    visibility.Remove(addonName);

                    if (IsRunning)
                        UnregisterListeners(addonName);
                }
                else
                {
                    nameInterest[addonName] = count - 1;
                }
            }
        };
    }

    /// <summary>Registers a node text watcher and returns the removal action.</summary>
    internal Action AddNodeWatcher(NodeWatcherRegistration registration)
    {
        var releaseName = AddAddonInterest(registration.AddonName);

        lock (nodeWatchers)
            nodeWatchers.Add(registration);

        return () =>
        {
            lock (nodeWatchers)
                nodeWatchers.Remove(registration);

            releaseName();
        };
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        listening = true;
        visibility.Clear();
        nextSafetyPoll = DateTimeOffset.MinValue;

        lock (nameInterest)
        {
            foreach (var name in nameInterest.Keys)
            {
                RegisterListeners(name);
                visibility[name] = ReadIsVisible(name);
            }
        }

        lock (nodeWatchers)
        {
            foreach (var watcher in nodeWatchers)
                watcher.Seeded = false;
        }
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        listening = false;

        lock (nameInterest)
        {
            foreach (var name in nameInterest.Keys)
                UnregisterListeners(name);
        }

        visibility.Clear();
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (now < nextSafetyPoll)
            return;

        nextSafetyPoll = now + Owner.ActiveOptions.AddonSafetyPollInterval;

        string[] names;

        lock (nameInterest)
            names = nameInterest.Keys.ToArray();

        foreach (var name in names)
            UpdateVisibility(name);

        EvaluateNodeWatchers(addonNameFilter: null);
    }

    private void RegisterListeners(string addonName)
    {
        foreach (var evt in ListenedEvents)
            NoireService.AddonLifecycle.RegisterListener(evt, addonName, OnAddonLifecycle);
    }

    private void UnregisterListeners(string addonName)
    {
        foreach (var evt in ListenedEvents)
            NoireService.AddonLifecycle.UnregisterListener(evt, addonName, OnAddonLifecycle);
    }

    private void OnAddonLifecycle(AddonEvent type, AddonArgs args)
    {
        if (!listening)
            return;

        var name = args.AddonName;

        Owner.DispatchEvent(new AddonLifecycleEvent(name, type));

        switch (type)
        {
            case AddonEvent.PostSetup:
                SetVisibility(name, true);
                break;

            case AddonEvent.PreFinalize:
                SetVisibility(name, false);
                break;

            case AddonEvent.PostRefresh:
            case AddonEvent.PostRequestedUpdate:
                EvaluateNodeWatchers(name);
                break;
        }
    }

    private void UpdateVisibility(string addonName)
        => SetVisibility(addonName, ReadIsVisible(addonName));

    private void SetVisibility(string addonName, bool isVisible)
    {
        lock (nameInterest)
        {
            if (visibility.TryGetValue(addonName, out var wasVisible) && wasVisible == isVisible)
                return;

            visibility[addonName] = isVisible;
        }

        if (isVisible)
            Owner.DispatchEvent(new AddonShownEvent(addonName));
        else
            Owner.DispatchEvent(new AddonHiddenEvent(addonName));
    }

    private void EvaluateNodeWatchers(string? addonNameFilter)
    {
        NodeWatcherRegistration[] snapshot;

        lock (nodeWatchers)
        {
            if (nodeWatchers.Count == 0)
                return;

            snapshot = nodeWatchers.ToArray();
        }

        foreach (var watcher in snapshot)
        {
            if (addonNameFilter != null && !string.Equals(watcher.AddonName, addonNameFilter, StringComparison.Ordinal))
                continue;

            var value = ReadNodeText(watcher.AddonName, watcher.NodeId);

            if (!watcher.Seeded)
            {
                // Baseline seeding without firing.
                watcher.Seeded = true;
                watcher.LastValue = value;
                continue;
            }

            if (string.Equals(watcher.LastValue, value, StringComparison.Ordinal))
                continue;

            var previous = watcher.LastValue;
            watcher.LastValue = value;

            try
            {
                watcher.OnChanged(previous, value);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(Owner, ex, $"An addon node watcher callback threw ({watcher.AddonName}#{watcher.NodeId}).");
            }
        }
    }

    /// <summary>Whether an addon exists and is visible right now (live read).</summary>
    internal static unsafe bool ReadIsVisible(string addonName)
    {
        var unitBase = (AtkUnitBase*)NoireService.GameGui.GetAddonByName(addonName).Address;
        return unitBase != null && unitBase->IsVisible;
    }

    /// <summary>Whether an addon exists, is visible and fully loaded (live read).</summary>
    internal static unsafe bool ReadIsReady(string addonName)
    {
        var unitBase = (AtkUnitBase*)NoireService.GameGui.GetAddonByName(addonName).Address;
        return unitBase != null && unitBase->IsVisible && unitBase->UldManager.LoadedState == AtkLoadState.Loaded;
    }

    /// <summary>Reads the text of a node by id, or null when the addon or node is unavailable (live read).</summary>
    internal static unsafe string? ReadNodeText(string addonName, uint nodeId)
    {
        var unitBase = (AtkUnitBase*)NoireService.GameGui.GetAddonByName(addonName).Address;

        if (unitBase == null)
            return null;

        var node = unitBase->GetTextNodeById(nodeId);

        if (node == null)
            return null;

        return SeStringHelper.Utf8StringToPlainText(node->NodeText);
    }
}
