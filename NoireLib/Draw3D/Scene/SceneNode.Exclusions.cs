using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    // Framework thread.
    internal Func<IReadOnlyList<ExcludeVolume>>? ExclusionCollector;

    /// <summary>Stops the decal painting on game objects the predicate accepts, each as a cylinder sized by its hitbox radius and refreshed every tick. Fluent.</summary>
    /// <param name="predicate">Returns true for objects the decal should not paint on.</param>
    /// <param name="radiusScale">Multiplier on each accepted object's hitbox radius.</param>
    /// <returns>This node.</returns>
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

    /// <summary>Stops the decal painting inside the volume the selector returns for each game object, refreshed every tick. Fluent.</summary>
    /// <param name="selector">Per-object volume selector returning null to skip an object.</param>
    /// <returns>This node.</returns>
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

    /// <summary>Excludes a fixed set of volumes. Fluent.</summary>
    /// <param name="volumes">The exclusion volumes. Null or empty paints over everything.</param>
    /// <returns>This node.</returns>
    public SceneNode ExcludeVolumes(IReadOnlyList<ExcludeVolume> volumes)
    {
        ReleaseExclusions();
        if (Renderer is { } renderer)
            renderer.ExcludeVolumes = volumes;
        return this;
    }

    /// <summary>Excludes volumes produced by a collector invoked every framework tick. Fluent.</summary>
    /// <param name="collector">Returns the exclusion volumes to apply this frame.</param>
    /// <returns>This node.</returns>
    public SceneNode ExcludeVolumes(Func<IReadOnlyList<ExcludeVolume>> collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        SetExclusionCollector(collector);
        return this;
    }

    /// <summary>Clears any exclusions so the decal paints over everything again. Fluent.</summary>
    /// <returns>This node.</returns>
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

    private void ReleaseExclusions()
    {
        if (ExclusionCollector == null)
            return;

        ExclusionCollector = null;
        DecalExclusionService.Unregister(this);
    }
}
