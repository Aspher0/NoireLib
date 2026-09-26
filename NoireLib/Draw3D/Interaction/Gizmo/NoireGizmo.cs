using Dalamud.Bindings.ImGui;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>
/// A move, rotate and scale gizmo that edits a <see cref="SceneNode"/>, a node group or any world matrix.<br/>
/// Once attached it draws and edits itself every frame until <see cref="Dispose"/>.
/// </summary>
public sealed partial class NoireGizmo : IPointerInteractor, IDisposable
{
    private const int HandleCount = (int)GizmoHandle.ScaleUniform + 1;

    private const float ArmRatio = 0.78f;

    private const float ScaleKnobRatio = 1.1f;

    private const float ScaleKnobBallRatio = 0.09f;

    // Handles paint over translucent scene objects.
    private const int GizmoLayer = 100;

    // Keeps a matrix from decomposing to a zeroed, unrecoverable basis.
    private const float MinScale = 1e-3f;

    private readonly GizmoHandleRef[] tokens = new GizmoHandleRef[HandleCount];
    private readonly List<Vector3> circleScratch = new(80);
    private readonly string disposeKey;

    private SceneNode? node;
    private Func<Matrix4x4>? matrixGetter;
    private Action<Matrix4x4>? matrixSetter;
    private IReadOnlyList<SceneNode>? groupNodes;
    private Matrix4x4[] groupPressWorlds = Array.Empty<Matrix4x4>();
    private Matrix4x4 groupPressPivot = Matrix4x4.Identity;
    private Matrix4x4 groupPivot = Matrix4x4.Identity;
    private bool disposed;

    // Multiplying the current size would lock a zeroed axis at zero.
    private Vector3 baseScale = Vector3.One;

    // Frozen at press so handles do not wobble as the target moves.
    private bool dragging;
    private GizmoHandle activeHandle = GizmoHandle.None;
    private GizmoHandle hoveredHandle = GizmoHandle.None;
    private Vector3 dragOrigin;
    private Vector3 dragAx, dragAy, dragAz;
    private Vector3 dragSx, dragSy, dragSz;       // always object-space
    private Vector3 dragViewDir;
    private float dragHandleLen;
    private Vector3 pressScale, pressTrans;
    private Quaternion pressRot;
    private Vector2 originScreen;

    private Vector3 feedbackTranslate;
    private float feedbackAngleDeg;
    private Vector3 feedbackScale = Vector3.One;

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

    /// <summary>Space, snapping, backend and sizing options. The common ones are also exposed directly on the gizmo.</summary>
    public GizmoOptions Options { get; set; } = new();

    /// <summary>The frame translate and rotate handles align to (shortcut for <see cref="GizmoOptions.Space"/>).</summary>
    public GizmoSpace Space
    {
        get => Options.Space;
        set => Options.Space = value;
    }

    /// <summary>Which backend draws and drives the handles (shortcut for <see cref="GizmoOptions.Backend"/>).</summary>
    public GizmoBackend Backend
    {
        get => Options.Backend;
        set => Options.Backend = value;
    }

    /// <summary>Uniform translation snap in world units applied to all three axes (shortcut for <see cref="GizmoOptions.Snap"/>). The getter returns X.</summary>
    public float Snap
    {
        get => Options.Snap.X;
        set => Options.Snap = new Vector3(value);
    }

    /// <summary>Per-axis translation snap in world units (shortcut for <see cref="GizmoOptions.Snap"/>).</summary>
    public Vector3 SnapPerAxis
    {
        get => Options.Snap;
        set => Options.Snap = value;
    }

    /// <summary>Rotation snap in degrees, 0 or less for free (shortcut for <see cref="GizmoOptions.RotateSnapDeg"/>).</summary>
    public float RotateSnapDeg
    {
        get => Options.RotateSnapDeg;
        set => Options.RotateSnapDeg = value;
    }

    /// <summary>Scale snap increment, 0 or less for free (shortcut for <see cref="GizmoOptions.ScaleSnap"/>).</summary>
    public float ScaleSnap
    {
        get => Options.ScaleSnap;
        set => Options.ScaleSnap = value;
    }

    /// <summary>How the native gizmo's handles are occluded (shortcut for <see cref="GizmoOptions.Depth"/>).</summary>
    public GizmoDepth Depth
    {
        get => Options.Depth;
        set => Options.Depth = value;
    }

    /// <summary>Whether the gizmo draws and interacts (default true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the gizmo draws and interacts, independently of <see cref="Enabled"/> (default true).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Whether a handle is being dragged.</summary>
    public bool IsDragging => dragging;

    /// <summary>The handle currently under the cursor (or being dragged).</summary>
    public GizmoHandle HoveredHandle => dragging ? activeHandle : hoveredHandle;

    /// <summary>Raised when a drag begins, opening one edit transaction.</summary>
    public event Action<NoireGizmo>? OnEditStart;

    /// <summary>Raised on each frame the target is edited.</summary>
    public event Action<NoireGizmo>? OnEdit;

    /// <summary>Raised when a drag ends.</summary>
    public event Action<NoireGizmo>? OnEditEnd;

    /// <summary>The node the gizmo currently edits (null when bound to a matrix, a group, or nothing).</summary>
    public SceneNode? Target => node;

    /// <summary>The nodes edited as a group (null unless bound with <see cref="AttachGroup"/>).</summary>
    public IReadOnlyList<SceneNode>? TargetGroup => groupNodes;

    /// <summary>Binds the gizmo to a scene node, editing its local transform through the parent.</summary>
    /// <param name="target">The node to manipulate.</param>
    public NoireGizmo Attach(SceneNode target)
    {
        ArgumentNullException.ThrowIfNull(target);
        node = target;
        matrixGetter = null;
        matrixSetter = null;
        groupNodes = null;
        CaptureBaseScale();
        return this;
    }

    /// <summary>Binds the gizmo to any world matrix through a getter and setter pair.</summary>
    /// <param name="getter">Returns the current world matrix.</param>
    /// <param name="setter">Receives the edited world matrix.</param>
    public NoireGizmo AttachMatrix(Func<Matrix4x4> getter, Action<Matrix4x4> setter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        node = null;
        matrixGetter = getter;
        matrixSetter = setter;
        groupNodes = null;
        CaptureBaseScale();
        return this;
    }

    /// <summary>Binds the gizmo to several nodes, transforming them together around the group's center.</summary>
    /// <param name="targets">The nodes to manipulate together.</param>
    public NoireGizmo AttachGroup(IReadOnlyList<SceneNode> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 1)
            return Attach(targets[0]);

        node = null;
        matrixGetter = null;
        matrixSetter = null;
        groupNodes = new List<SceneNode>(targets); // the render thread iterates it
        baseScale = Vector3.One;
        return this;
    }

    /// <summary>Unbinds the gizmo from its target. It stops drawing until re-attached.</summary>
    public void Detach()
    {
        node = null;
        matrixGetter = null;
        matrixSetter = null;
        groupNodes = null;
    }

    private bool HasTarget
        => (node != null && !node.Destroyed) || matrixGetter != null || (groupNodes != null && groupNodes.Count > 0);

    // A plane-locked Ground or Wall decal can only yaw.
    private bool IsOrientationLockedDecal
        => node?.Renderer?.Material is { Domain: MaterialDomain.GroundDecal } decalMat && decalMat.Surface != DecalSurface.Both;

    private static int MostVerticalAxis(Vector3 ax, Vector3 ay, Vector3 az)
    {
        float vx = MathF.Abs(ax.Y), vy = MathF.Abs(ay.Y), vz = MathF.Abs(az.Y);
        return vy >= vx && vy >= vz ? 1 : vx >= vz ? 0 : 2;
    }

    // A collapsed axis uses a reference of 1 and can grow back.
    private void CaptureBaseScale()
    {
        if (TryGetWorld(out var w))
        {
            TransformHelper.DecomposeSafe(in w, out var s, out _, out _);
            baseScale = new Vector3(BaseReference(s.X), BaseReference(s.Y), BaseReference(s.Z));
        }
        else
        {
            baseScale = Vector3.One;
        }
    }

    private static float BaseReference(float scaleComponent)
    {
        var magnitude = MathF.Abs(scaleComponent);
        return magnitude > 0.01f ? magnitude : 1f;
    }

    private bool IsGroupDragActive => dragging || imguizmoUsing;

    /// <inheritdoc/>
    public int Priority => 1000; // above scene nodes

    /// <inheritdoc/>
    public bool Active => Enabled && Visible && Op != GizmoOp.None && HasTarget && !disposed;

    /// <inheritdoc/>
    public bool OwnsToken(object token) => token is GizmoHandleRef r && ReferenceEquals(r.Gizmo, this);

    private GizmoHandle TokenToHandle(object? token) => token is GizmoHandleRef r && ReferenceEquals(r.Gizmo, this) ? r.Handle : GizmoHandle.None;

    // Native is also the fallback when ImGuizmo failed or the fallback camera has no separate view and projection.
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
            return false; // ImGuizmo reads ImGui IO itself

        if (dragging)
        {
            token = tokens[(int)activeHandle];
            hitPoint = dragOrigin;
            return activeHandle != GizmoHandle.None;
        }

        if (!TryGetWorld(out var world))
            return false;

        var basis = ComputeBasis(in world, in frame);
        var best = PickHandle(screen, in frame, in basis, out var bestDistance, out var bestPoint);
        if (best == GizmoHandle.None)
            return false;

        token = tokens[(int)best];
        distance = bestDistance;
        hitPoint = bestPoint;
        return true;
    }

    /// <inheritdoc/>
    public bool OccludesBehindObstacles(object token)
        => IsNative && ResolveDepth() != GizmoDepth.AlwaysOnTop;

    /// <inheritdoc/>
    public void OnHoverEnter(object token) => hoveredHandle = TokenToHandle(token);

    /// <inheritdoc/>
    public void OnHoverExit(object token) => hoveredHandle = GizmoHandle.None;

    /// <inheritdoc/>
    public void OnClick(object token, MouseButton button) { }

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

        TransformHelper.DecomposeSafe(in world, out pressScale, out pressRot, out pressTrans);
        originScreen = context.Frame.TryWorldToScreen(dragOrigin, out var s) ? s : context.ScreenStart;

        if (groupNodes != null)
            CaptureGroupPress(in world);

        feedbackTranslate = Vector3.Zero;
        feedbackAngleDeg = 0f;
        feedbackScale = Vector3.One;

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

        feedbackTranslate = newTrans - pressTrans;
        feedbackScale = new Vector3(newScale.X / baseScale.X, newScale.Y / baseScale.Y, newScale.Z / baseScale.Z);
        feedbackAngleDeg = AngleBetweenDeg(pressRot, newRot);

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
        groupPressWorlds = Array.Empty<Matrix4x4>();
        RaiseEditEnd();
    }

    private static float AngleBetweenDeg(Quaternion a, Quaternion b)
    {
        var delta = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(a), b));
        return MathF.Acos(Math.Clamp(MathF.Abs(delta.W), 0f, 1f)) * 2f * (180f / MathF.PI);
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
            NoireLogger.LogError(ex, $"A NoireGizmo {what} handler threw.", "[Draw3D] ");
        }
    }

    private Vector3 SolveTranslate(DragContext ctx)
    {
        Vector3 moved;
        if (activeHandle is GizmoHandle.TranslateX or GizmoHandle.TranslateY or GizmoHandle.TranslateZ)
        {
            var axis = AxisByIndex(dragAx, dragAy, dragAz, GizmoHandleInfo.AxisIndex(activeHandle));
            var d = GizmoMath.AxisTranslationDelta(axis, dragOrigin, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
            moved = pressTrans + axis * d;
        }
        else
        {
            var normal = activeHandle switch
            {
                GizmoHandle.TranslateYZ => dragAx,
                GizmoHandle.TranslateZX => dragAy,
                GizmoHandle.TranslateXY => dragAz,
                _ => dragViewDir,
            };
            moved = pressTrans + GizmoMath.PlaneTranslationDelta(dragOrigin, normal, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
        }

        return SnapTranslation(moved);
    }

    // Snaps the movement since press, like ImGuizmo. An off-grid object keeps its offset.
    private Vector3 SnapTranslation(Vector3 moved)
    {
        var (dx, dy, dz) = ActiveTranslateAxes();

        if (Options.Space == GizmoSpace.World)
        {
            var result = pressTrans;
            if (dx) result.X = pressTrans.X + MathHelper.Snap(moved.X - pressTrans.X, Options.Snap.X);
            if (dy) result.Y = pressTrans.Y + MathHelper.Snap(moved.Y - pressTrans.Y, Options.Snap.Y);
            if (dz) result.Z = pressTrans.Z + MathHelper.Snap(moved.Z - pressTrans.Z, Options.Snap.Z);
            return result;
        }

        // Local space has no per-axis world grid.
        var step = MathF.Max(Options.Snap.X, MathF.Max(Options.Snap.Y, Options.Snap.Z));
        var delta = moved - pressTrans;
        var local = pressTrans;
        if (dx) local += dragAx * MathHelper.Snap(Vector3.Dot(delta, dragAx), step);
        if (dy) local += dragAy * MathHelper.Snap(Vector3.Dot(delta, dragAy), step);
        if (dz) local += dragAz * MathHelper.Snap(Vector3.Dot(delta, dragAz), step);
        return local;
    }

    private (bool X, bool Y, bool Z) ActiveTranslateAxes() => activeHandle switch
    {
        GizmoHandle.TranslateX => (true, false, false),
        GizmoHandle.TranslateY => (false, true, false),
        GizmoHandle.TranslateZ => (false, false, true),
        GizmoHandle.TranslateYZ => (false, true, true),
        GizmoHandle.TranslateZX => (true, false, true),
        GizmoHandle.TranslateXY => (true, true, false),
        _ => (true, true, true),
    };

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
        // Additive increments. A near-zero component can grow back.
        if (activeHandle == GizmoHandle.ScaleUniform)
        {
            var frac = GizmoMath.UniformScaleFactor(originScreen, ctx.ScreenStart, ctx.ScreenNow) - 1f;
            return new Vector3(
                ScaleComponent(pressScale.X, baseScale.X, frac),
                ScaleComponent(pressScale.Y, baseScale.Y, frac),
                ScaleComponent(pressScale.Z, baseScale.Z, frac));
        }

        var axisIndex = GizmoHandleInfo.AxisIndex(activeHandle);
        var axis = AxisByIndex(dragSx, dragSy, dragSz, axisIndex);
        var delta = GizmoMath.AxisTranslationDelta(axis, dragOrigin, ctx.PressRayOrigin, ctx.PressRayDirection, ctx.RayOrigin, ctx.RayDirection);
        var axisFrac = dragHandleLen > 1e-6f ? delta / dragHandleLen : 0f;
        var result = pressScale;
        result[axisIndex] = ScaleComponent(pressScale[axisIndex], baseScale[axisIndex], axisFrac);
        return result;
    }

    private float ScaleComponent(float pressValue, float baseValue, float frac)
        => GizmoMath.SnapScale(MathF.Max(MinScale, pressValue + baseValue * frac), Options.ScaleSnap);

    // Screen-space distance. A world-space ray test degenerates on edge-on rings and grazing planes.
    private GizmoHandle PickHandle(Vector2 cursor, in FrameContext frame, in Basis b, out float bestDistance, out Vector3 bestPoint)
    {
        var f = frame;                 // local functions cannot capture an 'in' parameter
        var origin = b.Origin;
        var eye = f.EyePos;
        var len = b.HandleLen;
        var arm = len * ArmRatio;
        var axisStart = len * 0.16f;   // the axis centre belongs to the center handle
        var tol = MathF.Max(4f, Options.GrabPixelTolerance + Options.HandlePixelThickness * 0.5f);

        var best = GizmoHandle.None;
        var bestPixels = float.MaxValue;
        var bestCam = float.MaxValue;
        var bestWorld = origin;

        void Consider(GizmoHandle h, float pixels, Vector3 world)
        {
            if (pixels > tol)
                return;

            var cam = Vector3.Distance(eye, world);
            if (pixels < bestPixels - 0.5f || (pixels <= bestPixels + 0.5f && cam < bestCam))
            {
                best = h;
                bestPixels = pixels;
                bestCam = cam;
                bestWorld = world;
            }
        }

        void ConsiderAxis(Vector3 axisDir, GizmoHandle h)
        {
            var tip = origin + axisDir * arm;
            if (f.TryWorldToScreen(origin + axisDir * axisStart, out var a) && f.TryWorldToScreen(tip, out var t))
                Consider(h, Geometry2DHelper.PointToSegmentDistance(cursor, a, t), tip);
        }

        void ConsiderPlane(Vector3 a, Vector3 bAxis, GizmoHandle h)
        {
            var lo = len * 0.18f;
            var hi = len * 0.45f;
            var p0 = origin + a * lo + bAxis * lo;
            var p1 = origin + a * hi + bAxis * lo;
            var p2 = origin + a * hi + bAxis * hi;
            var p3 = origin + a * lo + bAxis * hi;
            if (!f.TryWorldToScreen(p0, out var s0) || !f.TryWorldToScreen(p1, out var s1) ||
                !f.TryWorldToScreen(p2, out var s2) || !f.TryWorldToScreen(p3, out var s3))
                return;

            var center = origin + (a + bAxis) * ((lo + hi) * 0.5f);
            var px = Geometry2DHelper.PointInConvexQuad(cursor, s0, s1, s2, s3)
                ? 0f
                : Min4(Geometry2DHelper.PointToSegmentDistance(cursor, s0, s1), Geometry2DHelper.PointToSegmentDistance(cursor, s1, s2),
                       Geometry2DHelper.PointToSegmentDistance(cursor, s2, s3), Geometry2DHelper.PointToSegmentDistance(cursor, s3, s0));
            Consider(h, px, center);
        }

        void ConsiderRing(Vector3 axis, float radius, GizmoHandle h)
        {
            axis = Geometry3DHelper.SafeNormalize(axis, Vector3.UnitY);
            var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            var u = Geometry3DHelper.SafeNormalize(Vector3.Cross(axis, reference), Vector3.UnitX);
            var v = Vector3.Cross(axis, u);

            const int seg = 48;
            var prevValid = false;
            var prevS = Vector2.Zero;
            var prevW = origin;
            var ringPixels = float.MaxValue;
            var ringWorld = origin;
            for (var k = 0; k <= seg; k++)
            {
                var (sin, cos) = MathF.SinCos(k * MathF.Tau / seg);
                var w = origin + (u * cos + v * sin) * radius;
                if (!f.TryWorldToScreen(w, out var s))
                {
                    prevValid = false;
                    continue;
                }

                if (prevValid)
                {
                    var d = Geometry2DHelper.PointToSegmentDistance(cursor, prevS, s);
                    if (d < ringPixels)
                    {
                        ringPixels = d;
                        ringWorld = (prevW + w) * 0.5f;
                    }
                }

                prevS = s;
                prevW = w;
                prevValid = true;
            }

            if (ringPixels < float.MaxValue)
                Consider(h, ringPixels, ringWorld);
        }

        void ConsiderPoint(Vector3 world, GizmoHandle h)
        {
            if (f.TryWorldToScreen(world, out var s))
                Consider(h, Vector2.Distance(cursor, s), world);
        }

        var translate = (Op & GizmoOp.Translate) != 0;
        var rotate = (Op & GizmoOp.Rotate) != 0;
        var scale = (Op & GizmoOp.Scale) != 0;

        if (translate)
        {
            ConsiderAxis(b.Ax, GizmoHandle.TranslateX);
            ConsiderAxis(b.Ay, GizmoHandle.TranslateY);
            ConsiderAxis(b.Az, GizmoHandle.TranslateZ);
            ConsiderPlane(b.Ay, b.Az, GizmoHandle.TranslateYZ);
            ConsiderPlane(b.Az, b.Ax, GizmoHandle.TranslateZX);
            ConsiderPlane(b.Ax, b.Ay, GizmoHandle.TranslateXY);
        }

        if (rotate)
        {
            var yaw = IsOrientationLockedDecal ? MostVerticalAxis(b.Ax, b.Ay, b.Az) : -1; // -1 = all rings
            if (yaw is < 0 or 0) ConsiderRing(b.Ax, len, GizmoHandle.RotateX);
            if (yaw is < 0 or 1) ConsiderRing(b.Ay, len, GizmoHandle.RotateY);
            if (yaw is < 0 or 2) ConsiderRing(b.Az, len, GizmoHandle.RotateZ);
            if (yaw < 0) ConsiderRing(b.ViewDir, len * 1.18f, GizmoHandle.RotateScreen);
        }

        if (scale)
        {
            var scaleKnob = len * ScaleKnobRatio;
            ConsiderPoint(origin + b.Sx * scaleKnob, GizmoHandle.ScaleX);
            ConsiderPoint(origin + b.Sy * scaleKnob, GizmoHandle.ScaleY);
            ConsiderPoint(origin + b.Sz * scaleKnob, GizmoHandle.ScaleZ);
        }

        if (translate)
            ConsiderPoint(origin, GizmoHandle.TranslateScreen);
        else if (scale)
            ConsiderPoint(origin, GizmoHandle.ScaleUniform);

        bestDistance = best == GizmoHandle.None ? 0f : bestCam;
        bestPoint = bestWorld;
        return best;
    }

    private static float Min4(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));

    /// <inheritdoc/>
    public bool SelfDriven => !IsNative;

    /// <inheritdoc/>
    public bool DrawSelfDriven(in FrameContext frame)
    {
        if (!Active || IsNative || !TryGetWorld(out var world))
            return false;

        return DrawImGuizmo(in frame, in world);
    }

    /// <inheritdoc/>
    public void Draw(in FrameContext frame, object? hovered)
    {
        if (!Active || !IsNative)
            return;

        if (!dragging)
        {
            hoveredHandle = TokenToHandle(hovered);
            return;
        }

        if (Options.ShowDragFeedback)
            DrawDragFeedback(in frame);
    }

    private void DrawDragFeedback(in FrameContext frame)
    {
        var draw = ImGui.GetForegroundDrawList();
        var haveAnchor = frame.TryWorldToScreen(dragOrigin, out var anchor);
        var haveCurrent = frame.TryWorldToScreen(dragOrigin + feedbackTranslate, out var current);

        var white = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
        var whiteDim = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.65f));
        var amber = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.84f, 0.30f, 1f));
        var shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.75f));

        if (haveAnchor)
            draw.AddCircle(anchor, 7f, white, 24, 1.6f);

        string label;
        if (GizmoHandleInfo.IsRotate(activeHandle))
        {
            if (haveAnchor)
                draw.AddCircle(anchor, 22f, amber, 32, 1.5f);
            label = $"{feedbackAngleDeg:0.0} deg";
        }
        else
        {
            if (haveAnchor && haveCurrent)
            {
                draw.AddLine(anchor, current, whiteDim, 1.5f);
                draw.AddCircleFilled(current, 3.5f, amber);
            }

            if (GizmoHandleInfo.IsScale(activeHandle))
            {
                label = activeHandle == GizmoHandle.ScaleUniform
                    ? $"x{feedbackScale.X:0.00}"
                    : $"{"XYZ"[GizmoHandleInfo.AxisIndex(activeHandle)]} x{ScaleComponentReadout():0.00}";
            }
            else
            {
                label = TranslateReadout();
            }
        }

        var anchorText = haveCurrent ? current : anchor;
        var textPos = anchorText + new Vector2(12f, -6f);
        draw.AddText(textPos + new Vector2(1f, 1f), shadow, label);
        draw.AddText(textPos, white, label);
    }

    private string TranslateReadout()
    {
        const float eps = 0.005f;
        var (dx, dy, dz) = ActiveTranslateAxes();
        var text = string.Empty;
        if (dx)
            AppendReadoutAxis(ref text, 'X', Vector3.Dot(feedbackTranslate, dragAx), eps);
        if (dy)
            AppendReadoutAxis(ref text, 'Y', Vector3.Dot(feedbackTranslate, dragAy), eps);
        if (dz)
            AppendReadoutAxis(ref text, 'Z', Vector3.Dot(feedbackTranslate, dragAz), eps);
        return text.Length > 0 ? text : "0.00";
    }

    private static void AppendReadoutAxis(ref string text, char axis, float value, float eps)
    {
        if (MathF.Abs(value) < eps)
            return;

        text += $"{(text.Length > 0 ? "  " : string.Empty)}{axis} {value:+0.00;-0.00}";
    }

    private float ScaleComponentReadout() => GizmoHandleInfo.AxisIndex(activeHandle) switch
    {
        0 => feedbackScale.X,
        1 => feedbackScale.Y,
        _ => feedbackScale.Z,
    };

    /// <inheritdoc/>
    public void DrawOverlay(in FrameContext frame)
    {
        if (!Active || !IsNative || !TryGetWorld(out var world))
            return;

        if (Options.Backend == GizmoBackend.ImGuizmo && NoireInteract.DebugLog && !imguizmoNativeFallbackLogged)
        {
            imguizmoNativeFallbackLogged = true;
            NoireLogger.LogInfo(
                $"[Gizmo] ImGuizmo requested but drawing native: apiReady={EnsureImGuizmoApi()} " +
                $"fallbackCamera={(NoireDraw3D.LastFrameValid && NoireDraw3D.LastFrame.UsedFallbackCamera)}.",
                "[Draw3D] ");
        }

        // Drawn from a live basis. The solver reads the frozen press-time basis.
        var b = ComputeBasis(in world, in frame);
        var current = dragging ? activeHandle : hoveredHandle;

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
        var arm = len * ArmRatio;

        // While dragging only the active handle is drawn, like ImGuizmo.
        bool Show(GizmoHandle h) => !dragging || activeHandle == h;

        if ((Op & GizmoOp.Translate) != 0)
        {
            if (Show(GizmoHandle.TranslateX))
                im.DrawArrow(b.Origin, b.Origin + b.Ax * arm, thickness, AxisColor(0, current == GizmoHandle.TranslateX), style);
            if (Show(GizmoHandle.TranslateY))
                im.DrawArrow(b.Origin, b.Origin + b.Ay * arm, thickness, AxisColor(1, current == GizmoHandle.TranslateY), style);
            if (Show(GizmoHandle.TranslateZ))
                im.DrawArrow(b.Origin, b.Origin + b.Az * arm, thickness, AxisColor(2, current == GizmoHandle.TranslateZ), style);
            if (Show(GizmoHandle.TranslateYZ))
                DrawPlane(im, b.Origin, b.Ay, b.Az, len, thickness, AxisColor(0, current == GizmoHandle.TranslateYZ), style);
            if (Show(GizmoHandle.TranslateZX))
                DrawPlane(im, b.Origin, b.Az, b.Ax, len, thickness, AxisColor(1, current == GizmoHandle.TranslateZX), style);
            if (Show(GizmoHandle.TranslateXY))
                DrawPlane(im, b.Origin, b.Ax, b.Ay, len, thickness, AxisColor(2, current == GizmoHandle.TranslateXY), style);
        }

        if ((Op & GizmoOp.Rotate) != 0)
        {
            var yaw = IsOrientationLockedDecal ? MostVerticalAxis(b.Ax, b.Ay, b.Az) : -1; // -1 = all rings
            if (yaw is < 0 or 0 && Show(GizmoHandle.RotateX))
                DrawRing(im, b.Origin, b.Ax, len, thickness, AxisColor(0, current == GizmoHandle.RotateX), style);
            if (yaw is < 0 or 1 && Show(GizmoHandle.RotateY))
                DrawRing(im, b.Origin, b.Ay, len, thickness, AxisColor(1, current == GizmoHandle.RotateY), style);
            if (yaw is < 0 or 2 && Show(GizmoHandle.RotateZ))
                DrawRing(im, b.Origin, b.Az, len, thickness, AxisColor(2, current == GizmoHandle.RotateZ), style);
            if (yaw < 0 && Show(GizmoHandle.RotateScreen))
                DrawRing(im, b.Origin, b.ViewDir, len * 1.18f, thickness, NeutralColor(current == GizmoHandle.RotateScreen), style);
        }

        if ((Op & GizmoOp.Scale) != 0)
        {
            var scaleKnob = len * ScaleKnobRatio;
            var scaleBall = len * ScaleKnobBallRatio;
            if (Show(GizmoHandle.ScaleX))
                im.DrawSphere(b.Origin + b.Sx * scaleKnob, scaleBall, AxisColor(0, current == GizmoHandle.ScaleX), style);
            if (Show(GizmoHandle.ScaleY))
                im.DrawSphere(b.Origin + b.Sy * scaleKnob, scaleBall, AxisColor(1, current == GizmoHandle.ScaleY), style);
            if (Show(GizmoHandle.ScaleZ))
                im.DrawSphere(b.Origin + b.Sz * scaleKnob, scaleBall, AxisColor(2, current == GizmoHandle.ScaleZ), style);
        }

        if ((Op & GizmoOp.Translate) != 0)
        {
            if (Show(GizmoHandle.TranslateScreen))
                im.DrawSphere(b.Origin, len * 0.10f, NeutralColor(current == GizmoHandle.TranslateScreen), style);
        }
        else if ((Op & GizmoOp.Scale) != 0)
        {
            if (Show(GizmoHandle.ScaleUniform))
                im.DrawSphere(b.Origin, len * 0.10f, NeutralColor(current == GizmoHandle.ScaleUniform), style);
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
        BuildCircle(origin, axis, radius, 64);
        im.DrawPath(circleScratch, thickness * 0.85f, color, closed: true, style);
    }

    private void BuildCircle(Vector3 center, Vector3 axis, float radius, int segments)
    {
        axis = Geometry3DHelper.SafeNormalize(axis, Vector3.UnitY);
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Geometry3DHelper.SafeNormalize(Vector3.Cross(axis, reference), Vector3.UnitX);
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
            return new Vector4(1f, 0.84f, 0.30f, 1f);

        return axis switch
        {
            0 => new Vector4(0.95f, 0.28f, 0.34f, 1f),
            1 => new Vector4(0.44f, 0.86f, 0.32f, 1f),
            _ => new Vector4(0.28f, 0.56f, 0.98f, 1f),
        };
    }

    private static Vector4 NeutralColor(bool highlighted)
        => highlighted ? new Vector4(1f, 0.84f, 0.30f, 1f) : new Vector4(0.88f, 0.88f, 0.90f, 1f);

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
                NoireLogger.LogError(ex, "The gizmo OcclusionHeld predicate threw.", "[Draw3D] ");
            }
        }

        return Options.Depth;
    }

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

        if (groupNodes != null && groupNodes.Count > 0)
        {
            // Frozen during a drag.
            if (!IsGroupDragActive)
                groupPivot = Matrix4x4.CreateFromQuaternion(GroupOrientation()) * Matrix4x4.CreateTranslation(GroupCentroid());
            world = groupPivot;
            return true;
        }

        world = Matrix4x4.Identity;
        return false;
    }

    private void SetWorld(in Matrix4x4 world)
    {
        if (node != null && !node.Destroyed)
        {
            SetNodeWorld(node, in world);
            return;
        }

        if (groupNodes != null && groupNodes.Count > 0)
        {
            // newMember = pressMember * inv(pressPivot) * newPivot.
            groupPivot = world;
            if (!Matrix4x4.Invert(groupPressPivot, out var invPress))
                invPress = Matrix4x4.Identity;

            var delta = invPress * world;
            for (var i = 0; i < groupNodes.Count && i < groupPressWorlds.Length; i++)
            {
                var member = groupNodes[i];
                if (member is { Destroyed: false })
                    SetNodeWorld(member, groupPressWorlds[i] * delta);
            }

            return;
        }

        matrixSetter?.Invoke(world);
    }

    private static void SetNodeWorld(SceneNode target, in Matrix4x4 world)
    {
        var parentWorld = target.Parent?.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(parentWorld, out var invParent))
            invParent = Matrix4x4.Identity;

        var local = world * invParent;
        if (Matrix4x4.Decompose(local, out var scale, out var rot, out var trans))
        {
            target.LocalScale = scale;
            target.LocalRotation = rot;
            target.LocalPosition = trans;
        }
        else
        {
            target.LocalPosition = local.Translation;
        }
    }

    private Vector3 GroupCentroid()
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var member in groupNodes!)
        {
            if (member is { Destroyed: false })
            {
                sum += member.WorldMatrix.Translation;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector3.Zero;
    }

    private Quaternion GroupOrientation()
    {
        if (Options.Space != GizmoSpace.Local || groupNodes == null)
            return Quaternion.Identity;

        foreach (var member in groupNodes)
        {
            if (member is { Destroyed: false })
            {
                TransformHelper.DecomposeSafe(member.WorldMatrix, out _, out var rot, out _);
                return rot;
            }
        }

        return Quaternion.Identity;
    }

    private void CaptureGroupPress(in Matrix4x4 pivotWorld)
    {
        groupPressPivot = pivotWorld;
        var list = groupNodes!;
        groupPressWorlds = new Matrix4x4[list.Count];
        for (var i = 0; i < list.Count; i++)
            groupPressWorlds[i] = list[i] is { Destroyed: false } member ? member.WorldMatrix : Matrix4x4.Identity;
    }

    private Basis ComputeBasis(in Matrix4x4 world, in FrameContext frame)
    {
        TransformHelper.DecomposeSafe(in world, out _, out var rot, out var trans);
        var origin = trans;

        Vector3 ax, ay, az;
        if (Options.Space == GizmoSpace.Local)
        {
            ax = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitX, rot), Vector3.UnitX);
            ay = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitY, rot), Vector3.UnitY);
            az = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitZ, rot), Vector3.UnitZ);
        }
        else
        {
            ax = Vector3.UnitX; ay = Vector3.UnitY; az = Vector3.UnitZ;
        }

        // Scale handles are always object-local.
        var sx = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitX, rot), Vector3.UnitX);
        var sy = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitY, rot), Vector3.UnitY);
        var sz = Geometry3DHelper.SafeNormalize(Vector3.Transform(Vector3.UnitZ, rot), Vector3.UnitZ);
        var viewDir = Geometry3DHelper.SafeNormalize(frame.EyePos - origin, az);
        var handleLen = GizmoMath.ScreenConstantLength(in frame, origin, Options.HandlePixelLength);

        return new Basis(origin, ax, ay, az, sx, sy, sz, viewDir, handleLen);
    }

    private static Vector3 AxisByIndex(Vector3 x, Vector3 y, Vector3 z, int index) => index switch { 0 => x, 1 => y, _ => z };

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

    private readonly record struct Basis(Vector3 Origin, Vector3 Ax, Vector3 Ay, Vector3 Az, Vector3 Sx, Vector3 Sy, Vector3 Sz, Vector3 ViewDir, float HandleLen);
}

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
