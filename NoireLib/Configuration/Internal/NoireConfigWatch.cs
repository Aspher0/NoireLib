using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace NoireLib.Configuration;

internal static class NoireConfigWatch
{
    private static readonly object Gate = new();
    private static readonly List<NoireConfigBase> Armed = [];
    private static readonly List<NoireConfigBase> Scratch = [];
    private static bool pumpAttached;

    internal static void Arm(NoireConfigBase config)
    {
        lock (Gate)
        {
            Armed.Add(config);
            TryAttachPumpLocked();
        }
    }

    internal static void EnsurePumpIfPending()
    {
        lock (Gate)
        {
            if (Armed.Count > 0)
                TryAttachPumpLocked();
        }
    }

    private static void TryAttachPumpLocked()
    {
        if (pumpAttached || !NoireService.IsInitialized())
            return;

        NoireService.Framework.Update += OnFrameworkUpdate;
        pumpAttached = true;
    }

    private static void DetachPumpLocked()
    {
        if (!pumpAttached)
            return;

        try
        {
            if (NoireService.IsInitialized())
                NoireService.Framework.Update -= OnFrameworkUpdate;
        }
        catch
        {
            // Detaching during teardown can race
        }

        pumpAttached = false;
    }

    private static void OnFrameworkUpdate(IFramework framework) => RunChecks();

    internal static void RunChecks()
    {
        lock (Gate)
        {
            if (Armed.Count == 0)
            {
                DetachPumpLocked();
                return;
            }

            Scratch.Clear();
            Scratch.AddRange(Armed);
            Armed.Clear();
        }

        foreach (var config in Scratch)
        {
            config.ResetArm();

            bool rearm;

            try
            {
                rearm = config.RunAutoSaveCheck();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The autosave check for {config.GetType().Name} failed.", "[NoireConfig] ");
                rearm = false;
            }

            if (rearm)
                config.MarkAccessed();
        }

        Scratch.Clear();

        lock (Gate)
        {
            if (Armed.Count == 0)
                DetachPumpLocked();
        }
    }

    internal static void RunFinalSweep(IReadOnlyList<NoireConfigBase> cachedConfigs)
    {
        foreach (var config in cachedConfigs)
        {
            try
            {
                config.RunAutoSaveCheck();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The teardown autosave check for {config.GetType().Name} failed.", "[NoireConfig] ");
            }
        }

        NoireConfigBase.FlushAllPendingSaves();

        lock (Gate)
        {
            Armed.Clear();
            DetachPumpLocked();
        }
    }
}
