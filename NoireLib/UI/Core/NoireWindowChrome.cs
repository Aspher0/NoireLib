using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Turns a Dalamud window into one the plugin draws every pixel of: no ImGui title bar, no ImGui background, no ImGui
/// border, and a title bar, drag and close of your own.
/// </summary>
/// <remarks>
/// ImGui's window decoration is drawn from its style and cannot be replaced, so a design whose window is part of the
/// design has nowhere to go. Taking the decoration away is easy; what is not, and what this exists for, is everything
/// that stops working once you do. The window must still be draggable, and ImGui's own drag is attached to the title
/// bar that is now gone. It must still be closeable. It must still be resizable if it was. And the background is now
/// the plugin's to paint, which means it has to be painted before anything else and behind everything else.<br/>
/// Not a window class: creating and registering windows is Dalamud's job and wrapping it would take away the
/// <see cref="Dalamud.Interface.Windowing.Window"/> surface a plugin already knows. This is the chrome, applied inside a
/// window a plugin owns.
/// </remarks>
/// <example>
/// <code>
/// // On the window:
/// Flags = NoireWindowChrome.Flags;
///
/// // In Draw():
/// NoireWindowChrome.Draw(chrome, () => DrawMyContents());
/// </code>
/// </example>
public static class NoireWindowChrome
{
    /// <summary>
    /// The window flags a fully custom window needs.
    /// </summary>
    /// <remarks>
    /// The background, the title bar and the border are all removed because they would be drawn under, over and around
    /// what the chrome paints instead. Moving is left with ImGui, which drags a decorationless window from any empty
    /// space in it: with no title bar to take hold of, being able to pick the window up wherever it is not already busy
    /// is what makes it feel like a window at all.<br/>
    /// The scrollbar goes and the wheel stays. A design that paints its own edges cannot afford ImGui's scrollbar down
    /// the inside of one, but a window that silently refuses the wheel is broken rather than clean.
    /// </remarks>
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
    /// <remarks>
    /// For a design whose empty space is not spare: dragging from anywhere turns every gap between two controls into a
    /// handle, which suits a window that is mostly chrome and not one that is mostly content.
    /// </remarks>
    public const ImGuiWindowFlags HandleOnlyFlags = Flags | ImGuiWindowFlags.NoMove;

    /// <summary>
    /// The same as <see cref="Flags"/>, and the window itself never scrolls.
    /// </summary>
    /// <remarks>
    /// For a window whose masthead and rail stay put while a region inside it scrolls. Without this the wheel moves the
    /// whole design, header and all, which is not what a fixed masthead is for.
    /// </remarks>
    public const ImGuiWindowFlags FixedBodyFlags = Flags | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>
    /// Keeps the window in front of every other, for the frame being drawn. Call it once per frame from inside the
    /// window, and again from inside any popup it opens.
    /// </summary>
    /// <remarks>
    /// This is the whole of always on top, and it covers both halves of what "in front" means.<br/>
    /// <b>Clicks</b> are decided by the display list, which this moves the window to the front of. <b>Drawing</b> is
    /// decided by the draw layer first, and the display list is reordered when a window is focused, after every plugin
    /// has drawn and before the frame is rendered. A window holding its place by the display list alone is therefore
    /// drawn behind for one frame every time an overlapping window is clicked, and there is no point in plugin code
    /// later than that reorder to undo it. So this lifts the window into the top draw layer as well.<br/>
    /// It does that by setting the layer's flag on the window <i>after</i> it has been begun. The layer is read when the
    /// frame is rendered, so the flag counts for that frame, while none of what the same flag does inside <c>Begin</c>
    /// happens at all: the window is not moved to the cursor, its background and border keep reading the fields an
    /// ordinary window reads, and its default item width is unchanged. Nothing has to be passed at <c>PreDraw</c>.<br/>
    /// The layer covers the whole of the ordinary one, so anything the window opens over itself has to join it. Every
    /// NoireUI popup does that on its own; a popup of your own calls this from inside itself, which is also what settles
    /// the order between the two, since among the windows in front the last caller each frame wins.
    /// </remarks>
    public static void KeepInFront() => UiWindowOrder.KeepInFront();

    /// <summary>
    /// Paints the window's own surface and border, then runs the body inside it.
    /// </summary>
    /// <remarks>
    /// The chrome is painted across the whole window rather than measured from the body, which is the one way this
    /// differs from <see cref="NoirePanel"/>: a window already knows how big it is, so there is nothing to measure and
    /// no need to split the draw list.
    /// </remarks>
    /// <param name="body">The window's contents.</param>
    /// <param name="style">How the window is painted. When <see langword="null"/>, the theme's surface and border.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Draw(Action body, WindowChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Draw(body, static b => b(), style);
    }

    /// <inheritdoc cref="Draw(Action, WindowChromeStyle)"/>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>, so the body can stay a static lambda.</param>
    /// <param name="body">The window's contents.</param>
    /// <param name="style">How the window is painted. When <see langword="null"/>, the theme's surface and border.</param>
    public static void Draw<TState>(TState state, Action<TState> body, WindowChromeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoireUI.EnsureFrameServices();

        var settings = style ?? DefaultStyle;
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        // The opacity is spent on the surface alone. Fading the whole window through ImGui's alpha takes the text and
        // the controls with it, which is not a translucent window, it is a dim one: what a window wants to see through
        // is its background.
        var opacity = Math.Clamp(settings.Opacity, 0f, 1f);

        if (settings.Plate is { } plate)
            NoireShapes.Plate(min, max, opacity >= 1f ? plate : Faded(plate, opacity));
        else
            NoireShapes.Rect(min, max, ColorHelper.ScaleAlpha(NoireTheme.Current.Resolve(ThemeColor.Surface), opacity));

        if (settings.Frame is { } frame)
            NoireShapes.Frame(min, max, frame);

        var padding = NoireUI.Scaled(settings.Padding);

        // Advanced from wherever the cursor already is rather than placed at the window's corner. The two are the same
        // on the first frame and stop being so the moment the window scrolls: the corner is fixed while the content
        // moves up past it, so setting an absolute position would pin the contents in place and the wheel would appear
        // to do nothing. The chrome itself is painted at the corner deliberately, so the border stays with the window.
        ImGui.Indent(padding.X);
        ImGui.Dummy(new Vector2(0f, padding.Y));

        // Stated rather than inferred, for the reason a hand-drawn panel has to state its width: ImGui's content region
        // reports the window's own right edge, which is outside the padding this chrome just applied.
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
    /// <remarks>
    /// ImGui applies its own window padding inside the frame it is no longer drawing, which would sit between the
    /// window's edge and the chrome's own padding and put every measurement out by it. A Dalamud window pushes these
    /// from <c>PreDraw</c> and releases them in <c>PostDraw</c>.
    /// </remarks>
    /// <returns>How many style variables were pushed, to pop in <c>PostDraw</c>.</returns>
    public static int PushWindowStyle() => PushWindowStyle(null);

    /// <summary>
    /// The style variables a custom window has to be begun with.
    /// </summary>
    /// <param name="style">Kept for symmetry with the chrome's own style. Opacity is applied when painting, not here.</param>
    /// <returns>How many style variables were pushed, to pop in <c>PostDraw</c>.</returns>
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
    /// <remarks>
    /// Call it with the bounds of whatever reads as the window's handle: a title strip, a masthead, the whole top edge.
    /// The drag is driven from the pointer's movement while the button is held rather than from where it started,
    /// because the window moves under the pointer as it goes and an absolute position would fight itself.<br/>
    /// Held on to across frames by id, so a drag that leaves the rectangle keeps moving the window instead of dropping
    /// it the moment the pointer outruns the handle.
    /// </remarks>
    /// <param name="min">The top left of the handle, in screen space.</param>
    /// <param name="max">The bottom right of the handle.</param>
    /// <returns>True while the window is being dragged.</returns>
    public static bool DragFrom(Vector2 min, Vector2 max)
    {
        NoireUI.EnsureFrameServices();

        var current = ImGuiP.GetCurrentWindow();

        // Refused unless ImGui has been told not to move this window. This is the replacement for ImGui's own drag, not
        // an addition to it: with both running, a movement is applied twice in the same frame from two different
        // reference points, and the contents visibly swim and lag behind the frame as it is dragged.
        if (!current.Flags.HasFlag(ImGuiWindowFlags.NoMove))
            return false;

        var io = ImGui.GetIO();
        var window = current.ID;
        var inside = io.MousePos.X >= min.X && io.MousePos.X <= max.X && io.MousePos.Y >= min.Y && io.MousePos.Y <= max.Y;

        if (draggingWindow != 0u && draggingWindow == window)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                draggingWindow = 0u;
                return false;
            }

            if (io.MouseDelta != Vector2.Zero)
                ImGui.SetWindowPos(ImGui.GetWindowPos() + io.MouseDelta);

            return true;
        }

        // Started only from a press that lands on the handle with nothing else claiming it, so a button sitting in the
        // title strip is a button rather than a place the window happens to move from.
        if (inside && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !ImGui.IsAnyItemHovered())
        {
            draggingWindow = window;
            return true;
        }

        if (inside && !ImGui.IsAnyItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return false;
    }

    /// <summary>
    /// Draws one of the window's own chrome buttons, needing no icon font.
    /// </summary>
    /// <remarks>
    /// A bare mark floating in a corner reads as debris rather than as a control: nothing says it is clickable until
    /// the pointer is already on it. So it carries a plate that lights on hover, the mark thickens with it, and every
    /// part of both is a <see cref="ChromeButtonStyle"/> value, because a window drawing its own chrome will want its
    /// buttons to match that and not the library's taste.<br/>
    /// One call for every glyph, so a row of them cannot drift apart in size, weight or hover behaviour.
    /// </remarks>
    /// <param name="id">A unique id, so a row of buttons does not share one hit box.</param>
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

        var clicked = ImGui.InvisibleButton($"###NoireWindowChrome_{id}", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

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
    /// Draws a close button. Sugar over <see cref="ChromeButton"/>.
    /// </summary>
    /// <param name="centre">The middle of the button, in screen space.</param>
    /// <param name="size">How wide the button's hit box is, in real pixels.</param>
    /// <param name="style">How it is drawn. When <see langword="null"/>, the theme's.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public static bool CloseButton(Vector2 centre, float size, ChromeButtonStyle? style = null)
        => ChromeButton("close", centre, size, ChromeGlyph.Close, style);

    /// <summary>
    /// Paints one of the marks, from strokes rather than from an icon font.
    /// </summary>
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
                // A chevron rather than a bar, because a bar is what a window uses for "hide" and this is "collapse".
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

    /// <summary>
    /// A copy of a plate with its fills faded, for a window drawn at less than full opacity.
    /// </summary>
    /// <remarks>
    /// Copied rather than written through, because the style belongs to the caller and is usually a shared static: a
    /// window fading itself out must not fade every other thing drawn from the same style with it.
    /// </remarks>
    private static PlateStyle Faded(PlateStyle plate, float opacity)
    {
        var copy = plate.Clone();

        if (copy.Fill is { } fill)
            copy.Fill = ColorHelper.ScaleAlpha(fill, opacity);

        if (copy.FillTo is { } fillTo)
            copy.FillTo = ColorHelper.ScaleAlpha(fillTo, opacity);

        return copy;
    }

    private static readonly ChromeButtonStyle DefaultChromeStyle = new();

    /// <summary>
    /// Which window is being dragged, so a drag survives the pointer leaving the handle.
    /// </summary>
    /// <remarks>
    /// Held by window id rather than as a flag, because two custom windows can be on screen at once and only one of
    /// them is being dragged.
    /// </remarks>
    private static uint draggingWindow;

    private static readonly WindowChromeStyle DefaultStyle = new();
}
