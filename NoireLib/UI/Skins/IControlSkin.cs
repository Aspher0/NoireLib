using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a skin draws the controls. Every control draws at the ImGui cursor and advances it, like an ImGui widget.
/// </summary>
public interface IControlSkin
{
    /// <summary>The height of one list row, at the current scale.</summary>
    float RowHeight { get; }

    /// <summary>A button.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="icon">An icon before the label.</param>
    /// <param name="tone">What the button means.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <param name="width">The width in pixels, or 0 to fit the label.</param>
    /// <returns>True on the frame it is clicked.</returns>
    bool Button(string id, string label, NoireIcon? icon = null, ButtonTone tone = ButtonTone.Neutral, bool enabled = true, float width = 0f);

    /// <summary>A square icon button with a tooltip.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="tooltip">The tooltip, also shown while disabled.</param>
    /// <param name="on">Whether it shows as active.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <returns>True on the frame it is clicked.</returns>
    bool IconButton(string id, NoireIcon icon, string tooltip, bool on = false, bool enabled = true);

    /// <summary>A button that fires once held long enough, showing the hold's progress.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="hold">The hold state.</param>
    /// <param name="seconds">How long a full hold takes, or 0 for <see cref="NoireButtons.DefaultHoldSeconds"/>.</param>
    /// <param name="width">The width in pixels, or 0 to fit the label.</param>
    /// <returns>True on the frame the hold completes.</returns>
    bool HoldButton(string id, string label, NoireHold hold, float seconds = 0f, float width = 0f);

    /// <summary>A checkbox with its label.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="value">The value.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Checkbox(string id, string label, ref bool value);

    /// <summary>An on and off switch without a label.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="danger">Whether the on state is drawn as dangerous.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Switch(string id, ref bool value, bool danger = false);

    /// <summary>A row of options, one of them selected.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="index">The selected option.</param>
    /// <param name="width">The total width in pixels, or 0 to fit the labels.</param>
    /// <returns>True on the frame the selection changes.</returns>
    bool Segmented(string id, ReadOnlySpan<string> options, ref int index, float width);

    /// <summary>A dropdown.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="index">The selected option.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the selection changes.</returns>
    bool Combo(string id, ReadOnlySpan<string> options, ref int index, float width);

    /// <summary>A whole number with minus and plus.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value, kept within the bounds.</param>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Stepper(string id, ref int value, int min, int max, float width);

    /// <summary>A duration, typed or stepped.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value, kept within the bounds.</param>
    /// <param name="min">The shortest value.</param>
    /// <param name="max">The longest value.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the value changes.</returns>
    bool Duration(string id, ref TimeSpan value, TimeSpan min, TimeSpan max, float width);

    /// <summary>A slider.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The low end.</param>
    /// <param name="max">The high end.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="format">A .NET numeric format for the value shown.</param>
    /// <returns>True on the frames the value changes.</returns>
    bool Slider(string id, ref float value, float min, float max, float width, string format = "0.##");

    /// <summary>A search field with a hint.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="text">The text.</param>
    /// <param name="hint">The hint shown while empty.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the text changes.</returns>
    bool Search(string id, ref string text, string hint, float width);

    /// <summary>A row of tabs with optional icons and counts. Tabs may shrink to their icons when narrow.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="tabs">The tabs.</param>
    /// <param name="index">The selected tab.</param>
    /// <returns>True on the frame the selection changes.</returns>
    bool Tabs(string id, ReadOnlySpan<TabItem> tabs, ref int index);

    /// <summary>A help mark showing a text on hover.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="text">The text.</param>
    void HelpMark(string id, string text);

    /// <summary>A section title.</summary>
    /// <param name="title">The title.</param>
    void Section(string title);

    /// <summary>A notice between content, with an optional muted second paragraph.</summary>
    /// <param name="tone">The tone.</param>
    /// <param name="text">The text.</param>
    /// <param name="detail">The second paragraph.</param>
    void Notice(NoticeTone tone, string text, string? detail = null);

    /// <summary>A toggleable chip.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="selected">Whether it is selected.</param>
    /// <param name="color">Its colour, or <see langword="null"/> for the accent.</param>
    /// <returns>True on the frame it is clicked.</returns>
    bool Chip(string id, string label, bool selected, Vector4? color = null);

    /// <summary>A small label.</summary>
    /// <param name="text">The text.</param>
    /// <param name="tone">The tone.</param>
    void Badge(string text, BadgeTone tone);

    /// <summary>A game icon, or the skin's placeholder when the id is 0 or fails to load.</summary>
    /// <param name="iconId">The game icon id.</param>
    /// <param name="size">The side in pixels.</param>
    void GameIcon(uint iconId, float size);

    /// <summary>The message a list shows when it has nothing to show.</summary>
    /// <param name="text">The message.</param>
    void Empty(string text);

    /// <summary>A separator line.</summary>
    void Separator();

    /// <summary>Starts a virtualized list; rows are drawn for <see cref="NoireListState.First"/> to <see cref="NoireListState.Last"/>.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="size">The list's size in pixels.</param>
    /// <param name="state">The list's scroll state.</param>
    /// <param name="count">How many rows the list has.</param>
    /// <returns>True when the rows should be drawn; <see cref="EndList"/> is called only then.</returns>
    bool BeginList(string id, Vector2 size, NoireListState state, int count);

    /// <summary>One row of the list.</summary>
    /// <param name="id">An id unique within the list.</param>
    /// <param name="row">What the row shows.</param>
    /// <returns>What the mouse did to it.</returns>
    RowResult Row(string id, in RowInfo row);

    /// <summary>Ends the list.</summary>
    /// <param name="state">The list's scroll state.</param>
    void EndList(NoireListState state);
}
