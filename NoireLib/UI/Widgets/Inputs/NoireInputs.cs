using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Settings fields: a number carrying its unit, a duration typed as <c>1m30s</c>, a hex colour, a refusal message,
/// and a dot marking a setting changed from its default. Every field takes its value by reference and returns
/// whether it changed this frame. The only retained state is in-progress text, held in
/// <see cref="NoireUiSession"/> while the field has focus.
/// </summary>
[NoireFacade]
public static class NoireInputs
{
    // Reused rather than allocated per call since drawing runs on one thread.
    private static readonly NumberStyle Shorthand = new();

    private static readonly NumberStyle WholeNumbers = new();

    private static readonly NumberStyle NumberDefaults = new();
    private static readonly DurationStyle DurationDefaults = new();
    private static readonly HexColorStyle HexColorDefaults = new();

    /// <summary>How long an error takes to slide in or out, in seconds.</summary>
    public static float ErrorSlideSeconds { get; set; } = 0.18f;

    /// <summary>
    /// The minimum width the label column is padded out to, at 100%; zero sizes every row to its own label.
    /// </summary>
    public static float LabelWidth { get; set; } = 110f;

    private const string CallbackFault = "An input hook threw.";

    #region Number

    /// <summary>Draws a number field with its unit written inside it and a stepper beside it.</summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="unit">The unit written after the number, such as <c>ms</c>.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref float value, string? unit = null)
    {
        Shorthand.Unit = unit;
        Shorthand.Decimals = 2;

        return Number(label, ref value, Shorthand);
    }

    /// <summary>Draws a number field.</summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="style">How the field behaves, or null for the defaults.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref float value, NumberStyle? style)
    {
        using var draw = UiDraw.Begin();

        NoireUI.EnsureFrameServices();

        var resolved = style ?? NumberDefaults;
        var changed = false;

        BeginRow(label, resolved.Width, out var id);

        if (ImGui.InputFloat(UiIds.For("###NoireInputsNumber_", id), ref value, resolved.Step, resolved.FastStep, BuildFormat(resolved)))
        {
            value = Math.Clamp(value, resolved.Min, resolved.Max);
            changed = true;
        }

        // Before the reset dot, which submits an item of its own and would become the item the mark reads from.
        NoireFocus.OnLast(resolved.Focus);

        if (resolved.Default is { } fallback && ResetDot(id, !Nearly(value, fallback), customDraw: resolved.ResetDotDraw))
        {
            value = fallback;
            changed = true;
        }

        EndRow(id, Describe(resolved.Validate, value));
        return changed;
    }

    /// <summary>Draws a whole-number field with its unit written inside it and a stepper beside it.</summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="unit">The unit written after the number, such as <c>ms</c>.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref int value, string? unit = null)
    {
        Shorthand.Unit = unit;
        Shorthand.Decimals = 0;

        return Number(label, ref value, Shorthand);
    }

    /// <summary>
    /// Draws a whole-number field, sharing the decimal field's drawing, so values stay exact only within the
    /// range a float represents exactly, 16777216 either way.
    /// </summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="style">How the field behaves, or null for the defaults.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref int value, NumberStyle? style)
    {
        // Copied into a scratch style rather than cloned, avoiding an allocation on every field every frame.
        var source = style ?? NumberDefaults;

        WholeNumbers.Unit = source.Unit;
        WholeNumbers.Step = source.Step;
        WholeNumbers.FastStep = source.FastStep;
        WholeNumbers.Min = MathF.Max(source.Min, int.MinValue);
        WholeNumbers.Max = MathF.Min(source.Max, int.MaxValue);
        WholeNumbers.Decimals = 0;
        WholeNumbers.Default = source.Default;
        WholeNumbers.Validate = source.Validate;
        WholeNumbers.Width = source.Width;
        WholeNumbers.Focus = source.Focus;

        var working = (float)value;
        var changed = Number(label, ref working, WholeNumbers);

        if (changed)
            value = (int)MathF.Round(working);

        return changed;
    }

    // A percent sign in the unit is doubled, or it would be read as the start of another conversion.
    private static string BuildFormat(NumberStyle style)
    {
        var decimals = Math.Clamp(style.Decimals, 0, 9);
        var unit = style.Unit ?? string.Empty;

        // Cached because a format is constant per configuration while the field redraws every frame.
        var key = new FormatKey(decimals, unit);

        if (NumberFormats.TryGet(key, out var cached))
            return cached;

        var built = unit.Length == 0
            ? $"%.{decimals}f"
            : $"%.{decimals}f {unit.Replace("%", "%%")}";

        NumberFormats.Set(key, built);
        return built;
    }

    private readonly record struct FormatKey(int Decimals, string Unit);

    private const int MaxNumberFormats = 256;

    private static readonly HotPathCache<FormatKey, string> NumberFormats = new(MaxNumberFormats);

    #endregion

    #region Duration

    /// <summary>
    /// Draws a field accepting a written duration such as <c>90s</c>, <c>1m30s</c>, <c>1h30</c> or <c>1:30</c>,
    /// parsed when the field loses focus.
    /// </summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The duration, updated in place.</param>
    /// <param name="style">How the field behaves, or null for the defaults.</param>
    /// <returns>True on the frame the duration changes.</returns>
    public static bool Duration(string label, ref TimeSpan value, DurationStyle? style = null)
    {
        using var draw = UiDraw.Begin();

        NoireUI.EnsureFrameServices();

        var resolved = style ?? DurationDefaults;
        var changed = false;

        // A column of its own rather than leftover space, so the field does not resize as the text is typed.
        var previewWidth = resolved.ShowPreview
            ? NoireText.CalcSize("00h00m00s", TextSize.Caption).X + NoireUI.Scaled(8f)
            : 0f;

        BeginRow(label, resolved.Width, out var id, extraReserve: previewWidth);

        var textKey = UiIds.For("NoireInputs.Duration.", id);
        var editing = NoireUiSession.TryGet<string>(textKey, out var pending) && pending != null;
        var text = editing ? pending! : UiValueText.Duration(value);

        ImGui.InputTextWithHint(UiIds.For("###NoireInputsDuration_", id), resolved.Hint, ref text, 64);

        NoireFocus.OnLast(resolved.Focus);

        if (ImGui.IsItemActive())
        {
            NoireUiSession.Set(textKey, text);
            ClearRefusal(id);
        }
        else if (editing)
        {
            // Committed on losing focus rather than per keystroke: a prefix of "1m30s" parses as a valid duration
            // of its own, so the setting would jump to 1 minute on the way to 90 seconds.
            NoireUiSession.Remove(textKey);

            if (DurationHelper.TryParse(text, resolved.BareUnit, out var parsed))
            {
                var clamped = Clamp(parsed, resolved.Min, resolved.Max);
                ClearRefusal(id);

                if (clamped != value)
                {
                    value = clamped;
                    changed = true;
                }
            }
            else
            {
                Refuse(id, $"'{text}' is not a duration. Try 90s, 1m30s or 1:30.");
            }
        }

        if (previewWidth > 0f)
        {
            ImGui.SameLine(0f, NoireUI.Scaled(8f));

            ImGui.PushTextWrapPos(-1f);

            if (editing && DurationHelper.TryParse(text, resolved.BareUnit, out var preview))
                NoireText.Muted(UiValueText.Duration(Clamp(preview, resolved.Min, resolved.Max)), TextSize.Caption);
            else
                ImGui.Dummy(new Vector2(1f, NoireText.LineHeight()));

            ImGui.PopTextWrapPos();
        }

        if (resolved.Default is { } fallback && ResetDot(id, value != fallback, customDraw: resolved.ResetDotDraw))
        {
            value = fallback;
            NoireUiSession.Remove(textKey);
            ClearRefusal(id);
            changed = true;
        }

        EndRow(id, Refusal(id) ?? Describe(resolved.Validate, value));
        return changed;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;

    #endregion

    #region Hex colour

    /// <summary>
    /// Draws a hex colour field with a swatch that opens a picker; three-digit shorthand is accepted, so
    /// <c>#f00</c> is red.
    /// </summary>
    /// <param name="label">The label shown before the field, which is also the widget's id.</param>
    /// <param name="value">The colour, updated in place.</param>
    /// <param name="style">How the field behaves, or null for the defaults.</param>
    /// <returns>True on the frame the colour changes.</returns>
    public static bool HexColor(string label, ref Vector4 value, HexColorStyle? style = null)
    {
        using var draw = UiDraw.Begin();

        NoireUI.EnsureFrameServices();

        var resolved = style ?? HexColorDefaults;
        var changed = false;

        BeginRow(label, resolved.Width, out var id, sizeField: false);

        var swatch = ImGui.GetFrameHeight();

        if (ImGui.ColorButton(UiIds.For("###NoireInputsSwatch_", id), value, ImGuiColorEditFlags.AlphaPreview, new Vector2(swatch, swatch))
            && resolved.ShowPicker)
        {
            ImGui.OpenPopup(UiIds.For("###NoireInputsPicker_", id));
        }

        // Read before the popup opens, since inside it the current window is the popup itself.
        var ownerInFront = UiWindowOrder.InTopLayer;

        if (ImGui.BeginPopup(UiIds.For("###NoireInputsPicker_", id)))
        {
            if (ownerInFront)
                UiWindowOrder.KeepInFront();

            var flags = resolved.ShowAlpha ? ImGuiColorEditFlags.AlphaBar : ImGuiColorEditFlags.NoAlpha;

            if (ImGui.ColorPicker4(UiIds.For("###NoireInputsPicked_", id), ref value, flags))
                changed = true;

            ImGui.EndPopup();
        }

        ImGui.SameLine(0f, NoireUI.Scaled(6f));

        var textKey = UiIds.For("NoireInputs.HexColor.", id);
        var editing = NoireUiSession.TryGet<string>(textKey, out var pending) && pending != null;
        var text = editing ? pending! : UiValueText.HexColor(value, resolved.ShowAlpha);

        ImGui.SetNextItemWidth(NoireText.CalcSize("#12345678").X + (NoireTheme.Current.ResolveFramePadding().X * 2f));
        ImGui.InputTextWithHint(UiIds.For("###NoireInputsHex_", id), "#RRGGBB", ref text, 16);

        NoireFocus.OnLast(resolved.Focus);

        if (ImGui.IsItemActive())
        {
            NoireUiSession.Set(textKey, text);
            ClearRefusal(id);
        }
        else if (editing)
        {
            NoireUiSession.Remove(textKey);

            if (ColorHelper.TryHexToVector4(text, out var parsed))
            {
                var next = resolved.ShowAlpha ? parsed : parsed with { W = value.W };
                ClearRefusal(id);

                if (next != value)
                {
                    value = next;
                    changed = true;
                }
            }
            else
            {
                Refuse(id, $"'{text}' is not a colour. Try #RRGGBB.");
            }
        }

        if (resolved.Default is { } fallback && ResetDot(id, value != fallback, customDraw: resolved.ResetDotDraw))
        {
            value = fallback;
            NoireUiSession.Remove(textKey);
            ClearRefusal(id);
            changed = true;
        }

        EndRow(id, Refusal(id) ?? Describe(resolved.Validate, value));
        return changed;
    }

    #endregion

    #region The pieces on their own

    /// <summary>Wraps arbitrary drawing in the sliding refusal message the fields here use.</summary>
    /// <param name="id">A stable id for the message's animation.</param>
    /// <param name="error">The message to show, or <see langword="null"/> when there is nothing wrong.</param>
    /// <param name="body">The drawing to wrap.</param>
    /// <returns>Whatever <paramref name="body"/> returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static bool Validated(string id, string? error, Func<bool> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Validated(id, error, body, static b => b());
    }

    /// <summary>
    /// Wraps arbitrary drawing in the sliding refusal message the fields here use, passing state into the body
    /// without allocating a closure.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">A stable id for the message's animation.</param>
    /// <param name="error">The message to show, or <see langword="null"/> when there is nothing wrong.</param>
    /// <param name="state">The value passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to wrap.</param>
    /// <returns>Whatever <paramref name="body"/> returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static bool Validated<TState>(string id, string? error, TState state, Func<TState, bool> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        NoireUI.EnsureFrameServices();

        var changed = body(state);
        DrawError(id, error);

        return changed;
    }

    /// <summary>
    /// Draws the dot marking a setting as changed from its default, taking the same room whether or not it is shown.
    /// </summary>
    /// <param name="id">A stable id for the widget.</param>
    /// <param name="modified">Whether the value differs from its default.</param>
    /// <param name="tooltip">The hover text, or null for the default line.</param>
    /// <param name="customDraw">A replacement for the dot's painting. See <see cref="UiResetDotDraw"/>.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public static bool ResetDot(string id, bool modified, string? tooltip = null, Action<UiResetDotDraw>? customDraw = null)
    {
        NoireUI.EnsureFrameServices();

        var radius = NoireUI.Scaled(3.5f);

        // A field is taller than its text line and SameLine returns the cursor to the line's top, so the row height
        // centres the dot on the field rather than on the line.
        var rowHeight = MathF.Max(NoireText.LineHeight(), ImGui.GetFrameHeight());
        var size = new Vector2(radius * 4f, rowHeight);

        ImGui.SameLine(0f, NoireUI.Scaled(6f));

        var origin = ImGui.GetCursorScreenPos();

        if (!modified)
        {
            ImGui.Dummy(size);
            return false;
        }

        var clicked = ImGui.InvisibleButton(UiIds.For("###NoireInputsReset_", id), size);
        var hovered = ImGui.IsItemHovered();
        var centre = origin + (size * 0.5f);
        var color = NoireTheme.Current.Resolve(hovered ? ThemeColor.Accent : ThemeColor.TextMuted);

        using (var draw = UiDraw.Begin())
        {
            if (!draw.List.IsNull)
            {
                var resolvedRadius = hovered ? radius * 1.15f : radius;

                if (customDraw != null)
                {
                    var args = new UiResetDotDraw(draw.List, centre, resolvedRadius, hovered, color);
                    UiHook.Invoke(customDraw, args, nameof(ResetDot), CallbackFault);
                }
                else
                {
                    draw.List.AddCircleFilled(centre, resolvedRadius, ColorHelper.Vector4ToUint(color));
                }
            }
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(tooltip ?? "Changed from the default. Click to put it back.");
        }

        return clicked;
    }

    #endregion

    #region Row plumbing

    // The label doubles as the id, with anything after a "###" as the stable part, so a renamed or translated label
    // keeps the field's state. Returns how much of the row the label column took.
    internal static float BeginRow(string label, float width, out string id, bool sizeField = true, float extraReserve = 0f, float? labelWidth = null)
    {
        UiLabel.Split(label, out var visible, out id);

        // Measured before the label moves the cursor, since that is where the column the row must fit inside begins.
        var available = NoireLayout.ContentWidth();
        var startX = ImGui.GetCursorPosX();
        var gap = NoireUI.Scaled(8f);
        var column = 0f;

        if (!string.IsNullOrEmpty(visible))
        {
            // Without this the label sits at the top of a field two paddings taller than it, reading as part of
            // the row above.
            ImGui.AlignTextToFramePadding();

            ImGui.PushTextWrapPos(-1f);
            NoireText.Draw(visible);
            ImGui.PopTextWrapPos();

            // The shared default is a minimum, so a longer label pushes its own field along; a caller-given width
            // is the column itself and does not grow.
            column = labelWidth is { } stated
                ? NoireUI.Scaled(stated) + gap
                : MathF.Max(NoireText.CalcSize(visible).X, NoireUI.Scaled(LabelWidth)) + gap;

            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(startX + column);
        }

        if (!sizeField)
            return column;

        // The dot's column is reserved whether or not a dot is shown, so the field does not resize as a value
        // moves away from its default.
        var reserved = column + NoireUI.Scaled(14f) + NoireUI.Scaled(6f) + extraReserve;

        ImGui.SetNextItemWidth(width > 0f ? width : MathF.Max(NoireUI.Scaled(60f), available - reserved));
        return column;
    }

    private static void EndRow(string id, string? error) => DrawError(id, error);

    // The message is held until it finishes sliding out, or the row would snap shut instead of closing.
    private static void DrawError(string id, string? error)
    {
        var key = UiIds.For("NoireInputs.Error.", id);
        var showing = !string.IsNullOrEmpty(error);

        if (showing)
            NoireUiSession.Set(key, error!);

        var presence = NoireAnim.Presence(id, "NoireInputsError", showing, ErrorSlideSeconds);

        if (presence <= 0.001f)
        {
            NoireUiSession.Remove(key);
            return;
        }

        var message = showing ? error! : NoireUiSession.Get<string>(key);

        if (string.IsNullOrEmpty(message))
            return;

        var start = ImGui.GetCursorScreenPos();

        // Drawn before its space is reserved: a two-line wrap otherwise gets one line of room and the next field
        // overlaps it.
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y - ((1f - presence) * NoireUI.Scaled(5f))));

        NoireText.Colored(
            ColorHelper.ScaleAlpha(NoireTheme.Current.Resolve(ThemeColor.Danger), presence),
            message,
            TextSize.Caption);

        // The gap is part of the message's own reserved height, so the next row needs no knowledge of it.
        var height = MathF.Max(0f, ImGui.GetItemRectMax().Y - start.Y) + NoireUI.Scaled(6f);

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(1f, height * presence));
    }

    // A parse failure happens on the single frame the field loses focus and would otherwise slide straight back out,
    // so it is held until the field is typed in again.
    private static void Refuse(string id, string message) => NoireUiSession.Set(UiIds.For("NoireInputs.Refused.", id), message);

    private static void ClearRefusal(string id) => NoireUiSession.Remove(UiIds.For("NoireInputs.Refused.", id));

    private static string? Refusal(string id) => NoireUiSession.Get<string>(UiIds.For("NoireInputs.Refused.", id));

    // Runs a caller's validation without letting a throw escape into the frame.
    private static string? Describe<T>(Func<T, string?>? validate, T value)
    {
        if (validate == null)
            return null;

        try
        {
            var error = validate(value);
            return string.IsNullOrEmpty(error) ? null : error;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "The validation callback of an input threw an exception.", "[NoireInputs] ");
            return "Validation failed.";
        }
    }

    private static bool Nearly(float a, float b) => MathF.Abs(a - b) < 0.0001f;

    #endregion
}
