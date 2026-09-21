using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Core;

// Fits sample = A + B/clipW from collision raycasts against depth texels.
internal sealed class DepthCalibration
{
    private const int GridN = 5;                 // 5x5 grid across the screen interior
    private const int MinInliers = 8;
    private const float MinSpread = 1.3f;        // max(w)/min(w). A flat wall cannot calibrate.
    private const float MaxMedianResidual = 5e-4f;
    private const int RetryIntervalFrames = 30;
    private const int BackoffIntervalFrames = 300;
    private const int FailuresBeforeBackoff = 10;

    private float calibratedNear;
    private bool calibratedStandardZ;
    private bool calibratedFiniteFar;
    private long lastAttemptFrame = FrameThrottler.Never;
    private int failStreak;
    private bool troubleLogged;

    public bool IsValid { get; private set; }

    public float A { get; private set; }

    // Positive means reversed-Z. For reversed infinite far it equals the near plane.
    public float B { get; private set; }

    public string Description { get; private set; } = "uncalibrated";

    // x = A, y = B, z = 1 when valid.
    public Vector4 ShaderParams => new(A, B, IsValid ? 1f : 0f, 0f);

    public void Invalidate()
    {
        IsValid = false;
        Description = "uncalibrated";
        lastAttemptFrame = FrameThrottler.Never;
        failStreak = 0;
    }

    public unsafe bool Update(RenderDevice device, in Matrix4x4 viewProj, Vector2 displaySize, in GameRenderSources.CameraData cam, long frameId)
    {
        // Stale constants are worse than no depth test.
        if (IsValid && (MathF.Abs(cam.NearPlane - calibratedNear) > 1e-4f
                        || cam.StandardZ != calibratedStandardZ
                        || cam.FiniteFarPlane != calibratedFiniteFar))
        {
            IsValid = false;
            Description = "recalibrating (camera changed)";
        }

        if (IsValid)
            return true;

        var interval = failStreak >= FailuresBeforeBackoff ? BackoffIntervalFrames : RetryIntervalFrames;
        if (!FrameThrottler.TryPass(frameId, ref lastAttemptFrame, interval))
            return false;

        if (!GameRenderSources.TryGetDepthTexture(out var info))
            return false;

        var screens = new List<Vector2>(GridN * GridN);
        var clipWs = new List<float>(GridN * GridN);
        for (var gy = 0; gy < GridN; gy++)
        {
            for (var gx = 0; gx < GridN; gx++)
            {
                var screen = new Vector2(
                    displaySize.X * (0.15f + 0.7f * gx / (GridN - 1)),
                    displaySize.Y * (0.15f + 0.7f * gy / (GridN - 1)));
                if (!NoireService.GameGui.ScreenToWorld(screen, out var world))
                    continue;

                var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
                if (clip.W <= 0.01f)
                    continue;

                screens.Add(screen);
                clipWs.Add(clip.W);
            }
        }

        if (screens.Count < MinInliers)
            return Fail($"only {screens.Count} raycast hits");

        var measured = DepthReadback.TryReadAtPoints(device, in info, screens, displaySize, out _);
        if (measured == null)
            return Fail("depth readback failed");

        var xs = new List<float>(screens.Count);
        var ys = new List<float>(screens.Count);
        float wMin = float.MaxValue, wMax = 0f;
        for (var i = 0; i < screens.Count; i++)
        {
            if (float.IsNaN(measured[i]) || measured[i] < 0f || measured[i] > 1f)
                continue;

            xs.Add(1f / clipWs[i]);
            ys.Add(measured[i]);
            wMin = MathF.Min(wMin, clipWs[i]);
            wMax = MathF.Max(wMax, clipWs[i]);
        }

        if (xs.Count < MinInliers)
            return Fail($"only {xs.Count} readable depth texels");
        if (wMax / wMin < MinSpread)
            return Fail($"insufficient depth spread ({wMin:F1}-{wMax:F1})");

        if (!TrySolve(xs, ys, out var a, out var b, out var medianResidual, out var inliers)
            || inliers < MinInliers
            || medianResidual > MaxMedianResidual
            || MathF.Abs(b) < 1e-7f)
        {
            return Fail($"fit rejected (inliers {inliers}, med resid {medianResidual:E2})");
        }

        A = a;
        B = b;
        IsValid = true;
        failStreak = 0;
        troubleLogged = false;
        calibratedNear = cam.NearPlane;
        calibratedStandardZ = cam.StandardZ;
        calibratedFiniteFar = cam.FiniteFarPlane;
        Description = $"z={a:E2}+{b:F5}/w ({(b > 0 ? "reversed" : "standard")}-Z, {inliers} pts, resid {medianResidual:E1})";
        NoireLogger.LogInfo($"Draw3D depth calibrated: {Description} - camera NearPlane says {cam.NearPlane:F4}.", "Draw3D");
        return true;
    }

    // FFXIV is reversed-Z infinite far (sample = near/clipW). Raycasts often hit another surface than the depth texel.
    internal static Vector4 AnalyticMap(float near, float far, bool standardZ, bool finiteFar)
    {
        near = near > 1e-6f ? near : 0.1f;
        var hasFar = finiteFar && far > near;
        float a, b;
        if (!standardZ) // reversed-Z: near to 1, far to 0
        {
            if (hasFar) { b = near * far / (far - near); a = -near / (far - near); }
            else { a = 0f; b = near; }                    // infinite far: sample = near/w
        }
        else            // standard-Z: near to 0, far to 1
        {
            if (hasFar) { a = far / (far - near); b = -near * far / (far - near); }
            else { a = 1f; b = -near; }                   // infinite far: sample = 1 - near/w
        }

        return new Vector4(a, b, 1f, 0f);
    }

    private bool Fail(string reason)
    {
        failStreak++;
        Description = $"uncalibrated ({reason})";
        if (failStreak == FailuresBeforeBackoff && !troubleLogged)
        {
            troubleLogged = true;
            NoireLogger.LogError($"Draw3D depth calibration keeps failing ({reason}) - depth-dependent features stay off. Run '/noire3d probe' and report the log.", "Draw3D");
        }

        return false;
    }

    // One outlier-rejected refit. Raycasts can hit invisible walls absent from depth.
    internal static bool TrySolve(IReadOnlyList<float> xs, IReadOnlyList<float> ys, out float a, out float b, out float medianResidual, out int inliers)
    {
        a = b = 0f;
        medianResidual = float.MaxValue;
        inliers = 0;

        if (xs.Count != ys.Count || xs.Count < 2)
            return false;

        if (!SolveOnce(xs, ys, null, out a, out b))
            return false;

        var residuals = new float[xs.Count];
        for (var i = 0; i < xs.Count; i++)
            residuals[i] = MathF.Abs(ys[i] - (a + b * xs[i]));

        var sorted = (float[])residuals.Clone();
        Array.Sort(sorted);
        var med = sorted[sorted.Length / 2];
        var gate = MathF.Max(3f * med, 2e-4f);

        var keep = new bool[xs.Count];
        var kept = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            keep[i] = residuals[i] <= gate;
            if (keep[i])
                kept++;
        }

        if (kept < 2)
            return false;

        if (kept < xs.Count && !SolveOnce(xs, ys, keep, out a, out b))
            return false;

        var final = new List<float>(kept);
        for (var i = 0; i < xs.Count; i++)
        {
            if (keep[i])
                final.Add(MathF.Abs(ys[i] - (a + b * xs[i])));
        }

        final.Sort();
        medianResidual = final[final.Count / 2];
        inliers = kept;
        return true;
    }

    private static bool SolveOnce(IReadOnlyList<float> xs, IReadOnlyList<float> ys, bool[]? mask, out float a, out float b)
    {
        a = b = 0f;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        var n = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            if (mask != null && !mask[i])
                continue;

            sx += xs[i];
            sy += ys[i];
            sxx += (double)xs[i] * xs[i];
            sxy += (double)xs[i] * ys[i];
            n++;
        }

        var det = n * sxx - sx * sx;
        if (n < 2 || Math.Abs(det) < 1e-18)
            return false;

        b = (float)((n * sxy - sx * sy) / det);
        a = (float)((sy * sxx - sx * sxy) / det);
        return true;
    }
}
