using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The fields a settings window is actually made of: a number that carries its unit, a duration you can type as
/// <c>1m30s</c>, a colour you can paste a hex into, a reason shown when a value is refused, and a dot that says a
/// setting is no longer the shipped one.
/// </summary>
/// <remarks>
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
    /// Reused rather than allocated per call; drawing runs on one thread, so nothing here is seen half-written by a
    /// second caller.
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
    /// A minimum rather than a fixed width, so a label longer than the column pushes its own field along rather than
    /// being clipped. Set it to zero for rows that each size to their own label.
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

        // Before the reset dot, which submits an item of its own and would become the one the mark is read from.
        NoireFocus.OnLast(resolved.Focus);

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
    /// ±16,777,216.
    /// </remarks>
    /// <param name="label">The label shown before the field. Also the widget's id.</param>
    /// <param name="value">The value, updated in place.</param>
    /// <param name="style">How the field behaves. When <see langword="null"/>, the shipped defaults.</param>
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

    /// <summary>
    /// Builds the printf format ImGui writes the number with, unit included.
    /// </summary>
    /// <remarks>
    /// A percent sign in the unit has to be doubled, or ImGui reads it as the start of another conversion and prints
    /// something nobody asked for.
    /// </remarks>
    private static string BuildFormat(NumberStyle style)
    {
        var decimals = Math.Clamp(style.Decimals, 0, 9);
        var unit = style.Unit ?? string.Empty;

        // Cached, since a format is constant per configuration but the field redraws every frame.
        var key = new FormatKey(decimals, unit);

        if (NumberFormats.TryGet(key, out var cached))
            return cached;

        var built = unit.Length == 0
            ? $"%.{decimals}f"
            : $"%.{decimals}f {unit.Replace("%", "%%")}";

        NumberFormats.Set(key, built);
        return built;
    }

    /// <summary>
    /// How a number is written: the decimals asked for and the unit after them.
    /// </summary>
    private readonly record struct FormatKey(int Decimals, string Unit);

    /// <summary>
    /// How many distinct number formats are kept. Only matters when a unit is built from a runtime value.
    /// </summary>
    private const int MaxNumberFormats = 256;

    private static readonly HotPathCache<FormatKey, string> NumberFormats = new(MaxNumberFormats);

    #endregion

    #region Duration

    /// <summary>
    /// A field that reads a duration the way people write one: <c>90s</c>, <c>1m30s</c>, <c>1h30</c>, <c>1:30</c>.
    /// </summary>
    /// <remarks>
    /// While the field has focus it holds the text as typed; on leaving it, the text is read and the value written, or
    /// the text is put back to the value and the reason slides in underneath. See <see cref="DurationHelper"/> for
    /// exactly what is accepted.
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

        // The reading gets a column of its own, not leftover space, so the field does not resize as the text is typed.
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
                NoireText.Muted(UiValueText.Duration(Clamp(preview, resolved.Min, resolved.Max)), TextSize.Caption);
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
    /// <remarks>Both shorthands are accepted, so <c>#f00</c> is red.</remarks>
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

    #endregion

    #region The pieces on their own

    /// <summary>
    /// Wraps any drawing in the same refusal message the fields here use, for a widget this class does not ship.
    /// </summary>
    /// <remarks>
    /// The body draws whatever it likes and returns whether it changed anything; the message appears under it,
    /// sliding in rather than snapping.
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
    /// The body draws whatever it likes and returns whether it changed anything; the message appears under it,
    /// sliding in rather than snapping.
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

        // A field is taller than its text line, and SameLine returns the cursor to the line's top; the row height
        // centres the dot on the field, not just the line.
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
    /// The label doubles as the id, and anything after a "###" in it is the stable part, exactly as in ImGui, so a
    /// renamed or translated label keeps the field's state. Shared with <see cref="NoireSliders"/>, whose custom
    /// control still lines up with the fields around it.
    /// </remarks>
    /// <returns>How much of the row the label column took.</returns>
    internal static float BeginRow(string label, float width, out string id, bool sizeField = true, float extraReserve = 0f, float? labelWidth = null)
    {
        UiLabel.Split(label, out var visible, out id);

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

            // Padded to a shared column so a run of settings lines up, and a label longer than the column pushes its
            // own field along rather than being clipped. A caller-given width is the column itself, not a floor: the
            // shared default is a minimum, but an explicit one is fixed.
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
    /// The message is remembered until it finishes sliding out, or the row would snap shut instead of closing.
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

        // Drawn before its space is reserved, so the height comes from what the message actually took: a two-line
        // wrap needs two lines of room, or the next field would overlap it.
        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y - ((1f - presence) * NoireUI.Scaled(5f))));

        NoireText.Colored(
            ColorHelper.ScaleAlpha(NoireTheme.Current.Resolve(ThemeColor.Danger), presence),
            message,
            TextSize.Caption);

        // The gap belongs to the message, not to whatever follows it: the next row has no reason to know a refusal
        // is showing above it.
        var height = MathF.Max(0f, ImGui.GetItemRectMax().Y - start.Y) + NoireUI.Scaled(6f);

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(1f, height * presence));
    }

    /// <summary>
    /// Remembers that the text in a field could not be read, until it is typed in again.
    /// </summary>
    /// <remarks>
    /// Held rather than reported only on the frame it happens. A <c>Validate</c> refusal is recomputed from the value
    /// on every frame and so persists on its own; a parse failure happens on exactly one frame, when the field loses
    /// focus, and would otherwise slide straight back out again.
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
