using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// A pointer client that lives above the scene graph: it hit-tests the cursor against its own geometry
/// (gizmo handles, invisible hotspots, custom widgets) and receives hover / click / drag through the same
/// arbitration that governs clickable nodes, so it shares the one mouse-capture authority instead of fighting it.
/// The built-in <see cref="Gizmo.NoireGizmo"/> is such a client; register your own with <see cref="NoireInteract.RegisterInteractor"/>.
/// </summary>
public interface IPointerInteractor
{
    /// <summary>Higher wins when several interactors are hit at once (a gizmo drawn on top should outrank scene nodes). Nodes hit-test at priority 0.</summary>
    int Priority { get; }

    /// <summary>When false the interactor is skipped entirely (no hit-testing, no drawing).</summary>
    bool Active { get; }

    /// <summary>
    /// Tests the cursor against this interactor's grabbable geometry for the frame.
    /// </summary>
    /// <param name="rayOrigin">Cursor ray origin (world).</param>
    /// <param name="rayDirection">Cursor ray direction (world, normalized).</param>
    /// <param name="screen">Cursor position in screen pixels.</param>
    /// <param name="frame">The current frame (projection, viewport).</param>
    /// <param name="token">Receives a stable identity for the hit element (same instance across frames, used for hover/press latching).</param>
    /// <param name="distance">Receives the distance to the hit (nearer hits win ties within equal priority).</param>
    /// <param name="hitPoint">Receives the world point grabbed, used as the drag anchor and for occlusion tests.</param>
    /// <returns>True when the cursor hits something grabbable.</returns>
    bool HitTest(Vector3 rayOrigin, Vector3 rayDirection, Vector2 screen, in FrameContext frame, out object token, out float distance, out Vector3 hitPoint);

    /// <summary>Whether a token produced by <see cref="HitTest"/> belongs to this interactor (used to route events).</summary>
    bool OwnsToken(object token);

    /// <summary>The cursor started hovering one of this interactor's elements.</summary>
    void OnHoverEnter(object token);

    /// <summary>The cursor stopped hovering the element.</summary>
    void OnHoverExit(object token);

    /// <summary>A click (press+release without a drag) landed on the element.</summary>
    void OnClick(object token, MouseButton button);

    /// <summary>A drag started on the element (left press crossed the threshold).</summary>
    void OnDragStart(object token, DragContext context);

    /// <summary>The drag continued this frame.</summary>
    void OnDrag(object token, DragContext context);

    /// <summary>The drag ended (button released).</summary>
    void OnDragEnd(object token, DragContext context);

    /// <summary>Draw the interactor's visuals for the frame (called after hit-testing, on the UI thread).</summary>
    /// <param name="frame">The current frame snapshot.</param>
    /// <param name="hovered">The element currently hovered (belongs to this interactor), or null.</param>
    void Draw(in FrameContext frame, object? hovered);

    /// <summary>
    /// Optional zero-latency draw, called on the <b>render thread</b> each frame with the current frame (not a frame
    /// late like <see cref="Draw"/> from the UI thread). Emit <see cref="Im.ImDraw3D"/> geometry whose on-screen size
    /// or placement must track the live camera without lag; the native gizmo draws its handles here so they stay
    /// phase-locked to the camera during a zoom. Read only atomically-simple state captured on the UI thread; do not
    /// run input logic here. Default: no-op (interactors that do not need it keep drawing in <see cref="Draw"/>).
    /// <br/>
    /// The render thread is stricter than "not the framework thread": on the default under-UI path this fires
    /// <b>mid-frame, from inside one of the game's own D3D calls</b>. Emit geometry and nothing else - no game state,
    /// no chat, no Dalamud game service.
    /// </summary>
    /// <param name="frame">The current render-frame snapshot.</param>
    void DrawOverlay(in FrameContext frame) { }

    /// <summary>
    /// True when this interactor reads ImGui IO itself and owns the mouse through its <b>own</b> ImGui window (the
    /// ImGuizmo gizmo backend), instead of being ray-hit-tested by <see cref="NoireInteract"/>. A self-driven
    /// interactor runs its input and draw in a pre-pass <i>before</i> scene hover resolution (via
    /// <see cref="DrawSelfDriven"/>); while it reports it owns the mouse, the frame is a hard pass for scene picking
    /// and NoireInteract shows no capture window of its own. Default <b>false</b> (an ordinary ray-hit-tested interactor).
    /// </summary>
    bool SelfDriven => false;

    /// <summary>
    /// For a <see cref="SelfDriven"/> interactor only: run its own input and draw for this frame and return whether it
    /// owns the mouse right now (a handle is hovered or being dragged). Called in the pre-pass before scene hover
    /// resolution, so the answer gates scene picking and the capture window this frame, not a frame late. Never called
    /// when <see cref="SelfDriven"/> is false. Default: no-op.
    /// </summary>
    /// <param name="frame">The current frame snapshot.</param>
    bool DrawSelfDriven(in FrameContext frame) => false;

    /// <summary>
    /// Whether a hit reported by <see cref="HitTest"/> should be blocked when something the game draws (a wall,
    /// terrain, a character) is nearer to the camera than the hit point, under
    /// <see cref="NoireInteract.ObstacleOcclusionMode"/>. Default <b>false</b>: interactor geometry stays grabbable
    /// through obstacles. The native gizmo returns true when its handles are world-occluded (its depth mode is not
    /// fully on top), so a handle behind an obstacle is not grabbable there.
    /// </summary>
    /// <param name="token">The hit element from <see cref="HitTest"/>.</param>
    bool OccludesBehindObstacles(object token) => false;
}
