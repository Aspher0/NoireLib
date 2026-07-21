using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The fields a settings window is actually made of: a number that carries its unit, a duration you can type as
/// <c>1m30s</c>, a colour you can paste a hex into, a reason shown when a value is refused, and a dot that says a
/// setting is no longer the shipped one.
/// </summary>
/// <remarks>
/// Each of these is a small thing that every plugin writes again, slightly differently, and gets slightly wrong: the
/// unit that drifts away from its number, the duration stored in milliseconds that the user has to do arithmetic on,
/// the hex box that throws while it is being typed, the validation that refuses a keystroke without saying why.<br/>
/// Everything is immediate and stateless from the caller's side: pass the value by reference, take the return as
/// "changed this frame". The one piece of state, the text of a duration or a colour while it is being typed, lives in
/// <see cref="NoireUiSession"/> for as long as the field has focus and is dropped when it loses it.
/// </remarks>
/// <example>
/// <code>
/// NoireInputs.Number("Interval", ref config.IntervalMs, unit: "ms");
///
/// NoireInputs.Duration("Cooldown", ref config.Cooldown, new DurationStyle
/// {
///     Default = TimeSpan.FromSeconds(30),
///     Min = TimeSpan.FromSeconds(1),
/// });
///
/// NoireInputs.HexColor("Accent", ref config.Accent);
/// </code>
/// </example>
[NoireFacade]
public static class NoireInputs
{
    /// <summary>
    /// The style used by the overloads that take a unit rather than a style.
    /// </summary>
    /// <remarks>
    /// Reused rather than allocated per call, and only ever read inside the call that set it. Drawing runs on one
    /// thread, so there is nothing here for a second caller to see half-written.
    /// </remarks>
    private static readonly NumberStyle Shorthand = new();

    /// <summary>
    /// The style the integer overloads draw through. See <see cref="Shorthand"/> for why it is reused.
    /// </summary>
    private static readonly NumberStyle WholeNumbers = new();

    private static readonly NumberStyle NumberDefaults = new();
    private static readonly DurationStyle DurationDefaults = new();
    private static readonly HexColorStyle HexColorDefaults = new();

    /// <summary>
    /// How long an error takes to slide in or out, in seconds.
    /// </summary>
    public static float ErrorSlideSeconds { get; set; } = 0.18f;

    /// <summary>
    /// The width the label column is padded out to, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    /// <remarks>
    /// A minimum rather than a fixed width: a run of settings lines its fields up without anyone measuring anything,
    /// and a label longer than the column pushes its own field along rather than being clipped. Set it to zero for
    /// rows that each size to their own label.
    /// </remarks>
    public static float LabelWidth { get; set; } = 110f;

    #region Number

    /// <summary>
    /// A number field with its unit written inside it and a stepper beside it.
    /// </summary>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="unit">The unit written after the number, for example <c>ms</c>.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref float value, string? unit = null)
    {
        Shorthand.Unit = unit;
        Shorthand.Decimals = 2;

        return Number(label, ref value, Shorthand);
    }

    /// <summary>
    /// A number field, configured.
    /// </summary>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="style">How the field behaves. When <see langword="null"/>, the shipped defaults.</param>
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

        if (resolved.Default is { } fallback && ResetDot(id, !Nearly(value, fallback)))
        {
            value = fallback;
            changed = true;
        }

        EndRow(id, Describe(resolved.Validate, value));
        return changed;
    }

    /// <summary>
    /// A number field with its unit written inside it and a stepper beside it.
    /// </summary>
    public static bool Number(string label, ref int value, string? unit = null)
    {
        Shorthand.Unit = unit;
        Shorthand.Decimals = 0;

        return Number(label, ref value, Shorthand);
    }

    /// <summary>
    /// A whole-number field, configured.
    /// </summary>
    /// <remarks>
    /// Shares the decimal field's drawing, so the unit and the stepper behave identically. Values are exact to
    /// ±16,777,216, which is past anything a person types into a settings window.
    /// </remarks>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="style">How the field behaves. When <see langword="null"/>, the shipped defaults.</param>
    /// <returns>True on the frame the value changes.</returns>
    public static bool Number(string label, ref int value, NumberStyle? style)
    {
        // Copied into a scratch style rather than cloned, since a clone here would allocate on every field on every
        // frame for the sake of two fields the caller cannot set wrongly anyway.
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

        var working = (float)value;
        var changed = Number(label, ref working, WholeNumbers);

        if (changed)
            value = (int)MathF.Round(working);

        return changed;
    }

    /// <summary>
    /// Builds the printf format ImGui writes the number with, unit included.
    /// </summary>
    /// <remarks>
    /// A percent sign in the unit has to be doubled, or ImGui reads it as the start of another conversion and prints
    /// something nobody asked for. That is the one character that turns a unit into a bug.
    /// </remarks>
    private static string BuildFormat(NumberStyle style)
    {
        var decimals = Math.Clamp(style.Decimals, 0, 9);
        var unit = style.Unit ?? string.Empty;

        // Cached because a format is a constant per configuration, and the field it belongs to is redrawn every frame:
        // built inline, every numeric field on screen would compose the same short string sixty times a second.
        if (NumberFormats.TryGetValue((decimals, unit), out var cached))
            return cached;

        var built = unit.Length == 0
            ? $"%.{decimals}f"
            : $"%.{decimals}f {unit.Replace("%", "%%")}";

        if (NumberFormats.Count >= MaxNumberFormats)
            NumberFormats.Clear();

        NumberFormats[(decimals, unit)] = built;
        return built;
    }

    /// <summary>
    /// How many distinct number formats are kept. A format is a decimal count and a unit, so a plugin has as many as it
    /// has kinds of field, and the bound only matters for a unit built from a value.
    /// </summary>
    private const int MaxNumberFormats = 256;

    private static readonly Dictionary<(int Decimals, string Unit), string> NumberFormats = new();

    #endregion

    #region Duration

    /// <summary>
    /// A field that reads a duration the way people write one: <c>90s</c>, <c>1m30s</c>, <c>1h30</c>, <c>1:30</c>.
    /// </summary>
    /// <remarks>
    /// The value is a <see cref="TimeSpan"/>, so nothing downstream has to know which unit the field was typed in.
    /// While the field has focus it holds the text as typed; on leaving it, the text is read and the value written, or
    /// the text is put back to the value and the reason slides in underneath.<br/>
    /// See <see cref="DurationHelper"/> for exactly what is accepted.
    /// </remarks>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The duration, updated in place.</param>
    /// <param name="style">How the field behaves. When <see langword="null"/>, the shipped defaults.</param>
    /// <returns>True on the frame the duration changes.</returns>
    public static bool Duration(string label, ref TimeSpan value, DurationStyle? style = null)
    {
        using var draw = UiDraw.Begin();

        NoireUI.EnsureFrameServices();

        var resolved = style ?? DurationDefaults;
        var changed = false;

        // The reading is given a column of its own rather than whatever is left over, so the field does not resize as
        // the text is typed and the reading never lands somewhere too narrow to sit on one line.
        var previewWidth = resolved.ShowPreview
            ? NoireText.CalcSize("00h00m00s", TextSize.Caption).X + NoireUI.Scaled(8f)
            : 0f;

        BeginRow(label, resolved.Width, out var id, extraReserve: previewWidth);

        var textKey = UiIds.For("NoireInputs.Duration.", id);
        var editing = NoireUiSession.TryGet<string>(textKey, out var pending) && pending != null;
        var text = editing ? pending! : DurationHelper.Format(value);

        ImGui.InputTextWithHint(UiIds.For("###NoireInputsDuration_", id), resolved.Hint, ref text, 64);

        if (ImGui.IsItemActive())
        {
            NoireUiSession.Set(textKey, text);
            ClearRefusal(id);
        }
        else if (editing)
        {
            // Committed on losing focus rather than per keystroke, because half of "1m30s" is a valid duration and
            // writing it as one would have the setting jump to 1 minute on the way to 90 seconds.
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
                NoireText.Muted(DurationHelper.Format(Clamp(preview, resolved.Min, resolved.Max)), TextSize.Caption);
            else
                ImGui.Dummy(new Vector2(1f, NoireText.LineHeight()));

            ImGui.PopTextWrapPos();
        }

        if (resolved.Default is { } fallback && ResetDot(id, value != fallback))
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
    /// A colour field you can paste a hex into, with a swatch that opens a picker.
    /// </summary>
    /// <remarks>
    /// The point of the hex box is that a colour can be moved between plugins, a theme file and a chat message as six
    /// characters. Both shorthands are accepted, so <c>#f00</c> is red.
    /// </remarks>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The colour, updated in place.</param>
    /// <param name="style">How the field behaves. When <see langword="null"/>, the shipped defaults.</param>
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

        // Read before the popup opens, since inside one the current window is the popup itself.
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
        var text = editing ? pending! : Write(value, resolved.ShowAlpha);

        ImGui.SetNextItemWidth(NoireText.CalcSize("#12345678").X + (NoireTheme.Current.ResolveFramePadding().X * 2f));
        ImGui.InputTextWithHint(UiIds.For("###NoireInputsHex_", id), "#RRGGBB", ref text, 16);

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

        if (resolved.Default is { } fallback && ResetDot(id, value != fallback))
        {
            value = fallback;
            NoireUiSession.Remove(textKey);
            ClearRefusal(id);
            changed = true;
        }

        EndRow(id, Refusal(id) ?? Describe(resolved.Validate, value));
        return changed;
    }

    private static string Write(Vector4 color, bool withAlpha)
        => withAlpha ? ColorHelper.Vector4ToHexAlpha(color) : ColorHelper.Vector4ToHex(color);

    #endregion

    #region The pieces on their own

    /// <summary>
    /// Wraps any drawing in the same refusal message the fields here use, for a widget this class does not ship.
    /// </summary>
    /// <remarks>
    /// The body draws whatever it likes and returns whether it changed anything; the message appears under it, sliding
    /// in rather than snapping, so a row does not jump the moment a keystroke makes a value invalid.
    /// </remarks>
    /// <param name="id">A stable id for the message's animation.</param>
    /// <param name="error">The message to show, or <see langword="null"/> when there is nothing wrong.</param>
    /// <param name="body">The drawing to wrap. Its return value is passed through.</param>
    /// <returns>Whatever <paramref name="body"/> returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static bool Validated(string id, string? error, Func<bool> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Validated(id, error, body, static b => b());
    }

    /// <summary>
    /// Wraps any drawing in the same refusal message the fields here use, for a widget this class does not ship.
    /// </summary>
    /// <remarks>
    /// The body draws whatever it likes and returns whether it changed anything; the message appears under it, sliding
    /// in rather than snapping, so a row does not jump the moment a keystroke makes a value invalid.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="id">A stable id for the message's animation.</param>
    /// <param name="error">The message to show, or <see langword="null"/> when there is nothing wrong.</param>
    /// <param name="state">Passed to <paramref name="body"/>, so a closure is not allocated per frame.</param>
    /// <param name="body">The drawing to wrap. Its return value is passed through.</param>
    /// <returns>Whatever <paramref name="body"/> returned.</returns>
    public static bool Validated<TState>(string id, string? error, TState state, Func<TState, bool> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        NoireUI.EnsureFrameServices();

        var changed = body(state);
        DrawError(id, error);

        return changed;
    }

    /// <summary>
    /// The dot that says a setting is no longer the shipped one, and puts it back when clicked.
    /// </summary>
    /// <remarks>
    /// Takes the same room whether or not it is shown, so a column of settings does not shuffle sideways as values are
    /// changed. Give a <c>Default</c> on any of the styles here and this is drawn for you.
    /// </remarks>
    /// <param name="id">A stable id for the widget.</param>
    /// <param name="modified">Whether the value differs from its default.</param>
    /// <param name="tooltip">What hovering it says. When <see langword="null"/>, a shipped line.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public static bool ResetDot(string id, bool modified, string? tooltip = null)
    {
        NoireUI.EnsureFrameServices();

        var radius = NoireUI.Scaled(3.5f);

        // A field is a frame taller than the line of text inside it, and SameLine puts the cursor back at the top of
        // the line, so the row's height is what centres the dot on the field rather than on the field's first line.
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
                draw.List.AddCircleFilled(centre, hovered ? radius * 1.15f : radius, ColorHelper.Vector4ToUint(color));
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

    /// <summary>
    /// Draws the label and sizes the field that follows it.
    /// </summary>
    /// <remarks>
    /// The label doubles as the id, and anything after a "###" in it is the stable part, exactly as in ImGui. That is
    /// what lets a renamed or translated label keep the field's state.<br/>
    /// Shared with <see cref="NoireSliders"/>, which draws its own control rather than an ImGui field but has to line
    /// its label column up with the fields above and below it.
    /// </remarks>
    /// <returns>How much of the row the label column took, so a caller drawing its own control knows where it starts.</returns>
    internal static float BeginRow(string label, float width, out string id, bool sizeField = true, float extraReserve = 0f, float? labelWidth = null)
    {
        var marker = label.IndexOf("###", StringComparison.Ordinal);
        id = marker >= 0 ? label[(marker + 3)..] : label;

        var visible = marker >= 0 ? label[..marker] : label;

        // Measured from where the row starts, before the label moves the cursor, since that is where the column the
        // row has to fit inside begins.
        var available = NoireLayout.ContentWidth();
        var startX = ImGui.GetCursorPosX();
        var gap = NoireUI.Scaled(8f);
        var column = 0f;

        if (!string.IsNullOrEmpty(visible))
        {
            // Aligned to the frame padding, or the label sits at the top of a field that is two paddings taller than
            // it and reads as belonging to the row above.
            ImGui.AlignTextToFramePadding();

            ImGui.PushTextWrapPos(-1f);
            NoireText.Draw(visible);
            ImGui.PopTextWrapPos();

            // Padded to a shared column so a run of settings lines its fields up, and never clipped: a label longer
            // than the column pushes its own field along rather than being cut off.
            // A width given by the caller is the column, not a floor for it. The shared default is a minimum so a run
            // of settings lines up without anyone measuring, but a page laying its own rows out has already decided
            // where the controls start, and growing the column for one long label puts that row out of line with the
            // rest, which is exactly what the caller was trying to prevent.
            column = labelWidth is { } stated
                ? NoireUI.Scaled(stated) + gap
                : MathF.Max(NoireText.CalcSize(visible).X, NoireUI.Scaled(LabelWidth)) + gap;

            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(startX + column);
        }

        if (!sizeField)
            return column;

        // The dot's column is reserved whether or not there is a dot, so the field does not resize as a value moves
        // away from its default.
        var reserved = column + NoireUI.Scaled(14f) + NoireUI.Scaled(6f) + extraReserve;

        ImGui.SetNextItemWidth(width > 0f ? width : MathF.Max(NoireUI.Scaled(60f), available - reserved));
        return column;
    }

    /// <summary>
    /// Closes a row, showing whatever the value was refused for.
    /// </summary>
    private static void EndRow(string id, string? error) => DrawError(id, error);

    /// <summary>
    /// Draws a refusal under a field, sliding it in and back out again.
    /// </summary>
    /// <remarks>
    /// The message is remembered for as long as it takes to slide out, or there would be nothing to draw on the frames
    /// after it stops applying, and the row would snap shut instead of closing.
    /// </remarks>
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

        // Drawn before the space for it is reserved, so the height comes from what the message actually took: one that
        // wraps to two lines needs two lines of room, and reserving a single line would put the next field through it.
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y - ((1f - presence) * NoireUI.Scaled(5f))));

        NoireText.Colored(
            ColorHelper.ScaleAlpha(NoireTheme.Current.Resolve(ThemeColor.Danger), presence),
            message,
            TextSize.Caption);

        // The gap belongs to the message rather than to whatever follows it, since what follows is the next row of a
        // settings column and has no reason to know a refusal is showing above it.
        var height = MathF.Max(0f, ImGui.GetItemRectMax().Y - start.Y) + NoireUI.Scaled(6f);

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(1f, height * presence));
    }

    /// <summary>
    /// Remembers that the text in a field could not be read, until it is typed in again.
    /// </summary>
    /// <remarks>
    /// Held rather than reported on the frame it happens, which is the difference between a message and a flicker. A
    /// <c>Validate</c> refusal is recomputed from the value on every frame and so persists on its own; a parse failure
    /// happens on exactly one frame, when the field loses focus, and would otherwise slide straight back out again.
    /// </remarks>
    private static void Refuse(string id, string message) => NoireUiSession.Set(UiIds.For("NoireInputs.Refused.", id), message);

    private static void ClearRefusal(string id) => NoireUiSession.Remove(UiIds.For("NoireInputs.Refused.", id));

    private static string? Refusal(string id) => NoireUiSession.Get<string>(UiIds.For("NoireInputs.Refused.", id));

    /// <summary>
    /// Runs a caller's validation without letting it take the frame down with it.
    /// </summary>
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
            NoireLogger.LogError(ex, "The validation callback of an input threw an exception.", nameof(NoireInputs));
            return "Validation failed.";
        }
    }

    private static bool Nearly(float a, float b) => MathF.Abs(a - b) < 0.0001f;

    #endregion
}
