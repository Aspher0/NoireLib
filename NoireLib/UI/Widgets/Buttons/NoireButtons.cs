using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// The buttons ImGui does not ship: hold-to-confirm for destructive actions, a button that runs a task and reports on
/// it, a split button, an animated toggle and a segmented control.<br/>
/// All of them are immediate: there is nothing to construct, register or dispose, they take their colors from
/// <see cref="NoireTheme.Current"/>, and each exposes a full style object plus a custom-draw hook so a bespoke look is
/// configuration rather than a fork.
/// </summary>
/// <example>
/// <code>
/// if (NoireButtons.Button("Save", ButtonTone.Accent))
///     Save();
///
/// if (NoireButtons.HoldToConfirm("Hold to delete everything"))
///     DeleteEverything();
///
/// NoireButtons.Toggle("Enabled", ref config.Enabled);
/// </code>
/// </example>
[NoireFacade]
public static class NoireButtons
{
    /// <summary>
    /// The smallest a button is allowed to be, at 100%. Guards the degenerate cases: a fill-minus size larger than the
    /// space left, or a segmented control with more segments than pixels.
    /// </summary>
    private const float MinimumSize = 4f;

    /// <summary>
    /// How long a hold-to-confirm button must be held by default, in seconds.
    /// </summary>
    public static float DefaultHoldSeconds { get; set; } = 1.2f;

    /// <summary>
    /// The text shown on an asynchronous button while its task is running, replacing the label.<br/>
    /// Set to <see langword="null"/> or empty to show only the spinner.
    /// </summary>
    public static string? BusyText { get; set; } = null;

    #region Button

    /// <summary>
    /// Draws a button colored by what it means.
    /// </summary>
    /// <param name="label">The button label. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="tone">What the button means, which decides its colors. See <see cref="ButtonTone"/>.</param>
    /// <param name="size">The button size. A zero component is measured from the label; a negative one fills the
    /// available space, leaving that many pixels.</param>
    /// <returns>True on the frame the button was clicked.</returns>
    public static bool Button(string label, ButtonTone tone = ButtonTone.Neutral, Vector2 size = default)
        => Button(label, ToneStyles.For(tone), size);

    /// <summary>
    /// Draws a button.
    /// </summary>
    /// <param name="label">The button label. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="style">The button's look. When <see langword="null"/>, a neutral themed button is drawn.</param>
    /// <param name="size">The button size. A zero component is measured from the label; a negative one fills the
    /// available space, leaving that many pixels.</param>
    /// <returns>True on the frame the button was clicked.</returns>
    public static bool Button(string label, ButtonStyle? style, Vector2 size = default)
    {
        using var draw = UiDraw.Begin();

        ArgumentNullException.ThrowIfNull(label);

        style ??= ToneStyles.For(ButtonTone.Neutral);

        var resolved = Measure(label, style, size);
        var clicked = ImGui.InvisibleButton(label, resolved);

        Paint(label, style, 1f, false);
        return clicked;
    }

    #endregion

    #region Hold to confirm

    /// <summary>
    /// Draws a button that only fires once it has been held down for long enough, filling as it goes.<br/>
    /// This is the alternative to a confirmation dialog for a destructive action: the pause is the confirmation, so
    /// nothing is deleted by a misplaced click and nobody is asked "are you sure" for the hundredth time.
    /// </summary>
    /// <remarks>
    /// The fill runs off wall-clock time, not frames, so it takes the same real duration whatever the frame rate. It is
    /// deliberately not affected by <see cref="NoireUI.ReducedMotion"/>: the delay is a safety mechanism rather than
    /// decoration, and removing it would remove the protection.<br/>
    /// Releasing early drains the fill quickly rather than snapping it to empty, so a hold interrupted by a stutter
    /// does not feel like it was thrown away.
    /// </remarks>
    /// <param name="label">The button label. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="holdSeconds">How long the button must be held. Defaults to <see cref="DefaultHoldSeconds"/>.</param>
    /// <param name="style">The button's look. When <see langword="null"/>, a danger-toned button is drawn.</param>
    /// <param name="size">The button size, measured from the label when zero.</param>
    /// <returns>True on the single frame the hold completed.</returns>
    public static bool HoldToConfirm(string label, float holdSeconds = 0f, ButtonStyle? style = null, Vector2 size = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        NoireUI.EnsureFrameServices();

        style ??= ToneStyles.For(ButtonTone.Danger);
        holdSeconds = holdSeconds > 0f ? holdSeconds : DefaultHoldSeconds;

        var id = label;
        var resolved = Measure(label, style, size);

        ImGui.InvisibleButton(id, resolved);
        var held = ImGui.IsItemActive();

        var state = UiFrameState.Get<HoldState>(id, "hold", HoldState.Ready);
        var completed = false;
        var step = NoireUI.DeltaTime;

        if (held && state.Armed)
        {
            state.Progress += step / holdSeconds;

            if (state.Progress >= 1f)
            {
                completed = true;
                state.Progress = 0f;
                state.Armed = false;
            }
        }
        else if (!held)
        {
            state.Progress = MathF.Max(0f, state.Progress - step / (holdSeconds * 0.4f));
            state.Armed = true;
        }

        UiFrameState.Set(id, "hold", state);

        Paint(label, style, state.Progress, true);
        return completed;
    }

    #endregion

    #region Asynchronous

    /// <summary>
    /// Draws a button that runs a task, disabling itself and showing a spinner until the task finishes.<br/>
    /// Clicking twice cannot start the work twice, and a task that fails is reported through
    /// <see cref="UiDiagnostics"/> rather than disappearing into an unobserved exception.
    /// </summary>
    /// <remarks>
    /// The running task is tracked against the button's id for as long as the button keeps being drawn. A window closed
    /// mid-task does not cancel anything: the task runs to completion, it simply stops being watched, and the button
    /// starts idle the next time it appears.
    /// </remarks>
    /// <param name="label">The button label. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="action">The work to start. Invoked on the draw thread, so it should return quickly and do its work
    /// asynchronously.</param>
    /// <param name="style">The button's look. When <see langword="null"/>, a neutral themed button is drawn.</param>
    /// <param name="size">The button size, measured from the label when zero.</param>
    /// <param name="onCompleted">Invoked on the draw thread when the task finishes, with the exception that failed it or
    /// <see langword="null"/> when it succeeded.</param>
    /// <returns>True on the frame the task was started.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static bool Async(string label, Func<Task> action, ButtonStyle? style = null, Vector2 size = default, Action<Exception?>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(action);

        NoireUI.EnsureFrameServices();

        style ??= ToneStyles.For(ButtonTone.Neutral);

        var id = label;
        var running = UiFrameState.Get<Task?>(id, "async", null);

        if (running != null && running.IsCompleted)
        {
            UiFrameState.Set<Task?>(id, "async", null);

            var failure = running.Exception?.GetBaseException();
            if (failure != null)
                NoireUI.Diagnostics.ReportFault(VisibleLabel(label), "The button's task failed.", failure);

            if (onCompleted != null)
                Invoke(() => onCompleted(failure), nameof(Async));

            running = null;
        }

        var busy = running != null;
        var started = false;

        ImGui.BeginDisabled(busy);

        var resolved = Measure(label, style, size);
        var clicked = ImGui.InvisibleButton(label, resolved);

        if (busy)
            PaintBusy(style);
        else
            Paint(label, style, 1f, false);

        ImGui.EndDisabled();

        if (clicked && !busy)
        {
            started = true;
            var task = StartTask(action, label);
            UiFrameState.Set(id, "async", task);
        }

        return started;
    }

    /// <summary>
    /// Whether an asynchronous button's task is currently running.
    /// </summary>
    /// <param name="label">The same label the button is drawn with.</param>
    /// <returns>True while the task is running.</returns>
    public static bool IsRunning(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        var running = UiFrameState.Get<Task?>(label, "async", null);
        return running != null && !running.IsCompleted;
    }

    #endregion

    #region Split

    /// <summary>
    /// Draws a button with a caret beside it that opens a menu, for an action with variants: "Export" with "Export
    /// as..." behind the caret.
    /// </summary>
    /// <param name="label">The main button's label. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="menuBody">The menu contents, drawn inside the popup. Not called while the menu is closed, so there
    /// is no open flag to track and no end call to forget.</param>
    /// <param name="style">The button's look. When <see langword="null"/>, a neutral themed button is drawn.</param>
    /// <param name="size">The size of the main button, measured from the label when zero. The caret is added beside it.</param>
    /// <returns>True on the frame the main button was clicked. Opening the menu does not count as a click.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="menuBody"/> is <see langword="null"/>.</exception>
    public static bool Split(string label, Action menuBody, ButtonStyle? style = null, Vector2 size = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(menuBody);

        style ??= ToneStyles.For(ButtonTone.Neutral);

        var theme = NoireTheme.Current;
        var spacing = theme.ResolveItemSpacing().X * 0.25f;
        var caretWidth = ImGui.GetFrameHeight() * 0.8f;

        if (size.X < 0f)
            size.X -= caretWidth + spacing;

        var clicked = Button(label, style, size);
        var mainHeight = ImGui.GetItemRectSize().Y;

        ImGui.SameLine(0f, spacing);

        var popupId = label + "Menu";
        var caretStyle = style.Clone();
        caretStyle.Icon = FontAwesomeIcon.CaretDown;

        if (Button("##" + popupId, caretStyle, new Vector2(caretWidth, mainHeight)))
            ImGui.OpenPopup(popupId);

        // Read before the popup opens, since inside one the current window is the popup itself.
        var ownerInFront = UiWindowOrder.InTopLayer;

        if (ImGui.BeginPopup(popupId))
        {
            try
            {
                if (ownerInFront)
                    UiWindowOrder.KeepInFront();

                UiScope.Run(nameof(Split), menuBody, static b => b());
            }
            finally
            {
                ImGui.EndPopup();
            }
        }

        return clicked;
    }

    #endregion

    #region Toggle

    /// <summary>
    /// Draws an animated on/off switch.
    /// </summary>
    /// <param name="label">The label beside the switch. Anything after "##" is used as the id and not displayed.</param>
    /// <param name="value">The value to read and write.</param>
    /// <param name="style">The switch's look. When <see langword="null"/>, a themed switch is drawn.</param>
    /// <returns>True on the frame the value changed.</returns>
    public static bool Toggle(string label, ref bool value, ToggleStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(label);

        NoireUI.EnsureFrameServices();

        style ??= DefaultToggleStyle;

        var theme = NoireTheme.Current;
        var id = label;
        var text = VisibleLabel(label);
        var height = style.ResolveHeight();
        var width = height * MathF.Max(1f, style.WidthRatio);

        if (style.LabelFirst && text.Length > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
            ImGui.SameLine(0f, theme.ResolveItemSpacing().X);
        }

        var changed = ImGui.InvisibleButton(id, new Vector2(width, height));
        if (changed)
            value = !value;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();

        var travel = NoireAnim.Ease(id, "toggle", value ? 1f : 0f, style.AnimationDuration);

        var onColor = style.OnColor ?? theme.Resolve(ThemeColor.Accent);
        var offColor = style.OffColor ?? theme.Resolve(ThemeColor.SurfaceSunken);
        var trackColor = ColorHelper.Mix(offColor, onColor, travel);

        if (hovered)
            trackColor = theme.Hover(trackColor);

        var rounding = style.ResolveRounding(height);
        var knobRadius = height * 0.5f - MathF.Max(NoireUI.Scaled(2f), height * 0.12f);
        var knobTravel = width - height;
        var knobCenter = new Vector2(min.X + height * 0.5f + knobTravel * travel, (min.Y + max.Y) * 0.5f);
        var knobColor = style.KnobColor ?? theme.On(trackColor);

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        // Guarded rather than returned from: the label after this is laid out whether or not the switch was painted,
        // so a caller's row does not collapse when there is no list. The custom hook sits inside the same guard as the
        // built-in painting, since handing a consumer a null list would fault in their code rather than here.
        if (!drawList.IsNull)
        {
            if (style.CustomDraw != null)
            {
                var args = new UiToggleDraw(drawList, min, max, value, travel, hovered, trackColor, knobCenter, knobRadius, knobColor);
                Invoke(() => style.CustomDraw(args), nameof(Toggle));
            }
            else
            {
                drawList.AddRectFilled(min, max, ColorHelper.Vector4ToUint(trackColor), rounding);

                var borderSize = style.ResolveBorderSize();
                if (borderSize > 0f)
                    drawList.AddRect(min, max, ColorHelper.Vector4ToUint(style.BorderColor ?? theme.Resolve(ThemeColor.Border)), rounding, ImDrawFlags.None, borderSize);

                drawList.AddCircleFilled(knobCenter, knobRadius, ColorHelper.Vector4ToUint(knobColor));
            }
        }

        if (!style.LabelFirst && text.Length > 0)
        {
            ImGui.SameLine(0f, theme.ResolveItemSpacing().X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
        }

        return changed;
    }

    #endregion

    #region Segmented

    /// <summary>
    /// Draws a row of joined options where exactly one is selected, for a choice of three or four that is clearer laid
    /// out than hidden in a dropdown.
    /// </summary>
    /// <param name="id">A unique id for the control.</param>
    /// <param name="selected">The index of the selected option, read and written.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="style">The look of the segments. When <see langword="null"/>, a themed control is drawn.</param>
    /// <param name="width">The total width in pixels. Zero measures from the labels; a negative value fills the
    /// available space, leaving that many pixels.</param>
    /// <returns>True on the frame the selection changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public static bool Segmented(string id, ref int selected, IReadOnlyList<string> options, ButtonStyle? style = null, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count == 0)
            return false;

        var theme = NoireTheme.Current;
        style ??= ToneStyles.For(ButtonTone.Neutral);

        var padding = style.ResolvePadding();
        var height = ImGui.GetFrameHeight();

        // Every segment is the same width, taken from the longest label, so the control does not reflow as the
        // selection moves and the options stay equally weighted.
        float total;
        if (width > 0f)
        {
            total = width;
        }
        else
        {
            var widest = 0f;
            foreach (var option in options)
                widest = MathF.Max(widest, ImGui.CalcTextSize(VisibleLabel(option)).X);

            var measured = (widest + padding.X * 2f) * options.Count;
            total = width < 0f ? MathF.Max(measured, ImGui.GetContentRegionAvail().X + width) : measured;
        }

        var segmentWidth = MathF.Max(NoireUI.Scaled(MinimumSize), total / options.Count);
        var changed = false;
        var accent = theme.Resolve(ThemeColor.Accent);

        ImGui.BeginGroup();

        for (var index = 0; index < options.Count; index++)
        {
            if (index > 0)
                ImGui.SameLine(0f, NoireUI.Scaled(1f));

            var isSelected = index == selected;
            var segment = style.Clone();
            segment.Color = isSelected ? accent : theme.Resolve(ThemeColor.SurfaceSunken);
            segment.TextColor = style.TextColor ?? (isSelected ? theme.On(accent) : theme.Resolve(ThemeColor.TextMuted));

            if (Button(UiIds.Labelled(VisibleLabel(options[index]), "##", id, "Segment", index), segment, new Vector2(segmentWidth, height)))
            {
                if (!isSelected)
                {
                    selected = index;
                    changed = true;
                }
            }
        }

        ImGui.EndGroup();
        return changed;
    }

    #endregion

    #region Spinner

    /// <summary>
    /// Draws a spinning busy indicator as an ordinary item, so it takes part in layout like any widget.
    /// </summary>
    /// <remarks>
    /// Under <see cref="NoireUI.ReducedMotion"/> the dots stop rotating and are drawn at an even opacity, so the
    /// indicator still says "working" without any movement.
    /// </remarks>
    /// <param name="radius">The radius in pixels. Zero uses a size that matches the current line height.</param>
    /// <param name="color">The dot color. When <see langword="null"/>, the theme accent is used.</param>
    public static void Spinner(float radius = 0f, Vector4? color = null)
    {
        if (radius <= 0f)
            radius = ImGui.GetFrameHeight() * 0.32f;

        var size = new Vector2(radius * 2f, radius * 2f);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);

        using var draw = UiDraw.Begin();

        if (!draw.List.IsNull)
            DrawSpinner(draw.List, origin + size * 0.5f, radius, color ?? NoireTheme.Current.Resolve(ThemeColor.Accent));
    }

    #endregion

    #region Painting

    private static readonly ToggleStyle DefaultToggleStyle = new();

    /// <summary>
    /// Computes the button's size and returns it, without submitting anything.
    /// </summary>
    private static Vector2 Measure(string label, ButtonStyle style, Vector2 size)
    {
        var theme = NoireTheme.Current;
        var padding = style.ResolvePadding();
        var text = VisibleLabel(label);

        var contentWidth = ImGui.CalcTextSize(text).X;

        if (style.Icon.HasValue)
            contentWidth += MeasureIcon(style.Icon.Value).X + theme.ResolveItemSpacing().X * 0.6f;

        var width = size.X switch
        {
            > 0f => size.X,
            < 0f => MathF.Max(NoireUI.Scaled(MinimumSize), ImGui.GetContentRegionAvail().X + size.X),
            _ => contentWidth + padding.X * 2f,
        };

        var height = size.Y switch
        {
            > 0f => size.Y,
            < 0f => MathF.Max(NoireUI.Scaled(MinimumSize), ImGui.GetContentRegionAvail().Y + size.Y),
            _ => ImGui.GetTextLineHeight() + padding.Y * 2f,
        };

        return new Vector2(MathF.Max(NoireUI.Scaled(MinimumSize), width), MathF.Max(NoireUI.Scaled(MinimumSize), height));
    }

    /// <summary>
    /// Paints the button that was just submitted, using the item rectangle and hover state ImGui recorded for it.
    /// </summary>
    /// <param name="label">The label, including any id suffix.</param>
    /// <param name="style">The resolved style.</param>
    /// <param name="progress">How far a progressive fill has advanced, or 1 for an ordinary button.</param>
    /// <param name="progressive">Whether to paint the fill overlay at all.</param>
    private static void Paint(string label, ButtonStyle style, float progress, bool progressive)
    {
        var theme = NoireTheme.Current;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var baseColor = style.Color ?? BaseColorFor(style.Tone, theme);
        var fill = held
            ? style.ActiveColor ?? theme.Active(baseColor)
            : hovered
                ? style.HoveredColor ?? theme.Hover(baseColor)
                : baseColor;

        if (style.Tone == ButtonTone.Ghost && style.Color == null && !hovered && !held)
            fill = ColorHelper.WithAlpha(fill, 0f);

        var textColor = style.TextColor ?? (style.Tone == ButtonTone.Ghost ? theme.Resolve(ThemeColor.Text) : theme.On(baseColor));
        var rounding = style.ResolveRounding();

        if (style.CustomDraw != null)
        {
            var args = new UiButtonDraw(drawList, min, max, VisibleLabel(label), hovered, held, fill, textColor, rounding, progress);
            Invoke(() => style.CustomDraw(args), nameof(Button));
            return;
        }

        if (fill.W > 0f)
            drawList.AddRectFilled(min, max, ColorHelper.Vector4ToUint(fill), rounding);

        if (progressive && progress > 0f)
            PaintHoldProgress(drawList, min, max, style, baseColor, rounding, Math.Clamp(progress, 0f, 1f));

        var borderSize = style.ResolveBorderSize();
        if (borderSize > 0f)
            drawList.AddRect(min, max, ColorHelper.Vector4ToUint(style.BorderColor ?? theme.Resolve(ThemeColor.Border)), rounding, ImDrawFlags.None, borderSize);

        DrawContent(drawList, min, max, VisibleLabel(label), style, textColor);
    }

    /// <summary>
    /// Paints the progress of a hold-to-confirm button, in whichever shape the style asked for.
    /// </summary>
    private static void PaintHoldProgress(ImDrawListPtr drawList, Vector2 min, Vector2 max, ButtonStyle style, Vector4 baseColor, float rounding, float progress)
    {
        var color = ColorHelper.Vector4ToUint(style.HoldFillColor ?? ColorHelper.Lighten(baseColor, 0.5f));
        var width = max.X - min.X;
        var height = max.Y - min.Y;

        switch (style.HoldFill)
        {
            case HoldFillMode.RightToLeft:
                drawList.AddRectFilled(new Vector2(max.X - width * progress, min.Y), max, color, rounding);
                break;

            case HoldFillMode.CenterOut:
                var half = width * progress * 0.5f;
                var center = (min.X + max.X) * 0.5f;
                drawList.AddRectFilled(new Vector2(center - half, min.Y), new Vector2(center + half, max.Y), color, rounding);
                break;

            case HoldFillMode.BottomUp:
                drawList.AddRectFilled(new Vector2(min.X, max.Y - height * progress), max, color, rounding);
                break;

            case HoldFillMode.Border:
                UiOutline.TraceClockwise(drawList, min, max, color, style.ScaledHoldBorderThickness, progress);
                break;

            default:
                drawList.AddRectFilled(min, new Vector2(min.X + width * progress, max.Y), color, rounding);
                break;
        }
    }

    /// <summary>
    /// Paints an asynchronous button in its running state: the frame, a spinner, and the busy text if there is one.
    /// </summary>
    private static void PaintBusy(ButtonStyle style)
    {
        var theme = NoireTheme.Current;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var baseColor = style.Color ?? BaseColorFor(style.Tone, theme);
        var fill = theme.Muted(baseColor);
        var rounding = style.ResolveRounding();

        if (fill.W > 0f)
            drawList.AddRectFilled(min, max, ColorHelper.Vector4ToUint(fill), rounding);

        var center = (min + max) * 0.5f;
        var radius = (max.Y - min.Y) * 0.26f;
        var textColor = style.TextColor ?? theme.On(baseColor);
        var busyText = NoireUI.Localize("NoireUI.Button.Busy", BusyText ?? string.Empty);

        if (string.IsNullOrEmpty(busyText))
        {
            DrawSpinner(drawList, center, radius, textColor);
            return;
        }

        var textSize = ImGui.CalcTextSize(busyText);
        var gap = theme.ResolveItemSpacing().X * 0.6f;
        var totalWidth = radius * 2f + gap + textSize.X;
        var spinnerCenter = new Vector2(center.X - totalWidth * 0.5f + radius, center.Y);

        DrawSpinner(drawList, spinnerCenter, radius, textColor);
        drawList.AddText(new Vector2(spinnerCenter.X + radius + gap, center.Y - textSize.Y * 0.5f), ColorHelper.Vector4ToUint(textColor), busyText);
    }

    /// <summary>
    /// Draws the icon and label inside a painted button.
    /// </summary>
    private static void DrawContent(ImDrawListPtr drawList, Vector2 min, Vector2 max, string text, ButtonStyle style, Vector4 textColor)
    {
        var theme = NoireTheme.Current;
        var padding = style.ResolvePadding();
        var color = ColorHelper.Vector4ToUint(textColor);
        var iconColor = ColorHelper.Vector4ToUint(style.IconColor ?? textColor);
        var gap = theme.ResolveItemSpacing().X * 0.6f;

        var iconSize = style.Icon.HasValue ? MeasureIcon(style.Icon.Value) : Vector2.Zero;
        var textSize = text.Length > 0 ? ImGui.CalcTextSize(text) : Vector2.Zero;
        var contentWidth = iconSize.X + (style.Icon.HasValue && text.Length > 0 ? gap : 0f) + textSize.X;

        var centerY = (min.Y + max.Y) * 0.5f;
        var x = style.CenterLabel
            ? (min.X + max.X) * 0.5f - contentWidth * 0.5f
            : min.X + padding.X;

        if (style.Icon.HasValue)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                drawList.AddText(new Vector2(x, centerY - iconSize.Y * 0.5f), iconColor, style.Icon.Value.ToIconString());

            x += iconSize.X + (text.Length > 0 ? gap : 0f);
        }

        if (text.Length > 0)
            drawList.AddText(new Vector2(x, centerY - textSize.Y * 0.5f), color, text);
    }

    /// <summary>
    /// Draws the busy indicator: eight dots around a circle whose opacity rotates, which needs only the draw-list calls
    /// the rest of the library already relies on.
    /// </summary>
    private static void DrawSpinner(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 color)
    {
        const int Dots = 8;

        var dotRadius = MathF.Max(1f, radius * 0.22f);
        var ringRadius = MathF.Max(1f, radius - dotRadius);
        var phase = NoireUI.ReducedMotion ? 0f : NoireUI.Time * 1.4f;

        for (var index = 0; index < Dots; index++)
        {
            var angle = index / (float)Dots * MathF.Tau;
            var position = center + new Vector2(MathF.Cos(angle) * ringRadius, MathF.Sin(angle) * ringRadius);

            var alpha = NoireUI.ReducedMotion
                ? 0.55f
                : 0.20f + 0.80f * (1f - (index / (float)Dots + phase) % 1f);

            drawList.AddCircleFilled(position, dotRadius, ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(color, alpha)));
        }
    }

    private static Vector2 MeasureIcon(FontAwesomeIcon icon)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.CalcTextSize(icon.ToIconString());
    }

    private static Vector4 BaseColorFor(ButtonTone tone, NoireTheme theme) => tone switch
    {
        ButtonTone.Accent => theme.Resolve(ThemeColor.Accent),
        ButtonTone.Success => theme.Resolve(ThemeColor.Success),
        ButtonTone.Warning => theme.Resolve(ThemeColor.Warning),
        ButtonTone.Danger => theme.Resolve(ThemeColor.Danger),
        ButtonTone.Ghost => theme.Resolve(ThemeColor.SurfaceRaised),
        _ => theme.Resolve(ThemeColor.SurfaceRaised),
    };

    /// <summary>
    /// The part of a label that is displayed, dropping the "##" id suffix ImGui uses.
    /// </summary>
    private static string VisibleLabel(string label)
    {
        var marker = label.IndexOf("##", StringComparison.Ordinal);
        return marker < 0 ? label : label[..marker];
    }

    /// <summary>
    /// Runs a consumer callback, reporting anything it throws rather than letting it escape into the frame.
    /// </summary>
    private static void Invoke(Action callback, string source)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(source, "A button callback threw.", ex);
        }
    }

    private static Task StartTask(Func<Task> action, string label)
    {
        try
        {
            return action() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(VisibleLabel(label), "The button's task could not be started.", ex);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The progress of a hold, plus whether a new hold may start.
    /// </summary>
    /// <remarks>
    /// <c>Armed</c> is what stops a completed hold from firing again every frame while the mouse is still down: it only
    /// comes back on release, so one press is always one action.
    /// </remarks>
    private struct HoldState
    {
        public float Progress;
        public bool Armed;

        public static HoldState Ready => new() { Armed = true };
    }

    /// <summary>
    /// One reusable style per tone, so the common <c>Button(label, tone)</c> call allocates nothing per frame.
    /// </summary>
    /// <remarks>
    /// These are handed out by reference and are never written to by the drawing code, which only ever reads them and
    /// clones when it needs a variant.
    /// </remarks>
    private static class ToneStyles
    {
        private static readonly ButtonStyle Neutral = new() { Tone = ButtonTone.Neutral };
        private static readonly ButtonStyle Accent = new() { Tone = ButtonTone.Accent };
        private static readonly ButtonStyle Success = new() { Tone = ButtonTone.Success };
        private static readonly ButtonStyle Warning = new() { Tone = ButtonTone.Warning };
        private static readonly ButtonStyle Danger = new() { Tone = ButtonTone.Danger };
        private static readonly ButtonStyle Ghost = new() { Tone = ButtonTone.Ghost };

        public static ButtonStyle For(ButtonTone tone) => tone switch
        {
            ButtonTone.Accent => Accent,
            ButtonTone.Success => Success,
            ButtonTone.Warning => Warning,
            ButtonTone.Danger => Danger,
            ButtonTone.Ghost => Ghost,
            _ => Neutral,
        };
    }

    #endregion
}
