using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Drives per-decal actor exclusions on the framework thread: nodes that opted in (<see cref="SceneNode.ExcludeObjects(Func{Dalamud.Game.ClientState.Objects.Types.IGameObject, bool}, float)"/>
/// and friends) are refreshed here each tick - the library walks the object table where it is safe to and assigns the
/// resulting volumes to each node's renderer, so consumers never plumb the object table by hand. Fail-soft: a throwing
/// collector is logged and skipped; a destroyed node is auto-unregistered.
/// </summary>
internal static class DecalExclusionService
{
    private const string DisposeKey = "NoireLib.Draw3D.DecalExclusionService";

    private static readonly object Sync = new();
    private static readonly List<SceneNode> Nodes = new();
    private static bool hooked;

    /// <summary>Registers a node for per-frame exclusion refresh (idempotent). Hooks the framework tick on first use.</summary>
    public static void Register(SceneNode node)
    {
        lock (Sync)
        {
            if (!Nodes.Contains(node))
                Nodes.Add(node);
            EnsureHooked();
        }
    }

    /// <summary>Stops refreshing a node's exclusions.</summary>
    public static void Unregister(SceneNode node)
    {
        lock (Sync)
            Nodes.Remove(node);
    }

    private static void EnsureHooked()
    {
        if (hooked || !NoireService.IsInitialized())
            return;

        NoireService.Framework.Update += OnUpdate;
        hooked = true;

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeKey))
            NoireLibMain.RegisterOnDispose(DisposeKey, Cleanup);
    }

    private static void OnUpdate(IFramework framework)
    {
        SceneNode[] snapshot;
        lock (Sync)
        {
            if (Nodes.Count == 0)
                return;
            snapshot = Nodes.ToArray();
        }

        foreach (var node in snapshot)
        {
            if (node.Destroyed || node.ExclusionCollector is not { } collector)
            {
                Unregister(node);
                continue;
            }

            var renderer = node.Renderer;
            if (renderer == null)
                continue;

            try
            {
                renderer.ExcludeVolumes = collector();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "A decal exclusion collector threw; the decal reads over actors this frame.", "Draw3D");
            }
        }
    }

    private static void Cleanup()
    {
        lock (Sync)
        {
            if (hooked && NoireService.IsInitialized())
                NoireService.Framework.Update -= OnUpdate;
            hooked = false;
            Nodes.Clear();
        }
    }
}
