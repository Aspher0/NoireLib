using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Draw3D;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows;

// A form is a two-column table, captions left, controls right. Widgets write back only on change.
internal static class Ui
{
    private const float LabelColumnWidth = 190f;

    private const float TooltipWrapEm = 24f;

    // Nav selection and section rules only. Everything else is theme default or grey.
    public static readonly Vector4 Accent = new(0.45f, 0.72f, 0.90f, 1f);

    static Ui() => NoireTheme.Current = NoireTheme.FromAccent(Accent);

    private static readonly SliderStyle SliderLook = new()
    {
        TrackThickness = 4f,
        FillColor = new Vector4(0.26f, 0.45f, 0.62f, 1f),
        FillTo = new Vector4(0.55f, 0.82f, 1f, 1f),
        Grab = SliderGrab.Circle,
        GrabSize = 12f,
        GrabColor = new Vector4(0.97f, 0.99f, 1f, 1f),
        GrabColorTo = new Vector4(0.72f, 0.86f, 0.98f, 1f),
        GlowColor = new Vector4(0.45f, 0.72f, 0.90f, 0.38f),
        GlowSpread = 6f,
    };

    // Sized independently of the frame height.
    private static readonly ToggleStyle ToggleLook = new()
    {
        Height = 18f,
        WidthRatio = 1.95f,
        BorderSize = 1f,
    };

    private static readonly ButtonStyle ButtonLook = new()
    {
        Rounding = 4f,
        BorderSize = 1f,
    };

    private static readonly ButtonStyle SmallButtonLook = new()
    {
        Rounding = 3f,
        BorderSize = 1f,
        Padding = new Vector2(7f, 2f),
    };

    private static int formDepth;

    public static float Scale => ImGuiHelpers.GlobalScale;

    // Pushed once per frame around the whole window.
    public static IDisposable Style() => new Skin();

    private static readonly Vector4 Hairline = new(1f, 1f, 1f, 0.08f);

    private static readonly Vector4 AccentSoft = new(0.45f, 0.72f, 0.90f, 0.22f);
    private static readonly Vector4 AccentHover = new(0.45f, 0.72f, 0.90f, 0.32f);
    private static readonly Vector4 AccentActive = new(0.45f, 0.72f, 0.90f, 0.45f);

    // Readable at glyph size.
    private static readonly Vector4 AccentBright = new(0.62f, 0.85f, 1f, 1f);

    private sealed class Skin : IDisposable
    {
        private readonly IDisposable colors;
        private readonly IDisposable metrics;

        public Skin()
        {
            colors = ImRaii.PushColor(ImGuiCol.Border, Hairline)
                .Push(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.045f))
                .Push(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.075f))
                .Push(ImGuiCol.FrameBgActive, AccentSoft)
                .Push(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.055f))
                .Push(ImGuiCol.ButtonHovered, AccentHover)
                .Push(ImGuiCol.ButtonActive, AccentActive)
                .Push(ImGuiCol.CheckMark, AccentBright)
                .Push(ImGuiCol.SliderGrab, Accent)
                .Push(ImGuiCol.SliderGrabActive, AccentBright)
                .Push(ImGuiCol.Header, AccentSoft)
                .Push(ImGuiCol.HeaderHovered, AccentHover)
                .Push(ImGuiCol.HeaderActive, AccentActive)
                .Push(ImGuiCol.Separator, Hairline)
                .Push(ImGuiCol.SeparatorHovered, AccentHover)
                .Push(ImGuiCol.ChildBg, Vector4.Zero)
                .Push(ImGuiCol.PopupBg, new Vector4(0.075f, 0.08f, 0.095f, 0.98f))
                .Push(ImGuiCol.ScrollbarGrab, new Vector4(1f, 1f, 1f, 0.12f))
                .Push(ImGuiCol.ScrollbarGrabHovered, new Vector4(1f, 1f, 1f, 0.20f))
                .Push(ImGuiCol.ScrollbarGrabActive, AccentActive)
                .Push(ImGuiCol.ResizeGrip, new Vector4(1f, 1f, 1f, 0.05f))
                .Push(ImGuiCol.ResizeGripHovered, AccentHover)
                .Push(ImGuiCol.ResizeGripActive, AccentActive)
                .Push(ImGuiCol.TextSelectedBg, AccentHover)
                .Push(ImGuiCol.NavHighlight, Accent);

            metrics = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f) * Scale, true)
                .Push(ImGuiStyleVar.FramePadding, new Vector2(9f, 5f) * Scale, true)
                .Push(ImGuiStyleVar.ItemInnerSpacing, new Vector2(6f, 4f) * Scale, true)
                .Push(ImGuiStyleVar.CellPadding, new Vector2(4f, 4f) * Scale, true)
                .Push(ImGuiStyleVar.FrameRounding, 4f * Scale, true)
                .Push(ImGuiStyleVar.GrabRounding, 4f * Scale, true)
                .Push(ImGuiStyleVar.ChildRounding, 5f * Scale, true)
                .Push(ImGuiStyleVar.PopupRounding, 5f * Scale, true)
                .Push(ImGuiStyleVar.GrabMinSize, 10f * Scale, true)
                .Push(ImGuiStyleVar.ScrollbarSize, 11f * Scale, true);
        }

        public void Dispose()
        {
            metrics.Dispose();
            colors.Dispose();
        }
    }

    // Drawn to the draw list: the label and its rule share one line.
    public static void Section(string title)
    {
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted(title.ToUpperInvariant());

        var rect = (Min: ImGui.GetItemRectMin(), Max: ImGui.GetItemRectMax());
        var y = MathF.Floor((rect.Min.Y + rect.Max.Y) * 0.5f) + 0.5f;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var left = rect.Max.X + 8f * Scale;
        if (right > left)
            ImGui.GetWindowDrawList().AddLine(new Vector2(left, y), new Vector2(right, y), ImGui.GetColorU32(ImGuiCol.Separator));

        ImGui.Spacing();
    }

    public static void Note(string text)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
        ImGui.TextWrapped(text);
    }

    // Draws nothing when empty.
    public static void Status(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Note(text);
    }

    // Defaults to Dalamud's warning yellow.
    public static void Callout(string text, Vector4? color = null)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color ?? ImGuiColors.DalamudYellow);
        ImGui.TextWrapped(text);
    }

    public static void Gap() => ImGui.Dummy(new Vector2(0f, 3f * Scale));

    // Anything that can overflow goes in one. Whatever sits above it stays put.
    public static ImRaii.ChildDisposable Scroll(string id) => ImRaii.Child(id, Vector2.Zero, false);

    public static IDisposable Disabled(bool disabled) => ImRaii.Disabled(disabled);

    // Mono digits line up and stop jittering as they change.
    public static void Mono(string text, Vector4? color = null)
    {
        using var font = ImRaii.PushFont(UiBuilder.MonoFont);
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.TextUnformatted(text);
    }

    public static void Icon(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFontFixedWidth);
        ImGui.TextUnformatted(icon.ToIconString());
    }

    // Width before scaling. 0 fits the content.
    public static bool IconButton(FontAwesomeIcon icon, string label, float width = 0f)
    {
        var iconText = icon.ToIconString();
        float iconWidth;
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
            iconWidth = ImGui.CalcTextSize(iconText).X;

        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var textWidth = ImGui.CalcTextSize(label, true).X;
        var size = new Vector2(
            width > 0f ? width * Scale : iconWidth + textWidth + spacing + ImGui.GetStyle().FramePadding.X * 2f,
            0f);

        var start = ImGui.GetCursorScreenPos();
        var pressed = ImGui.Button($"##{label}", size);

        // A button label cannot carry two fonts.
        var pad = ImGui.GetStyle().FramePadding;
        var dl = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
            dl.AddText(start + pad, textColor, iconText);
        dl.AddText(start + pad with { X = pad.X + iconWidth + spacing }, textColor, label);

        return pressed;
    }

    public readonly ref struct FormScope
    {
        private readonly bool open;

        internal FormScope(string id, float labelWidth)
        {
            open = ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingFixedFit);
            if (!open)
                return;

            ImGui.TableSetupColumn("##caption", ImGuiTableColumnFlags.WidthFixed, labelWidth * Scale);
            ImGui.TableSetupColumn("##control", ImGuiTableColumnFlags.WidthStretch);
            formDepth++;
        }

        public void Dispose()
        {
            if (!open)
                return;

            formDepth--;
            ImGui.EndTable();
        }
    }

    // Rows drawn outside a form still render, stacked, without corrupting the enclosing table.
    public static FormScope Form(string id, float labelWidth = LabelColumnWidth) => new(id, labelWidth);

    public static void Row(string label, string? hint = null)
    {
        if (formDepth > 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        HelpMarker(hint);

        if (formDepth > 0)
            ImGui.TableNextColumn();

        ImGui.SetNextItemWidth(-1f);
    }

    // The caption is a hover target too. Nothing is drawn when the hint is empty.
    public static void HelpMarker(string? hint)
    {
        if (string.IsNullOrEmpty(hint))
            return;

        var captionHovered = ImGui.IsItemHovered();
        ImGui.SameLine(0f, 4f * Scale);
        ImGui.TextDisabled("(?)");
        if (captionHovered || ImGui.IsItemHovered())
            Tooltip(hint);
    }

    // Off by default: correct for the game's own models and for a conforming glTF.
    public static bool ImportFlips(string id)
    {
        var flips = NoireDraw3D.Diagnostics.ImportFlips;

        Note("Overrides for files authored in an unusual convention. Leave off for game models and spec-conforming glTF.");
        Gap();
        Note("A single mirror reflects, turning a model into its mirror image. Mirror X and Mirror Z together make a 180 degree turn, which changes only which way the model faces.");
        Gap();

        var changed = false;
        using (Form(id))
        {
            changed |= Toggle2("Mirror Z", () => flips.MirrorZ, v => flips.MirrorZ = v, "Reflects the model through the XY plane.");
            changed |= Toggle2("Mirror X", () => flips.MirrorX, v => flips.MirrorX = v, "Reflects through the YZ plane. With Mirror Z on, the two together make a 180 degree turn about Y.");
            changed |= Toggle2("Reverse winding", () => flips.ReverseWinding, v => flips.ReverseWinding = v, "Undoes the reversal the loaders already apply. A file whose winding was converted before it got here needs this. Anything else will render inside out with it.");
            changed |= Toggle2("Flip texture U", () => flips.FlipU, v => flips.FlipU = v, "Mirrors the texture horizontally.");
            changed |= Toggle2("Flip texture V", () => flips.FlipV, v => flips.FlipV = v, "Mirrors the texture vertically.");
        }

        return changed;
    }

    public static bool Toggle2(string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (!NoireButtons.Toggle($"##{label}", ref v, ToggleLook))
            return false;

        set(v);
        return true;
    }

    // An explicit newline still forces a break.
    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * TooltipWrapEm);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.EndTooltip();
        }
    }

    public static void Toggle(string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (NoireButtons.Toggle($"##{label}", ref v, ToggleLook))
            set(v);
    }

    public static void Slider(string label, Func<float> get, Action<float> set, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (NoireSliders.Float($"###{label}", ref v, min, max, SliderLook))
            set(v);
    }

    public static bool Drag(string label, Func<float> get, Action<float> set, float speed, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        var changed = ImGui.DragFloat($"##{label}", ref v, speed, min, max);
        if (changed)
            set(v);
        return changed;
    }

    public static void Int(string label, Func<int> get, Action<int> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (NoireInputs.Number($"###{label}", ref v, (NumberStyle?)null))
            set(v);
    }

    public static void Text(string label, Func<string> get, Action<string> set, string placeholder = "", int maxLength = 512, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.InputTextWithHint($"##{label}", placeholder, ref v, maxLength))
            set(v);
    }

    private static readonly HexColorStyle OpaqueColor = new() { ShowAlpha = false };
    private static readonly HexColorStyle AlphaColor = new() { ShowAlpha = true };

    public static void Color3(string label, Func<Vector3> get, Action<Vector3> set, string? hint = null)
    {
        Row(label, hint);
        var v = new Vector4(get(), 1f);
        if (NoireInputs.HexColor($"###{label}", ref v, OpaqueColor))
            set(new Vector3(v.X, v.Y, v.Z));
    }

    public static void Color4(string label, Func<Vector4> get, Action<Vector4> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (NoireInputs.HexColor($"###{label}", ref v, AlphaColor))
            set(v);
    }

    public static void Slider3(string label, Func<Vector3> get, Action<Vector3> set, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.SliderFloat3($"##{label}", ref v, min, max))
            set(v);
    }

    public static bool Drag3(string label, Func<Vector3> get, Action<Vector3> set, float speed = 0.05f, float min = 0f, float max = 0f, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        var changed = ImGui.DragFloat3($"##{label}", ref v, speed, min, max);
        if (changed)
            set(v);
        return changed;
    }

    public static void Drag4(string label, Func<Vector4> get, Action<Vector4> set, float speed, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.DragFloat4($"##{label}", ref v, speed))
            set(v);
    }

    public static void Value(string label, string value, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(value);
    }

    public static void Value(string label, string value, Vector4 color, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(value);
    }

    public static void Counter(string label, long value, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        Mono(value.ToString("N0"), value == 0 ? ImGuiColors.DalamudGrey3 : null);
    }

    public static bool Enum<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var values = System.Enum.GetValues<T>();
        var index = Array.IndexOf(values, get());
        if (index < 0)
            index = 0;

        if (!Combo($"##{label}", System.Enum.GetNames<T>(), ref index))
            return false;

        set(values[index]);
        return true;
    }

    // Backed by an external index, for a setting whose live value cannot be read back.
    public static bool Enum<T>(string label, ref int index, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var names = System.Enum.GetNames<T>();
        if (index < 0 || index >= names.Length)
            index = 0;

        return Combo($"##{label}", names, ref index);
    }

    // The zero member and combined aliases are skipped: they are states of the single-bit toggles.
    public static void Flags<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var current = Convert.ToInt64(get());
        var value = current;

        var first = true;
        foreach (var member in System.Enum.GetValues<T>())
        {
            var bit = Convert.ToInt64(member);
            if (bit == 0 || (bit & (bit - 1)) != 0)
                continue;

            if (!first)
                ImGui.SameLine();
            first = false;

            var on = (current & bit) != 0;
            if (NoireButtons.Toggle($"{member}##{label}.{member}", ref on, ToggleLook))
                value = on ? value | bit : value & ~bit;
        }

        if (value != current)
            set((T)System.Enum.ToObject(typeof(T), value));
    }

    // Keyed by widget id. The combo is stateful.
    private static readonly Dictionary<string, (NoireComboBox<string> Combo, string[] Names)> combos = new();

    // Pass "##..." as the id to suppress a duplicate caption.
    public static bool Combo(string id, string[] names, ref int index)
    {
        if (!combos.TryGetValue(id, out var entry))
        {
            entry = (new NoireComboBox<string>(id, names), names);
            combos[id] = entry;
        }
        else if (!SameNames(entry.Names, names))
        {
            entry.Combo.SetItems(names, keepSelection: false);
            combos[id] = (entry.Combo, names);
        }

        // A value changed behind the widget shows in the preview.
        entry.Combo.SelectedIndex = index;

        if (!entry.Combo.Draw())
            return false;

        index = entry.Combo.SelectedIndex;
        return true;
    }

    private static bool SameNames(string[] a, string[] b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.Length != b.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // A zero size component is measured from the label. A negative one fills the space, leaving that many pixels.
    public static bool Button(string label, Vector2 size = default)
        => NoireButtons.Button(label, ButtonLook, size);

    public static bool SmallButton(string label)
        => NoireButtons.Button(label, SmallButtonLook);

    public static bool Check(string label, ref bool value) => NoireButtons.Toggle(label, ref value, ToggleLook);
}
