using System;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Frame-time band classification for the camera-phase trace's load analysis, free of rendering state.
/// </summary>
internal static class CameraSwimAnalysis
{
    /// <summary>Upper bound of each frame-time band in milliseconds; the last band is open-ended.</summary>
    private static readonly float[] bandUpperMs = { 8.5f, 17f, 25f, 40f, 66f, float.MaxValue };

    /// <summary>Labels matching <see cref="bandUpperMs"/>, expressed as the frame-rate range the band covers.</summary>
    private static readonly string[] bandLabels = { "<8.5ms (118+fps)", "8.5-17ms (60-118)", "17-25ms (40-60)", "25-40ms (25-40)", "40-66ms (15-25)", ">66ms (<15fps)" };

    /// <summary>How many frame-time bands the trace accumulates into.</summary>
    public static int BandCount => bandUpperMs.Length;

    /// <summary>The band a frame time in milliseconds falls into.</summary>
    /// <param name="frameMs">Frame time in milliseconds; non-finite or non-positive values land in band 0.</param>
    /// <returns>Band index in <c>[0, BandCount)</c>.</returns>
    public static int BandOf(float frameMs)
    {
        if (!float.IsFinite(frameMs) || frameMs <= 0f)
            return 0;

        for (var i = 0; i < bandUpperMs.Length - 1; i++)
        {
            if (frameMs < bandUpperMs[i])
                return i;
        }

        return bandUpperMs.Length - 1;
    }

    /// <summary>The display label of a band.</summary>
    /// <param name="band">Band index from <see cref="BandOf"/>.</param>
    /// <returns>The label, or the index as text when out of range.</returns>
    public static string BandLabel(int band)
        => band >= 0 && band < bandLabels.Length ? bandLabels[band] : band.ToString();
}

/// <summary>What a traced frame classified as, from the camera's motion and the overlay's residual.</summary>
internal enum SettleFrame
{
    /// <summary>The camera moved this frame; any residual is ordinary under-motion error.</summary>
    Moving,

    /// <summary>Camera quiet and the overlay aligned.</summary>
    QuietClean,

    /// <summary>Camera quiet, overlay still off, shortly after motion stopped: the settle signature.</summary>
    Settle,

    /// <summary>Camera quiet, overlay off, long after motion stopped: a persistent offset, not a settle.</summary>
    LateDrift,
}

/// <summary>
/// Classifies traced frames into <see cref="SettleFrame"/> and accumulates the settle statistics: how often the
/// overlay keeps drifting after the camera stops, and for how long. A settle event is a maximal run of drifting
/// frames beginning within <see cref="WindowFrames"/> frames of the last camera motion; drift beyond the window
/// counts separately as late drift.
/// </summary>
internal sealed class SettleTracker
{
    /// <summary>How many quiet frames after motion still count toward a settle rather than a persistent offset.</summary>
    public const int WindowFrames = 45;

    private int framesSinceMotion = WindowFrames + 1;
    private int currentRun;

    /// <summary>How many settle events (distinct after-stop drift runs) were seen.</summary>
    public int Events { get; private set; }

    /// <summary>Total frames classified <see cref="SettleFrame.Settle"/>.</summary>
    public int SettleFrames { get; private set; }

    /// <summary>The longest single settle run, in frames.</summary>
    public int LongestRun { get; private set; }

    /// <summary>Frames classified <see cref="SettleFrame.QuietClean"/>.</summary>
    public int QuietCleanFrames { get; private set; }

    /// <summary>Frames classified <see cref="SettleFrame.LateDrift"/>.</summary>
    public int LateDriftFrames { get; private set; }

    /// <summary>Frames classified <see cref="SettleFrame.Moving"/>.</summary>
    public int MovingFrames { get; private set; }

    /// <summary>
    /// Advances the tracker by one traced frame and returns its classification.
    /// </summary>
    /// <param name="cameraMoving">Whether the camera moved this frame (anchor motion above the caller's threshold).</param>
    /// <param name="overlayDrifting">Whether the overlay's residual is above the caller's visibility threshold.</param>
    /// <returns>The frame's classification.</returns>
    public SettleFrame Advance(bool cameraMoving, bool overlayDrifting)
    {
        if (cameraMoving)
        {
            framesSinceMotion = 0;
            currentRun = 0;
            MovingFrames++;
            return SettleFrame.Moving;
        }

        if (framesSinceMotion < int.MaxValue)
            framesSinceMotion++;

        if (!overlayDrifting)
        {
            currentRun = 0;
            QuietCleanFrames++;
            return SettleFrame.QuietClean;
        }

        if (framesSinceMotion > WindowFrames && currentRun == 0)
        {
            LateDriftFrames++;
            return SettleFrame.LateDrift;
        }

        if (currentRun == 0)
            Events++;
        currentRun++;
        SettleFrames++;
        LongestRun = Math.Max(LongestRun, currentRun);
        return SettleFrame.Settle;
    }
}
