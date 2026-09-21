using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A window's options menu: opacity and text size sliders, the behaviour switches and the stay-visible switches,
/// opened from a button of the window's own chrome.
/// </summary>
[NoireFacade]
public static class NoireWindowMenu
{
    private const string PopupPrefix = "###NoireWindowMenu_";

    private static readonly WindowMenuToggle[] Behaviour =
    [
        WindowMenuToggle.AlwaysOnTop, WindowMenuToggle.ReducedMotion,
        WindowMenuToggle.LockPosition, WindowMenuToggle.ClickThrough,
        WindowMenuToggle.LockWidth, WindowMenuToggle.LockHeight,
    ];

    private static readonly WindowMenuToggle[] Visibility =
    [
        WindowMenuToggle.StayInGpose, WindowMenuToggle.StayWhenUiHidden,
        WindowMenuToggle.StayInCutscenes, WindowMenuToggle.StayAutoHide,
    ];

    private static readonly string[] ToggleKeys = ["t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8", "t9"];
    private static readonly string[] HoverKeys = ["h0", "h1", "h2", "h3", "h4", "h5", "h6", "h7", "h8", "h9"];

    private static readonly WindowMenuStyle DefaultStyle = new();

    private static readonly string[] Percent = BuildPercent();

    private static string? openId;
    private static float openedAt;
    private static bool openedInTopLayer;
    private static string? pressClosedId;
    private static string? closeId;
    private static bool pressClosedSeen;
    private static WindowMenuStyle? widthStyle;
    private static float widthScale;
    private static float widthValue;
    private static string? noteSource;
    private static string? noteExtra;
    private static string noteJoined = string.Empty;
    private static Vector2 measured;

    /// <summary>Opens or closes the menu. Call it from the button that owns the menu.</summary>
    /// <param name="id">The menu's id, unique within the window.</param>
    /// <returns>True when the menu is now open.</returns>
    public static bool Toggle(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        NoireUI.EnsureFrameServices();

        var popupId = UiIds.For(PopupPrefix, id);

        // The press on this button already closed the menu. The release would reopen it.
        if (pressClosedId == popupId)
        {
            pressClosedId = null;
            return false;
        }

        if (ImGui.IsPopupOpen(popupId))
        {
            openId = null;
            return false;
        }

        openedInTopLayer = UiWindowOrder.InTopLayer;
        openedAt = NoireUI.Time;
        openId = popupId;

        if (closeId == popupId)
            closeId = null;

        ImGui.OpenPopup(popupId);
        return true;
    }

    /// <summary>
    /// Closes the menu on the next frame it is drawn, for a window closing its own menu.
    /// </summary>
    /// <param name="id">The menu's id.</param>
    public static void Close(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        closeId = UiIds.For(PopupPrefix, id);
    }

    /// <summary>
    /// Whether the menu is open.
    /// </summary>
    /// <param name="id">The menu's id.</param>
    /// <returns>True while it is open.</returns>
    public static bool IsOpen(string id)
        => UiDraw.Available && ImGui.IsPopupOpen(UiIds.For(PopupPrefix, id ?? string.Empty));

    /// <summary>Draws the menu below its button when open. Call it every frame in the same window.</summary>
    /// <param name="id">The menu's id, as given to <see cref="Toggle"/>.</param>
    /// <param name="anchorMin">The top left of the button, in screen pixels.</param>
    /// <param name="anchorMax">The bottom right of the button. The menu's right edge lines up with it.</param>
    /// <param name="settings">The values the menu edits.</param>
    /// <param name="style">The style. When <see langword="null"/>, the default.</param>
    /// <returns>What changed, and which switch is hovered or was right clicked.</returns>
    public static WindowMenuResult Draw(string id, Vector2 anchorMin, Vector2 anchorMax, WindowMenuSettings settings, WindowMenuStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(settings);

        if (!UiDraw.Available)
            return default;

        var popupId = UiIds.For(PopupPrefix, id);

        if (!ImGui.IsPopupOpen(popupId))
        {
            TrackPressClose(popupId, anchorMin, anchorMax);
            return default;
        }

        pressClosedSeen = false;

        var s = style ?? DefaultStyle;
        var scale = NoireUI.Scale;
        var width = MathF.Round(ResolveWidth(s) * scale);
        var io = ImGui.GetIO();
        var margin = s.ScreenMargin * scale;

        var elapsed = openId == popupId ? NoireUI.Time - openedAt : float.MaxValue;
        var still = NoireUI.ReducedMotion;
        var slide = still || s.OpenSeconds <= 0f ? 1f : s.OpenCurve.Evaluate(Math.Clamp(elapsed / s.OpenSeconds, 0f, 1f));
        var fade = still || s.FadeSeconds <= 0f ? 1f : Math.Clamp(elapsed / s.FadeSeconds, 0f, 1f);

        var x = Math.Clamp(anchorMax.X - width, margin, MathF.Max(margin, io.DisplaySize.X - width - margin));
        var y = anchorMax.Y + (s.AnchorGap * scale) - ((1f - slide) * s.OpenSlide * scale);

        ImGui.SetNextWindowPos(new Vector2(MathF.Round(x), MathF.Round(y)), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);

        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, s.Rounding * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var open = ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);

        if (!open)
            return default;

        WindowMenuResult result;

        try
        {
            if (openedInTopLayer || settings.AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || closeId == popupId)
            {
                closeId = null;
                ImGui.CloseCurrentPopup();
            }

            using var spacing = UiPush.Style(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            result = DrawContents(settings, s, width, fade);
        }
        finally
        {
            ImGui.EndPopup();
        }

        return result;
    }

    private static void TrackPressClose(string popupId, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (openId != popupId)
        {
            if (pressClosedId == popupId && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (pressClosedSeen)
                    pressClosedId = null;

                pressClosedSeen = true;
            }

            return;
        }

        // ImGui closes a popup on the press, before anything else sees it.
        openId = null;

        var mouse = ImGui.GetMousePos();
        var onAnchor = mouse.X >= anchorMin.X && mouse.X <= anchorMax.X && mouse.Y >= anchorMin.Y && mouse.Y <= anchorMax.Y;

        if (onAnchor && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            pressClosedId = popupId;
            pressClosedSeen = false;
        }
    }

    private static WindowMenuResult DrawContents(WindowMenuSettings settings, WindowMenuStyle s, float width, float fade)
    {
        var scale = NoireUI.Scale;
        var origin = ImGui.GetWindowPos();
        var size = new Vector2(width, ImGui.GetWindowSize().Y);

        PaintSurface(origin, size, s, fade, scale);

        var padX = s.PaddingX * scale;
        var inner = width - (padX * 2f);
        var y = s.PaddingTop * scale;
        var changes = WindowMenuChange.None;
        WindowMenuToggle? hovered = null;
        WindowMenuToggle? rightClicked = null;

        y += s.FirstHeadingTop * scale;
        y = Heading(s.WindowHeading, origin, padX, inner, y, s, fade);
        y += (s.FirstHeadingGapBelow ?? s.HeadingGapBelow) * scale;

        var opacity = Math.Clamp(settings.Opacity, s.OpacityMin, s.OpacityMax);

        if (Slider(false, s.OpacityLabel, ref opacity, s.OpacityMin, s.OpacityMax, origin, padX, inner, ref y, s, fade, s.OpacitySlider))
        {
            settings.Opacity = opacity;
            changes |= WindowMenuChange.Opacity;
        }

        y += s.SliderGap * scale;

        var steps = Math.Max(1, s.TextStepCount);
        var step = (float)Math.Clamp(settings.TextStep, 0, steps - 1);

        if (Slider(true, s.TextSizeLabel, ref step, 0f, steps - 1, origin, padX, inner, ref y, s, fade, s.TextStepSlider))
        {
            var whole = (int)MathF.Round(step);

            if (whole != settings.TextStep)
            {
                settings.TextStep = whole;
                changes |= WindowMenuChange.TextStep;
            }
        }

        y += s.HeadingGapAbove * scale;
        y = Heading(s.BehaviourHeading, origin, padX, inner, y, s, fade);
        y += s.HeadingGapBelow * scale;
        y = Grid(Behaviour, settings, origin, padX, inner, y, s, fade, ref changes, ref hovered, ref rightClicked);

        y += s.HeadingGapAbove * scale;
        y = Heading(s.VisibilityHeading, origin, padX, inner, y, s, fade);
        y += s.HeadingGapBelow * scale;
        y = Grid(Visibility, settings, origin, padX, inner, y, s, fade, ref changes, ref hovered, ref rightClicked);

        var note = ResolveNote(s, settings.ClickThrough);

        if (note.Length > 0)
        {
            y += s.NoteGap * scale;

            var inset = s.NoteInset * scale;
            var boxMin = origin + new Vector2(padX + inset, y);
            var boxWidth = inner - (inset * 2f);
            var text = new UiWindowMenuText(note, WindowMenuTextRole.Note, boxMin, boxMin + new Vector2(boxWidth, 0f), UiAlign.Start,
                Faded(s.NoteColor, fade), s.NoteSizePx * s.TextScale, 0f, s.NoteLineHeight);
            var height = Measure(text, s).Y;

            text = text with { BoxMax = boxMin + new Vector2(boxWidth, height) };
            DrawText(text, s);
            y += height;
        }

        y += s.PaddingBottom * scale;

        ImGui.SetCursorPos(new Vector2(0f, y));
        ImGui.Dummy(new Vector2(1f, 0f));

        return new WindowMenuResult(changes, hovered, rightClicked);
    }

    private static void PaintSurface(Vector2 origin, Vector2 size, WindowMenuStyle s, float fade, float scale)
    {
        if (size.Y <= 1f)
            return;

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var rounding = s.Rounding * scale;
        var max = origin + size;
        var reach = (s.ShadowBlur + MathF.Abs(s.ShadowOffset.Y) + MathF.Abs(s.ShadowOffset.X) + s.BorderSize + 4f) * scale;

        drawList.PushClipRect(origin - new Vector2(reach, reach), max + new Vector2(reach, reach), false);

        try
        {
            if (s.ShadowColor is { } shadow && s.ShadowBlur > 0f)
            {
                var inset = s.ShadowInset * scale;
                var offset = s.ShadowOffset * scale;
                var shadowMin = origin + offset + new Vector2(inset, inset);
                var shadowMax = max + offset - new Vector2(inset, inset);

                if (shadowMax.X > shadowMin.X && shadowMax.Y > shadowMin.Y)
                    NoireShapes.Glow(shadowMin, shadowMax, Faded(shadow, fade), s.ShadowBlur * scale, CornerShape.Rounded, MathF.Max(0f, rounding - inset));
            }

            NoireShapes.Rect(origin, max, Faded(s.Background, fade), CornerShape.Rounded, rounding);

            if (s.BorderSize > 0f && s.BorderColor.W > 0f)
            {
                var thickness = s.BorderSize * scale;
                var grow = s.BorderOutside ? thickness * 0.5f : -thickness * 0.5f;

                NoireShapes.RectOutline(
                    origin - new Vector2(grow, grow), max + new Vector2(grow, grow), Faded(s.BorderColor, fade), thickness,
                    CornerShape.Rounded, MathF.Max(0f, rounding + grow));
            }
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private static float Heading(string text, Vector2 origin, float padX, float inner, float y, WindowMenuStyle s, float fade)
    {
        var scale = NoireUI.Scale;
        var size = s.HeadingSizePx * s.TextScale;
        var height = MathF.Round(size * s.HeadingLineHeight * scale);
        var inset = s.HeadingInset * scale;
        var min = origin + new Vector2(padX + inset, y);

        DrawText(new UiWindowMenuText(text, WindowMenuTextRole.Heading, min, min + new Vector2(inner - (inset * 2f), height), UiAlign.Start,
            Faded(s.HeadingColor, fade), size, s.HeadingTrackingEm, s.HeadingLineHeight), s);

        return y + height;
    }

    private static bool Slider(bool textStep, string label, ref float value, float min, float max, Vector2 origin, float padX, float inner, ref float y, WindowMenuStyle s, float fade, SliderStyle? external)
    {
        var scale = NoireUI.Scale;

        if (external != null)
        {
            ImGui.SetCursorPos(new Vector2(padX, y));

            using var wrap = UiPush.TextWrapPos(padX + inner);

            var changedExternal = textStep
                ? SliderInt(label, ref value, (int)min, (int)max, external)
                : NoireSliders.Float(label, ref value, min, max, external);

            y = ImGui.GetCursorPosY();
            return changedExternal;
        }

        var inset = s.SliderInset * scale;
        var rowHeight = s.SliderRowHeight * scale;
        var spacing = s.SliderSpacing * scale;
        var labelWidth = s.SliderLabelWidth * scale;
        var valueWidth = s.SliderValueWidth * scale;
        var rowMin = origin + new Vector2(padX + inset, y);
        var rowMax = rowMin + new Vector2(inner - (inset * 2f), rowHeight);

        DrawText(new UiWindowMenuText(label, WindowMenuTextRole.SliderLabel, rowMin, new Vector2(rowMin.X + labelWidth, rowMax.Y), UiAlign.Start,
            Faded(s.SliderLabelColor, fade), s.SliderLabelSizePx * s.TextScale, 0f, 1.4f), s);

        var trackMin = new Vector2(rowMin.X + labelWidth + spacing, rowMin.Y);
        var trackMax = new Vector2(rowMax.X - valueWidth - spacing, rowMax.Y);
        var thumb = s.ThumbSize * scale;
        var travel = MathF.Max(1f, trackMax.X - trackMin.X - thumb);

        ImGui.SetCursorScreenPos(trackMin);
        ImGui.InvisibleButton(textStep ? "###NoireWindowMenuStep" : "###NoireWindowMenuOpacity", new Vector2(MathF.Max(1f, trackMax.X - trackMin.X), rowHeight));

        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var changed = false;

        if (held)
        {
            var at = Math.Clamp((ImGui.GetMousePos().X - trackMin.X - (thumb * 0.5f)) / travel, 0f, 1f);
            var next = min + (at * (max - min));
            next = textStep ? MathF.Round(next) : MathF.Round(next * 100f) / 100f;

            if (MathF.Abs(next - value) > 0.0001f)
            {
                value = next;
                changed = true;
            }
        }

        if (hovered || held)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var fraction = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        var args = new UiWindowMenuSliderDraw(textStep, trackMin, trackMax, fraction, hovered, held, fade, s);

        if (s.CustomDrawSlider != null)
            UiHook.Invoke(s.CustomDrawSlider, args, nameof(NoireWindowMenu), CallbackFault);
        else
            PaintSlider(args);

        var valueText = textStep ? StepText(s, (int)MathF.Round(value)) : Percent[Math.Clamp((int)MathF.Round(value * 100f), 0, 100)];

        DrawText(new UiWindowMenuText(valueText, WindowMenuTextRole.SliderValue, new Vector2(rowMax.X - valueWidth, rowMin.Y), rowMax, UiAlign.End,
            Faded(s.SliderValueColor, fade), s.SliderValueSizePx * s.TextScale, 0f, 1.4f), s);

        y += rowHeight;
        return changed;
    }

    private static bool SliderInt(string label, ref float value, int min, int max, SliderStyle style)
    {
        var whole = (int)MathF.Round(value);

        if (!NoireSliders.Int(label, ref whole, min, max, style))
            return false;

        value = whole;
        return true;
    }

    /// <summary>
    /// Paints the built-in slider track and thumb, for a <see cref="WindowMenuStyle.CustomDrawSlider"/> hook that only adds to it.
    /// </summary>
    /// <param name="args">The slider being painted.</param>
    public static void PaintSlider(UiWindowMenuSliderDraw args)
    {
        var s = args.Style;
        var scale = NoireUI.Scale;
        var accent = NoireTheme.Current.Resolve(ThemeColor.Accent);
        var centreY = MathF.Round((args.Min.Y + args.Max.Y) * 0.5f);
        var half = s.TrackHeight * scale * 0.5f;
        var trackMin = new Vector2(args.Min.X, centreY - half);
        var trackMax = new Vector2(args.Max.X, centreY + half);
        var split = args.Min.X + ((args.Max.X - args.Min.X) * args.Fraction);

        NoireShapes.Rect(trackMin, trackMax, Faded(s.TrackColor, args.Fade), CornerShape.Rounded, half);

        if (args.Fraction > 0f)
        {
            NoireShapes.Rect(trackMin, new Vector2(split, trackMax.Y), Faded(s.TrackFillColor ?? accent, args.Fade), CornerShape.Rounded, half,
                args.Fraction >= 1f ? RectCorners.All : RectCorners.Left);
        }

        var thumb = s.ThumbSize * scale;
        var radius = thumb * 0.5f;
        var centre = new Vector2(args.Min.X + radius + ((args.Max.X - args.Min.X - thumb) * args.Fraction), centreY);

        if (s.ThumbShadowColor is { } shadow && s.ThumbShadowBlur > 0f)
        {
            var at = centre + new Vector2(0f, s.ThumbShadowOffsetY * scale);
            NoireShapes.Glow(at - new Vector2(radius, radius), at + new Vector2(radius, radius), Faded(shadow, args.Fade), s.ThumbShadowBlur * scale,
                CornerShape.Rounded, radius);
        }

        var drawList = NoireShapes.DrawList;

        if (drawList.IsNull)
            return;

        if (s.ThumbRingWidth > 0f)
        {
            var ring = s.ThumbRingColor ?? ColorHelper.ScaleAlpha(accent, 0.35f);
            drawList.AddCircleFilled(centre, radius + (s.ThumbRingWidth * scale), ColorHelper.Vector4ToUint(Faded(ring, args.Fade)), Segments(radius));
        }

        drawList.AddCircleFilled(centre, radius, ColorHelper.Vector4ToUint(Faded(s.ThumbColor, args.Fade)), Segments(radius));
    }

    private static float Grid(WindowMenuToggle[] toggles, WindowMenuSettings settings, Vector2 origin, float padX, float inner, float y, WindowMenuStyle s,
        float fade, ref WindowMenuChange changes, ref WindowMenuToggle? hovered, ref WindowMenuToggle? rightClicked)
    {
        var scale = NoireUI.Scale;
        var gap = s.ToggleColumnGap * scale;
        var column = MathF.Floor((inner - gap) * 0.5f);
        var height = ResolveToggleHeight(s);
        var rowGap = s.ToggleRowGap * scale;

        for (var i = 0; i < toggles.Length; i++)
        {
            var toggle = toggles[i];
            var col = i % 2;
            var row = i / 2;
            var min = origin + new Vector2(padX + (col * (column + gap)), y + (row * (height + rowGap)));
            var max = min + new Vector2(col == 1 ? inner - column - gap : column, height);

            if (Toggle(toggle, settings, min, max, s, fade, out var isHovered, out var isRightClicked))
                changes |= WindowMenuSettings.ChangeOf(toggle);

            if (isHovered)
            {
                hovered = toggle;

                if (s.GetHint(toggle) is { Length: > 0 } hint)
                {
                    if (s.CustomShowHint != null)
                        UiHook.Invoke(s.CustomShowHint, new UiWindowMenuHint(toggle, hint, min, max), nameof(NoireWindowMenu), CallbackFault);
                    else
                        NoireTooltip.Show(hint, s.HintStyle, HoverKeys[(int)toggle]);
                }
            }

            if (isRightClicked)
                rightClicked = toggle;
        }

        var rows = (toggles.Length + 1) / 2;
        return y + (rows * height) + ((rows - 1) * rowGap) + (s.GridBottomGap * scale);
    }

    private static bool Toggle(WindowMenuToggle toggle, WindowMenuSettings settings, Vector2 min, Vector2 max, WindowMenuStyle s, float fade,
        out bool hovered, out bool rightClicked)
    {
        var on = settings.Get(toggle);
        var label = s.GetLabel(toggle);
        var key = ToggleKeys[(int)toggle];
        bool clicked;

        ImGui.SetCursorScreenPos(min);

        if (s.ToggleOn != null && s.ToggleOff != null)
        {
            clicked = NoireButtons.Button(UiIds.Labelled(label, "###NoireWindowMenu_", key), on ? s.ToggleOn : s.ToggleOff, max - min);
            hovered = ImGui.IsItemHovered();
            rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        }
        else
        {
            clicked = ImGui.InvisibleButton(UiIds.For("###NoireWindowMenu_", key), max - min);
            hovered = ImGui.IsItemHovered();
            rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

            var held = ImGui.IsItemActive();
            var next = clicked ? !on : on;
            var onProgress = NoireAnim.Ease("NoireWindowMenu", key, next ? 1f : 0f, s.ToggleTransitionSeconds);
            var hoverProgress = NoireAnim.Ease("NoireWindowMenu", HoverKeys[(int)toggle], hovered ? 1f : 0f, s.ToggleTransitionSeconds);
            var args = new UiWindowMenuToggleDraw(toggle, label, min, max, next, hovered, held, onProgress, hoverProgress, fade, s);

            if (s.CustomDrawToggle != null)
                UiHook.Invoke(s.CustomDrawToggle, args, nameof(NoireWindowMenu), CallbackFault);
            else
                PaintToggle(args);

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (clicked)
            settings.Set(toggle, !on);

        return clicked;
    }

    /// <summary>
    /// Paints the built-in switch, for a <see cref="WindowMenuStyle.CustomDrawToggle"/> hook that only adds to it.
    /// </summary>
    /// <param name="args">The switch being painted.</param>
    public static void PaintToggle(UiWindowMenuToggleDraw args)
    {
        var s = args.Style;
        var scale = NoireUI.Scale;
        var accent = NoireTheme.Current.Resolve(ThemeColor.Accent);
        var rounding = s.ToggleRounding * scale;
        var t = args.OnProgress;

        var fill = Vector4.Lerp(s.ToggleFill, s.ToggleOnFill ?? ColorHelper.ScaleAlpha(accent, 0.12f), t);
        var border = Vector4.Lerp(s.ToggleBorder, s.ToggleOnBorder ?? ColorHelper.ScaleAlpha(accent, 0.35f), t);
        var text = Vector4.Lerp(s.ToggleText, s.ToggleActiveText, MathF.Max(t, args.HoverProgress));

        NoireShapes.Rect(args.Min, args.Max, Faded(fill, args.Fade), CornerShape.Rounded, rounding);
        NoireShapes.RectOutline(args.Min + new Vector2(0.5f, 0.5f), args.Max - new Vector2(0.5f, 0.5f), Faded(border, args.Fade), 1f, CornerShape.Rounded,
            MathF.Max(0f, rounding - 0.5f));

        var x = args.Min.X + (s.TogglePaddingX * scale);
        var centreY = (args.Min.Y + args.Max.Y) * 0.5f;

        if (s.DotSize > 0f)
        {
            var radius = s.DotSize * scale * 0.5f;
            var dot = new Vector2(x + radius, centreY);
            var lit = s.DotOnColor ?? accent;
            var color = Vector4.Lerp(s.DotColor, lit, t);

            if (t > 0f && s.DotGlowSpread > 0f)
            {
                NoireShapes.Glow(dot - new Vector2(radius, radius), dot + new Vector2(radius, radius), Faded(ColorHelper.ScaleAlpha(lit, t), args.Fade),
                    s.DotGlowSpread * scale, CornerShape.Rounded, radius);
            }

            var drawList = NoireShapes.DrawList;

            if (!drawList.IsNull)
                drawList.AddCircleFilled(dot, radius, ColorHelper.Vector4ToUint(Faded(color, args.Fade)), Segments(radius));

            x += (radius * 2f) + (s.DotGap * scale);
        }

        DrawText(new UiWindowMenuText(args.Label, WindowMenuTextRole.Toggle, new Vector2(x, args.Min.Y), new Vector2(args.Max.X - (s.TogglePaddingX * scale), args.Max.Y),
            UiAlign.Start, Faded(text, args.Fade), s.ToggleSizePx * s.TextScale, 0f, 1.4f), s);
    }

    private static float ResolveToggleHeight(WindowMenuStyle s)
    {
        if (s.ToggleHeight > 0f)
            return s.ToggleHeight * NoireUI.Scale;

        return MathF.Ceiling(NoireText.CalcSize(" ", s.ToggleSizePx * s.TextScale).Y + (s.TogglePaddingY * NoireUI.Scale * 2f));
    }

    private static float ResolveWidth(WindowMenuStyle s)
    {
        if (s.Width > 0f)
            return s.Width;

        if (ReferenceEquals(widthStyle, s) && MathF.Abs(widthScale - (NoireUI.Scale * s.TextScale * s.ToggleSizePx)) < 0.0001f)
            return widthValue;

        var widest = 0f;

        for (var i = 0; i < ToggleKeys.Length; i++)
        {
            var label = s.GetLabel((WindowMenuToggle)i);
            widest = MathF.Max(widest, Measure(new UiWindowMenuText(label, WindowMenuTextRole.Toggle, Vector2.Zero, Vector2.Zero, UiAlign.Start,
                s.ToggleText, s.ToggleSizePx * s.TextScale, 0f, 1.4f), s).X);
        }

        var scale = NoireUI.Scale;
        var needed = MathF.Ceiling(widest + (s.ToggleLabelPadding * scale * 2f)) / scale;

        widthValue = MathF.Max(s.MinWidth, (needed * 2f) + s.ToggleColumnGap + (s.PaddingX * 2f));
        widthStyle = s;
        widthScale = NoireUI.Scale * s.TextScale * s.ToggleSizePx;

        return widthValue;
    }

    private static string ResolveNote(WindowMenuStyle s, bool clickThrough)
    {
        var extra = clickThrough ? s.ClickThroughNote : null;
        var note = s.Note;

        if (string.IsNullOrEmpty(extra))
            return note ?? string.Empty;

        if (string.IsNullOrEmpty(note))
            return extra;

        if (!ReferenceEquals(note, noteSource) || !ReferenceEquals(extra, noteExtra))
        {
            noteSource = note;
            noteExtra = extra;
            noteJoined = string.Concat(note, " ", extra);
        }

        return noteJoined;
    }

    private static string StepText(WindowMenuStyle s, int step)
    {
        if (s.TextStepNames is { Length: > 0 } names)
            return names[Math.Clamp(step, 0, names.Length - 1)];

        var steps = s.TextSteps;

        if (steps.Length == 0)
            return string.Empty;

        return Percent[Math.Clamp((int)MathF.Round(steps[Math.Clamp(step, 0, steps.Length - 1)] * 100f), 0, Percent.Length - 1)];
    }

    private static void DrawText(UiWindowMenuText text, WindowMenuStyle s)
    {
        if (string.IsNullOrEmpty(text.Text) || text.Color.W <= 0f)
            return;

        if (s.CustomDrawText != null)
        {
            UiHook.Invoke(s.CustomDrawText, text, nameof(NoireWindowMenu), CallbackFault);
            return;
        }

        if (text.Role == WindowMenuTextRole.Note)
        {
            ImGui.SetCursorScreenPos(text.BoxMin);

            using var color = UiPush.Color(ImGuiCol.Text, text.Color);
            NoireText.Wrapped(MathF.Max(1f, text.BoxMax.X - text.BoxMin.X), text.Text, text.SizePx);
            return;
        }

        var size = Measure(text, s);
        var x = text.Align switch
        {
            UiAlign.End => text.BoxMax.X - size.X,
            UiAlign.Center => text.BoxMin.X + ((text.BoxMax.X - text.BoxMin.X - size.X) * 0.5f),
            _ => text.BoxMin.X,
        };
        var y = text.BoxMin.Y + ((text.BoxMax.Y - text.BoxMin.Y - size.Y) * 0.5f);
        var at = new Vector2(MathF.Round(x), MathF.Round(y));

        if (text.TrackingEm != 0f)
        {
            ImGui.SetCursorScreenPos(at);

            using var color = UiPush.Color(ImGuiCol.Text, text.Color);
            NoireText.Tracked(text.Text, text.TrackingEm, text.SizePx);
            return;
        }

        NoireText.DrawAt(at, text.Color, text.Text, text.SizePx);
    }

    private static Vector2 Measure(UiWindowMenuText text, WindowMenuStyle s)
    {
        if (s.MeasureText != null)
        {
            try
            {
                return s.MeasureText(text);
            }
            catch (Exception ex)
            {
                NoireUI.Diagnostics.ReportFault(nameof(NoireWindowMenu), CallbackFault, ex);
                s.MeasureText = null;
            }
        }

        if (text.Role == WindowMenuTextRole.Note)
        {
            measured = Vector2.Zero;
            NoireText.At(text.SizePx, text, static t => measured = ImGui.CalcTextSize(t.Text, false, MathF.Max(1f, t.BoxMax.X - t.BoxMin.X)));
            return measured;
        }

        return text.TrackingEm != 0f
            ? NoireText.TrackedSize(text.Text, text.TrackingEm, text.SizePx)
            : NoireText.CalcSize(text.Text, text.SizePx);
    }

    private static Vector4 Faded(Vector4 color, float fade) => fade >= 1f ? color : new Vector4(color.X, color.Y, color.Z, color.W * fade);

    private static int Segments(float radius) => Math.Clamp((int)MathF.Ceiling(radius * 2.2f), 12, 48);

    private static string[] BuildPercent()
    {
        var values = new string[201];

        for (var i = 0; i < values.Length; i++)
            values[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        return values;
    }

    private const string CallbackFault = "A window menu callback threw.";
}
