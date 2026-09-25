using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

// Edge and bottom-corner resizing for a window drawing its own frame. Submitted before the contents: its handles win the hover.
internal sealed class WindowResize
{
    private static readonly string[] Ids = ["##rsz-br", "##rsz-bl", "##rsz-l", "##rsz-r", "##rsz-t", "##rsz-b"];

    private int handle = -1;
    private Vector2 startMouse;
    private Vector2 startSize;
    private Vector2 startPos;

    internal bool Resizing => handle >= 0;

    internal void Tick(in ChromeMetrics metrics, float scale, Vector2 minimumSize, bool lockWidth, bool lockHeight, bool lockPosition)
    {
        var min = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var restore = ImGui.GetCursorScreenPos();

        for (var i = 0; i < Ids.Length; i++)
        {
            var dx = lockWidth ? 0 : DirectionX(i);
            var dy = lockHeight ? 0 : DirectionY(i);

            if ((dx == 0 && dy == 0) || (lockPosition && (dx < 0 || dy < 0)))
                continue;

            Rect(i, min, min + size, metrics, scale, out var handleMin, out var handleMax);

            if (handleMax.X - handleMin.X < 1f || handleMax.Y - handleMin.Y < 1f)
                continue;

            ImGui.SetCursorScreenPos(handleMin);
            ImGui.InvisibleButton(Ids[i], handleMax - handleMin);
            var active = ImGui.IsItemActive();

            if (ImGui.IsItemHovered() || active)
            {
                ImGui.SetMouseCursor(dx != 0 && dy != 0
                    ? dx == dy ? ImGuiMouseCursor.ResizeNwse : ImGuiMouseCursor.ResizeNesw
                    : dx != 0 ? ImGuiMouseCursor.ResizeEw : ImGuiMouseCursor.ResizeNs);
            }

            if (ImGui.IsItemActivated())
            {
                handle = i;
                startMouse = ImGui.GetMousePos();
                startSize = size;
                startPos = min;
            }

            if (!active || handle != i)
                continue;

            var delta = ImGui.GetMousePos() - startMouse;
            var minimum = minimumSize * scale;
            var next = startSize;
            var position = startPos;

            if (dx != 0)
            {
                next.X = MathF.Max(minimum.X, startSize.X + (delta.X * dx));

                if (dx < 0)
                    position.X = startPos.X + startSize.X - next.X;
            }

            if (dy != 0)
            {
                next.Y = MathF.Max(minimum.Y, startSize.Y + (delta.Y * dy));

                if (dy < 0)
                    position.Y = startPos.Y + startSize.Y - next.Y;
            }

            if (next != size)
                ImGui.SetWindowSize(next);

            if (position != min)
                ImGui.SetWindowPos(position);
        }

        if (handle >= 0 && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            handle = -1;

        ImGui.SetCursorScreenPos(restore);
    }

    internal static bool IsOver(Vector2 point, Vector2 min, Vector2 max, in ChromeMetrics metrics, float scale)
    {
        for (var i = 0; i < Ids.Length; i++)
        {
            Rect(i, min, max, metrics, scale, out var handleMin, out var handleMax);

            if (point.X >= handleMin.X && point.Y >= handleMin.Y && point.X < handleMax.X && point.Y < handleMax.Y)
                return true;
        }

        return false;
    }

    // Handles: 0 bottom right, 1 bottom left, 2 left, 3 right, 4 top, 5 bottom.
    private static int DirectionX(int i) => i is 0 or 3 ? 1 : i is 1 or 2 ? -1 : 0;

    private static int DirectionY(int i) => i is 0 or 1 or 5 ? 1 : i == 4 ? -1 : 0;

    private static void Rect(int i, Vector2 min, Vector2 max, in ChromeMetrics metrics, float scale, out Vector2 handleMin, out Vector2 handleMax)
    {
        var corner = metrics.GripSize * scale;
        var edge = metrics.EdgeGrab * scale;

        (handleMin, handleMax) = i switch
        {
            0 => (max - new Vector2(corner, corner), max),
            1 => (new Vector2(min.X, max.Y - corner), new Vector2(min.X + corner, max.Y)),
            2 => (new Vector2(min.X, min.Y + corner), new Vector2(min.X + edge, max.Y - corner)),
            3 => (new Vector2(max.X - edge, min.Y + corner), new Vector2(max.X, max.Y - corner)),
            4 => (new Vector2(min.X + corner, min.Y), new Vector2(max.X - corner, min.Y + edge)),
            _ => (new Vector2(min.X + corner, max.Y - edge), new Vector2(max.X - corner, max.Y)),
        };
    }
}
