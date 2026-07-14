using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using HexaGen.Runtime;
using System;
using System.Numerics;
using System.Threading;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>
/// The <see cref="GizmoBackend.ImGuizmo"/> backend of <see cref="NoireGizmo"/>: the classic 2D-projected handles drawn
/// by <c>Dalamud.Bindings.ImGuizmo</c>, fed the render camera's view plus a Z-rebuilt projection (see
/// <see cref="BuildImGuizmoProjection"/>). Same public API as the native backend — the consumer flips
/// <see cref="GizmoOptions.Backend"/> without touching call sites. It is <b>self-driven</b>: ImGuizmo reads ImGui IO
/// directly, so it is not a ray-hover target — it runs in <see cref="NoireInteract"/>'s self-driven pre-pass and hosts
/// itself in a fullscreen, <b>always-<c>NoInputs</c></b> window (ImGuizmo hit-tests + manipulates through a passthrough
/// window via the global IO — Brio's model). When a handle is hovered/dragged it reports it owns the mouse, which makes
/// the frame a hard pass for scene picking; it blocks the game camera itself with <c>SetNextFrameWantCaptureMouse</c>.
/// Crucially there is NO extra ImGui window while it is used: ImGuizmo gates hover on "is any other window hovered", so a
/// capture window — or toggling the host's own input flags — flickers it on and off; a windowless capture flag does not.
/// Unlike the native backend it is not depth-tested (no occlusion) and has no Screen space or per-axis universal snapping
/// — those are the native backend's strengths. This file is the only part of the gizmo that touches ImGui/ImGuizmo, kept
/// out of the renderer core per Law 11.
/// </summary>
public sealed partial class NoireGizmo
{
    private static int nextImguizmoId;
    private static long imguizmoFrameStamp = -1;
    private static int imguizmoApiState; // 0 = untried, 1 = ready, 2 = unavailable
    private static bool imguizmoDrewOnce;
    private static bool imguizmoNativeFallbackLogged; // once-only diag when ImGuizmo was requested but native drew instead

    private readonly int imguizmoId = Interlocked.Increment(ref nextImguizmoId);
    private readonly float[] imguizmoSnap = new float[3];
    private bool imguizmoUsing;

    /// <summary>
    /// Binds the ImGuizmo native function table once. Dalamud initialises the ImGui binding but not ImGuizmo's, so
    /// without this every ImGuizmo call hits a null table and draws nothing (the "gizmo doesn't show" symptom). We bind
    /// against the same native module ImGui uses (ImGuizmo lives in it), then point ImGuizmo at Dalamud's live ImGui
    /// context. Attempted once; failure disables the backend cleanly (no per-frame retry, no crash spam).
    /// </summary>
    /// <summary>
    /// Re-arms the once-only ImGuizmo draw/fallback diagnostics so a fresh <c>[Gizmo]</c> line lands in the log the next
    /// time the backend draws — the flags are process-static and would otherwise stay silent after the first session.
    /// Logs the current API state too (which persists a real capability check). Called when the smoke scene spawns.
    /// </summary>
    internal static void ResetImGuizmoDiagnostics()
    {
        imguizmoDrewOnce = false;
        imguizmoNativeFallbackLogged = false;
        NoireLogger.LogInfo($"[Gizmo] diagnostics re-armed. imguizmoApiState={imguizmoApiState} (0=untried, 1=ready, 2=unavailable).", "Draw3D");
    }

    /// <returns>True when the ImGuizmo API is bound and usable.</returns>
    private static bool EnsureImGuizmoApi()
    {
        var state = Volatile.Read(ref imguizmoApiState);
        if (state != 0)
            return state == 1;

        try
        {
            var context = LibraryLoader.LoadLibraryEx(ImGuizmo.GetLibraryName, LibraryLoader.GetExtension);
            ImGuizmo.InitApi(context);
            ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
            Volatile.Write(ref imguizmoApiState, 1);
            NoireLogger.LogInfo("ImGuizmo backend initialised.", "Draw3D");
            return true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref imguizmoApiState, 2);
            NoireLogger.LogError(ex, "ImGuizmo backend unavailable (InitApi failed); the ImGuizmo gizmo backend is disabled — use GizmoBackend.Native.", "Draw3D");
            return false;
        }
    }

    /// <summary>
    /// Runs the ImGuizmo manipulation for the frame inside a fullscreen passthrough host window, applies the edit, and
    /// returns whether it owns the mouse right now (a handle hovered or being dragged) — purely from ImGuizmo's own
    /// <c>IsOver</c>/<c>IsUsing</c>, so <see cref="NoireInteract"/> can make the frame a hard pass for scene picking. The
    /// game camera is blocked here via <c>SetNextFrameWantCaptureMouse</c> (no window). Driven from the self-driven pre-pass.
    /// </summary>
    private bool DrawImGuizmo(in FrameContext frame, in Matrix4x4 world)
    {
        if (frame.UsedFallbackCamera)
            return false; // the wholesale-VP fallback exposes no separate view/proj — nothing to feed ImGuizmo

        if (!EnsureImGuizmoApi())
            return false; // native function table not bound — backend disabled (logged once)

        // ImGuizmo must run INSIDE a live ImGui window and draw to THAT window's draw list (SetDrawlist() with no
        // argument) — the pattern every working Dalamud gizmo uses (e.g. Brio's posing overlay). Driving it straight
        // from the UiBuilder.Draw callback with an explicit fore/background draw list leaves ImGuizmo's current window
        // null, so it hit-tests fine yet renders nothing (the "takes input, never shows" symptom).
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        // The host window is ALWAYS input-transparent (NoInputs), and there must be NO OTHER ImGui window hovered while
        // the gizmo is used. ImGuizmo gates every handle's hover on its internal IsHoveringWindow(): (1) its own window
        // hovered → yes; (2) ANY OTHER window hovered → NO; (3) no window hovered + mouse over its rect → yes (the
        // NoInputs case). A separate capture window trips rule (2) → hover off → cycle with rule (3) = the fast hover
        // flicker. So we host in a NoInputs window (rule 3 keeps hover stable) and block the game camera WITHOUT any
        // window — ImGuizmo already calls SetNextFrameWantCaptureMouse when hovering/using, and we reinforce it below.
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var ownsMouse = false;
        if (ImGui.Begin("##NoireImGuizmoHost", flags))
        {
            // BeginFrame + re-point at the live ImGui context once per ImGui frame, however many gizmos exist.
            var frameCount = ImGui.GetFrameCount();
            if (imguizmoFrameStamp != frameCount)
            {
                imguizmoFrameStamp = frameCount;
                ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
                ImGuizmo.BeginFrame();
            }

            ImGuizmo.SetID(imguizmoId);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetDrawlist();   // the host window's own draw list — drawn with Dalamud's windows, above the composite
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);
            // ALWAYS enabled. The gizmo is a pure overlay on top of everything; it must never be greyed by game-UI
            // detection. IsPointOverVisibleAddon walks every loaded addon and flips true/false as HUD elements near the
            // cursor animate or resize frame-to-frame, so gating Enable() on it toggled the whole gizmo on and off every
            // frame — that WAS the "greys by cursor position + flickers indefinitely" bug. Ownership below is driven only
            // by ImGuizmo's own geometry (IsOver/IsUsing), which has no such feedback.
            ImGuizmo.Enable(true);

            // Feed the game's view (w term forced to 1 — FFXIV leaves M44 unset) with a REBUILT projection. The game's
            // own projection is reversed-Z AND infinite-far, so ImGuizmo (which inverts view*proj to unproject the
            // cursor ray) gets a degenerate ray — translate reads 0, rotate makes a NaN transform, and the handles
            // shrink to a dot. BuildImGuizmoProjection swaps in a finite-far, non-reversed Z that shares the game's
            // exact x/y/w, so the gizmo still overlays the object pixel-for-pixel but the ray math is stable.
            var view = frame.View;
            view.M44 = 1f;
            var proj = BuildImGuizmoProjection(in frame);
            var op = MapOperation(Op);
            var mode = Options.Space == GizmoSpace.Local ? ImGuizmoMode.Local : ImGuizmoMode.World;
            var matrix = world;

            bool changed;
            if (TryBuildSnap(op, out var snap))
                changed = ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix, ref snap[0]);
            else
                changed = ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix);

            var isOver = ImGuizmo.IsOver();
            var isUsing = ImGuizmo.IsUsing();
            ownsMouse = isOver || isUsing; // pure ImGuizmo geometry — no UI-detection term, so nothing can make it oscillate

            // Block the game camera the way ImGuizmo itself does — a next-frame capture-mouse flag, NOT a window. A
            // window would be "another window hovered" to ImGuizmo (rule 2 above) and kill its hover → flicker. This
            // creates nothing to hover, so ImGuizmo's rule (3) keeps a rock-steady hover while the camera is still held.
            if (ownsMouse)
                ImGui.SetNextFrameWantCaptureMouse(true);

            if (NoireInteract.DebugLog && !imguizmoDrewOnce)
            {
                imguizmoDrewOnce = true;
                var onScreen = frame.TryWorldToScreen(world.Translation, out var scr);
                NoireLogger.LogInfo(
                    $"[Gizmo] ImGuizmo drawing (host window): over={isOver} using={isUsing} changed={changed} " +
                    $"objWorld=({world.Translation.X:F1},{world.Translation.Y:F1},{world.Translation.Z:F1}) " +
                    $"screen={(onScreen ? $"({scr.X:F0},{scr.Y:F0})" : "OFF-SCREEN")} " +
                    $"rect=({viewport.Pos.X:F0},{viewport.Pos.Y:F0} {viewport.Size.X:F0}x{viewport.Size.Y:F0}).",
                    "Draw3D");
            }

            if (isUsing && !imguizmoUsing)
            {
                imguizmoUsing = true;
                RaiseEditStart();
            }

            // Apply only a finite, non-degenerate result. A bad manipulate (e.g. a NaN from a projection ImGuizmo can't
            // invert) would otherwise blank the object — the "click it and it poofs" symptom — so a garbage matrix is
            // dropped rather than written to the target.
            if (changed && IsUsableTransform(in matrix))
            {
                SetWorld(in matrix);
                RaiseEdit();
            }

            if (!isUsing && imguizmoUsing)
            {
                imguizmoUsing = false;
                RaiseEditEnd();
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();

        // ownsMouse (a handle hovered/dragged now) → NoireInteract makes the frame a hard pass for scene picking. The
        // game camera was already blocked above via SetNextFrameWantCaptureMouse — no window, so no hover-gate flicker.
        return ownsMouse;
    }

    /// <summary>
    /// A finite-far, non-reversed perspective projection for ImGuizmo, built from the game's projection but with a
    /// well-conditioned Z column. ImGuizmo unprojects the cursor ray by inverting <c>view · proj</c>; the game's
    /// projection is reversed-Z <b>and</b> infinite-far (far plane at w→0), which collapses that inverse and breaks
    /// every ray-based handle. Only clip.z (M13/M23/M33/M43) is rebuilt — clip.x/clip.y/clip.w are copied verbatim, so
    /// the gizmo projects to the exact same screen pixels as the object it edits; the w-column sign (M34) carries the
    /// game's handedness, so this works whether the game view is left- or right-handed.
    /// </summary>
    private static Matrix4x4 BuildImGuizmoProjection(in FrameContext frame)
    {
        var proj = frame.Proj;
        var near = frame.NearPlane > 1e-4f ? frame.NearPlane : 0.1f;
        var far = MathF.Max(near * 10000f, 10000f); // the game is infinite-far; any large finite far conditions the ray
        var wSign = proj.M34;                        // coefficient of view-Z into clip.w — carries handedness (±1)
        proj.M13 = 0f;
        proj.M23 = 0f;
        proj.M33 = wSign * far / (far - near);
        proj.M43 = -near * far / (far - near);
        return proj;
    }

    /// <summary>True when every element is finite and the matrix does not collapse the target to nothing (guards the "poof").</summary>
    private static bool IsUsableTransform(in Matrix4x4 m)
    {
        var finite =
            float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
            float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
            float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
            float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
        if (!finite)
            return false;

        // Reject a zeroed basis (would decompose to scale 0 and make the object vanish).
        return !Matrix4x4.Decompose(m, out var scale, out _, out _)
            ? true // non-decomposable but finite (e.g. sheared) — let SetWorld fall back to translation-only
            : scale.LengthSquared() > 1e-12f;
    }

    private static ImGuizmoOperation MapOperation(GizmoOp op)
    {
        ImGuizmoOperation r = 0;
        if ((op & GizmoOp.Translate) != 0)
            r |= ImGuizmoOperation.Translate;
        if ((op & GizmoOp.Rotate) != 0)
            r |= ImGuizmoOperation.Rotate;
        if ((op & GizmoOp.Scale) != 0)
            r |= ImGuizmoOperation.Scale;
        return r;
    }

    /// <summary>Builds ImGuizmo's single snap array, preferring translate → rotate → scale (universal mode uses whichever is set first).</summary>
    private bool TryBuildSnap(ImGuizmoOperation op, out float[] snap)
    {
        snap = imguizmoSnap;
        if ((op & ImGuizmoOperation.Translate) != 0 && Options.Snap != Vector3.Zero)
        {
            snap[0] = Options.Snap.X;
            snap[1] = Options.Snap.Y;
            snap[2] = Options.Snap.Z;
            return true;
        }

        if ((op & ImGuizmoOperation.Rotate) != 0 && Options.RotateSnapDeg > 0f)
        {
            snap[0] = snap[1] = snap[2] = Options.RotateSnapDeg;
            return true;
        }

        if ((op & ImGuizmoOperation.Scale) != 0 && Options.ScaleSnap > 0f)
        {
            snap[0] = snap[1] = snap[2] = Options.ScaleSnap;
            return true;
        }

        return false;
    }
}
