using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A set of ImGui colours and style variables applied around a block of drawing, and taken back off afterwards.<br/>
/// The named properties cover what most widgets touch; the <see cref="Colors"/>, <see cref="Scalars"/> and
/// <see cref="Vectors"/> maps underneath them reach every value ImGui has, so nothing is out of range because it did not
/// get a property of its own.<br/>
/// Nothing here pushes anything on its own: hand it to <see cref="NoireStyle.With(UiStyle, System.Action)"/>, or set it
/// as a widget's style, and the scope is handled for you.
/// </summary>
/// <example>
/// <code>
/// var danger = new UiStyle { TextColor = theme.Danger, FrameRounding = 0f };
/// danger.Colors[ImGuiCol.PlotHistogram] = theme.Danger;   // anything without a named property
/// </code>
/// </example>
public sealed class UiStyle
{
    /// <summary>
    /// Every colour this style overrides, keyed by ImGui colour slot. The named colour properties read and write here.
    /// </summary>
    public Dictionary<ImGuiCol, Vector4> Colors { get; } = new();

    /// <summary>
    /// Every single-value style variable this style overrides. The named scalar properties read and write here.
    /// </summary>
    public Dictionary<ImGuiStyleVar, float> Scalars { get; } = new();

    /// <summary>
    /// Every two-value style variable this style overrides. The named vector properties read and write here.
    /// </summary>
    public Dictionary<ImGuiStyleVar, Vector2> Vectors { get; } = new();

    /// <summary>Whether this style changes anything at all.</summary>
    public bool IsEmpty => Colors.Count == 0 && Scalars.Count == 0 && Vectors.Count == 0;

    #region Named colours

    /// <summary>The text colour.</summary>
    public Vector4? TextColor { get => GetColor(ImGuiCol.Text); set => SetColor(ImGuiCol.Text, value); }

    /// <summary>The dimmed text colour used by disabled items.</summary>
    public Vector4? TextDisabledColor { get => GetColor(ImGuiCol.TextDisabled); set => SetColor(ImGuiCol.TextDisabled, value); }

    /// <summary>The background of framed widgets (inputs, sliders, checkboxes).</summary>
    public Vector4? FrameColor { get => GetColor(ImGuiCol.FrameBg); set => SetColor(ImGuiCol.FrameBg, value); }

    /// <summary>The hovered background of framed widgets.</summary>
    public Vector4? FrameHoveredColor { get => GetColor(ImGuiCol.FrameBgHovered); set => SetColor(ImGuiCol.FrameBgHovered, value); }

    /// <summary>The active background of framed widgets.</summary>
    public Vector4? FrameActiveColor { get => GetColor(ImGuiCol.FrameBgActive); set => SetColor(ImGuiCol.FrameBgActive, value); }

    /// <summary>The button background.</summary>
    public Vector4? ButtonColor { get => GetColor(ImGuiCol.Button); set => SetColor(ImGuiCol.Button, value); }

    /// <summary>The hovered button background.</summary>
    public Vector4? ButtonHoveredColor { get => GetColor(ImGuiCol.ButtonHovered); set => SetColor(ImGuiCol.ButtonHovered, value); }

    /// <summary>The held button background.</summary>
    public Vector4? ButtonActiveColor { get => GetColor(ImGuiCol.ButtonActive); set => SetColor(ImGuiCol.ButtonActive, value); }

    /// <summary>The border colour of frames, windows and children.</summary>
    public Vector4? BorderColor { get => GetColor(ImGuiCol.Border); set => SetColor(ImGuiCol.Border, value); }

    /// <summary>The separator colour.</summary>
    public Vector4? SeparatorColor { get => GetColor(ImGuiCol.Separator); set => SetColor(ImGuiCol.Separator, value); }

    /// <summary>The background of child regions.</summary>
    public Vector4? ChildColor { get => GetColor(ImGuiCol.ChildBg); set => SetColor(ImGuiCol.ChildBg, value); }

    /// <summary>The background of popups, tooltips and dropdowns.</summary>
    public Vector4? PopupColor { get => GetColor(ImGuiCol.PopupBg); set => SetColor(ImGuiCol.PopupBg, value); }

    /// <summary>The background of selected rows, headers and tree nodes.</summary>
    public Vector4? HeaderColor { get => GetColor(ImGuiCol.Header); set => SetColor(ImGuiCol.Header, value); }

    #endregion

    #region Named scalars

    /// <summary>The opacity multiplier applied to everything drawn in the scope.</summary>
    public float? Alpha { get => GetScalar(ImGuiStyleVar.Alpha); set => SetScalar(ImGuiStyleVar.Alpha, value); }

    /// <summary>The corner radius of framed widgets.</summary>
    public float? FrameRounding { get => GetScalar(ImGuiStyleVar.FrameRounding); set => SetScalar(ImGuiStyleVar.FrameRounding, value); }

    /// <summary>The corner radius of child regions.</summary>
    public float? ChildRounding { get => GetScalar(ImGuiStyleVar.ChildRounding); set => SetScalar(ImGuiStyleVar.ChildRounding, value); }

    /// <summary>The corner radius of popups and tooltips.</summary>
    public float? PopupRounding { get => GetScalar(ImGuiStyleVar.PopupRounding); set => SetScalar(ImGuiStyleVar.PopupRounding, value); }

    /// <summary>The corner radius of windows.</summary>
    public float? WindowRounding { get => GetScalar(ImGuiStyleVar.WindowRounding); set => SetScalar(ImGuiStyleVar.WindowRounding, value); }

    /// <summary>The corner radius of slider and scrollbar grabs.</summary>
    public float? GrabRounding { get => GetScalar(ImGuiStyleVar.GrabRounding); set => SetScalar(ImGuiStyleVar.GrabRounding, value); }

    /// <summary>The border thickness of framed widgets.</summary>
    public float? FrameBorderSize { get => GetScalar(ImGuiStyleVar.FrameBorderSize); set => SetScalar(ImGuiStyleVar.FrameBorderSize, value); }

    /// <summary>The border thickness of child regions.</summary>
    public float? ChildBorderSize { get => GetScalar(ImGuiStyleVar.ChildBorderSize); set => SetScalar(ImGuiStyleVar.ChildBorderSize, value); }

    /// <summary>The horizontal distance one indent level moves by.</summary>
    public float? IndentSpacing { get => GetScalar(ImGuiStyleVar.IndentSpacing); set => SetScalar(ImGuiStyleVar.IndentSpacing, value); }

    #endregion

    #region Named vectors

    /// <summary>The padding inside framed widgets.</summary>
    public Vector2? FramePadding { get => GetVector(ImGuiStyleVar.FramePadding); set => SetVector(ImGuiStyleVar.FramePadding, value); }

    /// <summary>The spacing between consecutive items.</summary>
    public Vector2? ItemSpacing { get => GetVector(ImGuiStyleVar.ItemSpacing); set => SetVector(ImGuiStyleVar.ItemSpacing, value); }

    /// <summary>The spacing between an item and its own label.</summary>
    public Vector2? ItemInnerSpacing { get => GetVector(ImGuiStyleVar.ItemInnerSpacing); set => SetVector(ImGuiStyleVar.ItemInnerSpacing, value); }

    /// <summary>The padding inside windows and child regions.</summary>
    public Vector2? WindowPadding { get => GetVector(ImGuiStyleVar.WindowPadding); set => SetVector(ImGuiStyleVar.WindowPadding, value); }

    /// <summary>The padding inside table cells.</summary>
    public Vector2? CellPadding { get => GetVector(ImGuiStyleVar.CellPadding); set => SetVector(ImGuiStyleVar.CellPadding, value); }

    #endregion

    /// <summary>
    /// Overrides an ImGui colour that has no named property.
    /// </summary>
    /// <param name="color">The colour slot.</param>
    /// <param name="value">The colour to use.</param>
    /// <returns>This <see cref="UiStyle"/> instance, for chaining.</returns>
    public UiStyle With(ImGuiCol color, Vector4 value)
    {
        Colors[color] = value;
        return this;
    }

    /// <summary>
    /// Overrides a single-value ImGui style variable that has no named property.
    /// </summary>
    /// <param name="variable">The style variable.</param>
    /// <param name="value">The value to use.</param>
    /// <returns>This <see cref="UiStyle"/> instance, for chaining.</returns>
    public UiStyle With(ImGuiStyleVar variable, float value)
    {
        Scalars[variable] = value;
        return this;
    }

    /// <summary>
    /// Overrides a two-value ImGui style variable that has no named property.
    /// </summary>
    /// <param name="variable">The style variable.</param>
    /// <param name="value">The value to use.</param>
    /// <returns>This <see cref="UiStyle"/> instance, for chaining.</returns>
    public UiStyle With(ImGuiStyleVar variable, Vector2 value)
    {
        Vectors[variable] = value;
        return this;
    }

    /// <summary>
    /// Creates an independent copy, so a derived style can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public UiStyle Clone()
    {
        var clone = new UiStyle();

        foreach (var entry in Colors)
            clone.Colors[entry.Key] = entry.Value;

        foreach (var entry in Scalars)
            clone.Scalars[entry.Key] = entry.Value;

        foreach (var entry in Vectors)
            clone.Vectors[entry.Key] = entry.Value;

        return clone;
    }

    /// <summary>
    /// Pushes everything this style overrides.
    /// </summary>
    /// <returns>How many colours and how many style variables were pushed, to be popped in the same numbers.</returns>
    internal (int Colors, int Vars) Push()
    {
        foreach (var entry in Colors)
            ImGui.PushStyleColor(entry.Key, entry.Value);

        foreach (var entry in Scalars)
            ImGui.PushStyleVar(entry.Key, entry.Value);

        foreach (var entry in Vectors)
            ImGui.PushStyleVar(entry.Key, entry.Value);

        return (Colors.Count, Scalars.Count + Vectors.Count);
    }

    /// <summary>
    /// Pops exactly what <see cref="Push"/> pushed.
    /// </summary>
    /// <param name="pushed">The counts returned by <see cref="Push"/>.</param>
    internal static void Pop((int Colors, int Vars) pushed)
    {
        if (pushed.Vars > 0)
            ImGui.PopStyleVar(pushed.Vars);

        if (pushed.Colors > 0)
            ImGui.PopStyleColor(pushed.Colors);
    }

    private Vector4? GetColor(ImGuiCol color) => Colors.TryGetValue(color, out var value) ? value : null;

    private void SetColor(ImGuiCol color, Vector4? value)
    {
        if (value.HasValue)
            Colors[color] = value.Value;
        else
            Colors.Remove(color);
    }

    private float? GetScalar(ImGuiStyleVar variable) => Scalars.TryGetValue(variable, out var value) ? value : null;

    private void SetScalar(ImGuiStyleVar variable, float? value)
    {
        if (value.HasValue)
            Scalars[variable] = value.Value;
        else
            Scalars.Remove(variable);
    }

    private Vector2? GetVector(ImGuiStyleVar variable) => Vectors.TryGetValue(variable, out var value) ? value : null;

    private void SetVector(ImGuiStyleVar variable, Vector2? value)
    {
        if (value.HasValue)
            Vectors[variable] = value.Value;
        else
            Vectors.Remove(variable);
    }
}
