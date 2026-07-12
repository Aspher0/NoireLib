using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Local player targeting facts: target, focus target, soft target and mouse-over target changes,
/// with previous and current snapshots attached.
/// </summary>
public sealed class TargetWatcher : GameWatcherFacade
{
    internal TargetWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to target changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnTargetChanged(Action<TargetChangedEvent> handler, NoireSubscriptionOptions<TargetChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnTargetChanged));

    /// <inheritdoc cref="OnTargetChanged(Action{TargetChangedEvent}, NoireSubscriptionOptions{TargetChangedEvent}?)"/>
    public NoireSubscriptionToken OnTargetChangedAsync(Func<TargetChangedEvent, Task> handler, NoireSubscriptionOptions<TargetChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnTargetChanged));

    /// <summary>
    /// Subscribes to focus target changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnFocusTargetChanged(Action<FocusTargetChangedEvent> handler, NoireSubscriptionOptions<FocusTargetChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnFocusTargetChanged));

    /// <inheritdoc cref="OnFocusTargetChanged(Action{FocusTargetChangedEvent}, NoireSubscriptionOptions{FocusTargetChangedEvent}?)"/>
    public NoireSubscriptionToken OnFocusTargetChangedAsync(Func<FocusTargetChangedEvent, Task> handler, NoireSubscriptionOptions<FocusTargetChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnFocusTargetChanged));

    /// <summary>
    /// Subscribes to soft target changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnSoftTargetChanged(Action<SoftTargetChangedEvent> handler, NoireSubscriptionOptions<SoftTargetChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnSoftTargetChanged));

    /// <inheritdoc cref="OnSoftTargetChanged(Action{SoftTargetChangedEvent}, NoireSubscriptionOptions{SoftTargetChangedEvent}?)"/>
    public NoireSubscriptionToken OnSoftTargetChangedAsync(Func<SoftTargetChangedEvent, Task> handler, NoireSubscriptionOptions<SoftTargetChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnSoftTargetChanged));

    /// <summary>
    /// Subscribes to mouse-over target changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMouseOverTargetChanged(Action<MouseOverTargetChangedEvent> handler, NoireSubscriptionOptions<MouseOverTargetChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnMouseOverTargetChanged));

    /// <inheritdoc cref="OnMouseOverTargetChanged(Action{MouseOverTargetChangedEvent}, NoireSubscriptionOptions{MouseOverTargetChangedEvent}?)"/>
    public NoireSubscriptionToken OnMouseOverTargetChangedAsync(Func<MouseOverTargetChangedEvent, Task> handler, NoireSubscriptionOptions<MouseOverTargetChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnMouseOverTargetChanged));

    /// <summary>The current target's snapshot, or null. Live read (framework thread only).</summary>
    public ObjectSnapshot? Current => Snapshot(NoireService.TargetManager.Target);

    /// <summary>The current focus target's snapshot, or null. Live read (framework thread only).</summary>
    public ObjectSnapshot? Focus => Snapshot(NoireService.TargetManager.FocusTarget);

    /// <summary>The current soft target's snapshot, or null. Live read (framework thread only).</summary>
    public ObjectSnapshot? Soft => Snapshot(NoireService.TargetManager.SoftTarget);

    /// <summary>The current mouse-over target's snapshot, or null. Live read (framework thread only).</summary>
    public ObjectSnapshot? MouseOver => Snapshot(NoireService.TargetManager.MouseOverTarget);

    private static ObjectSnapshot? Snapshot(Dalamud.Game.ClientState.Objects.Types.IGameObject? obj)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return obj == null ? null : ObjectSource.CaptureObject(obj, DateTimeOffset.UtcNow);
    }
}
