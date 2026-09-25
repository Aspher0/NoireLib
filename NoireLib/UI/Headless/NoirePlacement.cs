using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Where a floating box goes next to what it belongs to, kept inside the main viewport's work area.</summary>
public static class NoirePlacement
{
    /// <summary>A box centred above an anchor, or below it when there is no room above.</summary>
    /// <param name="anchorMin">The anchor's top left, in screen pixels.</param>
    /// <param name="anchorMax">The anchor's bottom right.</param>
    /// <param name="size">The box's size.</param>
    /// <param name="gap">The space between the anchor and the box.</param>
    /// <returns>The box's top left.</returns>
    public static Vector2 Above(Vector2 anchorMin, Vector2 anchorMax, Vector2 size, float gap)
    {
        var viewport = ImGui.GetMainViewport();
        return Above(anchorMin, anchorMax, size, gap, viewport.WorkPos, viewport.WorkPos + viewport.WorkSize);
    }

    /// <summary>A box beside a window on its right, or on its left when there is no room, centred on a height.</summary>
    /// <param name="windowMin">The window's top left, in screen pixels.</param>
    /// <param name="windowMax">The window's bottom right.</param>
    /// <param name="size">The box's size.</param>
    /// <param name="gap">The space between the window and the box.</param>
    /// <param name="anchorY">The screen height the box centres on.</param>
    /// <returns>The box's top left.</returns>
    public static Vector2 Beside(Vector2 windowMin, Vector2 windowMax, Vector2 size, float gap, float anchorY)
    {
        var viewport = ImGui.GetMainViewport();
        return Beside(windowMin, windowMax, size, gap, anchorY, viewport.WorkPos, viewport.WorkPos + viewport.WorkSize);
    }

    internal static Vector2 Above(Vector2 anchorMin, Vector2 anchorMax, Vector2 size, float gap, Vector2 screenMin, Vector2 screenMax)
    {
        var x = ((anchorMin.X + anchorMax.X) * 0.5f) - (size.X * 0.5f);
        var y = anchorMin.Y - gap - size.Y;

        if (y < screenMin.Y)
            y = anchorMax.Y + gap;

        return Clamp(new Vector2(x, y), size, screenMin, screenMax);
    }

    internal static Vector2 Beside(Vector2 windowMin, Vector2 windowMax, Vector2 size, float gap, float anchorY, Vector2 screenMin, Vector2 screenMax)
    {
        var x = windowMax.X + gap;

        if (x + size.X > screenMax.X)
            x = windowMin.X - gap - size.X;

        return Clamp(new Vector2(x, anchorY - (size.Y * 0.5f)), size, screenMin, screenMax);
    }

    // A box already inside the work area stays where it is.
    internal static Vector2 OnScreen(Vector2 at, Vector2 size)
    {
        var viewport = ImGui.GetMainViewport();
        return Clamp(at, Vector2.Max(size, Vector2.Zero), viewport.WorkPos, viewport.WorkPos + viewport.WorkSize);
    }

    // A box larger than the screen keeps its top left on screen.
    private static Vector2 Clamp(Vector2 at, Vector2 size, Vector2 screenMin, Vector2 screenMax)
        => new(
            MathF.Round(Math.Clamp(at.X, screenMin.X, MathF.Max(screenMin.X, screenMax.X - size.X))),
            MathF.Round(Math.Clamp(at.Y, screenMin.Y, MathF.Max(screenMin.Y, screenMax.Y - size.Y))));
}
