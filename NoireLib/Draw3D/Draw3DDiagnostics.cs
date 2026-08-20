using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D;

/// <summary>
/// Runtime diagnostics behind <c>/noire3d</c>, exposed programmatically so a consumer can run them under another
/// command name. Every measurement samples from the render thread only, since an off-thread read would be scored
/// against a camera that has already moved. Findings go to the plugin log, with a one-line chat summary.
/// </summary>
public sealed unsafe class Draw3DDiagnostics
{
    /// <summary>
    /// Orientation overrides applied by both import paths, for a model authored in a convention the loaders do not
    /// expect. See <see cref="Assets.Draw3DImportFlips"/>.
    /// </summary>
    public Assets.Draw3DImportFlips ImportFlips { get; } = new();

    private int validateFramesRemaining;
    private float validateMaxDelta;
    private double validateDeltaSum;
    private int validateSamples;
    private float validateMaxMatrixDelta;
    private bool probePending;

    // Camera-phase trace: sweeps the recorded camera history for the frame-lag between the CPU camera the overlay
    // projects with and the pixels already in the present buffer.
    private const int LagSweepMax = 8;
    private const int DepthReadbackEvery = 6; // a whole-texture depth copy is heavy, so the depth residual samples a subset of frames
    private int camTraceFramesRemaining;
    private int camTraceInjectFrames;
    private int camTraceFallbackFrames;
    private int camTraceSnapshotFrames;
    private int camTraceMainPassFrames;
    private float camTraceMaxScreenDelta;
    private double camTraceScreenSum;
    private int camTraceScreenSamples;
    private float camTraceMaxMatrixDelta;

    // Frame-lag sweep accumulators: per candidate lag k (0 = the camera this frame projected with), how well that
    // camera reprojects world anchors onto this frame's rendered image. The depth residual is anchored to the pixels
    // and decides. The screen residual carries a lag-0 bias, since its anchors come from ScreenToWorld unprojecting
    // through the live struct camera, so a row beating lag 0 on depth while trailing it on screen is still in phase.
    private readonly double[] camTraceDepthResidual = new double[LagSweepMax];
    private readonly int[] camTraceDepthResidualN = new int[LagSweepMax];
    private readonly double[] camTraceScreenResidual = new double[LagSweepMax];
    private readonly int[] camTraceScreenResidualN = new int[LagSweepMax];
    private int camTraceDepthReadbacks;

    // The captured-GPU-camera row of the same sweep: when the capture is the pixels' true camera, it beats every
    // struct lag.
    private double camTraceCapDepthResidual;
    private int camTraceCapDepthResidualN;
    private double camTraceCapScreenResidual;
    private int camTraceCapScreenResidualN;
    private int camTraceCapAvailableFrames;
    private int camTraceCapUsedFrames;

    // Load analysis (see CameraSwimAnalysis): the same residuals bucketed by frame-time band, scored for the camera
    // the overlay actually projected with each frame, plus per-frame camera motion in anchor pixels and the settle
    // classification.
    private const float MovingPxPerFrame = 0.75f; // anchor motion below this = the camera is steady this frame
    private const float DriftingPx = 2f;          // used-camera residual above this = visibly off
    private long camTracePrevTimestamp;
    private readonly int[] bandFrames = new int[CameraSwimAnalysis.BandCount];
    private readonly double[] bandFrameMsSum = new double[CameraSwimAnalysis.BandCount];
    private readonly int[] bandInject = new int[CameraSwimAnalysis.BandCount];
    private readonly int[] bandCapFresh = new int[CameraSwimAnalysis.BandCount];
    private readonly int[] bandCapUsed = new int[CameraSwimAnalysis.BandCount];
    private readonly double[] bandMotionSum = new double[CameraSwimAnalysis.BandCount];
    private readonly int[] bandMotionN = new int[CameraSwimAnalysis.BandCount];
    private readonly double[] bandUsedScreenSum = new double[CameraSwimAnalysis.BandCount];
    private readonly float[] bandUsedScreenMax = new float[CameraSwimAnalysis.BandCount];
    private readonly int[] bandUsedScreenN = new int[CameraSwimAnalysis.BandCount];
    private readonly double[] bandUsedDepthSum = new double[CameraSwimAnalysis.BandCount];
    private readonly int[] bandUsedDepthN = new int[CameraSwimAnalysis.BandCount];
    private readonly double[] bandLag0ScreenSum = new double[CameraSwimAnalysis.BandCount];
    private readonly int[] bandLag0ScreenN = new int[CameraSwimAnalysis.BandCount];
    private SettleTracker? settleTracker;
    private int settleCapUsedFrames;
    private int settleFallbackCompositeFrames;
    private List<Vector2>? camTracePointScratch;
    private long camTraceArmRendered;
    private long camTraceArmEmpty;

    // Below these, after-stop drift is single-frame blips rather than a real multi-frame settle; the raw counts
    // still print, only the verdict holds back.
    private const int SettleVerdictMinEvents = 2;
    private const int SettleVerdictMinFrames = 6;
    private const int SettleVerdictMinRun = 3;

    internal Draw3DDiagnostics() { }

    /// <summary>
    /// Projects the layer with the captured GPU camera constants whenever a frame has them (default true).
    /// A frame with no fresh capture, and every frame while this is off, falls back to the struct snapshot.
    /// </summary>
    public bool PreferCapturedCamera { get; set; } = true;

    /// <summary>Arms the projection parity validator for the next 10 rendered frames (results logged). Gate: max &lt;= 1 px.</summary>
    public void RunValidate()
    {
        NoireDraw3D.EnsureInitialized();
        validateFramesRemaining = 10;
        validateMaxDelta = 0f;
        validateDeltaSum = 0;
        validateSamples = 0;
        validateMaxMatrixDelta = 0f;
    }

    /// <summary>
    /// Arms the ground-truth depth probe for the next rendered frame (results logged): the analytic depth map,
    /// a diagnostic-only raycast fit, and a per-point depth table for both candidate buffers. Read-only.
    /// Gate: &gt;= 90 % of hit points within 1e-3.
    /// </summary>
    public void RunProbe()
    {
        NoireDraw3D.EnsureInitialized();
        probePending = true;
    }

    /// <summary>
    /// Arms the camera-phase trace for the next <paramref name="frames"/> rendered frames (results logged): scores
    /// each candidate frame-lag's camera by how well it reprojects world anchors onto this frame's rendered image,
    /// and reports the best fit alongside the frame-time band breakdown and the after-stop drift classification.
    /// Read-only, and measures nothing useful unless the camera is moved hard while it runs.
    /// </summary>
    /// <param name="frames">Rendered frames to trace, clamped to 1..6000.</param>
    public void RunCameraPhaseTrace(int frames = 120)
    {
        NoireDraw3D.EnsureInitialized();
        camTraceFramesRemaining = Math.Clamp(frames, 1, 6000);
        camTraceInjectFrames = 0;
        camTraceFallbackFrames = 0;
        camTraceSnapshotFrames = 0;
        camTraceMainPassFrames = 0;
        camTraceMaxScreenDelta = 0f;
        camTraceScreenSum = 0;
        camTraceScreenSamples = 0;
        camTraceMaxMatrixDelta = 0f;
        Array.Clear(camTraceDepthResidual);
        Array.Clear(camTraceDepthResidualN);
        Array.Clear(camTraceScreenResidual);
        Array.Clear(camTraceScreenResidualN);
        camTraceDepthReadbacks = 0;
        camTraceCapDepthResidual = 0;
        camTraceCapDepthResidualN = 0;
        camTraceCapScreenResidual = 0;
        camTraceCapScreenResidualN = 0;
        camTraceCapAvailableFrames = 0;
        camTraceCapUsedFrames = 0;
        camTracePrevTimestamp = 0;
        Array.Clear(bandFrames);
        Array.Clear(bandFrameMsSum);
        Array.Clear(bandInject);
        Array.Clear(bandCapFresh);
        Array.Clear(bandCapUsed);
        Array.Clear(bandMotionSum);
        Array.Clear(bandMotionN);
        Array.Clear(bandUsedScreenSum);
        Array.Clear(bandUsedScreenMax);
        Array.Clear(bandUsedScreenN);
        Array.Clear(bandUsedDepthSum);
        Array.Clear(bandUsedDepthN);
        Array.Clear(bandLag0ScreenSum);
        Array.Clear(bandLag0ScreenN);
        settleTracker = new SettleTracker();
        settleCapUsedFrames = 0;
        settleFallbackCompositeFrames = 0;
        (camTraceArmRendered, camTraceArmEmpty) = NoireDraw3D.FrameCounters;
    }

    /// <summary>
    /// Wireframe rasterization of the scene pass. Ground decals have no mesh to wireframe, so they trace
    /// <see cref="DecalShapeOutlines"/> instead while this is on.
    /// </summary>
    public bool Wireframe
    {
        get => NoireDraw3D.Wireframe;
        set => NoireDraw3D.Wireframe = value;
    }

    /// <summary>Flips <see cref="Wireframe"/>.</summary>
    /// <returns>The new state.</returns>
    public bool ToggleWireframe() => Wireframe = !Wireframe;

    /// <summary>
    /// Traces every decal's painted shape as an outline over normal rendering, retained decals and immediate
    /// <see cref="Im.ImDraw3D"/> shapes alike. Always on while <see cref="Wireframe"/> is.
    /// </summary>
    public bool DecalShapeOutlines
    {
        get => NoireDraw3D.DecalShapeOutlines;
        set => NoireDraw3D.DecalShapeOutlines = value;
    }

    /// <summary>
    /// Draws every decal's projection box (the volume its SDF is evaluated in) as a wireframe over normal rendering.
    /// Independent of <see cref="Wireframe"/> and of <see cref="DecalShapeOutlines"/>;
    /// <see cref="Scene.SceneNode.ShowDecalVolume"/> is the per-node version.
    /// </summary>
    public bool DecalVolumeOutlines
    {
        get => NoireDraw3D.DecalVolumeOutlines;
        set => NoireDraw3D.DecalVolumeOutlines = value;
    }

    /// <summary>Formats the current stats snapshot.</summary>
    /// <returns>The formatted snapshot.</returns>
    public string GetStatsText() => NoireDraw3D.Stats.ToString();

    // Frame hooks, called by the render hub.

    internal void OnFrame(in FrameContext frame, in GameRenderSources.CameraData cam, bool hasDepth)
    {
        if (validateFramesRemaining <= 0)
            return;

        validateFramesRemaining--;

        // The object-table and GameGui reads happen on the render thread deliberately: marshalling to the framework
        // thread would compare the two projections against a camera that has since moved.
        Span<Vector3> points = stackalloc Vector3[24];
        var count = 0;

        var player = NoireService.ObjectTable.LocalPlayer?.Position;
        if (player is { } p)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / 8f);
                points[count++] = p + new Vector3(cos * 3f, (i % 3) * 0.8f, sin * 3f);
            }
        }

        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (frame.TryScreenToRay(screen, out var origin, out var dir))
                    points[count++] = origin + dir * (5f + gx * 12f + gy * 3f);
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (!frame.TryWorldToScreen(points[i], out var ours))
                continue;
            if (!NoireService.GameGui.WorldToScreen(points[i], out var theirs))
                continue;

            var delta = Vector2.Distance(ours, theirs);
            validateMaxDelta = MathF.Max(validateMaxDelta, delta);
            validateDeltaSum += delta;
            validateSamples++;
        }

        if (cam.HasRenderCamera && cam.HasControlViewProj)
        {
            var ours = cam.View * cam.Proj;
            var theirs = cam.ControlViewProj;
            var maxDelta = 0f;
            var a = ours;
            var b = theirs;
            Span<float> av = stackalloc float[16] { a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24, a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44 };
            Span<float> bv = stackalloc float[16] { b.M11, b.M12, b.M13, b.M14, b.M21, b.M22, b.M23, b.M24, b.M31, b.M32, b.M33, b.M34, b.M41, b.M42, b.M43, b.M44 };
            for (var i = 0; i < 16; i++)
                maxDelta = MathF.Max(maxDelta, MathF.Abs(av[i] - bv[i]));
            validateMaxMatrixDelta = MathF.Max(validateMaxMatrixDelta, maxDelta);
        }

        if (validateFramesRemaining == 0)
        {
            var mean = validateSamples > 0 ? validateDeltaSum / validateSamples : 0;
            var verdict = validateMaxDelta <= 1.0f ? "PASS" : "FAIL";
            var report = $"Draw3D validate [{verdict}]: {validateSamples} samples over 10 frames - max {validateMaxDelta:F3} px, mean {mean:F3} px (gate: max <= 1 px). " +
                         $"VP cross-check max element delta: {validateMaxMatrixDelta:E2}. Camera fallback active: {frame.UsedFallbackCamera}. " +
                         "Repeat across camera poses: orbit, side-on grazing, wall-collision camera, first-person, max zoom.";
            NoireLogger.PrintToChat($"Draw3D validate: {verdict} - max {validateMaxDelta:F3} px (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
    }

    /// <summary>
    /// Per-rendered-frame camera-phase sampling for <see cref="RunCameraPhaseTrace"/>, called from the shared render
    /// body on the render thread.
    /// </summary>
    /// <param name="device">Render device, for the throttled depth-buffer readback.</param>
    /// <param name="frame">This frame's viewport and projection.</param>
    /// <param name="projCam">The camera the overlay was projected with.</param>
    /// <param name="viaInject">Whether the frame rendered through the pre-UI inject path.</param>
    /// <param name="usedWorldSnapshot">Whether the inject path used the render-thread world-pass camera snapshot.</param>
    /// <param name="usedMainPass">Whether that snapshot came from the main scene pass rather than the first-depth fallback.</param>
    /// <param name="usedGpuCamera">Whether the frame projected with the captured GPU camera constants.</param>
    /// <param name="gpuVp">The frame's committed GPU camera constants, identity when <paramref name="hasGpuVp"/> is false.</param>
    /// <param name="hasGpuVp">Whether a fresh capture commit existed for this frame.</param>
    internal void OnCameraTrace(RenderDevice device, in FrameContext frame, in GameRenderSources.CameraData projCam, bool viaInject, bool usedWorldSnapshot, bool usedMainPass, bool usedGpuCamera, in Matrix4x4 gpuVp, bool hasGpuVp)
    {
        if (camTraceFramesRemaining <= 0)
            return;

        camTraceFramesRemaining--;
        if (viaInject)
        {
            camTraceInjectFrames++;
            if (usedWorldSnapshot)
                camTraceSnapshotFrames++;
            if (usedMainPass)
                camTraceMainPassFrames++;
        }
        else
        {
            camTraceFallbackFrames++;
        }

        if (hasGpuVp)
            camTraceCapAvailableFrames++;
        if (usedGpuCamera)
            camTraceCapUsedFrames++;

        // Frame-time banding over the render-thread cadence between traced frames; the first traced frame has no
        // predecessor and lands in no band.
        var now = Stopwatch.GetTimestamp();
        var band = -1;
        if (camTracePrevTimestamp != 0)
        {
            var frameMs = (float)((now - camTracePrevTimestamp) * 1000.0 / Stopwatch.Frequency);
            band = CameraSwimAnalysis.BandOf(frameMs);
            bandFrames[band]++;
            bandFrameMsSum[band] += frameMs;
            if (viaInject)
                bandInject[band]++;
            if (hasGpuVp)
                bandCapFresh[band]++;
            if (usedGpuCamera)
                bandCapUsed[band]++;
        }

        camTracePrevTimestamp = now;

        // Secondary signal only: both the projection camera and a live read taken now are ahead of the pixels
        // already in the present buffer, so the lag sweep below is what measures the real overlay-vs-pixels error.
        if (projCam.HasRenderCamera && GameRenderSources.TryGetCamera(out var live) && live.HasRenderCamera)
        {
            var projVp = projCam.View * projCam.Proj;
            var liveVp = live.View * live.Proj;
            camTraceMaxMatrixDelta = MathF.Max(camTraceMaxMatrixDelta, MaxElementDelta(in projVp, in liveVp));

            for (var gy = 0; gy < 4; gy++)
            {
                for (var gx = 0; gx < 4; gx++)
                {
                    var screen = new Vector2(frame.ViewportSize.X * (0.15f + 0.2f * gx), frame.ViewportSize.Y * (0.15f + 0.2f * gy));
                    if (!frame.TryScreenToRay(screen, out var origin, out var dir))
                        continue;

                    var wp = origin + dir * 20f;
                    if (!TryProjectToScreen(in projVp, wp, frame.ViewportSize, out var s1))
                        continue;
                    if (!TryProjectToScreen(in liveVp, wp, frame.ViewportSize, out var s2))
                        continue;

                    var d = Vector2.Distance(s1, s2);
                    camTraceMaxScreenDelta = MathF.Max(camTraceMaxScreenDelta, d);
                    camTraceScreenSum += d;
                    camTraceScreenSamples++;
                }
            }
        }

        SweepFrameLags(device, in frame, in gpuVp, hasGpuVp, band, usedGpuCamera, viaInject);

        if (camTraceFramesRemaining == 0)
            ReportCameraTrace();
    }

    /// <summary>
    /// Scores each candidate frame-lag's camera by reprojecting world anchors onto this frame's image, a screen
    /// residual every frame and a depth residual on a throttled subset, and files the captured-camera row, the
    /// per-band used-camera residual and the after-stop drift classification alongside it.
    /// </summary>
    private void SweepFrameLags(RenderDevice device, in FrameContext frame, in Matrix4x4 gpuVp, bool hasGpuVp, int band, bool usedGpuCamera, bool viaInject)
    {
        // The game's collision raycast under a screen grid gives camera-agnostic physical anchors.
        Span<Vector2> screens = stackalloc Vector2[16];
        Span<Vector3> worlds = stackalloc Vector3[16];
        var n = 0;
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var s = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(s, out var w))
                {
                    screens[n] = s;
                    worlds[n] = w;
                    n++;
                }
            }
        }

        if (n == 0)
            return;

        // Each candidate camera's View*Proj and analytic depth map: sample = map.x + map.y / clipW.
        var lags = Math.Min(LagSweepMax, NoireDraw3D.CameraHistoryDepth);
        Span<Matrix4x4> vp = stackalloc Matrix4x4[LagSweepMax];
        Span<Vector4> map = stackalloc Vector4[LagSweepMax];
        Span<bool> ok = stackalloc bool[LagSweepMax];
        var available = 0;
        for (var k = 0; k < lags; k++)
        {
            if (NoireDraw3D.TryGetCameraHistory(k, out var camK) && camK.HasRenderCamera)
            {
                vp[k] = camK.View * camK.Proj;
                var near = camK.NearPlane > 1e-6f ? camK.NearPlane : 0.1f;
                map[k] = DepthCalibration.AnalyticMap(near, camK.FarPlane, camK.StandardZ, camK.FiniteFarPlane);
                ok[k] = true;
                available = k + 1;
            }
        }

        if (available == 0)
            return;

        // The camera the frame was actually projected with, which is the misalignment the eye sees.
        var useCap = usedGpuCamera && hasGpuVp;
        var hasUsed = useCap || ok[0];
        var usedVp = useCap ? gpuVp : vp[0];

        // Screen residual: how far each candidate camera puts the anchor from where the game shows it.
        var motionSum = 0.0;
        var motionN = 0;
        var usedSum = 0.0;
        var usedN = 0;
        var lag0Sum = 0.0;
        var lag0N = 0;
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k < available; k++)
            {
                if (!ok[k] || !TryProjectToScreen(in vp[k], worlds[i], frame.ViewportSize, out var proj))
                    continue;
                camTraceScreenResidual[k] += Vector2.Distance(proj, screens[i]);
                camTraceScreenResidualN[k]++;
            }

            if (hasGpuVp && TryProjectToScreen(in gpuVp, worlds[i], frame.ViewportSize, out var capProj))
            {
                camTraceCapScreenResidual += Vector2.Distance(capProj, screens[i]);
                camTraceCapScreenResidualN++;
            }

            // Camera motion this frame in anchor pixels: this frame's camera against last frame's, same world point.
            if (ok[0] && TryProjectToScreen(in vp[0], worlds[i], frame.ViewportSize, out var s0))
            {
                lag0Sum += Vector2.Distance(s0, screens[i]);
                lag0N++;

                if (ok[1] && TryProjectToScreen(in vp[1], worlds[i], frame.ViewportSize, out var s1))
                {
                    motionSum += Vector2.Distance(s0, s1);
                    motionN++;
                }
            }

            if (hasUsed && TryProjectToScreen(in usedVp, worlds[i], frame.ViewportSize, out var usedProj))
            {
                usedSum += Vector2.Distance(usedProj, screens[i]);
                usedN++;
            }
        }

        // The band means and the settle classification both need this frame's motion and residual, so a frame
        // missing anchors for either stays unclassified.
        var usedMean = usedN > 0 ? usedSum / usedN : double.NaN;
        if (band >= 0)
        {
            if (motionN > 0)
            {
                bandMotionSum[band] += motionSum / motionN;
                bandMotionN[band]++;
            }

            if (usedN > 0)
            {
                bandUsedScreenSum[band] += usedMean;
                bandUsedScreenMax[band] = MathF.Max(bandUsedScreenMax[band], (float)usedMean);
                bandUsedScreenN[band]++;
            }

            if (lag0N > 0)
            {
                bandLag0ScreenSum[band] += lag0Sum / lag0N;
                bandLag0ScreenN[band]++;
            }
        }

        if (settleTracker != null && motionN > 0 && usedN > 0)
        {
            var kind = settleTracker.Advance(motionSum / motionN >= MovingPxPerFrame, usedMean >= DriftingPx);
            if (kind == SettleFrame.Settle)
            {
                if (usedGpuCamera)
                    settleCapUsedFrames++;
                if (!viaInject)
                    settleFallbackCompositeFrames++;
            }
        }

        // Depth residual (predicted depth-buffer sample against the actual texel), throttled because a whole-texture
        // readback is heavy. Anchored to the pixels themselves, so it is the tie-breaker, not the screen residual.
        if (camTraceFramesRemaining % DepthReadbackEvery != 0 || !GameRenderSources.TryGetDepthTexture(out var info))
            return;

        camTracePointScratch ??= new List<Vector2>(16);
        var pts = camTracePointScratch;
        pts.Clear();
        for (var i = 0; i < n; i++)
            pts.Add(screens[i]);

        var depth = DepthReadback.TryReadAtPoints(device, in info, pts, frame.ViewportSize, out _);
        if (depth == null)
            return;
        camTraceDepthReadbacks++;

        for (var i = 0; i < n; i++)
        {
            var actual = depth[i];
            if (float.IsNaN(actual) || actual < 0f || actual > 1f)
                continue;

            for (var k = 0; k < available; k++)
            {
                if (!ok[k])
                    continue;

                var clip = Vector4.Transform(new Vector4(worlds[i], 1f), vp[k]);
                if (clip.W <= 1e-4f)
                    continue;

                var predicted = map[k].X + map[k].Y / clip.W;
                camTraceDepthResidual[k] += MathF.Abs(predicted - actual);
                camTraceDepthResidualN[k]++;
            }

            // The capture's clip-w comes from the uploaded W column; the value mapping only needs the near plane,
            // so the lag-0 camera's map applies.
            if (hasGpuVp && ok[0])
            {
                var capClip = Vector4.Transform(new Vector4(worlds[i], 1f), gpuVp);
                if (capClip.W > 1e-4f)
                {
                    var capPredicted = map[0].X + map[0].Y / capClip.W;
                    camTraceCapDepthResidual += MathF.Abs(capPredicted - actual);
                    camTraceCapDepthResidualN++;
                }
            }

            // The used camera per frame-time band, accumulated per anchor sample like the other depth rows.
            if (band >= 0 && hasUsed && ok[0])
            {
                var usedClip = Vector4.Transform(new Vector4(worlds[i], 1f), usedVp);
                if (usedClip.W > 1e-4f)
                {
                    var usedPredicted = map[0].X + map[0].Y / usedClip.W;
                    bandUsedDepthSum[band] += MathF.Abs(usedPredicted - actual);
                    bandUsedDepthN[band]++;
                }
            }
        }
    }

    /// <summary>Formats the frame-lag sweep and names the best-fit lag (the correction the injected overlay should apply).</summary>
    private void ReportCameraTrace()
    {
        var traced = camTraceInjectFrames + camTraceFallbackFrames;
        var meanLegacy = camTraceScreenSamples > 0 ? camTraceScreenSum / camTraceScreenSamples : 0;

        var bestDepthK = -1;
        var bestDepth = double.MaxValue;
        var bestScreenK = -1;
        var bestScreen = double.MaxValue;

        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D camtrace: {traced} frames - inject {camTraceInjectFrames} (world-snapshot {camTraceSnapshotFrames}, main-pass {camTraceMainPassFrames}), present-time fallback {camTraceFallbackFrames}. Depth readbacks: {camTraceDepthReadbacks}.");
        sb.AppendLine($"  GPU camera capture: {NoireDraw3D.DescribeCameraCapture()}");
        sb.AppendLine($"  capture during trace: fresh on {camTraceCapAvailableFrames}/{traced} frames, projected with on {camTraceCapUsedFrames} (toggle: /noire3d gpucam).");
        if (camTraceSnapshotFrames > 0 && camTraceMainPassFrames == 0)
            sb.AppendLine("  WARNING: 0 main-pass snapshots - the RTM.DepthStencil fingerprint is not matching, so both the struct fix and the capture commit are inert. Report this.");
        sb.AppendLine($"  Secondary proj-vs-live drift (blind to the true swim): max {camTraceMaxScreenDelta:F2} px, mean {meanLegacy:F2} px; View*Proj max element delta {camTraceMaxMatrixDelta:E2}.");
        sb.AppendLine("  Frame-lag sweep - how well the camera k frames back reprojects onto THIS frame's rendered image (lower = better fit):");
        sb.AppendLine("   lag | depth residual (sample units, n) | screen residual (px, n)");
        for (var k = 0; k < LagSweepMax; k++)
        {
            var dN = camTraceDepthResidualN[k];
            var sN = camTraceScreenResidualN[k];
            if (dN == 0 && sN == 0)
                continue;

            var dMean = dN > 0 ? camTraceDepthResidual[k] / dN : double.NaN;
            var sMean = sN > 0 ? camTraceScreenResidual[k] / sN : double.NaN;
            if (dN > 0 && dMean < bestDepth) { bestDepth = dMean; bestDepthK = k; }
            if (sN > 0 && sMean < bestScreen) { bestScreen = sMean; bestScreenK = k; }

            var dTxt = dN > 0 ? $"{dMean:E3} ({dN})" : "-";
            var sTxt = sN > 0 ? $"{sMean:F2} ({sN})" : "-";
            sb.AppendLine($"   {k,3} | {dTxt,-32} | {sTxt}");
        }

        var capDepthMean = camTraceCapDepthResidualN > 0 ? camTraceCapDepthResidual / camTraceCapDepthResidualN : double.NaN;
        var capScreenMean = camTraceCapScreenResidualN > 0 ? camTraceCapScreenResidual / camTraceCapScreenResidualN : double.NaN;
        if (camTraceCapDepthResidualN > 0 || camTraceCapScreenResidualN > 0)
        {
            var dTxt = camTraceCapDepthResidualN > 0 ? $"{capDepthMean:E3} ({camTraceCapDepthResidualN})" : "-";
            var sTxt = camTraceCapScreenResidualN > 0 ? $"{capScreenMean:F2} ({camTraceCapScreenResidualN})" : "-";
            sb.AppendLine($"   cap | {dTxt,-32} | {sTxt}   (the captured GPU camera constants)");
        }

        sb.AppendLine("  Frame-time bands - 'used' is the camera the overlay actually projected with. Screen px carry the anchor bias under motion; used depth is the pixel-anchored truth:");
        sb.AppendLine("   band              | frames | inject | capFresh | capUsed | motion px | used px mean/max | used depth (n)  | lag0 px");
        for (var b = 0; b < CameraSwimAnalysis.BandCount; b++)
        {
            if (bandFrames[b] == 0)
                continue;

            var motionTxt = bandMotionN[b] > 0 ? (bandMotionSum[b] / bandMotionN[b]).ToString("F2") : "-";
            var usedTxt = bandUsedScreenN[b] > 0 ? $"{bandUsedScreenSum[b] / bandUsedScreenN[b]:F2}/{bandUsedScreenMax[b]:F1}" : "-";
            var usedDepthTxt = bandUsedDepthN[b] > 0 ? $"{bandUsedDepthSum[b] / bandUsedDepthN[b]:E2} ({bandUsedDepthN[b]})" : "-";
            var lag0Txt = bandLag0ScreenN[b] > 0 ? (bandLag0ScreenSum[b] / bandLag0ScreenN[b]).ToString("F2") : "-";
            sb.AppendLine($"   {CameraSwimAnalysis.BandLabel(b),-17} | {bandFrames[b],6} | {bandInject[b],6} | {bandCapFresh[b],8} | {bandCapUsed[b],7} | {motionTxt,9} | {usedTxt,-16} | {usedDepthTxt,-15} | {lag0Txt}");
        }

        var settle = settleTracker;
        if (settle != null)
        {
            sb.AppendLine($"  After-stop drift (camera steady, used residual >= {DriftingPx:F0} px, starting within {SettleTracker.WindowFrames} frames of motion): "
                          + $"{settle.Events} event(s), {settle.SettleFrames} frame(s), longest {settle.LongestRun}; capture-projected on {settleCapUsedFrames} of them, present-time composite on {settleFallbackCompositeFrames}. "
                          + $"Context: moving {settle.MovingFrames}, quiet-clean {settle.QuietCleanFrames}, late drift {settle.LateDriftFrames}.");
        }

        // The camera measures clean while the layer draws nothing, so a trace with an empty screen has reproduced
        // no visible misalignment at all.
        var (renderedNow, emptyNow) = NoireDraw3D.FrameCounters;
        var renderedDuringTrace = renderedNow - camTraceArmRendered;
        var emptyDuringTrace = emptyNow - camTraceArmEmpty;
        sb.AppendLine($"  Content during trace: {renderedDuringTrace} frame(s) drew content, {emptyDuringTrace} were empty."
                      + (renderedDuringTrace == 0 ? " NOTHING WAS DRAWN - the camera was measured, the visible swim was not. Put the swimming content on screen and trace again." : string.Empty));

        var best = bestDepthK >= 0 ? bestDepthK : bestScreenK;
        var capBeatsStruct = camTraceCapDepthResidualN > 0 && bestDepthK >= 0 && capDepthMean <= bestDepth * 1.05;
        string verdict;
        if (best < 0)
            verdict = "no usable anchors - aim at terrain/walls and keep the camera moving while tracing.";
        else if (capBeatsStruct)
            verdict = "the captured GPU camera fits the pixels at least as well as every struct lag - it is the pixels' camera. "
                      + (camTraceCapUsedFrames > 0 ? "The overlay is projecting with it; camera swim is eliminated." : "Turn it on (/noire3d gpucam) - the overlay is not using it.");
        else if (camTraceCapDepthResidualN > 0)
            verdict = "the captured GPU camera fits WORSE than the best struct lag - the capture may be locked on the wrong window. "
                      + "Run /noire3d cbprobe.";
        else if (best == 0)
            verdict = "best fit is lag 0 and no capture was available - the struct snapshot is in phase here; capture engages under load "
                      + "(check the capture state above if it never locks).";
        else
            verdict = $"the pixels best match the camera from {best} frame(s) back and no capture was available. "
                      + "The capture should remove this once locked - check its state above, and run /noire3d cbprobe if it stays unlocked.";

        sb.AppendLine($"  Verdict: {verdict}");

        // Load verdict over the populated frame-time bands: flat means the projection is not time-derived, rising
        // means something in the path still is.
        var loBand = -1;
        var hiBand = -1;
        for (var b = 0; b < CameraSwimAnalysis.BandCount; b++)
        {
            if (bandUsedDepthN[b] >= 8)
            {
                if (loBand < 0)
                    loBand = b;
                hiBand = b;
            }
        }

        if (loBand >= 0 && hiBand > loBand)
        {
            var loMean = bandUsedDepthSum[loBand] / bandUsedDepthN[loBand];
            var hiMean = bandUsedDepthSum[hiBand] / bandUsedDepthN[hiBand];
            sb.AppendLine(hiMean > loMean * 2 && hiMean > 1e-4
                ? $"  Load verdict: used-camera depth residual RISES with frame time ({loMean:E2} at {CameraSwimAnalysis.BandLabel(loBand)} -> {hiMean:E2} at {CameraSwimAnalysis.BandLabel(hiBand)}) - something in the path is still time-derived."
                : $"  Load verdict: used-camera depth residual is flat across frame-time bands ({loMean:E2} -> {hiMean:E2}) - the projection is not load-derived; low-fps judder and any after-stop drift are the remaining suspects.");
        }
        else
        {
            sb.AppendLine("  Load verdict: not enough band coverage - trace longer (camtrace 600+) while the load varies (heavy scene, zoom cycles), so several bands collect depth samples.");
        }

        if (settle is { Events: > 0 })
        {
            if (settle.Events >= SettleVerdictMinEvents && settle.SettleFrames >= SettleVerdictMinFrames && settle.LongestRun >= SettleVerdictMinRun)
            {
                sb.AppendLine(settleCapUsedFrames * 2 >= settle.SettleFrames
                    ? "  Settle verdict: the overlay keeps drifting after the camera stops WHILE projecting the captured camera - not a camera-phase error. Suspect the composite path: inject skips, or a stale scene render being re-presented."
                    : "  Settle verdict: after-stop drift happens on struct-fallback frames - the capture starves right after motion under load. Correlate with commit misses (/noire3d stats).");
            }
            else
            {
                sb.AppendLine($"  Settle verdict: {settle.SettleFrames} isolated frame(s) across {settle.Events} event(s) is within noise - no meaningful after-stop drift measured.");
            }
        }

        NoireLogger.PrintToChat($"Draw3D camtrace: best-fit lag = {(best < 0 ? "n/a" : best.ToString())}, cap row {(camTraceCapDepthResidualN > 0 ? capDepthMean.ToString("E2") : "n/a")}, fallback {camTraceFallbackFrames}/{traced}, settle events {settleTracker?.Events ?? 0} (details in log).");
        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
    }

    /// <summary>Largest absolute element-wise difference between two matrices.</summary>
    /// <param name="a">First matrix.</param>
    /// <param name="b">Second matrix.</param>
    /// <returns>The largest absolute per-element difference.</returns>
    private static float MaxElementDelta(in Matrix4x4 a, in Matrix4x4 b)
    {
        var m = MathF.Abs(a.M11 - b.M11);
        m = MathF.Max(m, MathF.Abs(a.M12 - b.M12));
        m = MathF.Max(m, MathF.Abs(a.M13 - b.M13));
        m = MathF.Max(m, MathF.Abs(a.M14 - b.M14));
        m = MathF.Max(m, MathF.Abs(a.M21 - b.M21));
        m = MathF.Max(m, MathF.Abs(a.M22 - b.M22));
        m = MathF.Max(m, MathF.Abs(a.M23 - b.M23));
        m = MathF.Max(m, MathF.Abs(a.M24 - b.M24));
        m = MathF.Max(m, MathF.Abs(a.M31 - b.M31));
        m = MathF.Max(m, MathF.Abs(a.M32 - b.M32));
        m = MathF.Max(m, MathF.Abs(a.M33 - b.M33));
        m = MathF.Max(m, MathF.Abs(a.M34 - b.M34));
        m = MathF.Max(m, MathF.Abs(a.M41 - b.M41));
        m = MathF.Max(m, MathF.Abs(a.M42 - b.M42));
        m = MathF.Max(m, MathF.Abs(a.M43 - b.M43));
        return MathF.Max(m, MathF.Abs(a.M44 - b.M44));
    }

    /// <summary>Projects a world point through a raw View*Proj into framebuffer-pixel screen space.</summary>
    /// <param name="viewProj">Combined view-projection matrix.</param>
    /// <param name="world">World-space point.</param>
    /// <param name="viewport">Viewport size in pixels.</param>
    /// <param name="screen">The projected pixel position.</param>
    /// <returns>False when the point is behind the camera.</returns>
    private static bool TryProjectToScreen(in Matrix4x4 viewProj, Vector3 world, Vector2 viewport, out Vector2 screen)
    {
        screen = default;
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-4f)
            return false;

        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        screen = new Vector2((ndc.X * 0.5f + 0.5f) * viewport.X, (0.5f - ndc.Y * 0.5f) * viewport.Y);
        return true;
    }

    internal void OnFrameRendered(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        if (!probePending)
            return;

        probePending = false;

        try
        {
            RunProbeNow(device, in frame, sceneDepth);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D depth probe failed.", "Draw3D");
        }
    }

    private void RunProbeNow(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        // The game's own raycast is read here on the render thread so the points compare against this frame's depth
        // texels under the same camera.
        var screens = new List<Vector2>();
        var worlds = new List<Vector3>();
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(screen, out var world))
                {
                    screens.Add(screen);
                    worlds.Add(world);
                }
            }
        }

        if (screens.Count == 0)
        {
            Report("Draw3D probe: no raycast hits (nothing under the sampled screen points?). Aim the camera at terrain and retry.");
            return;
        }

        // Expected buffer value from the analytic depth map rendering uses, plus the reconstructed device-z as a
        // cross-check. Both come from the camera, never from a fit.
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var expectedMap = new float[screens.Count];
        var expectedProjZ = new float[screens.Count];
        var clipWs = new float[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var clip = Vector4.Transform(new Vector4(worlds[i], 1f), frame.ViewProj);
            clipWs[i] = clip.W;
            expectedMap[i] = clip.W > 1e-6f ? map.X + map.Y / clip.W : float.NaN;
            expectedProjZ[i] = clip.W > 1e-6f ? clip.Z / clip.W : float.NaN;
        }

        float[]? actualMain = null, actualSwap = null;
        string mainDesc = "unavailable", swapDesc = "unavailable";
        if (GameRenderSources.TryGetDepthTexture(out var mainInfo))
            actualMain = DepthReadback.TryReadAtPoints(device, in mainInfo, screens, frame.ViewportSize, out mainDesc);
        if (GameRenderSources.TryGetSwapChainDepthTexture(out var swapInfo))
            actualSwap = DepthReadback.TryReadAtPoints(device, in swapInfo, screens, frame.ViewportSize, out swapDesc);

        // A least-squares fit of the same raycast points, reported only: a gap between it and the analytic map is
        // collision-vs-rendered-surface disagreement, and rendering always uses the analytic map.
        var fitXs = new List<float>(screens.Count);
        var fitYs = new List<float>(screens.Count);
        if (actualMain != null)
        {
            for (var i = 0; i < screens.Count; i++)
            {
                if (clipWs[i] > 1e-6f && !float.IsNaN(actualMain[i]) && actualMain[i] is >= 0f and <= 1f)
                {
                    fitXs.Add(1f / clipWs[i]);
                    fitYs.Add(actualMain[i]);
                }
            }
        }

        var fitDesc = DepthCalibration.TrySolve(fitXs, fitYs, out var fitA, out var fitB, out var fitResid, out var fitInliers)
            ? $"z={fitA:E2}{(fitB >= 0 ? "+" : "")}{fitB:F5}/w ({fitInliers} pts, resid {fitResid:E1})"
            : "unfittable this frame";

        var details = new StringBuilder();
        details.AppendLine($"Draw3D probe: {screens.Count} raycast points. Active source: {sceneDepth?.Description ?? "none"}.");
        details.AppendLine($"  analytic map (used by rendering): z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w");
        details.AppendLine($"  raycast fit (diagnostic only):    {fitDesc}");
        details.AppendLine($"  RenderTargetManager depth: {mainDesc}");
        details.AppendLine($"  SwapChain depth:           {swapDesc}");
        details.AppendLine("  point | expected(map) expected(projZ) | actual(RTM) actual(Swap)");
        for (var i = 0; i < screens.Count; i++)
            details.AppendLine($"  {i,2}: {Fmt(expectedMap, i)} {Fmt(expectedProjZ, i)} | {Fmt(actualMain, i)} {Fmt(actualSwap, i)}");

        var mainVsMap = CountMatches(expectedMap, actualMain);
        var swapVsMap = CountMatches(expectedMap, actualSwap);
        details.AppendLine($"  matches within 1e-3: RTM vs map {mainVsMap}/{screens.Count}, Swap vs map {swapVsMap}/{screens.Count}");

        var gate = (int)MathF.Ceiling(screens.Count * 0.9f);
        var verdict = mainVsMap >= gate ? "PASS"
            : swapVsMap >= gate ? "FAIL - scene depth lives in the SwapChain buffer at present time"
            : "FAIL - the analytic map does not match the RTM buffer "
              + "(mismatched rows are usually collision-vs-rendered-surface disagreement, harmless if few)";

        Report($"Draw3D probe [{verdict.Split(' ')[0]}]: RTM vs map {mainVsMap}/{screens.Count} (gate >= {gate}). {(verdict.Contains('-') ? verdict[(verdict.IndexOf('-') + 2)..] : "Analytic depth mapping confirmed against ground truth.")}");
        NoireLogger.LogInfo($"Draw3D probe details:\n{details}", "Draw3D");
    }

    private static string Fmt(float[]? values, int i)
        => values == null || float.IsNaN(values[i]) ? "   n/a  " : values[i].ToString("F6");

    private static int CountMatches(float[] expected, float[]? actual)
    {
        if (actual == null)
            return 0;

        var matches = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!float.IsNaN(expected[i]) && !float.IsNaN(actual[i]) && MathF.Abs(expected[i] - actual[i]) <= 1e-3f)
                matches++;
        }

        return matches;
    }

    private static void Report(string message)
    {
        NoireLogger.PrintToChat(message);
        NoireLogger.LogInfo(message, "Draw3D");
    }
}
