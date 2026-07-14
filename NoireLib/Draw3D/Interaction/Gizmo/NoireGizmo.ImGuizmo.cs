using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using HexaGen.Runtime;
using System;
using System.Numerics;
using System.Threading;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>
/// The <see cref="GizmoBackend.ImGuizmo"/> backend of <see cref="NoireGizmo"/>: the classic 2D-projected handles drawn
/// by <c>Dalamud.Bindings.ImGuizmo</c>, fed the render camera's view/projection from the <see cref="FrameContext"/>.
/// Same public API as the native backend — the consumer flips <see cref="GizmoOptions.Backend"/> without touching call
/// sites. It handles its own input (ImGuizmo reads ImGui IO directly), so it is not a ray-hover target; it still shares
/// the one mouse-capture authority via <see cref="NoireInteract.RequestCapture"/> so the game camera is blocked while a
/// handle is grabbed. Unlike the native backend it is not depth-tested (no occlusion) and has no Screen space or
/// per-axis universal snapping — those are the native backend's strengths. This file is the only part of the gizmo that
/// touches ImGui/ImGuizmo, kept out of the renderer core per Law 11.
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

    /// <summary>Runs the ImGuizmo manipulation for the frame: draws 2D handles, applies the edit, and claims the mouse while active.</summary>
    private void DrawImGuizmo(in FrameContext frame, in Matrix4x4 world)
    {
        if (frame.UsedFallbackCamera)
            return; // the wholesale-VP fallback exposes no separate view/proj — nothing to feed ImGuizmo

        if (!EnsureImGuizmoApi())
            return; // native function table not bound — backend disabled (logged once)

        var viewport = ImGui.GetMainViewport();

        // ImGuizmo.BeginFrame + re-point at the live ImGui context once per ImGui frame, however many gizmos exist.
        var frameCount = ImGui.GetFrameCount();
        if (imguizmoFrameStamp != frameCount)
        {
            imguizmoFrameStamp = frameCount;
            ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
            ImGuizmo.BeginFrame();
        }

        var foreign = NoireInteract.ForeignUiHasMouse;
        ImGuizmo.Enable(!foreign);   // still drawn (greyed) when another window owns the mouse, but not manipulable
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetID(imguizmoId);
        ImGuizmo.SetDrawlist(ImGui.GetBackgroundDrawList()); // under plugin windows, matching the in-world layer's z-order
        ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);

        // Row-vector matrices reinterpreted as column-major by the binding are exactly the column-vector matrices
        // ImGuizmo expects, so the game camera passes straight through. The projection's z column is rebuilt to a
        // plain finite-far, non-reversed mapping (see BuildImGuizmoProjection) — ImGuizmo's ray unprojection needs it,
        // and z never changes where a point lands on screen.
        var view = frame.View;
        var proj = BuildImGuizmoProjection(in frame);
        var op = MapOperation(Op);
        var mode = Options.Space == GizmoSpace.Local ? ImGuizmoMode.Local : ImGuizmoMode.World;
        var matrix = world;

        bool changed;
        if (TryBuildSnap(op, out var snap))
            changed = ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix, ref snap[0]);
        else
            changed = ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix);

        if (NoireInteract.DebugLog && !imguizmoDrewOnce)
        {
            imguizmoDrewOnce = true;
            NoireLogger.LogInfo(
                $"[Gizmo] ImGuizmo drawing: over={ImGuizmo.IsOver()} using={ImGuizmo.IsUsing()} changed={changed} " +
                $"rect={viewport.Size.X:F0}x{viewport.Size.Y:F0} fallbackCam={frame.UsedFallbackCamera}",
                "Draw3D");
        }

        var isUsing = ImGuizmo.IsUsing();
        if (isUsing && !imguizmoUsing)
        {
            imguizmoUsing = true;
            RaiseEditStart();
        }

        if (changed)
        {
            SetWorld(in matrix);
            RaiseEdit();
        }

        if (!isUsing && imguizmoUsing)
        {
            imguizmoUsing = false;
            RaiseEditEnd();
        }

        // Share the one capture authority: block the game camera while hovering or dragging a handle.
        if (!foreign && (isUsing || ImGuizmo.IsOver()))
            NoireInteract.RequestCapture();
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

    /// <summary>
    /// The game camera's projection with its z column replaced by a standard finite-far, non-reversed mapping.
    /// The x/y/w columns are kept exactly so a handle projects onto its object, while the well-behaved z column keeps
    /// ImGuizmo's mouse-ray unprojection from degenerating on our reversed-Z infinite-far buffer.
    /// </summary>
    private static Matrix4x4 BuildImGuizmoProjection(in FrameContext frame)
    {
        var p = frame.Proj;
        var n = frame.NearPlane > 1e-4f ? frame.NearPlane : 0.1f;
        const float f = 10000f;
        p.M13 = 0f;
        p.M23 = 0f;
        p.M33 = f / (f - n);
        p.M43 = -(n * f) / (f - n);
        return p;
    }
}
