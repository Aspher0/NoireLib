using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D;

/// <summary>
/// The runtime diagnostics behind <c>/noire3d</c>, every measurement sampling on the render thread and reporting to the log.
/// </summary>
public sealed unsafe class Draw3DDiagnostics
{
    /// <summary>Gets the orientation overrides both import paths apply, for a model authored in an unexpected convention.</summary>
    public Assets.Draw3DImportFlips ImportFlips { get; } = new();

    private int validateFramesRemaining;
    private float validateMaxDelta;
    private double validateDeltaSum;
    private int validateSamples;
    private float validateMaxMatrixDelta;
    private bool probePending;

    internal Draw3DDiagnostics() { }

    /// <summary>Gets or sets whether the layer projects with the captured GPU camera constants whenever a frame has them (default true).</summary>
    public bool PreferCapturedCamera { get; set; } = true;

    /// <summary>Gets or sets whether the temporal anti-aliasing jitter is removed from the captured camera before compositing (default true).</summary>
    public bool RemoveTemporalJitter { get; set; } = true;

    /// <summary>Gets or sets whether the layer is temporally resolved. Occlusion edges against the game's jittered depth settle (default true).</summary>
    public bool TemporalStabilization { get; set; } = true;

    /// <summary>Gets or sets the weight of the current frame in the temporal resolve (default 0.125, one jitter cycle).</summary>
    public float TemporalWeight { get; set; } = 0.125f;

    /// <summary>
    /// Gets or sets where the game rasterizes relative to its camera constants, in display pixels (x right, y down),
    /// which the layer's projection reproduces (default (0.5, 0.5)).
    /// </summary>
    public Vector2 GamePixelOffset { get; set; } = new(0.5f, 0.5f);

    /// <summary>Gets the temporal anti-aliasing jitter removed from the last composited frame, in normalized device units.</summary>
    public Vector2 LastRemovedJitter { get; internal set; }

    /// <summary>
    /// Gets or sets a fixed present-buffer bind ordinal to force the layer in at for testing, 0 (the default) injecting at
    /// the first present-buffer bind carrying a depth-stencil.
    /// </summary>
    public int InjectOrdinal
    {
        get => NoireDraw3D.RenderTargetTapForDiagnostics?.InjectOrdinal ?? 0;
        set
        {
            if (NoireDraw3D.RenderTargetTapForDiagnostics is { } tap)
                tap.InjectOrdinal = value;
        }
    }

    /// <summary>Gets where the layer went in during the last presented frame: the rule, the bind and the bind count.</summary>
    public string InjectionDescription => NoireDraw3D.RenderTargetTapForDiagnostics?.InjectionDescription ?? "no render-target tap";

    /// <summary>Logs one frame's render-target bind sequence, a few frames from now.</summary>
    public void CaptureBindSequence() => NoireDraw3D.RenderTargetTapForDiagnostics?.ArmCapture();

    /// <summary>Arms the projection parity validator for the next 10 rendered frames. The gate is a 1 px maximum.</summary>
    public void RunValidate()
    {
        NoireDraw3D.EnsureInitialized();
        validateFramesRemaining = 10;
        validateMaxDelta = 0f;
        validateDeltaSum = 0;
        validateSamples = 0;
        validateMaxMatrixDelta = 0f;
    }

    /// <summary>Arms the raycast ground-truth depth probe for the next rendered frame.</summary>
    public void RunProbe()
    {
        NoireDraw3D.EnsureInitialized();
        probePending = true;
    }

    /// <summary>Gets or sets wireframe rasterization of the scene pass, ground decals tracing <see cref="DecalShapeOutlines"/> instead.</summary>
    public bool Wireframe
    {
        get => NoireDraw3D.Wireframe;
        set => NoireDraw3D.Wireframe = value;
    }

    /// <summary>Flips <see cref="Wireframe"/>.</summary>
    /// <returns>The new state.</returns>
    public bool ToggleWireframe() => Wireframe = !Wireframe;

    /// <summary>
    /// Gets or sets whether every decal's painted shape, retained or immediate, is traced as an outline over normal
    /// rendering, always the case while <see cref="Wireframe"/> is on.
    /// </summary>
    public bool DecalShapeOutlines
    {
        get => NoireDraw3D.DecalShapeOutlines;
        set => NoireDraw3D.DecalShapeOutlines = value;
    }

    /// <summary>
    /// Gets or sets whether every decal's projection box, the volume its SDF is evaluated in, is drawn as a wireframe
    /// (per node: <see cref="Scene.SceneNode.ShowDecalVolume"/>).
    /// </summary>
    public bool DecalVolumeOutlines
    {
        get => NoireDraw3D.DecalVolumeOutlines;
        set => NoireDraw3D.DecalVolumeOutlines = value;
    }

    /// <summary>Formats the current stats snapshot.</summary>
    /// <returns>The formatted snapshot.</returns>
    public string GetStatsText() => NoireDraw3D.Stats.ToString();

    internal void OnFrame(in FrameContext frame, in GameRenderSources.CameraData cam, bool hasDepth)
    {
        if (validateFramesRemaining <= 0)
            return;

        validateFramesRemaining--;

        // On the framework thread the camera would move between the two projections.
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
            var report = $"Draw3D validate [{verdict}]: {validateSamples} samples over 10 frames, max {validateMaxDelta:F3} px, mean {mean:F3} px (gate: max <= 1 px). " +
                         $"VP cross-check max element delta: {validateMaxMatrixDelta:E2}. Camera fallback active: {frame.UsedFallbackCamera}. " +
                         "Repeat across camera poses: orbit, side-on grazing, wall-collision camera, first-person, max zoom.";
            NoireLogger.PrintToChat($"Draw3D validate: {verdict}, max {validateMaxDelta:F3} px (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
    }

    // Only the offset matching the game's raster convention reconstructs the same points from two views.
    private static readonly Vector2[] CalibrationOffsets =
    {
        new(-0.5f, 0f), new(0f, 0f), new(0.5f, 0f),
        new(-0.5f, 0.5f), new(0f, 0.5f), new(0.5f, 0.5f),
        new(-0.5f, 1f), new(0f, 1f), new(0.5f, 1f),
    };

    private const int CalibrationGridX = 24;
    private const int CalibrationGridY = 14;
    private int calibrationStage;      // 1 = snapshot armed, 2 = snapshot held, 3 = compare armed
    private Vector3[][]? calibrationPoints;
    private Vector3 calibrationEye;

    /// <summary>Takes the first view of the pixel-convention calibration. Turn the camera, then call <see cref="CompareDepthCalibration"/>.</summary>
    public void SnapshotDepthCalibration()
    {
        NoireDraw3D.EnsureInitialized();
        calibrationStage = 1;
    }

    /// <summary>Takes the second view of the pixel-convention calibration and logs each candidate offset's disagreement. The smallest wins.</summary>
    public void CompareDepthCalibration()
    {
        NoireDraw3D.EnsureInitialized();
        if (calibrationStage == 2)
            calibrationStage = 3;
    }

    // Reads the game's depth like the occlusion shader, shifted by a candidate pixel offset.
    private bool TryReconstruct(RenderDevice device, in FrameContext frame, IReadOnlyList<Vector2> displayPixels, Vector2 offset, Vector3[] into)
    {
        var vp = frame.ViewProj;
        if (!Core.CameraConstantCapture.TryReadViewShape(in vp, out var shape) || !GameRenderSources.TryGetDepthTexture(out var info)
            || !Matrix4x4.Invert(vp, out var inv))
            return false;

        var eye = shape.Eye;
        var jitterUv = NoireDraw3D.DepthSampleJitterUv;
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var size = new Vector2(info.ActualWidth, info.ActualHeight);
        var display = frame.ViewportSize;
        var texelScale = size / display;

        var jitterOnly = new Vector2(jitterUv.X, jitterUv.Y);

        var screens = new List<Vector2>(displayPixels.Count * 4);
        var fracs = new Vector2[displayPixels.Count];
        for (var i = 0; i < displayPixels.Count; i++)
        {
            var uv = (displayPixels[i] / display) + jitterOnly + (offset / display);
            var t = (uv * size) - new Vector2(0.5f);
            var i0 = new Vector2(MathF.Floor(t.X), MathF.Floor(t.Y));
            fracs[i] = t - i0;
            foreach (var o in new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One })
            {
                var centre = i0 + o + new Vector2(0.5f);
                screens.Add(centre / texelScale);
            }
        }

        var raw = DepthReadback.TryReadAtPoints(device, in info, screens, display, out _);
        if (raw == null)
            return false;

        for (var i = 0; i < displayPixels.Count; i++)
        {
            var f = fracs[i];
            var z00 = raw[(i * 4) + 0];
            var z10 = raw[(i * 4) + 1];
            var z01 = raw[(i * 4) + 2];
            var z11 = raw[(i * 4) + 3];
            var w00 = map.Y / (z00 - map.X);
            var w11 = map.Y / (z11 - map.X);
            var w10 = map.Y / (z10 - map.X);
            var w01 = map.Y / (z01 - map.X);
            var wMin = MathF.Min(MathF.Min(w00, w10), MathF.Min(w01, w11));
            var wMax = MathF.Max(MathF.Max(w00, w10), MathF.Max(w01, w11));

            // Across an object's edge the interpolation means nothing.
            if (!(wMin > 0f) || !float.IsFinite(wMax) || wMax - wMin > 0.03f * wMin || wMax > 400f)
            {
                into[i] = new Vector3(float.NaN);
                continue;
            }

            var z = ((z00 * (1 - f.X)) + (z10 * f.X)) * (1 - f.Y) + ((z01 * (1 - f.X)) + (z11 * f.X)) * f.Y;
            var sceneW = map.Y / (z - map.X);

            var ndc = new Vector2((displayPixels[i].X / display.X * 2f) - 1f, 1f - (displayPixels[i].Y / display.Y * 2f));
            var farPoint = Vector4.Transform(new Vector4(ndc, 0.5f, 1f), inv);
            var p = new Vector3(farPoint.X, farPoint.Y, farPoint.Z) / farPoint.W;
            var pClip = Vector4.Transform(new Vector4(p, 1f), vp);
            into[i] = eye + ((p - eye) * (sceneW / pClip.W));
        }

        return true;
    }

    private void RunCalibration(RenderDevice device, in FrameContext frame)
    {
        var display = frame.ViewportSize;
        if (calibrationStage == 1)
        {
            var pixels = new List<Vector2>();
            for (var gy = 0; gy < CalibrationGridY; gy++)
            {
                for (var gx = 0; gx < CalibrationGridX; gx++)
                    pixels.Add(new Vector2(MathF.Floor(display.X * (gx + 0.5f) / CalibrationGridX) + 0.5f, MathF.Floor(display.Y * (0.15f + (0.7f * (gy + 0.5f) / CalibrationGridY))) + 0.5f));
            }

            calibrationPoints = new Vector3[CalibrationOffsets.Length][];
            for (var c = 0; c < CalibrationOffsets.Length; c++)
            {
                calibrationPoints[c] = new Vector3[pixels.Count];
                if (!TryReconstruct(device, in frame, pixels, CalibrationOffsets[c], calibrationPoints[c]))
                {
                    Report("Draw3D depth calibration: snapshot failed (no camera shape or depth).");
                    calibrationStage = 0;
                    return;
                }
            }

            Core.CameraConstantCapture.TryReadViewShape(frame.ViewProj, out var s);
            calibrationEye = s.Eye;
            calibrationStage = 2;
            Report($"Draw3D depth calibration: snapshot taken from eye {calibrationEye:0.00}. Turn the camera, then compare.");
            return;
        }

        if (calibrationStage != 3 || calibrationPoints == null)
            return;

        calibrationStage = 0;
        Core.CameraConstantCapture.TryReadViewShape(frame.ViewProj, out var now);
        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D depth calibration: snapshot eye {calibrationEye:0.00}, compare eye {now.Eye:0.00} (moved {Vector3.Distance(calibrationEye, now.Eye):0.00} m).");
        sb.AppendLine("  offset (x, y) px | points | median error | mean error   (distance between the two views' reconstructions)");
        for (var c = 0; c < CalibrationOffsets.Length; c++)
        {
            var pts = calibrationPoints[c];
            var pixels = new List<Vector2>();
            var index = new List<int>();
            for (var i = 0; i < pts.Length; i++)
            {
                if (float.IsNaN(pts[i].X))
                    continue;

                var clip = Vector4.Transform(new Vector4(pts[i], 1f), frame.ViewProj);
                if (clip.W <= 0.5f)
                    continue;

                var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
                if (MathF.Abs(ndc.X) > 0.95f || MathF.Abs(ndc.Y) > 0.95f)
                    continue;

                pixels.Add(new Vector2(((ndc.X * 0.5f) + 0.5f) * display.X, (0.5f - (ndc.Y * 0.5f)) * display.Y));
                index.Add(i);
            }

            var again = new Vector3[pixels.Count];
            if (pixels.Count == 0 || !TryReconstruct(device, in frame, pixels, CalibrationOffsets[c], again))
            {
                sb.AppendLine($"  {CalibrationOffsets[c].X,4:+0.0;-0.0;0.0}, {CalibrationOffsets[c].Y,4:+0.0;-0.0;0.0} | no points");
                continue;
            }

            var errors = new List<float>();
            for (var k = 0; k < again.Length; k++)
            {
                if (!float.IsNaN(again[k].X))
                    errors.Add(Vector3.Distance(again[k], pts[index[k]]));
            }

            if (errors.Count == 0)
            {
                sb.AppendLine($"  {CalibrationOffsets[c].X,4:+0.0;-0.0;0.0}, {CalibrationOffsets[c].Y,4:+0.0;-0.0;0.0} | no points");
                continue;
            }

            errors.Sort();
            var mean = 0f;
            foreach (var e in errors)
                mean += e;
            mean /= errors.Count;
            sb.AppendLine($"  {CalibrationOffsets[c].X,4:+0.0;-0.0;0.0}, {CalibrationOffsets[c].Y,4:+0.0;-0.0;0.0} | {errors.Count,6} | {errors[errors.Count / 2] * 100f,8:0.00} cm | {mean * 100f,8:0.00} cm");
        }

        Report(sb.ToString());
    }

    private bool groundGridPending;
    private Vector3 groundGridCentre;
    private float groundGridSpacing;

    /// <summary>Logs, on the next rendered frame, the game's rendered surface height under a 3 x 3 world grid, read as the occlusion shader reads it.</summary>
    /// <param name="centre">The grid centre, at the height of the shape being judged.</param>
    /// <param name="spacing">The distance between grid points, in world units.</param>
    public void ProbeGroundGrid(Vector3 centre, float spacing)
    {
        NoireDraw3D.EnsureInitialized();
        groundGridCentre = centre;
        groundGridSpacing = spacing;
        groundGridPending = true;
    }

    private void RunGroundGrid(RenderDevice device, in FrameContext frame)
    {
        var vp = frame.ViewProj;
        if (!Core.CameraConstantCapture.TryReadViewShape(in vp, out var shape) || !GameRenderSources.TryGetDepthTexture(out var info))
        {
            Report("Draw3D ground grid: no camera shape or no depth texture this frame.");
            return;
        }

        var eye = shape.Eye;
        var jitterUv = NoireDraw3D.DepthSampleJitterUv;
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var texelScale = new Vector2(info.ActualWidth / frame.ViewportSize.X, info.ActualHeight / frame.ViewportSize.Y);

        var points = new List<Vector3>();
        var fracs = new List<Vector2>();
        var clips = new List<Vector4>();
        var screens = new List<Vector2>();
        for (var gz = -1; gz <= 1; gz++)
        {
            for (var gx = -1; gx <= 1; gx++)
            {
                var p = groundGridCentre + new Vector3(gx * groundGridSpacing, 0f, gz * groundGridSpacing);
                var clip = Vector4.Transform(new Vector4(p, 1f), vp);
                if (clip.W <= 1e-4f)
                    continue;

                var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
                var uv = new Vector2((ndc.X * 0.5f) + 0.5f + jitterUv.X, 0.5f - (ndc.Y * 0.5f) + jitterUv.Y);

                // Same texel arithmetic as DepthVisibility.
                var t = (uv * new Vector2(info.ActualWidth, info.ActualHeight)) - new Vector2(0.5f);
                var i0 = new Vector2(MathF.Floor(t.X), MathF.Floor(t.Y));
                points.Add(p);
                clips.Add(clip);
                fracs.Add(t - i0);
                foreach (var o in new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One })
                {
                    var texelCentre = i0 + o + new Vector2(0.5f);
                    screens.Add(new Vector2(texelCentre.X / texelScale.X, texelCentre.Y / texelScale.Y));
                }
            }
        }

        var raw = DepthReadback.TryReadAtPoints(device, in info, screens, frame.ViewportSize, out _);
        if (raw == null)
        {
            Report("Draw3D ground grid: depth readback failed.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D ground grid around {groundGridCentre:0.000}, spacing {groundGridSpacing:0.00}: eye {eye:0.000}, "
                      + $"forward {Vector3.Normalize(groundGridCentre - eye):0.000}, jitter uv {jitterUv.X:E2},{jitterUv.Y:E2}.");
        sb.AppendLine("  point (x, z) | distance | pitch | surface Y | surface Y - point Y");
        for (var k = 0; k < points.Count; k++)
        {
            var z00 = raw[(k * 4) + 0];
            var z10 = raw[(k * 4) + 1];
            var z01 = raw[(k * 4) + 2];
            var z11 = raw[(k * 4) + 3];
            var f = fracs[k];
            var z = ((z00 * (1 - f.X)) + (z10 * f.X)) * (1 - f.Y) + ((z01 * (1 - f.X)) + (z11 * f.X)) * f.Y;
            var denom = z - map.X;
            var p = points[k];
            var dist = Vector3.Distance(eye, p);
            var pitch = MathF.Asin(Math.Clamp((p.Y - eye.Y) / MathF.Max(dist, 1e-4f), -1f, 1f)) * 180f / MathF.PI;
            if (float.IsNaN(z) || denom * map.Y <= 1e-12f)
            {
                sb.AppendLine($"  {p.X:0.000}, {p.Z:0.000} | {dist:0.00} | {pitch:0.0} | sky/unwritten");
                continue;
            }

            var sceneW = map.Y / denom;
            var surface = eye + ((p - eye) * (sceneW / clips[k].W));
            sb.AppendLine($"  {p.X:0.000}, {p.Z:0.000} | {dist:0.00} | {pitch:0.0} | {surface.Y:0.0000} | {(surface.Y - p.Y) * 100f:0.00} cm");
        }

        Report(sb.ToString());
    }

    internal void OnFrameRendered(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        if (calibrationStage is 1 or 3)
        {
            try
            {
                RunCalibration(device, in frame);
            }
            catch (Exception ex)
            {
                calibrationStage = 0;
                NoireLogger.LogError(ex, "Draw3D depth calibration failed.", "Draw3D");
            }
        }

        if (groundGridPending)
        {
            groundGridPending = false;
            try
            {
                RunGroundGrid(device, in frame);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "Draw3D ground grid failed.", "Draw3D");
            }
        }

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

        // A gap to the analytic map is collision-vs-rendered-surface disagreement.
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
