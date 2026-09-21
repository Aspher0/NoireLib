using NoireLib.UI;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

// The only gauges that need the game.
internal static class NoireRemoteGameMetrics
{
    private static readonly List<NoireRemoteMetricHandle> Handles = [];
    private static readonly List<UiProfileEntry> ProfileBuffer = [];

    public static void Register()
    {
        if (Handles.Count > 0)
            return;

        Handles.Add(NoireRemoteMetrics.Register("frameMs", ReadFrameMs, NoireRemoteThread.Framework));
        Handles.Add(NoireRemoteMetrics.Register("uiSelfMs", ReadUiSelfMs, NoireRemoteThread.Framework));
    }

    public static void Unregister()
    {
        foreach (var handle in Handles)
            handle.Dispose();

        Handles.Clear();
    }

    private static double ReadFrameMs()
        => NoireService.IsInitialized() ? NoireService.Framework.UpdateDelta.TotalMilliseconds : 0;

    // Answers nothing while the profiler is off.
    private static double ReadUiSelfMs()
    {
        if (!NoireUI.Profiler.Enabled)
            return 0;

        ProfileBuffer.Clear();
        NoireUI.Profiler.Snapshot(ProfileBuffer);

        var total = 0d;

        foreach (var entry in ProfileBuffer)
            total += entry.SelfLastMs;

        return total;
    }
}
