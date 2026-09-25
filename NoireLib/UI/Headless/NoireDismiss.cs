using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// When a popup must close: Escape, or a press outside it anywhere over the game window, on ImGui or in the game world.
/// A press in another application never closes it.
/// </summary>
public static class NoireDismiss
{
    private const int LeftButton = 0x01;
    private const int RightButton = 0x02;

    private static int sampledFrame = int.MinValue;
    private static bool down;
    private static bool pressed;

    /// <summary>Whether a mouse button went down this frame over the game window, read from the OS.</summary>
    public static bool PressedInGame
    {
        get
        {
            var frame = NoireUI.FrameCount;

            if (frame != sampledFrame)
            {
                var now = NoireService.IsInitialized()
                    && (KeybindsHelper.IsAsyncKeyDown(LeftButton) || KeybindsHelper.IsAsyncKeyDown(RightButton));

                pressed = Edge(frame, now) && WindowHelper.IsCursorOverGameWindow();
            }

            return pressed;
        }
    }

    /// <summary>Whether a popup covering an area should close this frame.</summary>
    /// <param name="min">The popup's top left, in screen pixels.</param>
    /// <param name="max">The popup's bottom right.</param>
    /// <returns>True when it should close.</returns>
    public static bool Outside(Vector2 min, Vector2 max) => Outside(min, max, min, max);

    /// <summary>Whether a popup covering an area should close this frame, sparing a second area such as the window it belongs to.</summary>
    /// <param name="min">The popup's top left, in screen pixels.</param>
    /// <param name="max">The popup's bottom right.</param>
    /// <param name="spareMin">The spared area's top left.</param>
    /// <param name="spareMax">The spared area's bottom right.</param>
    /// <returns>True when it should close.</returns>
    public static bool Outside(Vector2 min, Vector2 max, Vector2 spareMin, Vector2 spareMax)
        => ShouldClose(ImGui.IsKeyPressed(ImGuiKey.Escape), PressedInGame, ImGui.GetMousePos(), min, max, spareMin, spareMax);

    internal static bool ShouldClose(bool escape, bool pressed, Vector2 mouse, Vector2 min, Vector2 max, Vector2 spareMin, Vector2 spareMax)
        => escape || (pressed && !Inside(min, max, mouse) && !Inside(spareMin, spareMax, mouse));

    // Only against the frame right before: a button held across a sampling gap never counts.
    internal static bool Edge(int frame, bool now)
    {
        var edge = now && !down && frame == sampledFrame + 1;

        sampledFrame = frame;
        down = now;
        return edge;
    }

    internal static void ResetSampling()
    {
        sampledFrame = int.MinValue;
        down = false;
        pressed = false;
    }

    private static bool Inside(Vector2 min, Vector2 max, Vector2 point)
        => point.X >= min.X && point.Y >= min.Y && point.X < max.X && point.Y < max.Y;
}
