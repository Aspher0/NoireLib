using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using HexaGen.Runtime;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
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
/// handles and the object are always the same matrix and cannot drift apart. The drag readout is <b>NoireGizmo's own</b>
/// (the same overlay the native backend draws): ImGuizmo's built-in info text is a world-space delta that reads wrong in
/// Local space, so it is hidden for the span of each manipulate call - its shared style restored immediately after, so
/// any other ImGuizmo consumer is untouched - and the space-correct readout is drawn in its place. This file is the only
/// part of the gizmo that touches ImGui/ImGuizmo, kept out of the renderer core per Law 11.
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

    private static INativeContext? imguizmoContext; // the loaded cimguizmo module, kept to resolve ImGuizmo_GetStyle (the binding wraps no style accessor)
    private static nint imguizmoStylePtr;            // cached &ImGuizmo::Style, or 0 when unavailable
    private static bool imguizmoStyleResolved;

    // ImGuizmo's Style is 8 leading floats then ImVec4 Colors[COUNT]; TEXT is colour index 13, TEXT_SHADOW 14. These
    // byte offsets land on each colour's alpha (w), so writing 0f there hides ImGuizmo's built-in drag info text.
    private const int ImGuizmoStyleTextAlpha = (8 + 13 * 4 + 3) * sizeof(float);
    private const int ImGuizmoStyleTextShadowAlpha = (8 + 14 * 4 + 3) * sizeof(float);

    private readonly int imguizmoId = Interlocked.Increment(ref nextImguizmoId);
    private readonly float[] imguizmoSnap = new float[3];
    private bool imguizmoUsing;
    private Vector3 imguizmoScaleGuard = Vector3.One; // last accepted scale this drag; rejects a degenerate 1-frame collapse
    private ImGuizmoOperation imguizmoDragOp;         // the single op locked for the current drag (disambiguates overlapping handles)
    private SnapKind imguizmoSnapKind;
    private SnapKind imguizmoHoverKind; // which op's handle is under the cursor (from the last frame), so a drag snaps from its first frame
    private int imguizmoSavedTextBits, imguizmoSavedShadowBits; // ImGuizmo TEXT / TEXT_SHADOW alpha saved across one Manipulate call
    private bool imguizmoTextHidden;

    /// <summary>Calls the native <c>ImGuizmo_GetStyle</c> (unwrapped by the binding); returns <c>&amp;ImGuizmo::Style</c>.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ImGuizmoGetStyleDelegate();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>The PHYSICAL (hardware) left-mouse state, read straight from the OS. ImGui's own <c>io.MouseDown</c> can
    /// desync - Dalamud routes a release to the game while the mouse is captured, so the up is never delivered to ImGui -
    /// which strands an ImGuizmo drag; reading the hardware bit is the only reliable release signal. VK_LBUTTON = 0x01.</summary>
    private static bool PhysicalLeftDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

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
            imguizmoContext = context; // kept so the (unwrapped) ImGuizmo_GetStyle export can be resolved to hide the built-in drag text
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
        // Hardware stuck-drag watchdog (runs before every early-out). A gizmo drag lives only while the button is
        // physically held. If we still think we are dragging with the button PHYSICALLY up, the ImGui mouse-up was lost
        // (Dalamud routed the release to the game while the mouse was captured), so io.MouseDown stays stuck true - then
        // ImGuizmo keeps dragging the object on mouse-move and our capture flag holds the cursor hostage, with no visible
        // handle to release on. Reading the REAL button (not io.MouseDown, the very bit that desynced) and resyncing
        // ImGui to it releases ImGuizmo the same frame. This is why the earlier IsMouseDown-based guard could not work.
        var physicalDown = PhysicalLeftDown();
        if (imguizmoUsing && !physicalDown)
        {
            ImGui.GetIO().MouseDown[0] = false; // resync ImGui to hardware so ImGuizmo's Manipulate stops using this frame
            EndImguizmoDrag();
        }

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

            // ImGuizmo scales *multiplicatively* (it multiplies the fed matrix's scale by a factor), so a component
            // driven to zero can never grow back (0 x factor = 0) and the rate depends on the current size. The native
            // backend is additive (pressScale + baseScale x frac). To match it, ImGuizmo is fed a PROXY whose scale is
            // the non-zero baseScale reference (its position/rotation are the object's real ones, so the handles still
            // overlay it pixel-for-pixel and translate/rotate are untouched); its multiplicative scale result is then
            // converted back to an additive delta on the real object below (RebuildFromScaleProxy). ImGuizmo captures
            // its own drag origin, so feeding a proxy never lets the handles drift from the gesture.
            DecomposeSafe(in world, out var curScale, out var curRot, out var curTrans);
            if (!imguizmoUsing)
            {
                pressScale = curScale;        // freeze the size at press; held for the whole drag (baseScale is the increment reference)
                imguizmoScaleGuard = curScale; // and the collapse guard's reference (last accepted scale)
            }
            var proxyBefore = Matrix4x4.CreateScale(baseScale)
                              * Matrix4x4.CreateFromQuaternion(curRot)
                              * Matrix4x4.CreateTranslation(curTrans);
            var matrix = proxyBefore;

            // One integrated call for every space. Scale maps to ImGuizmo's universal-scale operation (Scaleu), whose
            // bits are distinct from the plain-scale op that forces the whole gizmo local; so translate/rotate honour the
            // chosen space while scale stays object-local, with no split and no overlapping handles. All three snaps run
            // through ImGuizmo's own slot (translate included): ImGuizmo snaps the movement measured from the mouse-down
            // origin, and in Local mode it does so in the object's own frame, so a rotated object's translation snaps
            // cleanly along its local axes with no offset. Its one snap slot takes whichever op the drag drives, so the
            // matching increment is selected per operation below.
            ImGuizmo.SetID(imguizmoId);

            // The op fed to ImGuizmo. While NOT dragging, the full set - so every handle hit-tests and reports hover.
            // On the frame the button goes down over a handle, and for the rest of that drag, ONLY the topmost hovered
            // op (imguizmoHoverKind is Scale-first, matching ImGuizmo's own priority): where a scale ball overlaps the
            // screen rotation ring, this makes ImGuizmo capture a clean single-op origin instead of an ambiguous one
            // that collapsed the object. imguizmoDragOp is set every non-dragging frame, so the drag that begins this
            // frame is already locked to it.
            // A plane-locked decal (Ground/Wall) exposes only its yaw ring: every other rotation is dead (the node
            // re-constrains the box), so feeding ImGuizmo only the yaw operation hides the other rings on this backend too.
            var yawOnly = IsOrientationLockedDecal;
            var rotateFlag = yawOnly ? ConstrainedYawOperation(in curRot) : ImGuizmoOperation.Rotate;

            ImGuizmoOperation op;
            if (imguizmoUsing)
            {
                op = imguizmoDragOp;
            }
            else
            {
                var pressing = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && imguizmoHoverKind != SnapKind.None;
                op = pressing
                    ? (imguizmoHoverKind == SnapKind.Rotate ? rotateFlag : SnapKindToOperation(imguizmoHoverKind))
                    : MapOperationLocked(Op, rotateFlag);
                imguizmoDragOp = op;
            }

            // Choose this frame's snap increment BEFORE manipulating. During a drag the op is latched. Otherwise use the
            // handle the cursor is over (ImGuizmo reports it from the previous frame), so a drag snaps from its very
            // first frame: no unsnapped nudge is ever applied, which is what left a scale/rotation drag with a small
            // permanent offset when the op was only detected a frame late. A single-op gizmo knows its op up front.
            var snapKind = imguizmoUsing ? imguizmoSnapKind
                : imguizmoHoverKind != SnapKind.None ? imguizmoHoverKind
                : SingleOpKind();

            // Hide ImGuizmo's built-in drag text just for this Manipulate call, restored immediately after so the shared
            // native style is left exactly as found for every other plugin using the same ImGuizmo. Its text is a
            // world-space delta that reads wrong in Local space (moving a rotated object's local axis prints the world
            // projection, e.g. 0.353 for a 0.5 move at 45°); NoireGizmo draws its own space-correct readout below.
            var hideBuiltInText = Options.ShowDragFeedback;
            if (hideBuiltInText)
                HideImGuizmoText();

            var changed = TryBuildSnap(snapKind, out var snap)
                ? ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix, ref snap[0])
                : ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix);

            if (hideBuiltInText)
                RestoreImGuizmoText();

            // Convert ImGuizmo's proxy result back to the real object transform: translation/rotation pass through
            // verbatim, scale becomes an ADDITIVE delta on the size at press (pressScale + (resultScale - baseScale)),
            // floored so a component never degenerates - so a zeroed axis grows back at the base rate, exactly like the
            // native backend. Op detection (below) compares proxyBefore vs the proxy result, never the real world (whose
            // scale differs from baseScale), so a translate/rotate is not misread as a scale.
            var realMatrix = RebuildFromScaleProxy(in matrix);

            var isOver = ImGuizmo.IsOver();
            var isUsing = ImGuizmo.IsUsing();

            if (isUsing && !imguizmoUsing && physicalDown) // start only on a real, physically-held press (never a stuck "using")
            {
                imguizmoUsing = true;
                // Latch the driven op: the pre-drag hover normally has it (snapped from frame one); fall back to
                // detecting the change only if the press somehow landed with no hover frame before it.
                imguizmoSnapKind = snapKind != SnapKind.None ? snapKind : DetectChangedOp(in proxyBefore, in matrix);
                CaptureImGuizmoPress(in world); // freeze the press transform/basis for the drag readout (world is the target/group pivot at press)
                if (groupNodes != null)
                    CaptureGroupPress(in world); // world is the group pivot at the moment the drag began
                RaiseEditStart();
            }
            else if (isUsing && imguizmoSnapKind == SnapKind.None && changed)
            {
                imguizmoSnapKind = DetectChangedOp(in proxyBefore, in matrix); // fallback: op was unknown at press
            }

            // Remember the hovered handle for next frame's pre-snap (only while not mid-drag).
            if (!isUsing)
                imguizmoHoverKind = HoveredOpKind();

            // A first frame that still ran unsnapped (the rare press with no prior hover) is dropped rather than applied,
            // so a sub-increment nudge never shows and snaps back. With pre-snap this is normally already false.
            var dropUnsnappedFirstFrame = snapKind == SnapKind.None && OpHasSnap(imguizmoSnapKind);

            // Own the mouse on hover (so a click lands on the gizmo, not the game) or while genuinely dragging - but a
            // stuck "using" with the button physically up must never keep it, or the cursor stays hostage.
            ownsMouse = isOver || (isUsing && physicalDown);

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
            // Apply a manipulation only while the button is PHYSICALLY held - so a stuck "using" (io.MouseDown desynced)
            // can never drag the object around on plain mouse-moves with no button down.
            var applied = changed && physicalDown && !dropUnsnappedFirstFrame && IsUsableTransform(in realMatrix);

            // Reject a degenerate scale collapse: where a scale handle overlaps the screen rotation ring, ImGuizmo's
            // ambiguous pick can drive the scale to ~0 in a single frame (the object vanishes on all axes). A real scale
            // ramps gradually, so a > 4x shrink from the last accepted size is a bad pick and is dropped; growth (e.g.
            // recovering a zeroed axis) and translate/rotate (scale unchanged) are never affected.
            if (applied && Matrix4x4.Decompose(realMatrix, out var appliedScale, out _, out _))
            {
                if (appliedScale.X < imguizmoScaleGuard.X * 0.25f ||
                    appliedScale.Y < imguizmoScaleGuard.Y * 0.25f ||
                    appliedScale.Z < imguizmoScaleGuard.Z * 0.25f)
                    applied = false;
                else
                    imguizmoScaleGuard = appliedScale;
            }

            if (applied)
            {
                SetWorld(in realMatrix);
                RaiseEdit();
            }

            // NoireGizmo's own drag readout (space-correct, unlike ImGuizmo's hidden world-space text): the anchor, the
            // guide line and the amount moved / rotated / scaled, measured along the frozen press axes. Read from the
            // real object transform this frame - the applied result, or the unchanged world when this frame was dropped.
            if (imguizmoUsing && Options.ShowDragFeedback)
            {
                UpdateImGuizmoFeedback(applied ? realMatrix : world);
                DrawDragFeedback(in frame);
            }

            if (!isUsing && imguizmoUsing)
                EndImguizmoDrag();
        }

        ImGui.End();
        ImGui.PopStyleVar();

        return ownsMouse;
    }

    /// <summary>
    /// Ends the current ImGuizmo drag: clears the using/snap/group state and raises <c>EditEnd</c> once. Called on the
    /// normal release and by the hardware watchdog when the physical button comes up while we still think we are
    /// dragging - so a missed mouse-up can never leave the gizmo permanently owning the mouse.
    /// </summary>
    private void EndImguizmoDrag()
    {
        if (!imguizmoUsing)
            return;

        imguizmoUsing = false;
        imguizmoSnapKind = SnapKind.None;
        groupPressWorlds = Array.Empty<Matrix4x4>();
        RaiseEditEnd();
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
    /// already knows which single op to lock and which snap increment to feed. None when the cursor is over no handle.
    /// Priority is Scale &gt; Translate &gt; Rotate - the topmost handle where they overlap (a scale ball drawn over the
    /// screen rotation ring), matching ImGuizmo's own hit priority, so the lock agrees with what ImGuizmo would pick.
    /// </summary>
    private SnapKind HoveredOpKind()
    {
        if ((Op & GizmoOp.Scale) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Scaleu))
            return SnapKind.Scale;
        if ((Op & GizmoOp.Translate) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Translate))
            return SnapKind.Translate;
        if ((Op & GizmoOp.Rotate) != 0 && ImGuizmo.IsOver(ImGuizmoOperation.Rotate))
            return SnapKind.Rotate;
        return SnapKind.None;
    }

    /// <summary>Maps a driven <see cref="SnapKind"/> to the single ImGuizmo operation to feed during that drag.</summary>
    private static ImGuizmoOperation SnapKindToOperation(SnapKind kind) => kind switch
    {
        SnapKind.Translate => ImGuizmoOperation.Translate,
        SnapKind.Rotate => ImGuizmoOperation.Rotate,
        SnapKind.Scale => ImGuizmoOperation.Scaleu,
        _ => (ImGuizmoOperation)0,
    };

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

    /// <summary>
    /// Turns ImGuizmo's proxy result (whose scale is <see cref="NoireGizmo.baseScale"/> multiplied by ImGuizmo's factor)
    /// back into the real world transform: translation and rotation are taken verbatim, and scale becomes an additive
    /// delta on the size at press - <c>pressScale + (resultScale - baseScale)</c>, floored at <see cref="MinScale"/> so a
    /// component never collapses. That makes an axis that was zero (or any size) grow back at the base rate, matching the
    /// native backend instead of ImGuizmo's multiply-the-current-size behaviour. Falls back to the raw result if it will
    /// not decompose.
    /// </summary>
    private Matrix4x4 RebuildFromScaleProxy(in Matrix4x4 proxyResult)
    {
        if (!Matrix4x4.Decompose(proxyResult, out var s, out var rot, out var trans))
            return proxyResult;

        var newScale = new Vector3(
            MathF.Max(MinScale, pressScale.X + (s.X - baseScale.X)),
            MathF.Max(MinScale, pressScale.Y + (s.Y - baseScale.Y)),
            MathF.Max(MinScale, pressScale.Z + (s.Z - baseScale.Z)));

        return Matrix4x4.CreateScale(newScale)
               * Matrix4x4.CreateFromQuaternion(rot)
               * Matrix4x4.CreateTranslation(trans);
    }

    private static ImGuizmoOperation MapOperation(GizmoOp op) => MapOperationLocked(op, ImGuizmoOperation.Rotate);

    /// <summary>Like <see cref="MapOperation"/>, but the rotation bit is <paramref name="rotateFlag"/> - a single-axis rotate for a plane-locked decal (so only its yaw ring is fed to ImGuizmo), or the full <c>Rotate</c> otherwise.</summary>
    private static ImGuizmoOperation MapOperationLocked(GizmoOp op, ImGuizmoOperation rotateFlag)
    {
        ImGuizmoOperation r = 0;
        if ((op & GizmoOp.Translate) != 0)
            r |= ImGuizmoOperation.Translate;
        if ((op & GizmoOp.Rotate) != 0)
            r |= rotateFlag;
        if ((op & GizmoOp.Scale) != 0)
            r |= ImGuizmoOperation.Scaleu; // universal-scale bits: object-local scale that does not force the gizmo local
        return r;
    }

    /// <summary>
    /// The single ImGuizmo rotation operation a plane-locked decal allows: the yaw that re-aims it. In World space that
    /// is <c>RotateY</c> (world up); in Local space it is the object's local axis most aligned with world up (Ground keeps
    /// its box local Y up, Wall keeps local Z up), so the ring drawn is the one that actually rotates the decal.
    /// </summary>
    private ImGuizmoOperation ConstrainedYawOperation(in Quaternion rot)
    {
        if (Options.Space != GizmoSpace.Local)
            return ImGuizmoOperation.RotateY;

        var lx = Vector3.Transform(Vector3.UnitX, rot);
        var ly = Vector3.Transform(Vector3.UnitY, rot);
        var lz = Vector3.Transform(Vector3.UnitZ, rot);
        return MostVerticalAxis(lx, ly, lz) switch
        {
            0 => ImGuizmoOperation.RotateX,
            2 => ImGuizmoOperation.RotateZ,
            _ => ImGuizmoOperation.RotateY,
        };
    }

    /// <summary>
    /// Resolves <c>&amp;ImGuizmo::Style</c> once, straight from the native module's <c>ImGuizmo_GetStyle</c> export (the
    /// Dalamud binding wraps no style accessor). Zeroing two alpha floats in that struct is how the built-in drag text is
    /// hidden. Returns 0 when the export cannot be resolved, in which case the built-in text is simply left visible.
    /// </summary>
    private static nint ResolveImGuizmoStyle()
    {
        if (imguizmoStyleResolved)
            return imguizmoStylePtr;

        imguizmoStyleResolved = true;
        try
        {
            if (imguizmoContext != null && imguizmoContext.TryGetProcAddress("ImGuizmo_GetStyle", out var fn) && fn != 0)
            {
                var getStyle = Marshal.GetDelegateForFunctionPointer<ImGuizmoGetStyleDelegate>(fn);
                var style = getStyle();

                // Guard the raw-offset writes: only trust the pointer when both target slots read back as a plausible
                // alpha (a finite float in [0,1]). If a different ImGuizmo build shifted the Style layout, this reads
                // some other field, fails the check, and the built-in text is left alone rather than memory corrupted.
                if (style != 0 && IsAlpha(style, ImGuizmoStyleTextAlpha) && IsAlpha(style, ImGuizmoStyleTextShadowAlpha))
                    imguizmoStylePtr = style;
            }
        }
        catch (Exception ex)
        {
            imguizmoStylePtr = 0;
            NoireLogger.LogError(ex, "Could not resolve ImGuizmo_GetStyle; the ImGuizmo backend keeps its own world-space drag text.", "Draw3D");
        }

        return imguizmoStylePtr;
    }

    /// <summary>Reads the float at <paramref name="offset"/> in the ImGuizmo Style and reports whether it looks like a colour alpha (finite, 0..1), used to sanity-check the struct layout before writing.</summary>
    private static bool IsAlpha(nint style, int offset)
    {
        var value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(style, offset));
        return float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    /// <summary>
    /// Zeroes ImGuizmo's TEXT / TEXT_SHADOW alpha (saving the prior values) so its built-in drag info text is invisible
    /// for the next Manipulate call. Paired with <see cref="RestoreImGuizmoText"/> so the change never outlives the call:
    /// ImGuizmo's global style is shared with any other plugin using it, and is left exactly as found.
    /// </summary>
    private void HideImGuizmoText()
    {
        var style = ResolveImGuizmoStyle();
        if (style == 0)
            return;

        imguizmoSavedTextBits = Marshal.ReadInt32(style, ImGuizmoStyleTextAlpha);
        imguizmoSavedShadowBits = Marshal.ReadInt32(style, ImGuizmoStyleTextShadowAlpha);
        Marshal.WriteInt32(style, ImGuizmoStyleTextAlpha, 0);       // TEXT.w = 0f (0x00000000)
        Marshal.WriteInt32(style, ImGuizmoStyleTextShadowAlpha, 0); // TEXT_SHADOW.w = 0f
        imguizmoTextHidden = true;
    }

    /// <summary>Restores the TEXT / TEXT_SHADOW alpha saved by <see cref="HideImGuizmoText"/>, undoing the frame-local hide.</summary>
    private void RestoreImGuizmoText()
    {
        if (!imguizmoTextHidden)
            return;

        imguizmoTextHidden = false;
        var style = ResolveImGuizmoStyle();
        if (style == 0)
            return;

        Marshal.WriteInt32(style, ImGuizmoStyleTextAlpha, imguizmoSavedTextBits);
        Marshal.WriteInt32(style, ImGuizmoStyleTextShadowAlpha, imguizmoSavedShadowBits);
    }

    /// <summary>
    /// Freezes the press-time transform and readout basis for the ImGuizmo drag feedback: the origin, the rotation the
    /// gesture is measured against, and the axes the translation readout projects onto - world axes in World space, the
    /// object's own axes in Local space. Mirrors what the native backend captures in <c>OnDragStart</c>.
    /// </summary>
    private void CaptureImGuizmoPress(in Matrix4x4 world)
    {
        DecomposeSafe(in world, out _, out pressRot, out pressTrans);
        dragOrigin = pressTrans;

        if (Options.Space == GizmoSpace.Local)
        {
            dragAx = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitX, pressRot), Vector3.UnitX);
            dragAy = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitY, pressRot), Vector3.UnitY);
            dragAz = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitZ, pressRot), Vector3.UnitZ);
        }
        else
        {
            dragAx = Vector3.UnitX;
            dragAy = Vector3.UnitY;
            dragAz = Vector3.UnitZ;
        }
    }

    /// <summary>
    /// Refreshes the live drag readout from the current transform against the press transform: the movement (which the
    /// translate readout projects onto the frozen press axes, so a Local-space axis drag reads its own distance rather
    /// than a world projection), the angle swept and the scale relative to the base size. The representative handle
    /// selects which of the three the overlay prints, matching the operation ImGuizmo is driving.
    /// </summary>
    private void UpdateImGuizmoFeedback(in Matrix4x4 current)
    {
        DecomposeSafe(in current, out var scale, out var rot, out var trans);
        feedbackTranslate = trans - pressTrans;
        feedbackAngleDeg = AngleBetweenDeg(pressRot, rot);
        feedbackScale = new Vector3(scale.X / baseScale.X, scale.Y / baseScale.Y, scale.Z / baseScale.Z);
        activeHandle = imguizmoSnapKind switch
        {
            SnapKind.Rotate => GizmoHandle.RotateScreen,
            // ImGuizmo does not expose which scale sub-handle is driving, so pick it from the per-axis change since
            // press: the readout then prints the axis that is actually moving (a Y or Z drag no longer reads the
            // unchanged X ratio and shows a stuck x1.00).
            SnapKind.Scale => ScaleHandleFromDelta(in scale),
            _ => GizmoHandle.TranslateScreen,
        };
    }

    /// <summary>
    /// Which scale handle the readout should report, from the per-axis change since press (relative to
    /// <see cref="NoireGizmo.baseScale"/> so the axes compare fairly). Near-equal change on all three reads as a uniform
    /// scale (<see cref="GizmoHandle.ScaleUniform"/>, printed <c>xN.NN</c>); otherwise the axis that moved most (printed
    /// <c>Y xN.NN</c>). Used because ImGuizmo's universal-scale op does not surface which sub-handle is driving.
    /// </summary>
    private GizmoHandle ScaleHandleFromDelta(in Vector3 currentScale)
    {
        var rx = MathF.Abs((currentScale.X - pressScale.X) / baseScale.X);
        var ry = MathF.Abs((currentScale.Y - pressScale.Y) / baseScale.Y);
        var rz = MathF.Abs((currentScale.Z - pressScale.Z) / baseScale.Z);
        var max = MathF.Max(rx, MathF.Max(ry, rz));
        if (max < 1e-4f)
            return GizmoHandle.ScaleUniform; // nothing has moved yet this drag

        // Uniform when the other two axes moved comparably to the strongest (within 25%).
        if (rx > max * 0.75f && ry > max * 0.75f && rz > max * 0.75f)
            return GizmoHandle.ScaleUniform;

        return rx >= ry && rx >= rz ? GizmoHandle.ScaleX
             : ry >= rz ? GizmoHandle.ScaleY
             : GizmoHandle.ScaleZ;
    }
}
