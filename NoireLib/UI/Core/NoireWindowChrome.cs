using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Turns a Dalamud window into one the plugin draws every pixel of: no ImGui title bar, no ImGui background, no ImGui
/// border, and a title bar, drag and close of your own.
/// </summary>
[NoireFacade]
public static class NoireWindowChrome
{
    /// <summary>
    /// The window flags a fully custom window needs.
    /// </summary>
    public const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar;

    /// <summary>
    /// The same as <see cref="Flags"/>, plus taking away the resize grip for a window of a fixed size.
    /// </summary>
    public const ImGuiWindowFlags FixedFlags = Flags | ImGuiWindowFlags.NoResize;

    /// <summary>
    /// The same as <see cref="Flags"/>, but movable only from a region <see cref="DragFrom"/> names.
    /// </summary>
    public const ImGuiWindowFlags HandleOnlyFlags = Flags | ImGuiWindowFlags.NoMove;

    /// <summary>
    /// The same as <see cref="Flags"/>, and the window itself never scrolls.
    /// </summary>
    public const ImGuiWindowFlags FixedBodyFlags = Flags | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>
    /// Keeps the window in front of every other for the current frame.<br/>
    /// Call it once per frame from inside the window, and again inside any popup it opens.
    /// </summary>
    public static void KeepInFront() => UiWindowOrder.KeepInFront();

    /// <summary>
    /// Paints the window's own surface and border, then runs the body inside it.
    /// </summary>
    /// <param name="body">The window's contents.</param>
    /// <param name="style">How the window is painted. When <see langword="null"/>, the theme's surface and border.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Draw(Action body, WindowChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Draw(body, static b => b(), style);
    }

    /// <summary>
    /// Paints the window's own surface and border, then runs the body inside it.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The window's contents.</param>
    /// <param name="style">How the window is painted. When <see langword="null"/>, the theme's surface and border.</param>
    public static void Draw<TState>(TState state, Action<TState> body, WindowChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoireUI.EnsureFrameServices();

        var settings = style ?? DefaultStyle;
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        // Only the surface fades. ImGui's alpha would dim the text and controls too.
        var opacity = Math.Clamp(settings.Opacity, 0f, 1f);

        using (var draw = UiDraw.Begin())
        {
            if (settings.Plate is { } plate)
                NoireShapes.Plate(min, max, opacity >= 1f ? plate : Faded(plate, opacity));
            else
                NoireShapes.Rect(min, max, ColorHelper.ScaleAlpha(NoireTheme.Current.Resolve(ThemeColor.Surface), opacity));

            if (settings.Frame is { } frame)
                NoireShapes.Frame(min, max, frame);
        }

        var padding = NoireUI.Scaled(settings.Padding);

        // Advanced from the cursor. An absolute position would pin the contents while the window scrolls.
        ImGui.Indent(padding.X);
        ImGui.Dummy(new Vector2(0f, padding.Y));

        // ImGui's content region reports the window's right edge, outside this padding.
        var inner = MathF.Max(1f, (max.X - min.X) - (padding.X * 2f));

        try
        {
            NoireLayout.WrapText(inner, (state, body), static args => args.body(args.state));
        }
        finally
        {
            ImGui.Dummy(new Vector2(0f, padding.Y));
            ImGui.Unindent(padding.X);
        }
    }

    /// <summary>
    /// The style variables a custom window has to be begun with, pushed before <c>Begin</c> and popped after.
    /// </summary>
    /// <returns>How many style variables were pushed.</returns>
    public static int PushWindowStyle() => PushWindowStyle(null);

    /// <summary>
    /// The style variables a custom window has to be begun with.
    /// </summary>
    /// <param name="style">Kept for symmetry with the chrome's own style.</param>
    /// <returns>How many style variables were pushed.</returns>
    public static int PushWindowStyle(WindowChromeStyle? style)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        return 2;
    }

    /// <summary>
    /// Releases what <see cref="PushWindowStyle()"/> pushed.
    /// </summary>
    /// <param name="count">The count that call returned.</param>
    public static void PopWindowStyle(int count)
    {
        if (count > 0)
            ImGui.PopStyleVar(count);
    }

    /// <summary>
    /// Makes a rectangle drag the window, replacing the title bar ImGui is no longer drawing.
    /// </summary>
    /// <param name="min">The top left of the handle, in screen space.</param>
    /// <param name="max">The bottom right of the handle.</param>
    /// <returns>True while the window is being dragged.</returns>
    public static bool DragFrom(Vector2 min, Vector2 max) => DragFrom(min, max, ImGuiMouseCursor.Hand);

    /// <summary>Makes a rectangle drag the window, with your own cursor over the handle and during the drag.</summary>
    /// <param name="min">The top left of the handle, in screen space.</param>
    /// <param name="max">The bottom right of the handle.</param>
    /// <param name="cursor">The cursor to show. <see cref="ImGuiMouseCursor.Arrow"/> leaves the pointer alone.</param>
    /// <returns>True while the window is being dragged.</returns>
    public static bool DragFrom(Vector2 min, Vector2 max, ImGuiMouseCursor cursor)
    {
        NoireUI.EnsureFrameServices();

        if (!TryBeginDrag(out var window))
            return false;

        var mouse = ImGui.GetMousePos();
        var inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;

        if (Continue(window, cursor))
            return true;

        // A button in the title strip keeps its press.
        if (ShouldStart(inside, ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows), ImGui.IsAnyItemHovered(), ImGui.IsAnyItemActive(), ImGui.IsMouseClicked(ImGuiMouseButton.Left)))
        {
            Start(window);
            return true;
        }

        if (inside && !ImGui.IsAnyItemHovered())
            ImGui.SetMouseCursor(cursor);

        return false;
    }

    /// <summary>
    /// Reports a double click on a chrome handle, for a title bar's collapse.<br/>
    /// Can be used together with <see cref="DragFrom(Vector2, Vector2)"/> on the same rectangle.
    /// </summary>
    /// <param name="min">The top left of the handle, in screen space.</param>
    /// <param name="max">The bottom right of the handle.</param>
    /// <returns>True on the frame the handle is double clicked.</returns>
    public static bool DoubleClickFrom(Vector2 min, Vector2 max)
    {
        NoireUI.EnsureFrameServices();

        var mouse = ImGui.GetMousePos();
        var inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;

        return ShouldStart(
            inside,
            ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows),
            ImGui.IsAnyItemHovered(),
            ImGui.IsAnyItemActive(),
            ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left));
    }

    /// <summary>
    /// Makes every part of the window no item has claimed drag it, like a title bar.<br/>
    /// Call it once per frame from inside the window, <b>after</b> its contents.
    /// </summary>
    /// <returns>True while the window is being dragged.</returns>
    public static bool DragFromBody() => DragFromBody(ImGuiMouseCursor.Hand);

    /// <summary>Makes every part of the window no item has claimed drag it, with your own cursor during the drag.</summary>
    /// <param name="cursor">The cursor to show while dragging. <see cref="ImGuiMouseCursor.Arrow"/> leaves the pointer alone.</param>
    /// <returns>True while the window is being dragged.</returns>
    public static bool DragFromBody(ImGuiMouseCursor cursor)
    {
        NoireUI.EnsureFrameServices();

        if (!TryBeginDrag(out var window))
            return false;

        if (Continue(window, cursor))
            return true;

        var mouse = ImGui.GetMousePos();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;

        // Anything the mouse can act on owns its press. Only what is left over moves the window.
        if (!ShouldStart(inside, ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows), ImGui.IsAnyItemHovered(), ImGui.IsAnyItemActive(), ImGui.IsMouseClicked(ImGuiMouseButton.Left)))
            return false;

        Start(window);
        return true;
    }

    /// <summary>
    /// Moves the window with a drag already under way, before its contents are drawn.<br/>
    /// Call it first thing inside the window so the frame is drawn where the pointer is, as ImGui's own move does.
    /// </summary>
    /// <param name="cursor">The cursor to show while dragging.</param>
    /// <returns>True while the window is being dragged.</returns>
    public static bool ContinueDrag(ImGuiMouseCursor cursor = ImGuiMouseCursor.Hand)
    {
        NoireUI.EnsureFrameServices();
        return TryBeginDrag(out var window) && Continue(window, cursor);
    }

    // Replaces ImGui's own drag. With both running, the contents swim behind the frame.
    private static bool TryBeginDrag(out uint window)
    {
        var current = ImGuiP.GetCurrentWindow();
        window = current.ID;

        return (current.Flags & ImGuiWindowFlags.NoMove) != 0;
    }

    private static bool Continue(uint window, ImGuiMouseCursor cursor)
    {
        if (draggingWindow == 0u || draggingWindow != window)
            return false;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            draggingWindow = 0u;
            return false;
        }

        var frame = ImGui.GetFrameCount();

        if (draggedFrame != frame)
        {
            draggedFrame = frame;

            var mouse = ImGui.GetMousePos();
            var delta = mouse - dragOrigin;
            dragOrigin = mouse;

            if (delta != Vector2.Zero)
                ImGui.SetWindowPos(ImGui.GetWindowPos() + delta);
        }

        ImGui.SetMouseCursor(cursor);
        return true;
    }

    private static void Start(uint window)
    {
        draggingWindow = window;
        draggedFrame = ImGui.GetFrameCount();
        dragOrigin = ImGui.GetMousePos();
    }

    internal static bool ShouldStart(bool inside, bool windowHovered, bool itemHovered, bool itemActive, bool pressed)
        => inside && pressed && windowHovered && !itemHovered && !itemActive;

    /// <summary>
    /// Draws one of the window's own chrome buttons, needing no icon font.
    /// </summary>
    /// <param name="id">A unique id.</param>
    /// <param name="centre">The middle of the button, in screen space.</param>
    /// <param name="size">How wide the button's hit box is, in real pixels.</param>
    /// <param name="glyph">Which mark to draw.</param>
    /// <param name="style">How it is drawn. When <see langword="null"/>, the theme's.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public static bool ChromeButton(string id, Vector2 centre, float size, ChromeGlyph glyph, ChromeButtonStyle? style = null)
    {
        NoireUI.EnsureFrameServices();

        var settings = style ?? DefaultChromeStyle;
        var theme = NoireTheme.Current;
        var half = size * 0.5f;
        var restore = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(centre - new Vector2(half, half));

        var clicked = ImGui.InvisibleButton(UiIds.For("###NoireWindowChrome_", id), new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        using var draw = UiDraw.BeginMethod();

        var plateHalf = half * settings.PlateRatio;
        var min = centre - new Vector2(plateHalf, plateHalf);
        var max = centre + new Vector2(plateHalf, plateHalf);
        var corner = NoireUI.Scaled(settings.CornerSize);

        if (hovered || held)
        {
            var danger = glyph == ChromeGlyph.Close;
            var fallback = ColorHelper.ScaleAlpha(
                theme.Resolve(danger ? ThemeColor.Danger : ThemeColor.Accent),
                held ? 0.34f : 0.20f);

            NoireShapes.Rect(min, max, settings.HoveredFill ?? fallback, settings.CornerShape, corner);

            if (settings.HoveredBorder is { } border)
                NoireShapes.RectOutline(min, max, border, 1f, settings.CornerShape, corner);
        }

        var tint = hovered
            ? settings.HoveredColor ?? theme.Resolve(glyph == ChromeGlyph.Close ? ThemeColor.Danger : ThemeColor.Accent)
            : settings.Color ?? theme.Resolve(ThemeColor.TextMuted);

        var reach = plateHalf * settings.CrossRatio;
        var thickness = MathF.Max(1f, NoireUI.Scaled(hovered ? settings.HoveredThickness : settings.Thickness));

        PaintGlyph(glyph, centre, reach, tint, thickness);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        ImGui.SetCursorScreenPos(restore);
        return clicked;
    }

    /// <summary>
    /// Draws a close button.
    /// </summary>
    /// <param name="centre">The middle of the button, in screen space.</param>
    /// <param name="size">How wide the button's hit box is, in real pixels.</param>
    /// <param name="style">How it is drawn. When <see langword="null"/>, the theme's.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public static bool CloseButton(Vector2 centre, float size, ChromeButtonStyle? style = null)
        => ChromeButton("close", centre, size, ChromeGlyph.Close, style);

    private static void PaintGlyph(ChromeGlyph glyph, Vector2 centre, float reach, Vector4 color, float thickness)
    {
        switch (glyph)
        {
            case ChromeGlyph.Close:
            {
                Span<Vector2> down = [centre - new Vector2(reach, reach), centre + new Vector2(reach, reach)];
                Span<Vector2> up = [centre + new Vector2(-reach, reach), centre + new Vector2(reach, -reach)];

                NoireShapes.Stroke(down, color, thickness, closed: false);
                NoireShapes.Stroke(up, color, thickness, closed: false);
                break;
            }

            case ChromeGlyph.Minimize:
            {
                Span<Vector2> chevron =
                [
                    new(centre.X - reach, centre.Y - (reach * 0.4f)),
                    new(centre.X, centre.Y + (reach * 0.5f)),
                    new(centre.X + reach, centre.Y - (reach * 0.4f)),
                ];

                NoireShapes.Stroke(chevron, color, thickness, closed: false);
                break;
            }

            case ChromeGlyph.Restore:
            {
                Span<Vector2> chevron =
                [
                    new(centre.X - reach, centre.Y + (reach * 0.4f)),
                    new(centre.X, centre.Y - (reach * 0.5f)),
                    new(centre.X + reach, centre.Y + (reach * 0.4f)),
                ];

                NoireShapes.Stroke(chevron, color, thickness, closed: false);
                break;
            }

            case ChromeGlyph.Menu:
            {
                for (var bar = -1; bar <= 1; bar++)
                {
                    var y = centre.Y + (bar * reach * 0.62f);
                    Span<Vector2> line = [new(centre.X - reach, y), new(centre.X + reach, y)];

                    NoireShapes.Stroke(line, color, thickness, closed: false);
                }

                break;
            }
        }
    }

    private static PlateStyle Faded(PlateStyle plate, float opacity)
    {
        // Drawing is single threaded and the scratch is only read inside this call.
        FadedScratch.CopyFrom(plate);

        if (FadedScratch.Fill is { } fill)
            FadedScratch.Fill = ColorHelper.ScaleAlpha(fill, opacity);

        if (FadedScratch.FillTo is { } fillTo)
            FadedScratch.FillTo = ColorHelper.ScaleAlpha(fillTo, opacity);

        return FadedScratch;
    }

    private static readonly PlateStyle FadedScratch = new();

    private static readonly ChromeButtonStyle DefaultChromeStyle = new();

    // A drag survives the pointer leaving the handle.
    private static uint draggingWindow;

    // Two handles in the same window must not move it twice.
    private static int draggedFrame = -1;

    private static Vector2 dragOrigin;

    private static readonly WindowChromeStyle DefaultStyle = new();
}
