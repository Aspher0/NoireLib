using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using HexaGen.Runtime;
using NoireLib.Helpers;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace NoireLib.Draw3D.Interaction.Gizmo;

// ImGuizmo drops hover whenever another window is hovered. It is hosted in a fullscreen NoInputs window and blocks the camera with SetNextFrameWantCaptureMouse.
public sealed partial class NoireGizmo
{
    private enum SnapKind { None, Translate, Rotate, Scale }

    private static int nextImguizmoId;
    private static long imguizmoFrameStamp = -1;
    private static int imguizmoApiState; // 0 = untried, 1 = ready, 2 = unavailable
    private static bool imguizmoDrewOnce;
    private static bool imguizmoNativeFallbackLogged;

    private static INativeContext? imguizmoContext; // resolves ImGuizmo_GetStyle, which the binding does not wrap
    private static nint imguizmoStylePtr;
    private static bool imguizmoStyleResolved;

    // ImGuizmo's Style is 8 floats then ImVec4 Colors[COUNT]. TEXT is colour 13, TEXT_SHADOW 14. These offsets land on each alpha.
    private const int ImGuizmoStyleTextAlpha = (8 + 13 * 4 + 3) * sizeof(float);
    private const int ImGuizmoStyleTextShadowAlpha = (8 + 14 * 4 + 3) * sizeof(float);

    private readonly int imguizmoId = Interlocked.Increment(ref nextImguizmoId);
    private readonly float[] imguizmoSnap = new float[3];
    private bool imguizmoUsing;
    private Vector3 imguizmoScaleGuard = Vector3.One;
    private ImGuizmoOperation imguizmoDragOp;         // one op locked for the drag, disambiguating overlapping handles
    private SnapKind imguizmoSnapKind;
    private SnapKind imguizmoHoverKind;
    private int imguizmoSavedTextBits, imguizmoSavedShadowBits;
    private bool imguizmoTextHidden;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ImGuizmoGetStyleDelegate();

    // VK_LBUTTON. io.MouseDown can miss a release Dalamud routed to the game.
    private static bool PhysicalLeftDown() => KeybindsHelper.IsAsyncKeyDown(0x01);

    internal static void ResetImGuizmoDiagnostics()
    {
        imguizmoDrewOnce = false;
        imguizmoNativeFallbackLogged = false;
        NoireLogger.LogInfo($"[Gizmo] diagnostics re-armed. imguizmoApiState={imguizmoApiState} (0=untried, 1=ready, 2=unavailable).", "Draw3D");
    }

    // Dalamud only initialises the ImGui binding. A failed ImGuizmo binding disables the backend.
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
            imguizmoContext = context;
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

    private bool DrawImGuizmo(in FrameContext frame, in Matrix4x4 world)
    {
        // A lost mouse-up leaves io.MouseDown stuck. ImGui is resynced to the hardware button.
        var physicalDown = PhysicalLeftDown();
        if (imguizmoUsing && !physicalDown)
        {
            ImGui.GetIO().MouseDown[0] = false;
            EndImguizmoDrag();
        }

        if (frame.UsedFallbackCamera)
            return false;

        if (!EnsureImGuizmoApi())
            return false;

        // ImGuizmo must draw to a live window's draw list. A fore/background list hit-tests but renders nothing.
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        // ImGuizmo's IsHoveringWindow() fails whenever another window is hovered. No capture window may exist.
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var ownsMouse = false;
        if (ImGui.Begin("##NoireImGuizmoHost", flags))
        {
            var frameCount = ImGui.GetFrameCount();
            if (imguizmoFrameStamp != frameCount)
            {
                imguizmoFrameStamp = frameCount;
                ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
                ImGuizmo.BeginFrame();
            }

            ImGuizmo.SetID(imguizmoId);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetDrawlist();
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);

            ImGuizmo.Enable(true);

            // FFXIV leaves view M44 unset. Its reversed-Z infinite-far projection breaks ImGuizmo's ray unprojection.
            var view = frame.View;
            view.M44 = 1f;
            var proj = BuildImGuizmoProjection(in frame);
            var mode = Options.Space == GizmoSpace.Local ? ImGuizmoMode.Local : ImGuizmoMode.World;

            // ImGuizmo scales multiplicatively and zero never grows back. It gets a proxy at baseScale, converted back by RebuildFromScaleProxy.
            TransformHelper.DecomposeSafe(in world, out var curScale, out var curRot, out var curTrans);
            if (!imguizmoUsing)
            {
                pressScale = curScale;
                imguizmoScaleGuard = curScale;
            }
            var proxyBefore = Matrix4x4.CreateScale(baseScale)
                              * Matrix4x4.CreateFromQuaternion(curRot)
                              * Matrix4x4.CreateTranslation(curTrans);
            var matrix = proxyBefore;

            // Scaleu bits do not force the whole gizmo local like Scale does.
            ImGuizmo.SetID(imguizmoId);

            // From press, only the hovered op is locked. An overlapping scale ball and rotation ring would otherwise collapse the object.
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

            var snapKind = imguizmoUsing ? imguizmoSnapKind
                : imguizmoHoverKind != SnapKind.None ? imguizmoHoverKind
                : SingleOpKind();

            // ImGuizmo's text is a world-space delta, wrong in Local space. The style is shared with other plugins.
            var hideBuiltInText = Options.ShowDragFeedback;
            if (hideBuiltInText)
                HideImGuizmoText();

            var changed = TryBuildSnap(snapKind, out var snap)
                ? ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix, ref snap[0])
                : ImGuizmo.Manipulate(ref view, ref proj, op, mode, ref matrix);

            if (hideBuiltInText)
                RestoreImGuizmoText();

            // Proxy against proxy. The real world's scale differs from baseScale.
            var realMatrix = RebuildFromScaleProxy(in matrix);

            var isOver = ImGuizmo.IsOver();
            var isUsing = ImGuizmo.IsUsing();

            if (isUsing && !imguizmoUsing && physicalDown) // never start on a stuck "using"
            {
                imguizmoUsing = true;
                // Detects the changed op only when the press had no hover frame.
                imguizmoSnapKind = snapKind != SnapKind.None ? snapKind : DetectChangedOp(in proxyBefore, in matrix);
                CaptureImGuizmoPress(in world);
                if (groupNodes != null)
                    CaptureGroupPress(in world);
                RaiseEditStart();
            }
            else if (isUsing && imguizmoSnapKind == SnapKind.None && changed)
            {
                imguizmoSnapKind = DetectChangedOp(in proxyBefore, in matrix);
            }

            if (!isUsing)
                imguizmoHoverKind = HoveredOpKind();

            // A sub-increment nudge on an unsnapped first frame would never show and snap back.
            var dropUnsnappedFirstFrame = snapKind == SnapKind.None && OpHasSnap(imguizmoSnapKind);

            // A stuck "using" with the button physically up must never keep the mouse.
            ownsMouse = isOver || (isUsing && physicalDown);

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

            var applied = changed && physicalDown && !dropUnsnappedFirstFrame && IsUsableTransform(in realMatrix);

            // An ambiguous pick can collapse scale to ~0 in one frame. A 4x shrink is dropped.
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

    // Also called by the hardware watchdog.
    private void EndImguizmoDrag()
    {
        if (!imguizmoUsing)
            return;

        imguizmoUsing = false;
        imguizmoSnapKind = SnapKind.None;
        groupPressWorlds = Array.Empty<Matrix4x4>();
        RaiseEditEnd();
    }

    private SnapKind SingleOpKind() => Op switch
    {
        GizmoOp.Translate => SnapKind.Translate,
        GizmoOp.Rotate => SnapKind.Rotate,
        GizmoOp.Scale => SnapKind.Scale,
        _ => SnapKind.None,
    };

    // Scale, Translate, Rotate matches ImGuizmo's own hit priority.
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

    private static ImGuizmoOperation SnapKindToOperation(SnapKind kind) => kind switch
    {
        SnapKind.Translate => ImGuizmoOperation.Translate,
        SnapKind.Rotate => ImGuizmoOperation.Rotate,
        SnapKind.Scale => ImGuizmoOperation.Scaleu,
        _ => (ImGuizmoOperation)0,
    };

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

    private bool OpHasSnap(SnapKind kind) => kind switch
    {
        SnapKind.Translate => Options.Snap != Vector3.Zero,
        SnapKind.Rotate => Options.RotateSnapDeg > 0f,
        SnapKind.Scale => Options.ScaleSnap > 0f,
        _ => false,
    };

    // The game's reversed-Z infinite-far projection collapses ImGuizmo's inverse(view * proj). Only clip.z is rebuilt finite.
    private static Matrix4x4 BuildImGuizmoProjection(in FrameContext frame)
    {
        var proj = frame.Proj;
        var near = frame.NearPlane > 1e-4f ? frame.NearPlane : 0.1f;
        var far = MathF.Max(near * 10000f, 10000f);
        var wSign = proj.M34;                        // carries the game's handedness
        proj.M13 = 0f;
        proj.M23 = 0f;
        proj.M33 = wSign * far / (far - near);
        proj.M43 = -near * far / (far - near);
        return proj;
    }

    private static bool IsUsableTransform(in Matrix4x4 m)
    {
        var finite =
            float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
            float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
            float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
            float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
        if (!finite)
            return false;

        return !Matrix4x4.Decompose(m, out var scale, out _, out _)
            ? true // sheared: SetWorld falls back to translation only
            : scale.LengthSquared() > 1e-12f;
    }

    // pressScale + (resultScale - baseScale), floored at MinScale, like the native backend.
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

    private static ImGuizmoOperation MapOperationLocked(GizmoOp op, ImGuizmoOperation rotateFlag)
    {
        ImGuizmoOperation r = 0;
        if ((op & GizmoOp.Translate) != 0)
            r |= ImGuizmoOperation.Translate;
        if ((op & GizmoOp.Rotate) != 0)
            r |= rotateFlag;
        if ((op & GizmoOp.Scale) != 0)
            r |= ImGuizmoOperation.Scaleu;
        return r;
    }

    // Ground keeps local Y up, Wall local Z.
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

                // Only trusted when both slots read as a plausible alpha.
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

    private static bool IsAlpha(nint style, int offset)
    {
        var value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(style, offset));
        return float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    // Always paired with RestoreImGuizmoText. The style is shared with every plugin using ImGuizmo.
    private void HideImGuizmoText()
    {
        var style = ResolveImGuizmoStyle();
        if (style == 0)
            return;

        imguizmoSavedTextBits = Marshal.ReadInt32(style, ImGuizmoStyleTextAlpha);
        imguizmoSavedShadowBits = Marshal.ReadInt32(style, ImGuizmoStyleTextShadowAlpha);
        Marshal.WriteInt32(style, ImGuizmoStyleTextAlpha, 0);
        Marshal.WriteInt32(style, ImGuizmoStyleTextShadowAlpha, 0);
        imguizmoTextHidden = true;
    }

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

    private void CaptureImGuizmoPress(in Matrix4x4 world)
    {
        TransformHelper.DecomposeSafe(in world, out _, out pressRot, out pressTrans);
        dragOrigin = pressTrans;

        if (Options.Space == GizmoSpace.Local)
        {
            dragAx = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitX, pressRot), Vector3.UnitX);
            dragAy = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitY, pressRot), Vector3.UnitY);
            dragAz = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitZ, pressRot), Vector3.UnitZ);
        }
        else
        {
            dragAx = Vector3.UnitX;
            dragAy = Vector3.UnitY;
            dragAz = Vector3.UnitZ;
        }
    }

    private void UpdateImGuizmoFeedback(in Matrix4x4 current)
    {
        TransformHelper.DecomposeSafe(in current, out var scale, out var rot, out var trans);
        feedbackTranslate = trans - pressTrans;
        feedbackAngleDeg = AngleBetweenDeg(pressRot, rot);
        feedbackScale = new Vector3(scale.X / baseScale.X, scale.Y / baseScale.Y, scale.Z / baseScale.Z);
        activeHandle = imguizmoSnapKind switch
        {
            SnapKind.Rotate => GizmoHandle.RotateScreen,
            SnapKind.Scale => ScaleHandleFromDelta(in scale),
            _ => GizmoHandle.TranslateScreen,
        };
    }

    private GizmoHandle ScaleHandleFromDelta(in Vector3 currentScale)
    {
        var rx = MathF.Abs((currentScale.X - pressScale.X) / baseScale.X);
        var ry = MathF.Abs((currentScale.Y - pressScale.Y) / baseScale.Y);
        var rz = MathF.Abs((currentScale.Z - pressScale.Z) / baseScale.Z);
        var max = MathF.Max(rx, MathF.Max(ry, rz));
        if (max < 1e-4f)
            return GizmoHandle.ScaleUniform;

        if (rx > max * 0.75f && ry > max * 0.75f && rz > max * 0.75f)
            return GizmoHandle.ScaleUniform;

        return rx >= ry && rx >= rz ? GizmoHandle.ScaleX
             : ry >= rz ? GizmoHandle.ScaleY
             : GizmoHandle.ScaleZ;
    }
}
