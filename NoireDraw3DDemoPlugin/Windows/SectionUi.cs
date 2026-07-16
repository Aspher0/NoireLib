using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>
/// Tiny live-binding ImGui helpers shared by the demo sections - each reads a value through a getter and writes it back
/// through a setter only when the control changes, so the UI mirrors the live Draw3D setting without any local state.
/// Every control takes an optional <c>hint</c> that renders a hoverable "(?)" explaining what it does. Enum dropdowns
/// use a hand-rolled <see cref="ImGui.BeginCombo"/> list (the array <c>ImGui.Combo</c> overload misbehaves in this
/// binding).
/// </summary>
internal static class SectionUi
{
    /// <summary>
    /// Appends a hoverable "(?)" help marker to the current line when <paramref name="text"/> is non-empty. The tooltip
    /// wraps at a readable column instead of running off-screen as one line, so a hint is free to be several sentences
    /// long; an explicit "\n" in the text still forces a break where one is wanted.
    /// </summary>
    public static void Hint(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.EndTooltip();
        }
    }

    /// <summary>A checkbox bound to a bool getter/setter.</summary>
    public static void Toggle(string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        var v = get();
        if (ImGui.Checkbox(label, ref v))
            set(v);
        Hint(hint);
    }

    /// <summary>A float slider bound to a getter/setter.</summary>
    public static void Slider(string label, Func<float> get, Action<float> set, float min, float max, string? hint = null)
    {
        var v = get();
        if (ImGui.SliderFloat(label, ref v, min, max))
            set(v);
        Hint(hint);
    }

    /// <summary>An RGB color picker bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Color3(string label, Func<Vector3> get, Action<Vector3> set, string? hint = null)
    {
        var v = get();
        if (ImGui.ColorEdit3(label, ref v))
            set(v);
        Hint(hint);
    }

    /// <summary>A 3-component float slider bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Float3(string label, Func<Vector3> get, Action<Vector3> set, float min, float max, string? hint = null)
    {
        var v = get();
        if (ImGui.SliderFloat3(label, ref v, min, max))
            set(v);
        Hint(hint);
    }

    /// <summary>An enum dropdown bound to a getter/setter (all members, in declaration order).</summary>
    public static void EnumCombo<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        DrawEnumCombo(label, get(), set);
        Hint(hint);
    }

    /// <summary>An enum dropdown bound to a getter/setter, returning whether the value changed (for follow-up work on change).</summary>
    public static bool EnumComboChanged<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        var changed = DrawEnumCombo(label, get(), set);
        Hint(hint);
        return changed;
    }

    /// <summary>
    /// A checkbox per flag of a <c>[Flags]</c> enum, laid out on one line and bound to a getter/setter. Combined members
    /// (<c>GizmoOp.Universal</c>) and the zero member are skipped: they are the state of the single-bit boxes, not
    /// choices of their own. A dropdown cannot edit a flags enum - it can only ever hold one member - so anything
    /// combinable comes through here. Follows the ImGui convention that a label from "##" on is an id only, not caption.
    /// </summary>
    public static void Flags<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        var current = Convert.ToInt64(get());
        var value = current;

        var first = true;
        foreach (var member in Enum.GetValues<T>())
        {
            var bit = Convert.ToInt64(member);
            if (bit == 0 || (bit & (bit - 1)) != 0)
                continue; // the zero member and combined aliases are states of the single-bit boxes, not boxes of their own

            if (!first)
                ImGui.SameLine();
            first = false;

            var on = (current & bit) != 0;
            if (ImGui.Checkbox($"{member}##{label}", ref on))
                value = on ? value | bit : value & ~bit;
        }

        var caption = label.Split("##", 2)[0];
        if (caption.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(caption);
        }

        Hint(hint);

        if (value != current)
            set((T)Enum.ToObject(typeof(T), value));
    }

    /// <summary>An enum dropdown backed by an external index field (for scenes where the value is applied on a button, not live).</summary>
    public static bool EnumCombo<T>(string label, ref int index, string? hint = null) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        if (index < 0 || index >= names.Length)
            index = 0;
        var changed = DrawCombo(label, names, ref index);
        Hint(hint);
        return changed;
    }

    /// <summary>A drag editor for a <see cref="Vector3"/> getter/setter (min == max == 0 leaves it unbounded).</summary>
    public static bool DragVec3(string label, Func<Vector3> get, Action<Vector3> set, float speed = 0.05f, float min = 0f, float max = 0f, string? hint = null)
    {
        var v = get();
        var changed = ImGui.DragFloat3(label, ref v, speed, min, max);
        if (changed)
            set(v);
        Hint(hint);
        return changed;
    }

    /// <summary>A drag editor for a float getter/setter.</summary>
    public static bool DragFloat(string label, Func<float> get, Action<float> set, float speed, float min, float max, string? hint = null)
    {
        var v = get();
        var changed = ImGui.DragFloat(label, ref v, speed, min, max);
        if (changed)
            set(v);
        Hint(hint);
        return changed;
    }

    /// <summary>An integer input bound to a getter/setter.</summary>
    public static bool IntInput(string label, Func<int> get, Action<int> set, string? hint = null)
    {
        var v = get();
        var changed = ImGui.InputInt(label, ref v);
        if (changed)
            set(v);
        Hint(hint);
        return changed;
    }

    /// <summary>An RGBA color picker bound to a <see cref="Vector4"/> getter/setter.</summary>
    public static bool Color4(string label, Func<Vector4> get, Action<Vector4> set, string? hint = null)
    {
        var v = get();
        var changed = ImGui.ColorEdit4(label, ref v);
        if (changed)
            set(v);
        Hint(hint);
        return changed;
    }

    /// <summary>A dimmed "label: value" line for read-only state.</summary>
    public static void LabelValue(string label, string value, string? hint = null)
    {
        ImGui.TextDisabled(label + ":");
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
        Hint(hint);
    }

    /// <summary>A labeled separator (the binding here has no <c>ImGui.SeparatorText</c>): a rule plus a dimmed caption.</summary>
    public static void SeparatorText(string label)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
    }

    /// <summary>A <c>using</c>-scoped ImGui "disabled" block (greys out and blocks the controls inside it while <paramref name="disabled"/> is true).</summary>
    public static IDisposable Disabled(bool disabled) => ImRaii.Disabled(disabled);

    /// <summary>Draws an enum dropdown for <paramref name="current"/>, invoking <paramref name="set"/> when a new value is picked. Returns whether it changed.</summary>
    private static bool DrawEnumCombo<T>(string label, T current, Action<T> set) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, current);
        if (index < 0)
            index = 0;

        if (!DrawCombo(label, names, ref index))
            return false;

        set(values[index]);
        return true;
    }

    /// <summary>A hand-rolled combo over a name list, mutating <paramref name="index"/> on selection. Returns whether it changed.</summary>
    private static bool DrawCombo(string label, string[] names, ref int index)
    {
        var preview = index >= 0 && index < names.Length ? names[index] : string.Empty;
        if (!ImGui.BeginCombo(label, preview))
            return false;

        var changed = false;
        try
        {
            for (var i = 0; i < names.Length; i++)
            {
                var selected = i == index;
                if (ImGui.Selectable(names[i], selected))
                {
                    index = i;
                    changed = true;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
        }
        finally
        {
            ImGui.EndCombo();
        }

        return changed;
    }
}
