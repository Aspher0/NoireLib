using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Ground-decal actor exclusions, owned by the node (proxying its renderer). A layered, dev-owned predicate decides
/// which game objects a decal should <b>not</b> paint on: the decal paints the ground normally, and a surface inside
/// an excluded volume simply does not receive paint - the ground around it still does. Nothing is cut from the decal;
/// the excluded thing is just not drawn on. The library walks the object table on the framework thread and applies the
/// result for you - no per-frame plumbing.
/// </summary>
public sealed partial class SceneNode
{
    /// <summary>The per-frame exclusion collector set by <c>ExcludeObjects</c> / <c>ExcludeVolumes(collector)</c>, invoked on the framework thread. Null when the node has no dynamic exclusions.</summary>
    internal Func<IReadOnlyList<ExcludeVolume>>? ExclusionCollector;

    /// <summary>
    /// Excludes game objects the predicate accepts. You decide what counts (a player? a minion? furniture? by name,
    /// owner, distance, sub-kind, …). Each accepted object contributes a cylinder at its position sized by its hitbox
    /// radius × <paramref name="radiusScale"/>. Refreshed by the library each frame on the framework thread. Fluent.
    /// </summary>
    /// <param name="predicate">Returns true for objects the decal should not paint on.</param>
    /// <param name="radiusScale">Multiplier on each accepted object's hitbox radius (default 1).</param>
    public SceneNode ExcludeObjects(Func<IGameObject, bool> predicate, float radiusScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var scale = radiusScale <= 0f ? 1f : radiusScale;
        SetExclusionCollector(() =>
        {
            var list = new List<ExcludeVolume>();
            GameRenderSources.CollectActorExclusions(list, ScenePass.MaxActorVolumes, predicate, scale);
            return list;
        });
        return this;
    }

    /// <summary>
    /// Full control: the callback receives each game object and returns the exact <see cref="ExcludeVolume"/> to use, or
    /// null to skip it. Refreshed by the library each frame on the framework thread. Fluent.
    /// </summary>
    /// <param name="selector">Per-object volume selector; return null to skip an object.</param>
    public SceneNode ExcludeObjects(Func<IGameObject, ExcludeVolume?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        SetExclusionCollector(() =>
        {
            var list = new List<ExcludeVolume>();
            GameRenderSources.CollectActorExclusions(list, ScenePass.MaxActorVolumes, selector);
            return list;
        });
        return this;
    }

    /// <summary>Excludes a fixed set of volumes (no game objects, no per-frame recompute). Fluent.</summary>
    /// <param name="volumes">The exclusion volumes; null or empty paints over everything.</param>
    public SceneNode ExcludeVolumes(IReadOnlyList<ExcludeVolume> volumes)
    {
        ReleaseExclusions();
        if (Renderer is { } renderer)
            renderer.ExcludeVolumes = volumes;
        return this;
    }

    /// <summary>Excludes volumes produced by your own collector, recomputed each frame on the framework thread (no game objects). Fluent.</summary>
    /// <param name="collector">Returns the exclusion volumes to apply this frame.</param>
    public SceneNode ExcludeVolumes(Func<IReadOnlyList<ExcludeVolume>> collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        SetExclusionCollector(collector);
        return this;
    }

    /// <summary>Clears any exclusions so the decal paints over everything again. Fluent.</summary>
    public SceneNode ClearExclusions()
    {
        ReleaseExclusions();
        if (Renderer is { } renderer)
            renderer.ExcludeVolumes = null;
        return this;
    }

    private void SetExclusionCollector(Func<IReadOnlyList<ExcludeVolume>> collector)
    {
        ExclusionCollector = collector;
        DecalExclusionService.Register(this);
    }

    /// <summary>Stops the per-frame exclusion refresh and drops the collector (called on destroy, and when switching to a static list / clearing).</summary>
    private void ReleaseExclusions()
    {
        if (ExclusionCollector == null)
            return;

        ExclusionCollector = null;
        DecalExclusionService.Unregister(this);
    }
}
