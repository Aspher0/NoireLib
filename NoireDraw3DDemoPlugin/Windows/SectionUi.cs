using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>
/// Tiny live-binding ImGui helpers shared by the demo sections - each reads a value through a getter and writes it back
/// through a setter only when the control changes, so the UI mirrors the live Draw3D setting without any local state.
/// </summary>
internal static class SectionUi
{
    /// <summary>A checkbox bound to a bool getter/setter.</summary>
    public static void Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var v = get();
        if (ImGui.Checkbox(label, ref v))
            set(v);
    }

    /// <summary>A float slider bound to a getter/setter.</summary>
    public static void Slider(string label, Func<float> get, Action<float> set, float min, float max)
    {
        var v = get();
        if (ImGui.SliderFloat(label, ref v, min, max))
            set(v);
    }

    /// <summary>An RGB color picker bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Color3(string label, Func<Vector3> get, Action<Vector3> set)
    {
        var v = get();
        if (ImGui.ColorEdit3(label, ref v))
            set(v);
    }

    /// <summary>A 3-component float slider bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Float3(string label, Func<Vector3> get, Action<Vector3> set, float min, float max)
    {
        var v = get();
        if (ImGui.SliderFloat3(label, ref v, min, max))
            set(v);
    }

    /// <summary>An enum dropdown bound to a getter/setter (all members, in declaration order).</summary>
    public static void EnumCombo<T>(string label, Func<T> get, Action<T> set) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        var values = Enum.GetValues<T>();
        var idx = Array.IndexOf(values, get());
        if (idx < 0)
            idx = 0;
        if (ImGui.Combo(label, ref idx, names, names.Length))
            set(values[idx]);
    }

    /// <summary>An enum dropdown backed by an external index field (for scenes where the value is applied on a button, not live).</summary>
    public static bool EnumCombo<T>(string label, ref int index) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        return ImGui.Combo(label, ref index, names, names.Length);
    }

    /// <summary>A <c>using</c>-scoped ImGui "disabled" block (greys out and blocks the controls inside it while <paramref name="disabled"/> is true).</summary>
    public static IDisposable Disabled(bool disabled) => ImRaii.Disabled(disabled);
}
