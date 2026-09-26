using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Layout pieces ImGui does not provide: a draggable splitter, a collapsible section that remembers its state, and
/// a row that wraps.
/// </summary>
public static partial class NoireLayout
{
    private static readonly HashSet<string> PersistRefusals = new();

    #region Splitter

    // Large enough that a pane cannot be dragged shut and lost.
    private const float DefaultSplitterMinimum = 40f;

    private const string GrabKey = "grab";

    /// <summary>Draws a draggable divider that resizes the pane before it, clamping the size every frame.</summary>
    /// <param name="id">A unique id for the splitter.</param>
    /// <param name="size">The size of the pane before the splitter in real pixels, read and written.</param>
    /// <param name="minSize">The smallest the pane may become, or zero for a scaled default.</param>
    /// <param name="maxSize">The largest the pane may become, or zero for the space available.</param>
    /// <param name="thickness">The grab thickness, or zero for a scaled default.</param>
    /// <param name="vertical">Whether the divider is a vertical bar resizing the pane to its left.</param>
    /// <param name="length">How long the divider is across the panes it separates, or zero for the space remaining
    /// in the current region.</param>
    /// <returns>True while the splitter is being dragged.</returns>
    public static bool Splitter(string id, ref float size, float minSize = 0f, float maxSize = 0f, float thickness = 0f, bool vertical = true, float length = 0f)
    {
        Shorthand.MinSize = minSize;
        Shorthand.MaxSize = maxSize;
        Shorthand.Thickness = thickness;
        Shorthand.Vertical = vertical;
        Shorthand.Length = length;

        return Splitter(id, ref size, Shorthand);
    }

    private static readonly SplitterOptions Shorthand = new();

    /// <summary>
    /// Draws a draggable divider between two panes, clamping the size every frame.
    /// </summary>
    /// <param name="id">A unique id for the splitter.</param>
    /// <param name="size">The size of the pane before the splitter in real pixels, read and written.</param>
    /// <param name="options">How it behaves and looks.</param>
    /// <returns>True while the splitter is being dragged.</returns>
    public static bool Splitter(string id, ref float size, SplitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);

        var theme = NoireTheme.Current;
        var minSize = options.MinSize > 0f ? options.MinSize : NoireUI.Scaled(DefaultSplitterMinimum);
        var thickness = options.Thickness > 0f
            ? options.Thickness
            : MathF.Max(NoireUI.Scaled(4f), theme.ResolveItemSpacing().X);

        var available = ImGui.GetContentRegionAvail();
        var span = options.Length > 0f ? options.Length : options.Vertical ? available.Y : available.X;

        ImGui.InvisibleButton(id, options.Vertical
            ? new Vector2(thickness, MathF.Max(1f, span))
            : new Vector2(MathF.Max(1f, span), thickness));

        var hovered = ImGui.IsItemHovered();
        var dragging = ImGui.IsItemActive();

        if ((hovered || dragging) && options.ShowResizeCursor)
            ImGui.SetMouseCursor(options.Vertical ? ImGuiMouseCursor.ResizeEw : ImGuiMouseCursor.ResizeNs);

        var pointer = options.Vertical ? ImGui.GetMousePos().X : ImGui.GetMousePos().Y;

        if (ImGui.IsItemActivated())
            UiFrameState.Set(id, GrabKey, pointer - size);

        var upper = options.MaxSize > 0f ? options.MaxSize : MathF.Max(minSize, size);

        if (dragging)
            size = ResolveSize(pointer, UiFrameState.Get(id, GrabKey, pointer - size), minSize, upper);

        size = Math.Clamp(size, minSize, upper);

        var color = dragging
            ? options.ActiveColor ?? theme.Resolve(ThemeColor.Accent)
            : hovered
                ? options.HoveredColor ?? theme.Hover(theme.Resolve(ThemeColor.Border))
                : options.Color ?? theme.Muted(theme.Resolve(ThemeColor.Border));

        using var draw = UiDraw.BeginMethod();

        var args = new UiSplitterDraw(
            draw.List,
            ImGui.GetItemRectMin(),
            ImGui.GetItemRectMax(),
            options.Vertical,
            hovered,
            dragging,
            color,
            NoireUI.Scaled(options.LineWidth));

        if (options.CustomDraw is { } custom)
            custom(args);
        else
            args.DrawLine();

        return dragging;
    }

    // From the pointer's absolute position. A clamped delta must not accumulate into drift.
    internal static float ResolveSize(float pointer, float grabOffset, float minSize, float maxSize)
        => Math.Clamp(pointer - grabOffset, minSize, MathF.Max(minSize, maxSize));

    #endregion

    #region Collapsible

    /// <summary>
    /// Draws a section that folds away, with an optional memory of whether it was open.
    /// </summary>
    /// <param name="id">A unique id for the section, also the state key when
    /// <see cref="CollapsibleOptions.Persist"/> is set.</param>
    /// <param name="label">The heading.</param>
    /// <param name="body">The drawing to fold away.</param>
    /// <param name="options">How the section behaves and looks, or null for an open, unpersisted section.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Collapsible(string id, string label, Action body, CollapsibleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Collapsible(id, label, body, static b => b(), options);
    }

    /// <summary>
    /// Draws a section that folds away, passing state into the body without a closure.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">A unique id for the section, also the state key when
    /// <see cref="CollapsibleOptions.Persist"/> is set.</param>
    /// <param name="label">The heading.</param>
    /// <param name="state">The value passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to fold away.</param>
    /// <param name="options">How the section behaves and looks, or null for an open, unpersisted section.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the arguments is <see langword="null"/>.</exception>
    public static void Collapsible<TState>(string id, string label, TState state, Action<TState> body, CollapsibleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(body);

        NoireUI.EnsureFrameServices();

        options ??= DefaultCollapsibleOptions;

        var theme = NoireTheme.Current;
        var persistKey = ResolvePersistKey(id, options.Persist);
        var open = persistKey != null
            ? NoireUiState.Get(persistKey, options.DefaultOpen)
            : UiFrameState.Get(id, "open", options.DefaultOpen);

        var spacing = theme.ResolveItemSpacing();
        var padding = options.HeaderPadding.HasValue ? NoireUI.Scaled(options.HeaderPadding.Value) : theme.ResolveFramePadding();
        var lineHeight = ImGui.GetTextLineHeight();
        var arrowWidth = lineHeight * 0.8f;
        var available = ImGui.GetContentRegionAvail().X;

        // Extras need room reserved, or the header button takes the whole row.
        var extrasWidth = options.HeaderExtras == null
            ? 0f
            : MathF.Max(1f, options.HeaderExtrasWidth ?? available * 0.25f);

        var headerWidth = extrasWidth > 0f
            ? MathF.Max(arrowWidth + padding.X * 2f, available - extrasWidth - spacing.X)
            : available;

        if (ImGui.InvisibleButton(
                UiIds.Join(string.Empty, id, "##NoireCollapsibleHeader"),
                new Vector2(MathF.Max(1f, headerWidth), lineHeight + padding.Y * 2f)))
        {
            open = !open;

            if (persistKey != null)
                NoireUiState.Set(persistKey, open);
            else
                UiFrameState.Set(id, "open", open);
        }

        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        const float defaultHeaderAlpha = 0.30f;
        var background = options.HeaderBackground ?? ColorHelper.WithAlpha(theme.Resolve(ThemeColor.Control), defaultHeaderAlpha);
        var fill = hovered ? options.HeaderHoveredBackground ?? theme.Hover(background) : background;

        var headerColor = options.HeaderColor
            ?? (options.Danger ? theme.Resolve(ThemeColor.Danger) : theme.Resolve(ThemeColor.Text));

        if (hovered && fill.W <= 0f)
            headerColor = theme.Hover(headerColor);

        var turn = NoireUI.ReducedMotion
            ? (open ? 1f : 0f)
            : NoireAnim.Ease(id, "collapse", open ? 1f : 0f, options.AnimationDuration);

        using var draw = UiDraw.BeginMethod();

        var drawList = draw.List;

        if (!drawList.IsNull)
        {
            if (fill.W > 0f)
            {
                var rounding = options.HeaderRounding.HasValue ? NoireUI.Scaled(options.HeaderRounding.Value) : theme.ResolveRounding();
                drawList.AddRectFilled(min, max, ColorHelper.Vector4ToUint(fill), rounding);
            }

            var contentLeft = min.X + padding.X;

            DrawCaret(drawList, new Vector2(contentLeft + arrowWidth * 0.5f, (min.Y + max.Y) * 0.5f), arrowWidth * 0.34f, turn, headerColor);

            var textSize = NoireText.CalcSize(label);
            drawList.AddText(
                new Vector2(contentLeft + arrowWidth + spacing.X * 0.5f, (min.Y + max.Y) * 0.5f - textSize.Y * 0.5f),
                ColorHelper.Vector4ToUint(headerColor),
                label);
        }

        if (options.HeaderExtras != null)
        {
            ImGui.SameLine(0f, spacing.X);
            Group(options.HeaderExtras, static b => b());
        }

        if (options.Separator)
            ImGui.Separator();

        if (!open)
            return;

        if (!string.IsNullOrEmpty(options.Description))
        {
            using (UiPush.Color(ImGuiCol.Text, theme.Resolve(ThemeColor.TextMuted)))
                WrapText(ImGui.GetContentRegionAvail().X, options.Description, static text => ImGui.TextUnformatted(text));

            ImGui.Spacing();
        }

        Indent(options.Indent, state, body);
    }

    #endregion

    #region Flow

    /// <summary>
    /// Lays items out left to right, wrapping to a new line when the next one will not fit.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items to lay out.</param>
    /// <param name="measure">The size an item will occupy, of which only the width is used.</param>
    /// <param name="draw">The drawing for one item.</param>
    /// <param name="spacing">The gap between items in pixels, or a negative value for the theme item spacing.</param>
    /// <param name="width">How wide the row may grow from where it starts, or zero to resolve it. See
    /// <see cref="FlowItem"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    public static void Flow<T>(IReadOnlyList<T> items, Func<T, Vector2> measure, Action<T> draw, float spacing = -1f, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(draw);

        var gap = spacing >= 0f ? spacing : NoireTheme.Current.ResolveItemSpacing().X;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            FlowItem(measure(item).X, index == 0, gap, width);
            draw(item);
        }
    }

    /// <summary>Places the next item of a wrapping row. Call it right before drawing each item.</summary>
    /// <param name="itemWidth">The width of the item about to be drawn.</param>
    /// <param name="first">Whether this is the row's first item.</param>
    /// <param name="spacing">The gap between items in pixels. Negative for the theme item spacing.</param>
    /// <param name="width">How wide the row may grow. Zero to resolve it.</param>
    /// <returns>True when the item was moved to a new line.</returns>
    public static bool FlowItem(float itemWidth, bool first, float spacing = -1f, float width = 0f)
    {
        if (first)
            return false;

        var gap = spacing >= 0f ? spacing : NoireTheme.Current.ResolveItemSpacing().X;
        var rightEdge = ResolveRowRightEdge(width);

        if (ImGui.GetItemRectMax().X + gap + itemWidth <= rightEdge)
        {
            ImGui.SameLine(0f, gap);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Measures how wide content may be from the current cursor.
    /// </summary>
    /// <returns>The width available in real pixels.</returns>
    public static float ContentWidth()
        => MathF.Max(0f, ResolveRowRightEdge(0f) - ImGui.GetCursorScreenPos().X);

    // Explicit width, then the text wrap position, then the window's content edge.
    private static float ResolveRowRightEdge(float width)
    {
        var rowLeft = ImGui.GetCursorScreenPos().X;

        if (width > 0f)
            return rowLeft + width;

        if (TryGetWrapRightEdge(out var wrapRightEdge))
            return wrapRightEdge;

        return rowLeft + ImGui.GetContentRegionAvail().X;
    }

    private static bool TryGetWrapRightEdge(out float rightEdge)
    {
        rightEdge = 0f;

        // Also sees a wrap position when ImGui runs headless.
        if (!UiDraw.Available)
            return false;

        var window = ImGuiP.GetCurrentWindow();
        var wrapPos = window.DC.TextWrapPos;

        if (wrapPos <= 0f)
            return false;

        rightEdge = window.Pos.X - window.Scroll.X + wrapPos;
        return true;
    }

    /// <summary>
    /// Measures the wrap width text would be drawn against right now.
    /// </summary>
    /// <returns>
    /// The wrap width in real pixels from the current cursor, or <see langword="null"/> when no wrap position is pushed.
    /// </returns>
    public static float? ActiveWrapWidth()
        => TryGetWrapRightEdge(out var rightEdge) ? MathF.Max(0f, rightEdge - ImGui.GetCursorScreenPos().X) : null;

    #endregion

    private static readonly CollapsibleOptions DefaultCollapsibleOptions = new();

    // turn: 0 points right, 1 points down.
    private static void DrawCaret(ImDrawListPtr drawList, Vector2 center, float radius, float turn, Vector4 color)
    {
        var angle = turn * MathF.PI * 0.5f;
        var packed = ColorHelper.Vector4ToUint(color);

        Vector2 Point(float offsetAngle)
        {
            var a = angle + offsetAngle;
            return center + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
        }

        drawList.AddTriangleFilled(Point(0f), Point(MathF.Tau / 3f), Point(-MathF.Tau / 3f), packed);
    }

    // A blank id would add a state entry per session and never restore one.
    private static string? ResolvePersistKey(string id, bool persist)
    {
        if (!persist)
            return null;

        // Resolved every frame. The state file is keyed on the exact bytes.
        if (!string.IsNullOrWhiteSpace(id))
            return UiIds.Join("Collapsible.", id, ".open");

        lock (PersistRefusals)
        {
            if (PersistRefusals.Add("<blank>"))
            {
                NoireLogger.LogWarning(
                    "A collapsible section asked to persist its open state with a blank id. Its state is not saved. " +
                    "Give the section a stable id to persist it.",
                    "[NoireLayout] ");
            }
        }

        return null;
    }
}
