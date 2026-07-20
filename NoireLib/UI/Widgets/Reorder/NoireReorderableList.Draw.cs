using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using NoireLib.Helpers;
using NoireLib.HotkeyManager;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The drawing half of the reorderable list.
/// </summary>
public sealed partial class NoireReorderableList<T>
{
    private bool changedThisFrame;
    private int draggingIndex = -1;
    private int focusedIndex = -1;
    private int pendingRemoval = -1;
    private int pendingDuplicate = -1;
    private int pendingMoveFrom = -1;
    private int pendingMoveTo = -1;
    private int dropTarget = -1;

    private float listTop;
    private float listLeft;
    private float rowStep;

    /// <summary>
    /// Draws the list.
    /// </summary>
    /// <returns>True on the frame the list changes.</returns>
    public bool Draw()
    {
        NoireUI.EnsureFrameServices();
        changedThisFrame = false;
        pendingRemoval = -1;
        pendingDuplicate = -1;
        pendingMoveFrom = -1;
        pendingMoveTo = -1;
        dropTarget = -1;

        if (items.Count == 0)
        {
            draggingIndex = -1;

            // A list with no rows has no row to be working in, so it holds neither the focus nor the keys. The
            // watchdog would arrive at the same answer a frame later; saying it here means the keys are given back on
            // the frame the last row goes rather than after it.
            focusedIndex = -1;
            ApplyInputBlocking(false);

            NoireText.Muted(EmptyText, TextSize.Caption);
            return false;
        }

        var width = NoireLayout.ContentWidth();
        var height = RowHeight > 0f ? RowHeight : ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        var origin = ImGui.GetCursorScreenPos();
        listLeft = origin.X;
        listTop = origin.Y;
        rowStep = height + spacing;

        // Worked out from the pointer rather than from which row reports itself hovered. While a drag is running the
        // dragged row is the active item, and ImGui gives no other item the hover, so a hover-driven target only ever
        // resolves in whichever direction happened to keep the pointer inside the row it started on.
        if (draggingIndex >= 0)
            dropTarget = ResolveDropTarget(ImGui.GetIO().MousePos.Y);

        for (var index = 0; index < items.Count; index++)
            DrawRow(index, width, height);

        // Applied after the loop, since removing, inserting or reordering mid-draw shifts every row after it onto an
        // index that is still to be drawn. The keyboard move belongs here for the same reason the buttons do, and did
        // not, which is why the arrow keys appeared to do nothing.
        if (pendingDuplicate >= 0)
            DuplicateAt(pendingDuplicate);
        else if (pendingRemoval >= 0)
            RemoveAt(pendingRemoval);
        else if (pendingMoveFrom >= 0 && Move(pendingMoveFrom, pendingMoveTo))
            focusedIndex = Math.Clamp(pendingMoveTo, 0, items.Count - 1);

        // Clicking away drops the focus, so a list nobody is working in does not keep the arrow keys to itself. Tested
        // against the list's own bounds rather than against whether anything was hovered: clicking another control is
        // the ordinary way to stop working in a list, and hovering test would count that as still being in it, leaving
        // the keys held while the user is plainly somewhere else.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ListBounds(width, height).Contains(ImGui.GetIO().MousePos))
            focusedIndex = -1;

        ClaimKeyboardIfFocused();

        if (draggingIndex >= 0 && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            // Committed when the button comes up rather than as the pointer passes each row, so the list holds still
            // under the cursor while the drag is in flight and only the marker moves.
            if (dropTarget >= 0 && dropTarget != draggingIndex)
                Move(draggingIndex, dropTarget);

            // The focus follows the row that was dropped, not the position it used to occupy: the row the user is
            // working on is the one they just moved, and the keyboard path carries straight on from there.
            if (dropTarget >= 0)
                focusedIndex = Math.Clamp(dropTarget, 0, items.Count - 1);

            draggingIndex = -1;
            dropTarget = -1;
        }

        return changedThisFrame;
    }

    /// <summary>
    /// The area the rows occupy on screen, which is what counts as being inside the list.
    /// </summary>
    /// <param name="width">The width the rows were drawn at.</param>
    /// <param name="height">The height of one row.</param>
    /// <returns>The bounds in screen pixels.</returns>
    private UiRect ListBounds(float width, float height)
    {
        // The trailing spacing belongs to whatever comes next, so the last row's own height closes the list off.
        var total = rowStep <= 0f ? height : ((items.Count - 1) * rowStep) + height;

        return new UiRect(new Vector2(listLeft, listTop), new Vector2(width, MathF.Max(height, total)));
    }

    /// <summary>
    /// Which position the pointer is currently over.
    /// </summary>
    private int ResolveDropTarget(float pointerY)
        => rowStep <= 0f ? draggingIndex : ResolveSlot(pointerY, listTop, rowStep, items.Count);

    /// <summary>
    /// Draws one row: the grip, the content, the buttons, and the marker showing where a dragged row would land.
    /// </summary>
    private void DrawRow(int index, float width, float height)
    {
        var theme = NoireTheme.Current;
        var item = items[index];
        var origin = ImGui.GetCursorScreenPos();
        var isDragging = draggingIndex == index;
        var isFocused = focusedIndex == index;

        var gripWidth = NoireUI.Scaled(18f);
        var buttonWidth = ButtonColumnWidth(height);
        var size = new Vector2(width, height);

        if (ImGui.InvisibleButton($"###NoireReorderRow_{Id}_{index}", size))
            focusedIndex = index;

        // Without this the row's own hit box, submitted first and covering everything, keeps the click and the delete
        // and duplicate buttons drawn over it never see a press.
        ImGui.SetItemAllowOverlap();

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (active && draggingIndex < 0 && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && StartedOnAHandle(origin, gripWidth))
        {
            draggingIndex = index;
            focusedIndex = index;
            dropTarget = index;
        }

        PaintRow(origin, size, isDragging, hovered && draggingIndex < 0, isFocused, theme);
        PaintGrip(origin, size, gripWidth, isDragging || hovered, theme);

        // The content is overlaid on the row rather than drawn into it, because the row is one invisible button: that
        // is what makes the whole row draggable and hoverable rather than only the parts nothing else covers.
        var contentWidth = MathF.Max(0f, width - gripWidth - buttonWidth);

        ImGui.SetCursorScreenPos(new Vector2(
            origin.X + gripWidth,
            origin.Y + (height * 0.5f) - NoireText.CenterOffset()));

        DrawContent(item, index, isDragging, new Vector2(contentWidth, height));

        if (buttonWidth > 0f)
            DrawRowButtons(index, origin, size, buttonWidth, height);

        if (draggingIndex >= 0 && dropTarget == index)
            PaintDropMarker(origin, size, theme);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        if (hovered && draggingIndex < 0)
            ImGui.SetMouseCursor(DragAnywhere ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.Arrow);

        if (isFocused && AllowKeyboard)
            HandleKeyboard(index);
    }

    /// <summary>
    /// Whether the drag began somewhere that is allowed to start one.
    /// </summary>
    private bool StartedOnAHandle(Vector2 origin, float gripWidth)
    {
        if (DragAnywhere)
            return true;

        // Measured from where the button went down, not from where the pointer is now: a drag that began on the grip
        // stays a drag however far it travels.
        var pressedAt = ImGui.GetIO().MouseClickedPos[0].X;
        return pressedAt >= origin.X && pressedAt <= origin.X + gripWidth;
    }

    /// <summary>
    /// Draws the row's content, through the renderer when there is one.
    /// </summary>
    private void DrawContent(T item, int index, bool isDragging, Vector2 size)
    {
        var label = LabelOf(item);

        if (Renderer == null)
        {
            ImGui.PushTextWrapPos(-1f);
            NoireText.Draw(label);
            ImGui.PopTextWrapPos();
            return;
        }

        try
        {
            Renderer(new UiReorderRowDraw<T>(this, item, index, label, isDragging, size));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"The renderer of list '{Id}' threw an exception.", nameof(NoireReorderableList<T>));
        }
    }

    /// <summary>
    /// Draws the delete and duplicate buttons at the right end of a row.
    /// </summary>
    private void DrawRowButtons(int index, Vector2 origin, Vector2 size, float buttonWidth, float height)
    {
        var x = origin.X + size.X - buttonWidth;

        if (AllowDuplicate)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));

            if (SmallGlyphButton($"###NoireReorderCopy_{Id}_{index}", height, GlyphShape.Duplicate))
                pendingDuplicate = index;

            x += height;
        }

        if (!AllowDelete)
            return;

        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));

        if (SmallGlyphButton($"###NoireReorderDelete_{Id}_{index}", height, GlyphShape.Cross))
            pendingRemoval = index;
    }

    private float ButtonColumnWidth(float height)
    {
        var count = (AllowDelete ? 1 : 0) + (AllowDuplicate ? 1 : 0);
        return count * height;
    }

    /// <summary>
    /// Queues a move of the focused row from the reorder keys.
    /// </summary>
    /// <remarks>
    /// The keys are read through <see cref="KeybindsHelper.IsBindingHeld"/> rather than through ImGui, and that is the
    /// whole reason this works. ImGui only receives key events the host forwards, and the host forwards them only when
    /// ImGui says it wants the keyboard, which with no text field active it does not: the game takes the arrow keys
    /// and the widget is never told anything happened. Reading the key state directly is the same route
    /// <see cref="NoireHotkeyManager"/> takes, and it is why a hotkey works anywhere.<br/>
    /// The move is queued rather than made here, because this runs inside the loop drawing the rows and reordering the
    /// list under that loop moves every later row onto an index that has not been drawn yet.
    /// </remarks>
    private void HandleKeyboard(int index)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        if (Pressed(ResolvedMoveUpBinding, ref upHeldSince))
        {
            pendingMoveFrom = index;
            pendingMoveTo = index - 1;
        }
        else if (Pressed(ResolvedMoveDownBinding, ref downHeldSince))
        {
            pendingMoveFrom = index;
            pendingMoveTo = index + 1;
        }
    }

    /// <summary>
    /// Whether a binding counts as pressed this frame, including a repeat while it is held.
    /// </summary>
    /// <remarks>
    /// The key state read this way is a level, not an edge: it says the key is down, not that it has just gone down.
    /// So the edge is derived here, and a hold repeats on the same delay and rate ImGui uses for a held key, which is
    /// what makes walking a row several places feel like one gesture rather than several presses.
    /// </remarks>
    private static bool Pressed(HotkeyBinding binding, ref double heldSince)
    {
        if (!IsBound(binding) || !KeybindsHelper.IsBindingHeld(binding))
        {
            heldSince = 0d;
            return false;
        }

        var now = ImGui.GetTime();

        if (heldSince <= 0d)
        {
            heldSince = now;
            return true;
        }

        var held = now - heldSince;

        if (held < RepeatDelaySeconds)
            return false;

        // Counted from the moment the repeat started, so the rate does not drift with the frame rate.
        var elapsed = held - RepeatDelaySeconds;
        var ticks = (int)(elapsed / RepeatRateSeconds);
        var previous = (int)((elapsed - ImGui.GetIO().DeltaTime) / RepeatRateSeconds);

        return ticks > previous;
    }

    /// <summary>
    /// Whether a binding names anything at all, so an unbound or disabled hotkey is simply off rather than a key
    /// combination of no keys that reads as held.
    /// </summary>
    private static bool IsBound(HotkeyBinding binding)
        => binding.VkCode != 0 || binding.GamepadButton.HasValue;

    /// <summary>How long a reorder key must be held before it starts repeating.</summary>
    private const double RepeatDelaySeconds = 0.35d;

    /// <summary>How often a held reorder key repeats.</summary>
    private const double RepeatRateSeconds = 0.12d;

    private double upHeldSince;
    private double downHeldSince;

    /// <summary>
    /// Asks the host to keep the reorder keys from the game while a row is focused.
    /// </summary>
    /// <remarks>
    /// Not what makes the keys arrive: they are read from the key state directly and arrive either way. This is so
    /// that pressing the shortcut does not *also* do whatever the game does with that key, which for the default
    /// arrows is turn the character.<br/>
    /// Claimed only while a row is focused in a focused window, and the focus is dropped by clicking anywhere else,
    /// because it is holding those keys away from the game.
    /// </remarks>
    private void ClaimKeyboardIfFocused()
    {
        var live = AllowKeyboard
            && focusedIndex >= 0
            && focusedIndex < items.Count
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        // Told either way, so the blocking comes back off the moment the shortcut stops being usable rather than
        // waiting for something else to happen.
        ApplyInputBlocking(live);

        if (live)
            ImGui.SetNextFrameWantCaptureKeyboard(true);
    }

    #region Painting

    private void PaintRow(Vector2 origin, Vector2 size, bool dragging, bool hovered, bool focused, NoireTheme theme)
    {
        var accent = theme.Resolve(ThemeColor.Accent);
        var rounding = theme.ResolveRounding();

        var fill = hovered
            ? ColorHelper.ScaleAlpha(accent, 0.14f)
            : theme.Resolve(ThemeColor.SurfaceSunken);

        // The row being dragged is drawn as the hole it left rather than as itself, so the list reads as the order it
        // will be in once the button comes up instead of the order it is in now.
        if (dragging)
            fill = ColorHelper.ScaleAlpha(fill, 0.35f);

        NoireShapes.Rect(origin, origin + size, fill, CornerShape.Rounded, rounding);

        if (focused && !dragging)
            NoireShapes.RectOutline(origin, origin + size, ColorHelper.ScaleAlpha(accent, 0.5f), 1f, CornerShape.Rounded, rounding);
    }

    /// <summary>
    /// Draws the row that is following the pointer, and the line showing where it will land.
    /// </summary>
    private void PaintDropMarker(Vector2 origin, Vector2 size, NoireTheme theme)
    {
        var accent = theme.Resolve(ThemeColor.Accent);
        var rounding = theme.ResolveRounding();

        NoireShapes.Rect(origin, origin + size, ColorHelper.ScaleAlpha(accent, 0.22f), CornerShape.Rounded, rounding);
        NoireShapes.RectOutline(origin, origin + size, ColorHelper.ScaleAlpha(accent, 0.9f), NoireUI.Scaled(1.5f), CornerShape.Rounded, rounding);

        if (draggingIndex < 0 || draggingIndex >= items.Count)
            return;

        // The ghost follows the pointer rather than sitting in the gap, because the gap already says where the row
        // lands and the ghost is what says which row is moving.
        var ghostSize = new Vector2(size.X * 0.5f, size.Y);
        var ghostOrigin = ImGui.GetIO().MousePos + new Vector2(NoireUI.Scaled(12f), -ghostSize.Y * 0.5f);

        NoireShapes.On(ImGui.GetForegroundDrawList(), () =>
        {
            NoireShapes.Rect(ghostOrigin, ghostOrigin + ghostSize, ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.Surface), 0.92f), CornerShape.Rounded, rounding);
            NoireShapes.RectOutline(ghostOrigin, ghostOrigin + ghostSize, ColorHelper.ScaleAlpha(accent, 0.8f), 1f, CornerShape.Rounded, rounding);
        });

        var label = LabelOf(items[draggingIndex]);

        ImGui.GetForegroundDrawList().AddText(
            ghostOrigin + new Vector2(NoireUI.Scaled(8f), (ghostSize.Y * 0.5f) - (ImGui.GetTextLineHeight() * 0.5f)),
            ColorHelper.Vector4ToUint(theme.Resolve(ThemeColor.Text)),
            label);
    }

    /// <summary>
    /// Draws the handle: three short bars, which is what a grip looks like everywhere and needs no icon font.
    /// </summary>
    private static void PaintGrip(Vector2 origin, Vector2 size, float gripWidth, bool lit, NoireTheme theme)
    {
        var color = ColorHelper.ScaleAlpha(theme.Resolve(ThemeColor.TextMuted), lit ? 0.9f : 0.45f);
        var barWidth = NoireUI.Scaled(8f);
        var thickness = MathF.Max(1f, NoireUI.Scaled(1.5f));
        var centre = origin + new Vector2(gripWidth * 0.5f, size.Y * 0.5f);
        var spacing = NoireUI.Scaled(4f);

        for (var i = -1; i <= 1; i++)
        {
            var y = centre.Y + (i * spacing);

            Span<Vector2> bar =
            [
                new(centre.X - (barWidth * 0.5f), y),
                new(centre.X + (barWidth * 0.5f), y),
            ];

            NoireShapes.Stroke(bar, color, thickness, closed: false);
        }
    }

    /// <summary>
    /// The two marks the row buttons draw, so neither needs an icon font.
    /// </summary>
    private enum GlyphShape
    {
        Cross,
        Duplicate,
    }

    private static bool SmallGlyphButton(string id, float side, GlyphShape shape)
    {
        var theme = NoireTheme.Current;
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(side, side);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var centre = origin + (size * 0.5f);

        var color = ColorHelper.ScaleAlpha(
            theme.Resolve(shape == GlyphShape.Cross && hovered ? ThemeColor.Danger : ThemeColor.Text),
            hovered ? 0.95f : 0.5f);

        var reach = side * 0.18f;
        var thickness = MathF.Max(1f, NoireUI.Scaled(1.4f));

        if (shape == GlyphShape.Cross)
        {
            Span<Vector2> down = [centre - new Vector2(reach, reach), centre + new Vector2(reach, reach)];
            Span<Vector2> up = [centre + new Vector2(-reach, reach), centre + new Vector2(reach, -reach)];

            NoireShapes.Stroke(down, color, thickness, closed: false);
            NoireShapes.Stroke(up, color, thickness, closed: false);
        }
        else
        {
            var offset = reach * 0.45f;

            NoireShapes.RectOutline(
                centre - new Vector2(reach, reach) + new Vector2(offset, offset),
                centre + new Vector2(reach, reach) + new Vector2(offset, offset),
                color, thickness, CornerShape.Rounded, NoireUI.Scaled(2f));

            NoireShapes.RectOutline(
                centre - new Vector2(reach, reach) - new Vector2(offset, offset),
                centre + new Vector2(reach, reach) - new Vector2(offset, offset),
                color, thickness, CornerShape.Rounded, NoireUI.Scaled(2f));
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }

    #endregion
}
