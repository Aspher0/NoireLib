using Dalamud.Game.ClientState.Objects.Types;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// Polls <see cref="Dalamud.Plugin.Services.ITargetManager"/> for target, focus target, soft target and
/// mouse-over target changes, with previous and current <see cref="ObjectSnapshot"/>s attached.
/// </summary>
internal sealed class TargetSource : GameWatcherSource
{
    private ulong lastTargetId;
    private ulong lastFocusId;
    private ulong lastSoftId;
    private ulong lastMouseOverId;

    private ObjectSnapshot? lastTarget;
    private ObjectSnapshot? lastFocus;
    private ObjectSnapshot? lastSoft;
    private ObjectSnapshot? lastMouseOver;

    public TargetSource(NoireGameWatcher owner) : base(owner, SourceKind.Targets) { }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        // Baseline seeding without events.
        var now = DateTimeOffset.UtcNow;

        (lastTargetId, lastTarget) = Read(NoireService.TargetManager.Target, now);
        (lastFocusId, lastFocus) = Read(NoireService.TargetManager.FocusTarget, now);
        (lastSoftId, lastSoft) = Read(NoireService.TargetManager.SoftTarget, now);
        (lastMouseOverId, lastMouseOver) = Read(NoireService.TargetManager.MouseOverTarget, now);
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        lastTarget = lastFocus = lastSoft = lastMouseOver = null;
        lastTargetId = lastFocusId = lastSoftId = lastMouseOverId = 0;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        Check(NoireService.TargetManager.Target, ref lastTargetId, ref lastTarget, now,
            (prev, cur) => Owner.DispatchEvent(new TargetChangedEvent(prev, cur)));

        Check(NoireService.TargetManager.FocusTarget, ref lastFocusId, ref lastFocus, now,
            (prev, cur) => Owner.DispatchEvent(new FocusTargetChangedEvent(prev, cur)));

        Check(NoireService.TargetManager.SoftTarget, ref lastSoftId, ref lastSoft, now,
            (prev, cur) => Owner.DispatchEvent(new SoftTargetChangedEvent(prev, cur)));

        Check(NoireService.TargetManager.MouseOverTarget, ref lastMouseOverId, ref lastMouseOver, now,
            (prev, cur) => Owner.DispatchEvent(new MouseOverTargetChangedEvent(prev, cur)));
    }

    private static (ulong Id, ObjectSnapshot? Snapshot) Read(IGameObject? obj, DateTimeOffset now)
        => obj == null ? (0, null) : (obj.GameObjectId, ObjectSource.CaptureObject(obj, now));

    private static void Check(
        IGameObject? current,
        ref ulong lastId,
        ref ObjectSnapshot? lastSnapshot,
        DateTimeOffset now,
        Action<ObjectSnapshot?, ObjectSnapshot?> dispatch)
    {
        var currentId = current?.GameObjectId ?? 0;

        if (currentId == lastId)
            return;

        var previous = lastSnapshot;
        var snapshot = current == null ? null : ObjectSource.CaptureObject(current, now);

        lastId = currentId;
        lastSnapshot = snapshot;

        dispatch(previous, snapshot);
    }
}
