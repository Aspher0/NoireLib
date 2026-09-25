using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A drag from a row: a press becomes a drag past a few pixels, and the drop reports where the mouse was released,
/// over ImGui or over the game. What a drop does is the caller's.
/// </summary>
/// <typeparam name="T">What is dragged.</typeparam>
public sealed class NoireDrag<T> where T : class
{
    private Vector2 pressedAt;

    /// <summary>What is being dragged.</summary>
    public T? Payload { get; private set; }

    /// <summary>The phase this frame.</summary>
    public DragPhase Phase { get; private set; }

    /// <summary>The mouse position this frame, in screen pixels.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>How far the mouse must move before a press becomes a drag, in pixels at 100%.</summary>
    public float Threshold { get; init; } = 4f;

    /// <summary>Starts a possible drag from the row that was just pressed.</summary>
    /// <param name="payload">What would be dragged.</param>
    public void Press(T payload) => Press(payload, ImGui.GetMousePos());

    /// <summary>Advances the drag. Call once per frame from the drawing that owns it.</summary>
    /// <returns>The phase this frame.</returns>
    public DragPhase Update()
    {
        var phase = Step(ImGui.GetMousePos(), ImGui.IsMouseDown(ImGuiMouseButton.Left));

        if (phase == DragPhase.Dragging)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        return phase;
    }

    /// <summary>Drops the drag without a result.</summary>
    public void Cancel()
    {
        Phase = DragPhase.None;
        Payload = null;
    }

    internal void Press(T payload, Vector2 at)
    {
        Payload = payload;
        pressedAt = at;
        Position = at;
        Phase = DragPhase.Pressed;
    }

    internal DragPhase Step(Vector2 mouse, bool down)
    {
        Position = mouse;

        switch (Phase)
        {
            case DragPhase.Pressed when !down:
            case DragPhase.Dropped:
                Cancel();
                break;

            case DragPhase.Pressed when Vector2.Distance(mouse, pressedAt) >= NoireUI.Scaled(Threshold):
                Phase = DragPhase.Dragging;
                break;

            case DragPhase.Dragging when !down:
                Phase = DragPhase.Dropped;
                break;
        }

        return Phase;
    }
}
