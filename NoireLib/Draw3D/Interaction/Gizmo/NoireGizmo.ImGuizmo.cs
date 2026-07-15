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
/// <see cref="BuildImGuizmoProjection"/>). Same public API as the native backend, so the consumer flips
/// <see cref="GizmoOptions.Backend"/> without touching call sites. It is <b>self-driven</b>: ImGuizmo reads ImGui IO
/// directly, so it is not a ray-hover target. It runs in <see cref="NoireInteract"/>'s self-driven pre-pass and hosts
/// itself in a fullscreen, <b>always-<c>NoInputs</c></b> window. When a handle is hovered/dragged it reports it owns the
/// mouse (making the frame a hard pass for scene picking) and blocks the game camera with
/// <c>SetNextFrameWantCaptureMouse</c> rather than a window: ImGuizmo gates every handle's hover on "is any other window
/// hovered", so a second window would flicker the highlight, while a windowless capture flag does not. It follows the
/// gizmo's Local/World space and applies translate/rotate/scale snapping per operation. Scale maps to ImGuizmo's
/// universal-scale operation (Scaleu), whose bits are distinct from the plain-scale op that forces the whole gizmo
/// local, so a single integrated call keeps translate/rotate in the chosen space while scale stays object-local. All
/// snapping runs through ImGuizmo's own snap slot, including translation: ImGuizmo snaps the movement from the
/// mouse-down origin, and in Local mode it snaps in the object's own frame, so a rotated object's translation lands
/// cleanly on its local axes with no offset. Because ImGuizmo's result is stored verbatim and fed back next frame, the
/// handles and the object are always the same matrix and cannot drift apart. This file is the only part of the gizmo
/// that touches ImGui/ImGuizmo, kept out of the renderer core per Law 11.
/// </summary>
public sealed partial class NoireGizmo
{
    /// <summary>Which operation a drag is manipulating, so the matching snap increment can be fed to ImGuizmo's single snap slot.</summary>
    private enum SnapKind { None, Translate, Rotate, Scale }

    private static int nextImguizmoId;
    private static long imguizmoFrameStamp = -1;
    private static int imguizmoApiState; // 0 = untried, 1 = ready, 2 = unavailable
    private static bool imguizmoDrewOnce;
    private static bool imguizmoNativeFallbackLogged; // once-only diagnostic when ImGuizmo was requested but native drew instead

    private readonly int imguizmoId = Interlocked.Increment(ref nextImguizmoId);
    private readonly float[] imguizmoSnap = new float[3];
    private bool imguizmoUsing;
    private SnapKind imguizmoSnapKind;
    private SnapKind imguizmoHoverKind; // which op's handle is under the cursor (from the last frame), so a drag snaps from its first frame

    /// <summary>
    /// Re-arms the once-only diagnostics so a fresh <c>[Gizmo]</c> line lands in the log the next time the backend
    /// draws or falls back; the flags are process-static and would otherwise stay silent after the first use. Logs the
    /// current API state (which persists a real capability check). Called when the reference scene spawns.
    /// </summary>
    internal static void ResetImGuizmoDiagnostics()
    {
        imguizmoDrewOnce = false;
        imguizmoNativeFallbackLogged = false;
        NoireLogger.LogInfo($"[Gizmo] diagnostics re-armed. imguizmoApiState={imguizmoApiState} (0=untried, 1=ready, 2=unavailable).", "Draw3D");
    }

    /// <summary>
    /// Binds the ImGuizmo native function table once. Dalamud initialises the ImGui binding but not ImGuizmo's, so
    /// without this every ImGuizmo call hits a null table and draws nothing. This binds against the same native module
    /// ImGui uses (ImGuizmo lives in it), then points ImGuizmo at Dalamud's live ImGui context. Attempted once; failure
    /// disables the backend cleanly (no per-frame retry, no crash spam).
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
            NoireLogger.LogError(ex, "ImGuizmo backend unavailable (InitApi failed); the ImGuizmo gizmo backend is disabled. Use GizmoBackend.Native.", "Draw3D");
            return false;
        }
    }

    /// <summary>
    /// Runs the ImGuizmo manipulation for the frame inside a fullscreen passthrough host window, applies the edit, and
    /// returns whether it owns the mouse right now (a handle hovered or being dragged), so <see cref="NoireInteract"/>
    /// can make the frame a hard pass for scene picking. The game camera is blocked here via
    /// <c>SetNextFrameWantCaptureMouse</c> (no window). Driven from the self-driven pre-pass.
    /// </summary>
    private bool DrawImGuizmo(in FrameContext frame, in Matrix4x4 world)
    {
        if (frame.UsedFallbackCamera)
            return false; // the wholesale view-projection fallback exposes no separate view/proj to feed ImGuizmo

        if (!EnsureImGuizmoApi())
            return false; // native function table not bound; backend disabled (logged once)

        // ImGuizmo must run inside a live ImGui window and draw to that window's draw list (SetDrawlist with no
        // argument). Driving it from an explicit fore/background draw list leaves ImGuizmo's current window null, so it
        // hit-tests but renders nothing.
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        // The host window is always input-transparent (NoInputs), and there must be no other ImGui window hovered while
        // the gizmo is used. ImGuizmo gates every handle's hover on its internal IsHoveringWindow(): its own window
        // hovered gives yes; any other window hovered gives no; no window hovered with the mouse over its rect gives yes
        // (the NoInputs case). A separate capture window would trip the "another window" rule and flicker the highlight,
        // so the gizmo hosts in a NoInputs window and blocks the game camera with no window at all.
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
            ImGuizmo.SetDrawlist();   // the host window's own draw list, drawn with Dalamud's windows, above the composite
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);

            // Always enabled. The gizmo is a pure overlay on top of everything and must never be greyed by what is under
            // the cursor; ownership below is driven only by ImGuizmo's own geometry (IsOver/IsUsing).
            ImGuizmo.Enable(true);

            // Feed the game's view (w term forced to 1, since FFXIV leaves M44 unset) with a rebuilt projection. The
            // game's own projection is reversed-Z and infinite-far, which collapses ImGuizmo's cursor-ray unprojection;
            // BuildImGuizmoProjection swaps in a finite-far, non-reversed Z sharing the game's exact x/y/w, so the gizmo
            // still overlays the object pixel-for-pixel but the ray math is stable.
            var view = frame.View;
            view.M44 = 1f;
            var proj = BuildImGuizmoProjection(in frame);
            var mode = Options.Space == GizmoSpace.Local ? ImGuizmoMode.Local : ImGuizmoMode.World;

            // ImGuizmo is fed the target's own transform every frame and its result is stored back verbatim, so what it
            // draws and what the object holds are always the same matrix, snapped or not: the handles can never drift
            // away from the object.
            var matrix = world;

            // One integrated call for every space. Scale maps to ImGuizmo's universal-scale operation (Scaleu), whose
            // bits are distinct from the plain-scale op that forces the whole gizmo local; so translate/rotate honour the
            // chosen space while scale stays object-local, with no split and no overlapping handles. All three snaps run
            // through ImGuizmo's own slot (translate included): ImGuizmo snaps the movement measured from the mouse-down
            // origin, and in Local mode it does so in the object's own frame, so a rotated object's translation snaps
            // cleanly along its local axes with no offset. Its one snap slot takes whichever op the drag drives, so the
            // matching increment is selected per operation below.
            ImGuizmo.SetID(imguizmoId);
            var op = MapOperation(Op);

            // Choose this frame's snap increment BEFORE manipulating. During a drag the op is latched. Otherwise use the
            // handle the cursor is over (ImGuizmo reports it from the previous frame), so a drag snaps from its very
            // first frame: no unsnapped nudge is ever applied, which is what left a scale/rotation drag with a small
            // permanent offset when the op was only detected a frame late. A single-op gizmo knows its op up front.
            var snapKind = imguizmoUsing ? imguizmoSnapKind
                : imguizmoHoverKind != SnapKind.None ? imguizmoHoverKind
                : SingleOpKind();

            var changed = TryBuildSnap(snapKind, out var snap)
                ? ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix, ref snap[0])
                : ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix);

            var isOver = ImGuizmo.IsOver();
            var isUsing = ImGuizmo.IsUsing();

            if (isUsing && !imguizmoUsing)
            {
                imguizmoUsing = true;
                // Latch the driven op: the pre-drag hover normally has it (snapped from frame one); fall back to
                // detecting the change only if the press somehow landed with no hover frame before it.
                imguizmoSnapKind = snapKind != SnapKind.None ? snapKind : DetectChangedOp(in world, in matrix);
                if (groupNodes != null)
                    CaptureGroupPress(in world); // world is the group pivot at the moment the drag began
                RaiseEditStart();
            }
            else if (isUsing && imguizmoSnapKind == SnapKind.None && changed)
            {
                imguizmoSnapKind = DetectChangedOp(in world, in matrix); // fallback: op was unknown at press
            }

            // Remember the hovered handle for next frame's pre-snap (only while not mid-drag).
            if (!isUsing)
                imguizmoHoverKind = HoveredOpKind();

            // A first frame that still ran unsnapped (the rare press with no prior hover) is dropped rather than applied,
            // so a sub-increment nudge never shows and snaps back. With pre-snap this is normally already false.
            var dropUnsnappedFirstFrame = snapKind == SnapKind.None && OpHasSnap(imguizmoSnapKind);

            ownsMouse = isOver || isUsing;

            // Block the game camera the way ImGuizmo itself does: a next-frame capture-mouse flag, not a window (which
            // would be "another window hovered" to ImGuizmo and break its hover).
            if (ownsMouse)
                ImGui.SetNextFrameWantCaptureMouse(true);

            if (NoireInteract.DebugLog && !imguizmoDrewOnce)
            {
                imguizmoDrewOnce = true;
                var onScreen = frame.TryWorldToScreen(world.Translation, out var scr);
                NoireLogger.LogInfo(
                    $"[Gizmo] ImGuizmo drawing: over={isOver} using={isUsing} changed={changed} space={Options.Space} " +
                    $"objWorld=({world.Translation.X:F1},{world.Translation.Y:F1},{world.Translation.Z:F1}) " +
                    $"screen={(onScreen ? $"({scr.X:F0},{scr.Y:F0})" : "OFF-SCREEN")}.",
                    "Draw3D");
            }

            // Apply only a finite, non-degenerate result. A bad manipulate (for example a NaN from a projection ImGuizmo
            // cannot invert) would blank the object, so a garbage matrix is dropped rather than written to the target.
            // The single unsnapped frame at a snapping drag's start is dropped (see dropUnsnappedFirstFrame) so it never
            // shows before the snap takes over.
            if (changed && !dropUnsnappedFirstFrame && IsUsableTransform(in matrix))
            {
                SetWorld(in matrix);
                RaiseEdit();
            }

            if (!isUsing && imguizmoUsing)
            {
                imguizmoUsing = false;
                imguizmoSnapKind = SnapKind.None;
                groupPressWorlds = Array.Empty<Matrix4x4>();
                RaiseEditEnd();
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();

        return ownsMouse;
    }

    /// <summary>The snap kind for a single-operation gizmo (so its snap is applied without waiting to detect the active op), else None.</summary>
    private SnapKind SingleOpKind() => Op switch
    {
        GizmoOp.Translate => SnapKind.Translate,
        GizmoOp.Rotate => SnapKind.Rotate,
        GizmoOp.Scale => SnapKind.Scale,
        _ => SnapKind.None,
    };

    /// <summary>
    /// The op whose handle is under the cursor right now (from ImGuizmo's last frame), so a drag that starts next frame
    /// already knows which snap increment to feed and snaps from its first frame. None when the cursor is over no handle.
    /// </summary>
    private SnapKind HoveredOpKind()
    {
        if ((Op & GizmoOp.Translate) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Translate))
            return SnapKind.Translate;
        if ((Op & GizmoOp.Rotate) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Rotate))
            return SnapKind.Rotate;
        if ((Op & GizmoOp.Scale) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Scaleu))
            return SnapKind.Scale;
        return SnapKind.None;
    }

    /// <summary>Detects which operation changed between the pre- and post-manipulate matrices, so the right snap is fed next frame.</summary>
    private static SnapKind DetectChangedOp(in Matrix4x4 before, in Matrix4x4 after)
    {
        if (!Matrix4x4.Decompose(before, out var s0, out var r0, out var t0) ||
            !Matrix4x4.Decompose(after, out var s1, out var r1, out var t1))
            return SnapKind.None;

        var delta = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(r0), r1));
        var angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(delta.W), 0f, 1f));
        if (angle > 1e-3f)
            return SnapKind.Rotate;
        if (Vector3.Distance(s0, s1) > 1e-4f)
            return SnapKind.Scale;
        if (Vector3.Distance(t0, t1) > 1e-5f)
            return SnapKind.Translate;
        return SnapKind.None;
    }

    /// <summary>
    /// Builds ImGuizmo's snap array for the operation the drag is driving. Translation snaps per axis (World mode snaps
    /// world axes, Local mode snaps the object's own axes, both handled inside ImGuizmo); rotation and scale snap by a
    /// single increment. Returns false when the driven operation has no snap set, so the manipulation runs unsnapped.
    /// </summary>
    private bool TryBuildSnap(SnapKind kind, out float[] snap)
    {
        snap = imguizmoSnap;
        if (!OpHasSnap(kind))
            return false;

        switch (kind)
        {
            case SnapKind.Translate:
                snap[0] = Options.Snap.X;
                snap[1] = Options.Snap.Y;
                snap[2] = Options.Snap.Z;
                return true;
            case SnapKind.Rotate:
                snap[0] = snap[1] = snap[2] = Options.RotateSnapDeg;
                return true;
            case SnapKind.Scale:
                snap[0] = snap[1] = snap[2] = Options.ScaleSnap;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether the given operation has a snap increment configured.</summary>
    private bool OpHasSnap(SnapKind kind) => kind switch
    {
        SnapKind.Translate => Options.Snap != Vector3.Zero,
        SnapKind.Rotate => Options.RotateSnapDeg > 0f,
        SnapKind.Scale => Options.ScaleSnap > 0f,
        _ => false,
    };

    /// <summary>
    /// A finite-far, non-reversed perspective projection for ImGuizmo, built from the game's projection but with a
    /// well-conditioned Z column. ImGuizmo unprojects the cursor ray by inverting <c>view * proj</c>; the game's
    /// projection is reversed-Z and infinite-far (far plane at w to 0), which collapses that inverse and breaks every
    /// ray-based handle. Only clip.z (M13/M23/M33/M43) is rebuilt; clip.x/clip.y/clip.w are copied verbatim, so the
    /// gizmo projects to the same screen pixels as the object it edits. The w-column sign (M34) carries the game's
    /// handedness, so this works whether the game view is left- or right-handed.
    /// </summary>
    private static Matrix4x4 BuildImGuizmoProjection(in FrameContext frame)
    {
        var proj = frame.Proj;
        var near = frame.NearPlane > 1e-4f ? frame.NearPlane : 0.1f;
        var far = MathF.Max(near * 10000f, 10000f); // the game is infinite-far; any large finite far conditions the ray
        var wSign = proj.M34;                        // coefficient of view-Z into clip.w, carries handedness
        proj.M13 = 0f;
        proj.M23 = 0f;
        proj.M33 = wSign * far / (far - near);
        proj.M43 = -near * far / (far - near);
        return proj;
    }

    /// <summary>True when every element is finite and the matrix does not collapse the target to nothing.</summary>
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
            ? true // non-decomposable but finite (for example sheared); let SetWorld fall back to translation only
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
            r |= ImGuizmoOperation.Scaleu; // universal-scale bits: object-local scale that does not force the gizmo local
        return r;
    }
}
