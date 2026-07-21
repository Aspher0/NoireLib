using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The layout pieces ImGui leaves to you: a draggable splitter, a collapsible section that remembers itself, and a row
/// that wraps.
/// </summary>
public static partial class NoireLayout
{
    private static readonly HashSet<string> PersistRefusals = new();

    #region Splitter

    /// <summary>
    /// The smallest a splitter lets a pane become when the caller names no minimum, at 100%. Small enough not to fight
    /// a deliberate layout, large enough that a pane cannot be dragged shut and then never found again.
    /// </summary>
    private const float DefaultSplitterMinimum = 40f;

    /// <summary>Where a splitter's grab offset is kept for the length of a drag.</summary>
    private const string GrabKey = "grab";

    /// <summary>
    /// Draws a draggable divider that resizes the pane before it.<br/>
    /// ImGui ships no splitter at all, so every plugin that wants a resizable sidebar writes the same invisible button,
    /// mouse-delta and cursor dance. This is that, done once.
    /// </summary>
    /// <remarks>
    /// The value is clamped every frame, not only while dragging, so a size restored from a config written on a wider
    /// screen is corrected on the first frame rather than leaving a pane off the edge.
    /// </remarks>
    /// <param name="id">A unique id for the splitter.</param>
    /// <param name="size">The size of the pane before the splitter, read and written. In real pixels: it is driven by
    /// the mouse, so every measurement here shares that space rather than being written at 100%.</param>
    /// <param name="minSize">The smallest the pane may become. Zero uses a usable default, which does scale.</param>
    /// <param name="maxSize">The largest the pane may become. Zero leaves it bounded only by the space
    /// available.</param>
    /// <param name="thickness">The grab thickness. Zero uses a comfortable default, which does scale.</param>
    /// <param name="vertical">Whether the divider is a vertical bar, resizing the pane to its left. Set it to
    /// <see langword="false"/> for a horizontal bar resizing the pane above it.</param>
    /// <param name="length">How long the divider is, across the panes it separates. Zero fills the space remaining in
    /// the current region, which is only what you want when the panes do too: give it the pane height (or width) when
    /// they are a fixed size, or the divider will run past them.</param>
    /// <returns>True while the splitter is being dragged.</returns>
    public static bool Splitter(string id, ref float size, float minSize = 0f, float maxSize = 0f, float thickness = 0f, bool vertical = true, float length = 0f)
        => Splitter(id, ref size, new SplitterOptions
        {
            MinSize = minSize,
            MaxSize = maxSize,
            Thickness = thickness,
            Vertical = vertical,
            Length = length,
        });

    /// <summary>
    /// A draggable divider between two panes, with the look opened all the way up.
    /// </summary>
    /// <remarks>
    /// The value is clamped every frame, not only while dragging, so a size restored from a config written on a wider
    /// screen is corrected on the first frame rather than leaving a pane off the edge.<br/>
    /// A design that already draws its own divider wants the handle without the line: give
    /// <see cref="SplitterOptions.CustomDraw"/> a hook that draws nothing, and the pane becomes resizable without
    /// anything about it changing.
    /// </remarks>
    /// <param name="id">A unique id for the splitter.</param>
    /// <param name="size">The size of the pane before the splitter, read and written. In real pixels: it is driven by
    /// the mouse, so every measurement here shares that space rather than being written at 100%.</param>
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

        // The distance from the pointer to the edge it is holding, taken once when the drag starts. Everything after
        // that is read from where the pointer IS.
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

    /// <summary>
    /// Where a splitter's pane edge belongs for a pointer at the given position, clamped to its bounds.
    /// </summary>
    /// <remarks>
    /// Read from where the pointer <b>is</b>, never from how far it moved. A mouse delta that the clamp throws away is
    /// a delta the size never received, so accumulating deltas leaves the divider ahead of the pointer by everything
    /// that was discarded, for the rest of the drag: push past the minimum, come back, and the divider is already
    /// moving while the cursor is still nowhere near it. Resolving against the position makes overshooting free.<br/>
    /// Pure and separate so the arithmetic can be tested, since a drag is the one thing that cannot demonstrate it.
    /// </remarks>
    /// <param name="pointer">The pointer's position along the axis being resized, in screen coordinates.</param>
    /// <param name="grabOffset">The distance from the pointer to the pane edge, taken when the drag started.</param>
    /// <param name="minSize">The smallest the pane may be.</param>
    /// <param name="maxSize">The largest the pane may be.</param>
    /// <returns>The pane size.</returns>
    internal static float ResolveSize(float pointer, float grabOffset, float minSize, float maxSize)
        => Math.Clamp(pointer - grabOffset, minSize, MathF.Max(minSize, maxSize));

    #endregion

    #region Collapsible

    /// <summary>
    /// A section that folds away, with an optional memory of whether it was open.<br/>
    /// The body takes the usual form: it is simply not called while the section is closed, so nothing inside a folded
    /// section costs anything and there is no end call to forget.
    /// </summary>
    /// <param name="id">A unique id for the section. Also the key its open state is stored under when
    /// <see cref="CollapsibleOptions.Persist"/> is set, so it has to be stable across sessions.</param>
    /// <param name="label">The heading.</param>
    /// <param name="body">The drawing to fold away.</param>
    /// <param name="options">How the section behaves and looks. When <see langword="null"/>, an open, unpersisted
    /// section is drawn.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Collapsible(string id, string label, Action body, CollapsibleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Collapsible(id, label, body, static b => b(), options);
    }

    /// <summary>
    /// A section that folds away, with an optional memory of whether it was open.<br/>
    /// The body takes the usual form: it is simply not called while the section is closed, so nothing inside a folded
    /// section costs anything and there is no end call to forget.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">A unique id for the section.</param>
    /// <param name="label">The heading.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to fold away.</param>
    /// <param name="options">How the section behaves and looks.</param>
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
        var lineHeight = ImGui.GetTextLineHeight();
        var arrowWidth = lineHeight * 0.8f;
        var available = ImGui.GetContentRegionAvail().X;

        // Extras with no width given still need room reserved, or the header button would take the whole row and push
        // them onto the next line.
        var extrasWidth = options.HeaderExtras == null
            ? 0f
            : MathF.Max(1f, options.HeaderExtrasWidth ?? available * 0.25f);

        var headerWidth = extrasWidth > 0f
            ? MathF.Max(arrowWidth, available - extrasWidth - spacing.X)
            : available;

        if (ImGui.InvisibleButton($"{id}##NoireCollapsibleHeader", new Vector2(MathF.Max(1f, headerWidth), lineHeight + spacing.Y)))
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

        var headerColor = options.HeaderColor
            ?? (options.Danger ? theme.Resolve(ThemeColor.Danger) : theme.Resolve(ThemeColor.Text));

        if (hovered)
            headerColor = theme.Hover(headerColor);

        var turn = NoireUI.ReducedMotion
            ? (open ? 1f : 0f)
            : NoireAnim.Ease(id, "collapse", open ? 1f : 0f, options.AnimationDuration);

        using var draw = UiDraw.BeginMethod();

        var drawList = draw.List;

        if (!drawList.IsNull)
        {
            DrawCaret(drawList, new Vector2(min.X + arrowWidth * 0.5f, (min.Y + max.Y) * 0.5f), arrowWidth * 0.34f, turn, headerColor);

            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(
                new Vector2(min.X + arrowWidth + spacing.X * 0.5f, (min.Y + max.Y) * 0.5f - textSize.Y * 0.5f),
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
            using (ImRaii.PushColor(ImGuiCol.Text, theme.Resolve(ThemeColor.TextMuted)))
                WrapText(ImGui.GetContentRegionAvail().X, options.Description, static text => ImGui.TextUnformatted(text));

            ImGui.Spacing();
        }

        Indent(options.Indent, state, body);
    }

    #endregion

    #region Flow

    /// <summary>
    /// Lays items out left to right, wrapping to a new line when the next one will not fit.<br/>
    /// This is what chips, tags and small cards need and what ImGui has no answer for: <c>SameLine</c> alone cannot
    /// know whether the item after it fits, so a hand-written row either overflows or breaks early.
    /// </summary>
    /// <remarks>
    /// The measure runs before each item is drawn, which is the only way the decision can be made in an immediate-mode
    /// pass. It only has to be close: a measurement a few pixels short wraps one item early, never off the edge.
    /// </remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items to lay out.</param>
    /// <param name="measure">Returns the size an item will occupy. Only the width is used.</param>
    /// <param name="draw">Draws one item.</param>
    /// <param name="spacing">The gap between items in pixels. A negative value uses the theme item spacing.</param>
    /// <param name="width">How wide the row may grow, measured from where it starts. Zero works it out: see
    /// <see cref="FlowItem"/> for what that means and when it cannot.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    public static void Flow<T>(IReadOnlyList<T> items, Func<T, Vector2> measure, Action<T> draw, float spacing = -1f, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(draw);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            FlowItem(measure(item).X, index == 0, spacing, width);
            draw(item);
        }
    }

    /// <summary>
    /// Places the next item of a wrapping row, either beside the previous one or at the start of a new line.<br/>
    /// Call it immediately before drawing each item when the items are not a list you can hand to
    /// <see cref="Flow{T}(IReadOnlyList{T}, Func{T, Vector2}, Action{T}, float, float)"/>.
    /// </summary>
    /// <param name="itemWidth">How wide the item about to be drawn will be.</param>
    /// <param name="first">Whether this is the first item of the row, which always starts on the current line.</param>
    /// <param name="spacing">The gap between items in pixels. A negative value uses the theme item spacing.</param>
    /// <param name="width">How wide the row may grow, measured from where it starts. Zero works it out.</param>
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
    /// How wide the content is allowed to be from where the cursor is, which is not always what
    /// <c>GetContentRegionAvail</c> answers.
    /// </summary>
    /// <remarks>
    /// This is the question every widget that defaults to "the space available" is really asking, and ImGui has no
    /// direct answer for it: the content region always reports the *window's* right edge, so a widget inside a page
    /// that centres its content in a narrower column runs straight past it.<br/>
    /// The one narrower right edge ImGui carries is the text wrap position, so a widget inside a
    /// <see cref="WrapText(float, Action)"/> or a hand-rolled <c>PushTextWrapPos</c> column stops where the prose
    /// around it stops. Failing that, the window's content edge is the honest answer.
    /// </remarks>
    /// <returns>The width available in real pixels.</returns>
    public static float ContentWidth()
        => MathF.Max(0f, ResolveRowRightEdge(0f) - ImGui.GetCursorScreenPos().X);

    /// <summary>
    /// Works out where a wrapping row has to stop.
    /// </summary>
    /// <remarks>
    /// ImGui has no concept of a right margin. Indenting moves the left edge only, and the content region keeps
    /// reporting the window's own right edge however deeply nested the drawing is, so a row inside a hand-drawn panel
    /// wraps against the window and overflows the panel.<br/>
    /// The one narrower right edge ImGui does carry is the text wrap position, which is exactly what
    /// <see cref="WrapText(float, Action)"/> sets, so a row inside one wraps where its text does. Failing that there is
    /// nothing left to infer from and the window's content edge is the honest answer, which is why the explicit
    /// <paramref name="width"/> exists: a panel that owns a width nobody else can see should say so.
    /// </remarks>
    /// <param name="width">An explicit row width, or zero to work it out.</param>
    /// <returns>The screen x coordinate the row must not cross.</returns>
    private static float ResolveRowRightEdge(float width)
    {
        // Submitting an item puts the cursor back at the start of the next line, so this is the row's left edge.
        var rowLeft = ImGui.GetCursorScreenPos().X;

        if (width > 0f)
            return rowLeft + width;

        if (NoireService.IsInitialized())
        {
            var window = ImGuiP.GetCurrentWindow();
            var wrapPos = window.DC.TextWrapPos;

            if (wrapPos > 0f)
                return window.Pos.X - window.Scroll.X + wrapPos;
        }

        return rowLeft + ImGui.GetContentRegionAvail().X;
    }

    #endregion

    private static readonly CollapsibleOptions DefaultCollapsibleOptions = new();

    /// <summary>
    /// Draws the caret of a collapsible header, turned by <paramref name="turn"/> from pointing right (0) to pointing
    /// down (1).
    /// </summary>
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

    /// <summary>
    /// Resolves the state key a collapsible section persists under, refusing an id that cannot be relied on between
    /// sessions.
    /// </summary>
    /// <remarks>
    /// An unstable key is worse than no memory at all: the state file grows an entry per session and never restores
    /// one, and the only symptom is a section that silently forgets. Refusing once, by name, is the whole point.
    /// </remarks>
    private static string? ResolvePersistKey(string id, bool persist)
    {
        if (!persist)
            return null;

        if (!string.IsNullOrWhiteSpace(id))
            return $"Collapsible.{id}.open";

        lock (PersistRefusals)
        {
            if (PersistRefusals.Add("<blank>"))
            {
                NoireLogger.LogWarning(
                    "A collapsible section asked to persist its open state but was given a blank id, so there is nothing to key it on. " +
                    "Its state is not being saved. Give the section a stable id to persist it.",
                    nameof(NoireLayout));
            }
        }

        return null;
    }
}
