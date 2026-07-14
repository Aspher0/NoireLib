using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>
/// A move / rotate / scale gizmo for the V2 renderer: grab any <see cref="SceneNode"/> (or any world matrix) and
/// transform it with axis / plane / screen handles, snapping and Local/World/Screen space. Handles are <b>real
/// geometry</b> drawn through <see cref="ImDraw3D"/> and hit-tested with the render-time camera, so they never wobble
/// under camera motion; by default (<see cref="GizmoOptions.Depth"/>) they draw on top of other 3D objects but are
/// occluded by the game world, so a handle is never buried inside the object it edits yet still hides behind a wall.<br/>
/// The gizmo is a client of <see cref="NoireInteract"/>: it shares the one mouse-capture authority, so grabbing a
/// handle takes the lead of input and the camera never pans underneath a drag. Construct one, <see cref="Attach"/> it
/// to a node, and it draws and edits itself every frame until <see cref="Dispose"/>.
/// </summary>
public sealed partial class NoireGizmo : IPointerInteractor, IDisposable
{
    private const int HandleCount = (int)GizmoHandle.ScaleUniform + 1;

    /// <summary>Translate arrows end at this fraction of the handle length; scale knobs sit at the full length, past them, on the same axis.</summary>
    private const float ArmRatio = 0.78f;

    /// <summary>Immediate-layer draw layer for the handles — high, so they paint over translucent scene objects instead of being blended under them.</summary>
    private const int GizmoLayer = 100;

    private readonly GizmoHandleRef[] tokens = new GizmoHandleRef[HandleCount];
    private readonly List<Vector3> circleScratch = new(64);
    private readonly string disposeKey;

    private SceneNode? node;
    private Func<Matrix4x4>? matrixGetter;
    private Action<Matrix4x4>? matrixSetter;
    private bool disposed;

    // Frozen drag state (captured at press so handles don't wobble as the target moves).
    private bool dragging;
    private GizmoHandle activeHandle = GizmoHandle.None;
    private GizmoHandle hoveredHandle = GizmoHandle.None;
    private Vector3 dragOrigin;
    private Vector3 dragAx, dragAy, dragAz;       // translate/rotate primary basis (per Space)
    private Vector3 dragSx, dragSy, dragSz;       // scale basis (always object-space)
    private Vector3 dragViewDir;                   // toward the camera, for screen-plane handles
    private float dragHandleLen;
    private Vector3 pressScale, pressTrans;
    private Quaternion pressRot;
    private Vector2 originScreen;

    /// <summary>Creates a gizmo and registers it with <see cref="NoireInteract"/> (disposed automatically with NoireLib).</summary>
    /// <param name="op">Which operations to expose. Default <see cref="GizmoOp.Universal"/>.</param>
    public NoireGizmo(GizmoOp op = GizmoOp.Universal)
    {
        Op = op;
        for (var i = 0; i < HandleCount; i++)
            tokens[i] = new GizmoHandleRef(this, (GizmoHandle)i);

        disposeKey = $"NoireLib.Draw3D.NoireGizmo.{Guid.NewGuid():N}";
        NoireInteract.RegisterInteractor(this);
        if (NoireService.IsInitialized())
            NoireLibMain.RegisterOnDispose(disposeKey, Dispose);
    }

    /// <summary>Which operations the gizmo exposes.</summary>
    public GizmoOp Op { get; set; }

    /// <summary>Space / snapping / sizing options.</summary>
    public GizmoOptions Options { get; set; } = new();

    /// <summary>Master enable. When false the gizmo neither draws nor interacts.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Draw + interact only while this is true (independent of <see cref="Enabled"/>).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>True while a handle is being dragged.</summary>
    public bool IsDragging => dragging;

    /// <summary>The handle currently under the cursor (or being dragged).</summary>
    public GizmoHandle HoveredHandle => dragging ? activeHandle : hoveredHandle;

    /// <summary>Raised when a drag begins (one edit transaction — pair with your undo system).</summary>
    public event Action<NoireGizmo>? OnEditStart;

    /// <summary>Raised on each frame the target is edited.</summary>
    public event Action<NoireGizmo>? OnEdit;

    /// <summary>Raised when a drag ends.</summary>
    public event Action<NoireGizmo>? OnEditEnd;

    /// <summary>The node the gizmo currently edits (null when bound to a matrix or nothing).</summary>
    public SceneNode? Target => node;

    /// <summary>Binds the gizmo to a scene node; it edits the node's local TRS (converting through the parent as needed).</summary>
    /// <param name="target">The node to manipulate.</param>
    public NoireGizmo Attach(SceneNode target)
    {
        ArgumentNullException.ThrowIfNull(target);
        node = target;
        matrixGetter = null;
        matrixSetter = null;
        return this;
    }

    /// <summary>Binds the gizmo to any world matrix you own via a getter/setter pair.</summary>
    /// <param name="getter">Returns the current world matrix.</param>
    /// <param name="setter">Receives the edited world matrix.</param>
    public NoireGizmo AttachMatrix(Func<Matrix4x4> getter, Action<Matrix4x4> setter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        node = null;
        matrixGetter = getter;
        matrixSetter = setter;
        return this;
    }

    /// <summary>Unbinds the gizmo from any target (it stops drawing until re-attached).</summary>
    public void Detach()
    {
        node = null;
        matrixGetter = null;
        matrixSetter = null;
    }

    private bool HasTarget => (node != null && !node.Destroyed) || matrixGetter != null;

    // ---------------------------------------------------------------- IPointerInteractor

    /// <inheritdoc/>
    public int Priority => 1000; // above scene nodes — handles read on top

    /// <inheritdoc/>
    public bool Active => Enabled && Visible && Op != GizmoOp.None && HasTarget && !disposed;

    /// <inheritdoc/>
    public bool OwnsToken(object token) => token is GizmoHandleRef r && ReferenceEquals(r.Gizmo, this);

    private GizmoHandle TokenToHandle(object? token) => token is GizmoHandleRef r && ReferenceEquals(r.Gizmo, this) ? r.Handle : GizmoHandle.None;

    /// <summary>
    /// Whether the native (in-world, ray-hit-tested) backend is active. The ImGuizmo backend handles its own input —
    /// but we fall back to native so a gizmo still shows and stays grabbable (instead of silently vanishing) when either
    /// its binding can't initialise (see <see cref="EnsureImGuizmoApi"/>) <b>or</b> the frame used the wholesale-VP
    /// fallback camera, which exposes no separate view/proj for ImGuizmo to consume (<see cref="DrawImGuizmo"/> would
    /// otherwise early-out and draw nothing — the "ImGuizmo never appears" symptom).
    /// </summary>
    private bool IsNative
        => Options.Backend != GizmoBackend.ImGuizmo
           || !EnsureImGuizmoApi()
           || (NoireDraw3D.LastFrameValid && NoireDraw3D.LastFrame.UsedFallbackCamera);

    /// <inheritdoc/>
    public bool HitTest(Vector3 rayOrigin, Vector3 rayDirection, Vector2 screen, in FrameContext frame, out object token, out float distance, out Vector3 hitPoint)
    {
        token = null!;
        distance = 0f;
        hitPoint = default;

        if (!IsNative)
            return false; // the ImGuizmo backend reads ImGui IO itself; it is not a ray-hover target

        if (dragging)
        {
            // Keep the grabbed handle latched and highlighted for the whole drag.
            token = tokens[(int)activeHandle];
            hitPoint = dragOrigin;
            return activeHandle != GizmoHandle.None;
        }

        if (!TryGetWorld(out var world))
            return false;

        var basis = ComputeBasis(in world, in frame);
        var best = PickHandle(rayOrigin, rayDirection, in frame, in basis, out var bestT, out var bestPoint);
        if (best == GizmoHandle.None)
            return false;

        token = tokens[(int)best];
        distance = bestT;
        hitPoint = bestPoint;
        return true;
    }

    /// <inheritdoc/>
    public void OnHoverEnter(object token) => hoveredHandle = TokenToHandle(token);

    /// <inheritdoc/>
    public void OnHoverExit(object token) => hoveredHandle = GizmoHandle.None;

    /// <inheritdoc/>
    public void OnClick(object token, MouseButton button) { /* a click that never became a drag — nothing to apply */ }

    /// <inheritdoc/>
    public void OnDragStart(object token, DragContext context)
    {
        activeHandle = TokenToHandle(token);
        if (activeHandle == GizmoHandle.None || !TryGetWorld(out var world))
            return;

        var frame = context.Frame;
        var basis = ComputeBasis(in world, in frame);
        dragOrigin = basis.Origin;
        dragAx = basis.Ax; dragAy = basis.Ay; dragAz = basis.Az;
        dragSx = basis.Sx; dragSy = basis.Sy; dragSz = basis.Sz;
        dragViewDir = basis.ViewDir;
        dragHandleLen = basis.HandleLen;

        DecomposeSafe(in world, out pressScale, out pressRot, out pressTrans);
        originScreen = context.Frame.TryWorldToScreen(dragOrigin, out var s) ? s : context.ScreenStart;

        dragging = true;
        RaiseEditStart();
    }

    /// <inheritdoc/>
    public void OnDrag(object token, DragContext context)
    {
        if (!dragging)
            return;

        var newScale = pressScale;
        var newRot = pressRot;
        var newTrans = pressTrans;

        if (GizmoHandleInfo.IsTranslate(activeHandle))
            newTrans = SolveTranslate(context);
        else if (GizmoHandleInfo.IsRotate(activeHandle))
            newRot = SolveRotate(context);
        else if (GizmoHandleInfo.IsScale(activeHandle))
            newScale = SolveScale(context);

        var world = Matrix4x4.CreateScale(newScale)
                    * Matrix4x4.CreateFromQuaternion(newRot)
                    * Matrix4x4.CreateTranslation(newTrans);
        SetWorld(in world);
        RaiseEdit();
    }

    /// <inheritdoc/>
    public void OnDragEnd(object token, DragContext context)
    {
        if (!dragging)
            return;

        dragging = false;
        activeHandle = GizmoHandle.None;
        RaiseEditEnd();
    }

    private void RaiseEditStart() => SafeRaise(OnEditStart, "OnEditStart");

    private void RaiseEdit() => SafeRaise(OnEdit, "OnEdit");

    private void RaiseEditEnd() => SafeRaise(OnEditEnd, "OnEditEnd");

    private void SafeRaise(Action<NoireGizmo>? handler, string what)
    {
        if (handler == null)
            return;

        try
        {
            handler(this);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"A NoireGizmo {what} handler threw.", "Draw3D");
        }
    }

    // ---------------------------------------------------------------- drag solvers

    private Vector3 SolveTranslate(DragContext ctx)
    {
        var axisIndex = GizmoHandleInfo.AxisIndex(activeHandle);
        if (activeHandle is GizmoHandle.TranslateX or GizmoHandle.TranslateY or GizmoHandle.TranslateZ)
        {
            var axis = AxisByIndex(dragAx, dragAy, dragAz, axisIndex);
            var d = GizmoMath.AxisTranslationDelta(axis, dragOrigin, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
            d = InteractMath.Snap(d, TranslateSnapScalar(axisIndex));
            return pressTrans + axis * d;
        }

        // Plane / screen move.
        var normal = activeHandle switch
        {
            GizmoHandle.TranslateYZ => dragAx,
            GizmoHandle.TranslateZX => dragAy,
            GizmoHandle.TranslateXY => dragAz,
            _ => dragViewDir, // TranslateScreen
        };

        var delta = GizmoMath.PlaneTranslationDelta(dragOrigin, normal, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
        var moved = pressTrans + delta;
        return Options.Space == GizmoSpace.World ? GizmoMath.SnapTranslation(moved, Options.Snap) : moved;
    }

    private Quaternion SolveRotate(DragContext ctx)
    {
        var axis = activeHandle == GizmoHandle.RotateScreen
            ? dragViewDir
            : AxisByIndex(dragAx, dragAy, dragAz, GizmoHandleInfo.AxisIndex(activeHandle));

        var angle = GizmoMath.RotationAngle(dragOrigin, axis, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
        angle = GizmoMath.SnapAngle(angle, Options.RotateSnapDeg);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, angle) * pressRot);
    }

    private Vector3 SolveScale(DragContext ctx)
    {
        if (activeHandle == GizmoHandle.ScaleUniform)
        {
            var f = GizmoMath.UniformScaleFactor(originScreen, ctx.ScreenStart, ctx.ScreenNow);
            var s = pressScale * f;
            return new Vector3(GizmoMath.SnapScale(s.X, Options.ScaleSnap), GizmoMath.SnapScale(s.Y, Options.ScaleSnap), GizmoMath.SnapScale(s.Z, Options.ScaleSnap));
        }

        var axisIndex = GizmoHandleInfo.AxisIndex(activeHandle);
        var axis = AxisByIndex(dragSx, dragSy, dragSz, axisIndex);
        var factor = GizmoMath.AxisScaleFactor(axis, dragOrigin, dragHandleLen, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
        var result = pressScale;
        SetComponent(ref result, axisIndex, GizmoMath.SnapScale(GetComponent(pressScale, axisIndex) * factor, Options.ScaleSnap));
        return result;
    }

    private float TranslateSnapScalar(int axisIndex)
        => Options.Space == GizmoSpace.World
            ? GetComponent(Options.Snap, axisIndex)
            : MathF.Max(Options.Snap.X, MathF.Max(Options.Snap.Y, Options.Snap.Z));

    // ---------------------------------------------------------------- hit-testing

    private GizmoHandle PickHandle(Vector3 ro, Vector3 rd, in FrameContext frame, in Basis b, out float bestT, out Vector3 bestPoint)
    {
        var best = GizmoHandle.None;
        var closest = float.MaxValue;
        var point = b.Origin;

        var grab = GizmoMath.ScreenConstantLength(in frame, b.Origin, Options.GrabPixelTolerance);
        var len = b.HandleLen;
        var arm = len * ArmRatio;   // translate arrow ends here; the scale knob sits past it at len, on the same axis

        void Consider(GizmoHandle h, float t, Vector3 p)
        {
            if (t >= 0f && t < closest)
            {
                closest = t;
                best = h;
                point = p;
            }
        }

        var translate = (Op & GizmoOp.Translate) != 0;
        var rotate = (Op & GizmoOp.Rotate) != 0;
        var scale = (Op & GizmoOp.Scale) != 0;

        if (translate)
        {
            TestAxisSegment(ro, rd, b.Origin, b.Ax, arm, grab, GizmoHandle.TranslateX, Consider);
            TestAxisSegment(ro, rd, b.Origin, b.Ay, arm, grab, GizmoHandle.TranslateY, Consider);
            TestAxisSegment(ro, rd, b.Origin, b.Az, arm, grab, GizmoHandle.TranslateZ, Consider);
            TestPlane(ro, rd, b.Origin, b.Ay, b.Az, b.Ax, len, GizmoHandle.TranslateYZ, Consider);
            TestPlane(ro, rd, b.Origin, b.Az, b.Ax, b.Ay, len, GizmoHandle.TranslateZX, Consider);
            TestPlane(ro, rd, b.Origin, b.Ax, b.Ay, b.Az, len, GizmoHandle.TranslateXY, Consider);
        }

        if (rotate)
        {
            TestRing(ro, rd, b.Origin, b.Ax, len, grab, GizmoHandle.RotateX, Consider);
            TestRing(ro, rd, b.Origin, b.Ay, len, grab, GizmoHandle.RotateY, Consider);
            TestRing(ro, rd, b.Origin, b.Az, len, grab, GizmoHandle.RotateZ, Consider);
            TestRing(ro, rd, b.Origin, b.ViewDir, len * 1.18f, grab, GizmoHandle.RotateScreen, Consider);
        }

        if (scale)
        {
            // Scale knobs sit at len (past the translate arrows at arm), on the same axes — so they read as the same
            // axis line without the shafts collapsing into each other.
            TestAxisKnob(ro, rd, b.Origin, b.Sx, len, arm, grab, GizmoHandle.ScaleX, Consider);
            TestAxisKnob(ro, rd, b.Origin, b.Sy, len, arm, grab, GizmoHandle.ScaleY, Consider);
            TestAxisKnob(ro, rd, b.Origin, b.Sz, len, arm, grab, GizmoHandle.ScaleZ, Consider);
        }

        // Center handle: uniform scale wins the center when scale is enabled, else the screen-move handle.
        if (scale && InteractMath.RaySphere(ro, rd, b.Origin, len * 0.13f, out var tc))
            Consider(GizmoHandle.ScaleUniform, tc, b.Origin);
        else if (translate && InteractMath.RaySphere(ro, rd, b.Origin, len * 0.12f, out var ts))
            Consider(GizmoHandle.TranslateScreen, ts, b.Origin);

        bestT = closest;
        bestPoint = point;
        return best;
    }

    private static void TestAxisSegment(Vector3 ro, Vector3 rd, Vector3 origin, Vector3 axis, float len, float grab, GizmoHandle h, Action<GizmoHandle, float, Vector3> consider)
    {
        var end = origin + axis * len;
        var dist = InteractMath.RaySegmentDistance(ro, rd, origin + axis * (len * 0.12f), end, out var t);
        if (dist <= grab)
            consider(h, t, ro + rd * t);
    }

    private static void TestAxisKnob(Vector3 ro, Vector3 rd, Vector3 origin, Vector3 axis, float knobLen, float stubStart, float grab, GizmoHandle h, Action<GizmoHandle, float, Vector3> consider)
    {
        var knob = origin + axis * knobLen;
        if (InteractMath.RaySphere(ro, rd, knob, MathF.Max(grab, knobLen * 0.10f), out var t))
        {
            consider(h, t, knob);
            return;
        }

        // Only the short connector from the translate arrow tip out to the knob is grabbable — so it never overlaps the
        // translate shaft's grab region below it.
        var dist = InteractMath.RaySegmentDistance(ro, rd, origin + axis * stubStart, knob, out var ts);
        if (dist <= grab)
            consider(h, ts, ro + rd * ts);
    }

    private static void TestPlane(Vector3 ro, Vector3 rd, Vector3 origin, Vector3 axisA, Vector3 axisB, Vector3 normal, float len, GizmoHandle h, Action<GizmoHandle, float, Vector3> consider)
    {
        if (!InteractMath.RayPlane(ro, rd, origin, normal, out var t, out var hit) || t < 0f)
            return;

        var rel = hit - origin;
        var u = Vector3.Dot(rel, axisA);
        var v = Vector3.Dot(rel, axisB);
        var lo = len * 0.18f;
        var hi = len * 0.45f;
        if (u >= lo && u <= hi && v >= lo && v <= hi)
            consider(h, t, hit);
    }

    private static void TestRing(Vector3 ro, Vector3 rd, Vector3 origin, Vector3 axis, float radius, float grab, GizmoHandle h, Action<GizmoHandle, float, Vector3> consider)
    {
        if (InteractMath.RayRing(ro, rd, origin, axis, radius, grab, out var t))
            consider(h, t, ro + rd * t);
    }

    // ---------------------------------------------------------------- drawing

    /// <inheritdoc/>
    /// <remarks>The ImGuizmo backend reads ImGui IO through its own host window, so it runs in the pre-pass (<see cref="DrawSelfDriven"/>), not here.</remarks>
    public bool SelfDriven => !IsNative;

    /// <inheritdoc/>
    public bool DrawSelfDriven(in FrameContext frame)
    {
        if (!Active || IsNative || !TryGetWorld(out var world))
            return false;

        return DrawImGuizmo(in frame, in world);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// UI-thread pass for the native backend: it only tracks which handle is hovered. The handles themselves are drawn
    /// in <see cref="DrawOverlay"/> on the render thread with the CURRENT frame, so their screen-constant size tracks
    /// the live camera instead of lagging it a frame during zoom (the handle "swim"). ImGuizmo is self-driven and
    /// never reaches here.
    /// </remarks>
    public void Draw(in FrameContext frame, object? hovered)
    {
        if (!Active || !IsNative)
            return;

        if (!dragging)
            hoveredHandle = TokenToHandle(hovered);
    }

    /// <inheritdoc/>
    public void DrawOverlay(in FrameContext frame)
    {
        if (!Active || !IsNative || !TryGetWorld(out var world))
            return;

        // The ImGuizmo backend was asked for but native is drawing (binding failed or fallback camera) — say why, once.
        if (Options.Backend == GizmoBackend.ImGuizmo && NoireInteract.DebugLog && !imguizmoNativeFallbackLogged)
        {
            imguizmoNativeFallbackLogged = true;
            NoireLogger.LogInfo(
                $"[Gizmo] ImGuizmo requested but drawing NATIVE: apiReady={EnsureImGuizmoApi()} " +
                $"fallbackCamera={(NoireDraw3D.LastFrameValid && NoireDraw3D.LastFrame.UsedFallbackCamera)}. " +
                "fallbackCamera=true → the renderer exposes no separate view/proj for ImGuizmo (native is correct); " +
                "apiReady=false → the ImGuizmo binding failed to initialise.",
                "Draw3D");
        }

        // Draw from a LIVE basis every frame — even mid-drag. It recomputes the origin (a translate carries the gizmo
        // with the object), the on-screen size (screen-constant regardless of camera distance/zoom, so the handles
        // never shrink/grow as the object moves nearer/farther) and, in Local space, the axes (translate arrows and the
        // always-local scale knobs rotate with the object as you turn it). The drag SOLVER still reads the frozen
        // press-time basis (dragOrigin/dragAx/dragSx/dragHandleLen), so the math stays stable and the grabbed handle
        // never slips — a rotate drag's grabbed ring is its (fixed) rotation axis, which the live basis leaves in place.
        var b = ComputeBasis(in world, in frame);
        var current = dragging ? activeHandle : hoveredHandle;
        // Map the gizmo depth policy onto the immediate-layer style, and draw on a high layer so the handles paint over
        // translucent scene objects (e.g. a fading ground plane) instead of being blended under them.
        var depth = ResolveDepth();
        var style = new ImShapeStyle
        {
            IgnoreDepth = depth == GizmoDepth.AlwaysOnTop,
            OnTopOfObjects = depth == GizmoDepth.OnTopOfObjects,
            Layer = GizmoLayer,
        };
        var im = NoireDraw3D.Im;
        var thickness = GizmoMath.ScreenConstantLength(in frame, b.Origin, Options.HandlePixelThickness);
        var len = b.HandleLen;
        var arm = len * ArmRatio;   // translate arrows stop here so the scale knobs past them don't overlap the shafts

        if ((Op & GizmoOp.Translate) != 0)
        {
            im.DrawArrow(b.Origin, b.Origin + b.Ax * arm, thickness, AxisColor(0, current == GizmoHandle.TranslateX), style);
            im.DrawArrow(b.Origin, b.Origin + b.Ay * arm, thickness, AxisColor(1, current == GizmoHandle.TranslateY), style);
            im.DrawArrow(b.Origin, b.Origin + b.Az * arm, thickness, AxisColor(2, current == GizmoHandle.TranslateZ), style);
            DrawPlane(im, b.Origin, b.Ay, b.Az, len, thickness, AxisColor(0, current == GizmoHandle.TranslateYZ), style);
            DrawPlane(im, b.Origin, b.Az, b.Ax, len, thickness, AxisColor(1, current == GizmoHandle.TranslateZX), style);
            DrawPlane(im, b.Origin, b.Ax, b.Ay, len, thickness, AxisColor(2, current == GizmoHandle.TranslateXY), style);
        }

        if ((Op & GizmoOp.Rotate) != 0)
        {
            DrawRing(im, b.Origin, b.Ax, len, thickness, AxisColor(0, current == GizmoHandle.RotateX), style);
            DrawRing(im, b.Origin, b.Ay, len, thickness, AxisColor(1, current == GizmoHandle.RotateY), style);
            DrawRing(im, b.Origin, b.Az, len, thickness, AxisColor(2, current == GizmoHandle.RotateZ), style);
            DrawRing(im, b.Origin, b.ViewDir, len * 1.18f, thickness, NeutralColor(current == GizmoHandle.RotateScreen), style);
        }

        if ((Op & GizmoOp.Scale) != 0)
        {
            // Cube-tipped scale handles at len, connected to the origin by a thin stub that starts past the translate
            // arrow tip — same axis line, clearly separated from the translate arrows.
            DrawScaleArm(im, b.Origin, b.Sx, arm, len, thickness, AxisColor(0, current == GizmoHandle.ScaleX), style);
            DrawScaleArm(im, b.Origin, b.Sy, arm, len, thickness, AxisColor(1, current == GizmoHandle.ScaleY), style);
            DrawScaleArm(im, b.Origin, b.Sz, arm, len, thickness, AxisColor(2, current == GizmoHandle.ScaleZ), style);
            im.DrawSphere(b.Origin, len * 0.11f, NeutralColor(current == GizmoHandle.ScaleUniform), style);
        }
        else if ((Op & GizmoOp.Translate) != 0)
        {
            im.DrawSphere(b.Origin, len * 0.10f, NeutralColor(current == GizmoHandle.TranslateScreen), style);
        }
    }

    private void DrawPlane(ImDraw3D im, Vector3 origin, Vector3 a, Vector3 bAxis, float len, float thickness, Vector4 color, ImShapeStyle style)
    {
        var lo = len * 0.18f;
        var hi = len * 0.45f;
        var p0 = origin + a * lo + bAxis * lo;
        var p1 = origin + a * hi + bAxis * lo;
        var p2 = origin + a * hi + bAxis * hi;
        var p3 = origin + a * lo + bAxis * hi;
        circleScratch.Clear();
        circleScratch.Add(p0);
        circleScratch.Add(p1);
        circleScratch.Add(p2);
        circleScratch.Add(p3);
        im.DrawPath(circleScratch, thickness * 0.7f, color, closed: true, style);
    }

    private void DrawRing(ImDraw3D im, Vector3 origin, Vector3 axis, float radius, float thickness, Vector4 color, ImShapeStyle style)
    {
        BuildCircle(origin, axis, radius, 48);
        im.DrawPath(circleScratch, thickness, color, closed: true, style);
    }

    private static void DrawScaleArm(ImDraw3D im, Vector3 origin, Vector3 axis, float stubStart, float knobLen, float thickness, Vector4 color, ImShapeStyle style)
    {
        // Thin connector from just past the translate arrow tip out to the knob, then the knob itself.
        im.DrawLine(origin + axis * stubStart, origin + axis * knobLen, thickness * 0.7f, color, style);
        im.DrawSphere(origin + axis * knobLen, knobLen * 0.08f, color, style);
    }

    private void BuildCircle(Vector3 center, Vector3 axis, float radius, int segments)
    {
        axis = InteractMath.SafeNormalize(axis, Vector3.UnitY);
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = InteractMath.SafeNormalize(Vector3.Cross(axis, reference), Vector3.UnitX);
        var v = Vector3.Cross(axis, u);

        circleScratch.Clear();
        for (var k = 0; k < segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            circleScratch.Add(center + (u * cos + v * sin) * radius);
        }
    }

    private static Vector4 AxisColor(int axis, bool highlighted)
    {
        if (highlighted)
            return new Vector4(1f, 0.9f, 0.35f, 1f);

        return axis switch
        {
            0 => new Vector4(0.90f, 0.25f, 0.25f, 1f),
            1 => new Vector4(0.40f, 0.85f, 0.30f, 1f),
            _ => new Vector4(0.30f, 0.55f, 0.95f, 1f),
        };
    }

    private static Vector4 NeutralColor(bool highlighted)
        => highlighted ? new Vector4(1f, 0.9f, 0.35f, 1f) : new Vector4(0.85f, 0.85f, 0.85f, 1f);

    /// <summary>
    /// The effective occlusion for this frame: the static <see cref="GizmoOptions.Depth"/>, unless
    /// <see cref="GizmoOptions.OcclusionHeld"/> is set — then the gizmo is world-occluded only while it returns true
    /// (x-ray otherwise), for a hold-to-occlude key.
    /// </summary>
    private GizmoDepth ResolveDepth()
    {
        if (Options.OcclusionHeld is { } held)
        {
            try
            {
                return held() ? GizmoDepth.OnTopOfObjects : GizmoDepth.AlwaysOnTop;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "The gizmo OcclusionHeld predicate threw.", "Draw3D");
            }
        }

        return Options.Depth;
    }

    // ---------------------------------------------------------------- target & basis

    private bool TryGetWorld(out Matrix4x4 world)
    {
        if (node != null && !node.Destroyed)
        {
            world = node.WorldMatrix;
            return true;
        }

        if (matrixGetter != null)
        {
            world = matrixGetter();
            return true;
        }

        world = Matrix4x4.Identity;
        return false;
    }

    private void SetWorld(in Matrix4x4 world)
    {
        if (node != null && !node.Destroyed)
        {
            var parentWorld = node.Parent?.WorldMatrix ?? Matrix4x4.Identity;
            if (!Matrix4x4.Invert(parentWorld, out var invParent))
                invParent = Matrix4x4.Identity;

            var local = world * invParent;
            if (Matrix4x4.Decompose(local, out var scale, out var rot, out var trans))
            {
                node.LocalScale = scale;
                node.LocalRotation = rot;
                node.LocalPosition = trans;
            }
            else
            {
                node.LocalPosition = local.Translation;
            }

            return;
        }

        matrixSetter?.Invoke(world);
    }

    private Basis ComputeBasis(in Matrix4x4 world, in FrameContext frame)
    {
        DecomposeSafe(in world, out _, out var rot, out var trans);
        var origin = trans;

        Vector3 ax, ay, az;
        switch (Options.Space)
        {
            case GizmoSpace.Local:
                ax = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitX, rot), Vector3.UnitX);
                ay = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitY, rot), Vector3.UnitY);
                az = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitZ, rot), Vector3.UnitZ);
                break;
            case GizmoSpace.Screen:
                InteractMath.WorldPerPixel(in frame, origin, out _, out var right, out var up);
                ax = right;
                ay = up;
                az = InteractMath.SafeNormalize(Vector3.Cross(right, up), Vector3.UnitZ);
                break;
            default:
                ax = Vector3.UnitX; ay = Vector3.UnitY; az = Vector3.UnitZ;
                break;
        }

        // Scale handles are always object-local — they rotate with the object so a scale axis follows the geometry it
        // stretches (World-space translate/rotate arrows stay world-aligned; only scale tracks the object's own frame).
        var sx = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitX, rot), Vector3.UnitX);
        var sy = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitY, rot), Vector3.UnitY);
        var sz = InteractMath.SafeNormalize(Vector3.Transform(Vector3.UnitZ, rot), Vector3.UnitZ);
        var viewDir = InteractMath.SafeNormalize(frame.EyePos - origin, az);
        var handleLen = GizmoMath.ScreenConstantLength(in frame, origin, Options.HandlePixelLength);

        return new Basis(origin, ax, ay, az, sx, sy, sz, viewDir, handleLen);
    }

    private static void DecomposeSafe(in Matrix4x4 world, out Vector3 scale, out Quaternion rot, out Vector3 trans)
    {
        if (Matrix4x4.Decompose(world, out scale, out rot, out trans))
            return;

        scale = Vector3.One;
        rot = Quaternion.Identity;
        trans = world.Translation;
    }

    private static Vector3 AxisByIndex(Vector3 x, Vector3 y, Vector3 z, int index) => index switch { 0 => x, 1 => y, _ => z };

    private static float GetComponent(Vector3 v, int index) => index switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static void SetComponent(ref Vector3 v, int index, float value)
    {
        switch (index)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        NoireInteract.UnregisterInteractor(this);
        if (NoireService.IsInitialized())
            NoireLibMain.UnregisterOnDispose(disposeKey);
    }

    /// <summary>The gizmo's per-frame handle frame (origin + axis bases + on-screen size). Frozen during a drag.</summary>
    private readonly record struct Basis(Vector3 Origin, Vector3 Ax, Vector3 Ay, Vector3 Az, Vector3 Sx, Vector3 Sy, Vector3 Sz, Vector3 ViewDir, float HandleLen);
}

/// <summary>Stable identity for one gizmo handle, so the arbiter can latch a press to it across frames.</summary>
internal sealed class GizmoHandleRef
{
    public GizmoHandleRef(NoireGizmo gizmo, GizmoHandle handle)
    {
        Gizmo = gizmo;
        Handle = handle;
    }

    public NoireGizmo Gizmo { get; }

    public GizmoHandle Handle { get; }
}
