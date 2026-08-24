using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// An animated stack of notifications anchored somewhere on screen, with buttons, live progress and a countdown that
/// pauses on hover.<br/>
/// Adding a toast is safe from any thread; everything else runs on the draw thread.
/// </summary>
public class NoireToastArea : NoireDrawable
{
    private const ImGuiWindowFlags ToastWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoBackground;

    private static readonly object DefaultLock = new();

    private const string CallbackFault = "A toast hook threw.";

    // The dismiss button's style, reused rather than composed per toast per frame.
    private static readonly ButtonStyle CloseStyle = new()
    {
        Tone = ButtonTone.Ghost,
        Icon = FontAwesomeIcon.Times,
    };

    private static NoireToastArea? defaultArea;

    private readonly object syncRoot = new();
    private readonly List<NoireToast> toasts = new();

    /// <summary>
    /// Creates a toast area and registers it for drawing.
    /// </summary>
    /// <param name="id">An optional unique identifier. When <see langword="null"/>, a random one is generated.</param>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public NoireToastArea(string? id = null)
        : base(id, "ToastArea")
    {
        Register();
    }

    /// <summary>
    /// The area the static <see cref="NoireToast"/> helpers put their toasts in, created the first time one is raised.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public static NoireToastArea Default
    {
        get
        {
            if (defaultArea is { IsDisposed: false })
                return defaultArea;

            lock (DefaultLock)
            {
                if (defaultArea is { IsDisposed: false })
                    return defaultArea;

                defaultArea = new NoireToastArea("Default") { AutoDraw = true };
                return defaultArea;
            }
        }
    }

    #region Configuration

    /// <summary>Where the stack sits on screen, defaulting to the bottom right corner clear of the game's own notifications.</summary>
    public UiPosition Position { get; set; } = UiPosition.AtAnchor(UiAnchor.BottomRight, new Vector2(-20f, -20f));

    /// <summary>The width of a toast before <see cref="NoireUI.Scale"/> is applied.</summary>
    public float Width { get; set; } = 340f;

    private float ScaledWidth => NoireUI.Scaled(Width);

    /// <summary>How many toasts are shown at once, the rest waiting their turn.</summary>
    public int MaxVisible { get; set; } = 4;

    /// <summary>How many toasts may wait in total before the oldest are dropped.</summary>
    public int Capacity { get; set; } = 64;

    /// <summary>Whether the newest toast appears at the top of the stack rather than the bottom.</summary>
    public bool NewestFirst { get; set; }

    /// <summary>
    /// Whether the stack is kept in front of every other window for clicks as well as drawing.
    /// </summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>The look of the toasts in this area.</summary>
    public ToastStyle Style { get; set; } = new();

    /// <summary>How many toasts have been dropped because <see cref="Capacity"/> was full.</summary>
    public int DroppedCount { get; private set; }

    /// <summary>The toasts currently in the area, newest last.</summary>
    /// <returns>A snapshot of the toasts.</returns>
    public IReadOnlyList<NoireToast> GetToasts()
    {
        lock (syncRoot)
            return toasts.ToArray();
    }

    private PooledBuffer<NoireToast> SnapshotToasts()
    {
        lock (syncRoot)
        {
            var buffer = PooledBuffer<NoireToast>.Rent(toasts.Count);
            var span = buffer.Span;

            for (var index = 0; index < toasts.Count; index++)
                span[index] = toasts[index];

            return buffer;
        }
    }

    #endregion

    #region Adding and removing

    /// <summary>Adds a toast to this area, from any thread.</summary>
    /// <param name="toast">The toast to show.</param>
    /// <returns>The toast, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="toast"/> is <see langword="null"/>.</exception>
    public NoireToast Add(NoireToast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);

        NoireUI.EnsureFrameServices();

        lock (syncRoot)
        {
            toast.Area = this;
            toasts.Add(toast);

            while (toasts.Count > Math.Max(1, Capacity))
            {
                toasts.RemoveAt(0);
                DroppedCount++;
            }
        }

        return toast;
    }

    /// <summary>Dismisses every toast in the area, each playing its exit animation.</summary>
    public void DismissAll()
    {
        foreach (var toast in GetToasts())
            toast.Dismiss();
    }

    /// <summary>
    /// Removes every toast immediately, without an exit animation and without firing their dismissal callbacks.
    /// </summary>
    public void Clear()
    {
        lock (syncRoot)
            toasts.Clear();
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        Clear();

        if (ReferenceEquals(defaultArea, this))
            defaultArea = null;
    }

    #endregion

    #region Drawing

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        // Borrowed per frame rather than kept: a toast's action button runs consumer code mid-draw, which could
        // refill an area-owned buffer underneath the loop still walking it.
        using var snapshot = SnapshotToasts();
        using var buffer = PooledBuffer<NoireToast>.Rent(Math.Min(snapshot.Length, Math.Max(1, MaxVisible)));

        var count = Measure(snapshot.Span, buffer.Span, out var total);

        if (count == 0)
            return;

        var visible = buffer.Span[..count];

        // Sized from heights measured this frame: an auto-resizing window lags its contents by a frame, which a
        // bottom-anchored stack shows as jitter. The height is a whole pixel because a bottom-anchored window sits at
        // (fixed edge - its own height), and those only cancel back to the fixed edge for an integer height.
        var height = ResolveStackHeight(total);
        var size = new Vector2(ScaledWidth, height);
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(Position.Resolve(size, viewport.Pos, viewport.Size), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        using var padding = UiPush.Style(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var border = UiPush.Style(ImGuiStyleVar.WindowBorderSize, 0f);
        using var minSize = UiPush.Style(ImGuiStyleVar.WindowMinSize, Vector2.One);

        var flags = ToastWindowFlags;
        if (AlwaysOnTop)
            flags |= UiWindowOrder.TopLayerFlag;

        if (ImGui.Begin(ImGuiId, flags))
        {
            if (AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            DrawStack(visible, height);
        }

        ImGui.End();
    }

    // Advances every visible toast's transition, works out how much vertical room each one takes this frame, and
    // retires the ones that have finished leaving.
    private int Measure(ReadOnlySpan<NoireToast> snapshot, Span<NoireToast> visible, out float total)
    {
        using var buffer = PooledBuffer<NoireToast>.Rent(snapshot.Length);

        var expired = buffer.Span;
        var expiredCount = 0;
        var count = 0;

        total = 0f;

        for (var index = 0; index < snapshot.Length && count < visible.Length; index++)
        {
            var toast = snapshot[NewestFirst ? snapshot.Length - 1 - index : index];

            // The transition is seeded at zero on a toast's first frame; the animation state would otherwise be
            // created already at its target and the toast would pop into place.
            var firstFrame = !toast.Started;
            if (firstFrame)
            {
                toast.Started = true;
                toast.Remaining = (float)toast.Duration.TotalSeconds;
            }

            var presence = NoireAnim.Presence(toast.Id, "toast", !firstFrame && !toast.IsDismissed, Style.TransitionDuration);
            toast.Presence = Math.Clamp(presence, 0f, 1f);

            if (toast.IsDismissed && toast.Presence <= 0.01f)
            {
                expired[expiredCount++] = toast;
                continue;
            }

            var full = toast.LastHeight > 0f ? toast.LastHeight : EstimateHeight(toast);
            toast.Reserved = ResolveSlotHeight(full * toast.Presence);

            total += toast.Reserved + (count > 0 ? StackGap : 0f);
            visible[count++] = toast;
        }

        Retire(expired[..expiredCount]);
        return count;
    }

    // Removes toasts that have finished leaving and fires their dismissal callbacks.
    private void Retire(ReadOnlySpan<NoireToast> expired)
    {
        if (expired.Length == 0)
            return;

        lock (syncRoot)
        {
            foreach (var toast in expired)
                toasts.Remove(toast);
        }

        foreach (var toast in expired)
            toast.NotifyDismissed();
    }

    // Draws the measured stack, laid out from whichever edge of it is pinned to the screen. height is the window's own
    // height, the measured total rounded up to a whole pixel.
    private void DrawStack(ReadOnlySpan<NoireToast> visible, float height)
    {
        var window = ImGui.GetWindowPos();

        if (!AnchoredAtBottom())
        {
            var top = MathF.Floor(window.Y);

            for (var index = 0; index < visible.Length; index++)
            {
                if (index > 0)
                    top += StackGap;

                DrawToast(visible[index], top);
                top += visible[index].Reserved;
            }

            return;
        }

        // Position plus the height passed in is the one pair of numbers that cancels back to the fixed screen edge;
        // reading the size back from ImGui, or using the unrounded total, reintroduces a value that only nearly agrees.
        var bottom = MathF.Floor(window.Y + height);

        for (var index = visible.Length - 1; index >= 0; index--)
        {
            if (index < visible.Length - 1)
                bottom -= StackGap;

            bottom -= visible[index].Reserved;
            DrawToast(visible[index], bottom);
        }
    }

    private float StackGap => MathF.Ceiling(Style.ScaledGap);

    internal static float ResolveSlotHeight(float content) => MathF.Max(1f, MathF.Ceiling(content));

    internal static float ResolveStackHeight(float total) => MathF.Max(1f, MathF.Ceiling(total));

    private bool AnchoredAtBottom()
    {
        if (Position.Mode != UiPositionMode.Anchor)
            return false;

        return Position.Anchor is UiAnchor.BottomLeft or UiAnchor.BottomCenter or UiAnchor.BottomRight;
    }

    // Draws one toast into the vertical slot Measure reserved for it, and advances its clock. top is in screen
    // coordinates.
    private void DrawToast(NoireToast toast, float top)
    {
        var theme = NoireTheme.Current;
        var accent = SeverityColor(toast.Severity, theme);

        var alpha = toast.Presence;
        var slide = (1f - alpha) * Style.ScaledSlideDistance * SlideDirection();

        var left = ImGui.GetWindowPos().X + slide;
        var min = new Vector2(left, top);
        var max = new Vector2(left + ScaledWidth, top + toast.Reserved);

        var hovered = ImGui.IsMouseHoveringRect(min, max);

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var rounding = Style.ResolveRounding();

        // The slot is shorter than the toast while it arrives or leaves, so without the clip the contents spill over
        // the toasts above and below.
        ImGui.PushClipRect(min, max, true);

        try
        {
            // A slot closes toward the edge the stack hangs from, and the toast is painted from that same edge, or it
            // slides down the screen while the slot shrinks around it.
            var full = MathF.Max(toast.Reserved, toast.LastHeight);
            var body = new Vector2(min.X, AnchoredAtBottom() ? max.Y - full : min.Y);
            var painted = new Vector2(max.X, body.Y + full);

            // The background is painted at the toast's full height and cropped to the slot, so a leaving toast looks
            // covered rather than squashed. The countdown uses the slot itself, so its geometry matches the clip rect.
            var chrome = BuildChrome(drawList, toast, body, painted, min, max, accent, alpha, hovered, rounding, theme);

            if (Style.CustomDraw != null)
            {
                UiHook.Invoke(Style.CustomDraw, chrome, nameof(NoireToastArea), CallbackFault);
            }
            else
            {
                chrome.DrawBackground();
                chrome.DrawStripe();
                chrome.DrawBorder();
                chrome.DrawTimer();
            }

            ImGui.SetCursorScreenPos(body + Style.ScaledPadding + new Vector2(Style.ScaledStripeWidth, 0f));
            ImGui.BeginGroup();

            using (UiPush.Style(ImGuiStyleVar.Alpha, alpha))
                DrawBody(toast, accent, theme);

            ImGui.EndGroup();

            // Frozen once a toast starts leaving, so its share of the stack only shrinks: a height re-measured while
            // clipped and slid feeds back into the share computed from it and the collapse stops being monotonic.
            if (!toast.IsDismissed)
                toast.LastHeight = ResolveSlotHeight(ImGui.GetItemRectSize().Y + (Style.ScaledPadding.Y * 2f));
        }
        finally
        {
            ImGui.PopClipRect();
        }

        AdvanceClock(toast, hovered);
        HandleBodyClick(toast, hovered);
    }

    // 1 to arrive from the right, -1 from the left, 0 to fade in place.
    private float SlideDirection()
    {
        if (Position.Mode != UiPositionMode.Anchor)
            return 1f;

        return Position.Anchor switch
        {
            UiAnchor.TopRight or UiAnchor.MiddleRight or UiAnchor.BottomRight => 1f,
            UiAnchor.TopLeft or UiAnchor.MiddleLeft or UiAnchor.BottomLeft => -1f,
            _ => 0f,
        };
    }

    // min and max are the toast at its full height; slotMin and slotMax are the slot it is clipped to.
    private UiToastDraw BuildChrome(
        ImDrawListPtr drawList,
        NoireToast toast,
        Vector2 min,
        Vector2 max,
        Vector2 slotMin,
        Vector2 slotMax,
        Vector4 accent,
        float alpha,
        bool hovered,
        float rounding,
        NoireTheme theme)
    {
        var timerFraction = 0f;

        if (Style.Timer != ToastTimerMode.None && toast.Duration > TimeSpan.Zero)
        {
            var total = (float)toast.Duration.TotalSeconds;

            if (total > 0f)
            {
                var left = Math.Clamp(toast.Remaining / total, 0f, 1f);
                timerFraction = Style.TimerDrains ? left : 1f - left;
            }
        }

        return new UiToastDraw(
            drawList,
            toast,
            min,
            max,
            slotMin,
            slotMax,
            accent,
            alpha,
            hovered,
            rounding,
            ColorHelper.ScaleAlpha(Style.BackgroundColor ?? theme.Resolve(ThemeColor.SurfaceRaised), alpha),
            ColorHelper.ScaleAlpha(accent, alpha),
            Style.ScaledStripeWidth,
            ColorHelper.ScaleAlpha(Style.BorderColor ?? theme.Resolve(ThemeColor.Border), alpha),
            Style.ScaledBorderSize,
            Style.Timer,
            timerFraction,
            ColorHelper.ScaleAlpha(Style.TimerColor ?? accent, alpha),
            MathF.Max(1f, Style.ScaledTimerThickness),
            Style.TimerTintAlpha);
    }

    // Draws the contents of a toast: icon, title, message, progress and actions.
    private void DrawBody(NoireToast toast, Vector4 accent, NoireTheme theme)
    {
        // The toast paints its own surface from the theme, so its text comes from the theme too; the inherited host
        // colour is near-white and disappears on a light palette.
        using var textColor = UiPush.Color(ImGuiCol.Text, Style.TextColor ?? theme.Resolve(ThemeColor.Text));

        var contentWidth = ScaledWidth - Style.ScaledPadding.X * 2f - Style.ScaledStripeWidth;
        var closeWidth = toast.Closable ? ImGui.GetFrameHeight() : 0f;

        if (Style.ShowIcon)
        {
            var icon = SeverityIcon(toast.Severity);

            using (UiPush.Font(UiIconFont.Current))
                ImGui.TextColored(accent, UiValueText.Icon(icon));

            ImGui.SameLine(0f, theme.ResolveItemSpacing().X * 0.75f);
            contentWidth -= ImGui.GetItemRectSize().X + theme.ResolveItemSpacing().X * 0.75f;
        }

        // Placed from the toast's right edge rather than measured off the text block, so a long message cannot push
        // the close button out of the toast.
        var contentRight = ImGui.GetCursorScreenPos().X + contentWidth;

        ImGui.BeginGroup();

        var textWidth = MathF.Max(NoireUI.Scaled(40f), contentWidth - closeWidth);

        if (!string.IsNullOrEmpty(toast.Title))
        {
            ImGui.TextColored(Style.TitleColor ?? accent, toast.Title);
        }

        NoireLayout.WrapText(textWidth, toast, static t => t.Content.Draw());

        if (toast.Progress != null)
        {
            var value = ReadProgress(toast);
            ImGui.Dummy(new Vector2(1f, NoireUI.Scaled(2f)));
            DrawProgressBar(textWidth, value, accent, theme);
        }

        if (toast.Actions.Count > 0)
        {
            ImGui.Dummy(new Vector2(1f, NoireUI.Scaled(2f)));
            DrawActions(toast);
        }

        ImGui.EndGroup();

        if (!toast.Closable)
            return;

        var groupTop = ImGui.GetItemRectMin().Y;
        ImGui.SetCursorScreenPos(new Vector2(contentRight - closeWidth, groupTop));

        // Written into the shared scratch rather than composed per toast per frame; only the two theme colours move.
        var closeStyle = Style.CloseButtonStyle;

        if (closeStyle == null)
        {
            CloseStyle.TextColor = theme.Resolve(ThemeColor.TextMuted);
            CloseStyle.IconColor = theme.Resolve(ThemeColor.TextMuted);
            closeStyle = CloseStyle;
        }

        if (NoireButtons.Button(UiIds.Join("##", toast.Id, "Close"), closeStyle, new Vector2(closeWidth, closeWidth)))
            toast.Dismiss();
    }

    // Draws a toast's action buttons in a wrapping row, so a toast with several actions grows rather than overflowing.
    private void DrawActions(NoireToast toast)
    {
        using var buffer = PooledBuffer<ToastAction>.Rent(toast.Actions.Count);

        var snapshot = buffer.Span;

        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = toast.Actions[index];

        for (var index = 0; index < snapshot.Length; index++)
        {
            var action = snapshot[index];
            var width = NoireText.CalcSizeInCurrentFont(action.Label).X + NoireTheme.Current.ResolveFramePadding().X * 2f;

            NoireLayout.FlowItem(width, index == 0);

            var id = UiIds.Labelled(action.Label, "##", toast.Id, "Action", index);
            var clicked = Style.ActionButtonStyle is { } actionStyle
                ? NoireButtons.Button(id, actionStyle)
                : NoireButtons.Button(id, action.Tone);

            if (!clicked)
                continue;

            try
            {
                action.OnInvoke(toast);
            }
            catch (Exception ex)
            {
                NoireUI.Diagnostics.ReportFault(nameof(NoireToast), $"The toast action '{action.Label}' threw.", ex);
            }

            if (action.DismissesToast)
                toast.Dismiss();
        }
    }

    // value is the fill fraction, clamped to 0-1.
    private void DrawProgressBar(float width, float value, Vector4 accent, NoireTheme theme)
    {
        var height = MathF.Max(1f, Style.ScaledProgressHeight);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var fill = Style.ProgressColor ?? ColorHelper.Darken(accent, Style.ProgressDarken);
        var track = Style.ProgressTrackColor ?? theme.Resolve(ThemeColor.SurfaceSunken);

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var max = origin + new Vector2(width, height);

        drawList.AddRectFilled(origin, max, ColorHelper.Vector4ToUint(track), height * 0.5f);
        drawList.AddRectFilled(
            origin,
            new Vector2(origin.X + width * Math.Clamp(value, 0f, 1f), max.Y),
            ColorHelper.Vector4ToUint(fill),
            height * 0.5f);
    }

    // Counts a toast's duration down, pausing while it is hovered.
    private static void AdvanceClock(NoireToast toast, bool hovered)
    {
        if (toast.IsDismissed || toast.Duration <= TimeSpan.Zero)
            return;

        if (hovered && toast.PauseOnHover)
            return;

        toast.Remaining -= NoireUI.DeltaTime;

        if (toast.Remaining <= 0f)
            toast.Dismiss();
    }

    // Fires a toast's click callback when its body, rather than one of its items, was clicked.
    private static void HandleBodyClick(NoireToast toast, bool hovered)
    {
        if (toast.OnClick == null || !hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered())
            return;

        try
        {
            toast.OnClick(toast);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireToast), "A toast's click callback threw.", ex);
        }
    }

    // Reads a toast's progress callback, clearing it if it throws.
    private static float ReadProgress(NoireToast toast)
    {
        try
        {
            return Math.Clamp(toast.Progress!(), 0f, 1f);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireToast), "A toast's progress callback threw.", ex);
            toast.Progress = null;
            return 0f;
        }
    }

    // A first-frame height estimate, used only until the toast has measured itself once.
    private float EstimateHeight(NoireToast toast)
    {
        var lines = 1;

        if (!string.IsNullOrEmpty(toast.Title))
            lines++;

        if (toast.Progress != null)
            lines++;

        if (toast.Actions.Count > 0)
            lines++;

        return Style.ScaledPadding.Y * 2f + ImGui.GetTextLineHeightWithSpacing() * lines;
    }

    private static Vector4 SeverityColor(ToastSeverity severity, NoireTheme theme) => severity switch
    {
        ToastSeverity.Success => theme.Resolve(ThemeColor.Success),
        ToastSeverity.Warning => theme.Resolve(ThemeColor.Warning),
        ToastSeverity.Error => theme.Resolve(ThemeColor.Danger),
        _ => theme.Resolve(ThemeColor.Info),
    };

    private static FontAwesomeIcon SeverityIcon(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Success => FontAwesomeIcon.CheckCircle,
        ToastSeverity.Warning => FontAwesomeIcon.ExclamationTriangle,
        ToastSeverity.Error => FontAwesomeIcon.TimesCircle,
        _ => FontAwesomeIcon.InfoCircle,
    };

    #endregion
}
