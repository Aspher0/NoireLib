using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Every control drawn with NoireUI's own widgets where one exists and plain ImGui elsewhere, coloured by the active
/// theme. The controls of <see cref="StockSkin"/>; derive from it to change one control and keep the others.
/// </summary>
public class StockControlSkin : IControlSkin
{
    private const int MaxCachedLabels = 512;

    private static readonly ButtonStyle Blank = new();

    private readonly ButtonStyle buttonScratch = new();
    private readonly ToggleStyle switchScratch = new();
    private readonly NumberStyle numberScratch = new();
    private readonly DurationStyle durationScratch = new() { ShowPreview = false };
    private readonly SliderStyle sliderScratch = new();
    private readonly Dictionary<(string Label, int Count), string> tabLabels = new();
    private readonly Dictionary<uint, int> tabIndexSeen = new();
    private readonly Dictionary<uint, UiImageSource> gameIcons = new();

    /// <summary>A list row is one frame tall.</summary>
    public virtual float RowHeight => ImGui.GetFrameHeight();

    /// <summary>A <see cref="NoireButtons"/> button of the tone, dimmed while disabled.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="icon">An icon before the label.</param>
    /// <param name="tone">What the button means.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <param name="width">The width in pixels, or 0 to fit the label.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public virtual bool Button(string id, string label, NoireIcon? icon = null, ButtonTone tone = ButtonTone.Neutral, bool enabled = true, float width = 0f)
    {
        var style = PrepareButton(tone, enabled);
        style.NoireIcon = icon;

        ImGui.PushID(id);
        ImGui.BeginDisabled(!enabled);
        var clicked = NoireButtons.Button(label, style, new Vector2(width, 0f));
        ImGui.EndDisabled();
        ImGui.PopID();

        return clicked && enabled;
    }

    /// <summary>A square ghost <see cref="NoireButtons"/> button carrying the icon, accented while on.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="tooltip">The tooltip, also shown while disabled.</param>
    /// <param name="on">Whether it shows as active.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public virtual bool IconButton(string id, NoireIcon icon, string tooltip, bool on = false, bool enabled = true)
    {
        var style = PrepareButton(on ? ButtonTone.Accent : ButtonTone.Ghost, enabled);
        style.NoireIcon = icon;
        style.Padding = Vector2.Zero;

        var side = ImGui.GetFrameHeight();

        ImGui.PushID(id);
        ImGui.BeginDisabled(!enabled);
        var clicked = NoireButtons.Button("##i", style, new Vector2(side, side));
        ImGui.EndDisabled();
        ImGui.PopID();

        if (tooltip.Length > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            PlainTooltip(tooltip);

        return clicked && enabled;
    }

    /// <summary>A danger <see cref="NoireButtons"/> hold button filling as it is held.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="hold">The hold state.</param>
    /// <param name="seconds">How long a full hold takes, or 0 for <see cref="NoireButtons.DefaultHoldSeconds"/>.</param>
    /// <param name="width">The width in pixels, or 0 to fit the label.</param>
    /// <returns>True on the frame the hold completes.</returns>
    public virtual bool HoldButton(string id, string label, NoireHold hold, float seconds = 0f, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(hold);

        var style = PrepareButton(ButtonTone.Danger, true);

        ImGui.PushID(id);
        var fired = NoireButtons.Hold(label, hold, seconds, style, new Vector2(width, 0f));
        ImGui.PopID();

        return fired;
    }

    /// <summary>ImGui's checkbox.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="value">The value.</param>
    /// <returns>True on the frame the value changes.</returns>
    public virtual bool Checkbox(string id, string label, ref bool value)
    {
        ImGui.PushID(id);
        var changed = ImGui.Checkbox(label, ref value);
        ImGui.PopID();
        return changed;
    }

    /// <summary>A <see cref="NoireButtons.Toggle"/> switch, in the danger colour when asked.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="danger">Whether the on state is drawn as dangerous.</param>
    /// <returns>True on the frame the value changes.</returns>
    public virtual bool Switch(string id, ref bool value, bool danger = false)
    {
        switchScratch.OnColor = danger ? NoireTheme.Current.Resolve(ThemeColor.Danger) : null;
        return NoireButtons.Toggle(ScopedLabel("##switch", id), ref value, switchScratch);
    }

    /// <summary>Joined <see cref="NoireButtons"/> segments, the selected one in the accent colour.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="index">The selected option.</param>
    /// <param name="width">The total width in pixels, or 0 to fit the labels.</param>
    /// <returns>True on the frame the selection changes.</returns>
    public virtual bool Segmented(string id, ReadOnlySpan<string> options, ref int index, float width)
    {
        if (options.Length == 0)
            return false;

        var theme = NoireTheme.Current;
        var padding = theme.ResolveFramePadding();
        var segmentWidth = width / options.Length;

        if (width <= 0f)
        {
            var widest = 0f;

            foreach (var option in options)
                widest = MathF.Max(widest, NoireText.CalcSizeInCurrentFont(option).X);

            segmentWidth = widest + (padding.X * 2f);
        }

        var accent = theme.Resolve(ThemeColor.Accent);
        var changed = false;

        ImGui.PushID(id);
        ImGui.BeginGroup();

        for (var i = 0; i < options.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, NoireUI.Scaled(1f));

            var selected = i == index;
            var style = PrepareButton(ButtonTone.Neutral, true);
            style.Color = selected ? accent : theme.Resolve(ThemeColor.SurfaceSunken);
            style.TextColor = selected ? theme.On(accent) : theme.Resolve(ThemeColor.TextMuted);

            ImGui.PushID(i);

            if (NoireButtons.Button(options[i], style, new Vector2(segmentWidth, ImGui.GetFrameHeight())) && !selected)
            {
                index = i;
                changed = true;
            }

            ImGui.PopID();
        }

        ImGui.EndGroup();
        ImGui.PopID();
        return changed;
    }

    /// <summary>ImGui's combo.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="options">The option labels.</param>
    /// <param name="index">The selected option.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the selection changes.</returns>
    public virtual bool Combo(string id, ReadOnlySpan<string> options, ref int index, float width)
    {
        if (options.Length == 0)
            return false;

        var changed = false;

        ImGui.PushID(id);
        ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo("##combo", options[Math.Clamp(index, 0, options.Length - 1)]))
        {
            for (var i = 0; i < options.Length; i++)
            {
                ImGui.PushID(i);

                if (ImGui.Selectable(options[i], i == index) && i != index)
                {
                    index = i;
                    changed = true;
                }

                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        ImGui.PopID();
        return changed;
    }

    /// <summary>A <see cref="NoireInputs.Number(string, ref int, NumberStyle)"/> field kept within the bounds.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the value changes.</returns>
    public virtual bool Stepper(string id, ref int value, int min, int max, float width)
    {
        numberScratch.Min = min;
        numberScratch.Max = max;
        numberScratch.Width = width;
        numberScratch.Decimals = 0;

        var edited = value;

        if (!NoireInputs.Number(ScopedLabel("###stepper", id), ref edited, numberScratch))
            return false;

        edited = Math.Clamp(edited, min, max);

        if (edited == value)
            return false;

        value = edited;
        return true;
    }

    /// <summary>A <see cref="NoireInputs.Duration"/> field kept within the bounds.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The shortest value.</param>
    /// <param name="max">The longest value.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the value changes.</returns>
    public virtual bool Duration(string id, ref TimeSpan value, TimeSpan min, TimeSpan max, float width)
    {
        durationScratch.Min = min;
        durationScratch.Max = max;
        durationScratch.Width = width;

        return NoireInputs.Duration(ScopedLabel("###duration", id), ref value, durationScratch);
    }

    /// <summary>A <see cref="NoireSliders.Float"/> slider, as wide as asked.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The low end.</param>
    /// <param name="max">The high end.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="format">A .NET numeric format for the value shown.</param>
    /// <returns>True on the frames the value changes.</returns>
    public virtual bool Slider(string id, ref float value, float min, float max, float width, string format = "0.##")
    {
        sliderScratch.ValueFormat = format;

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        var changed = NoireSliders.Float(ScopedLabel("###slider", id), ref value, min, max, sliderScratch);
        ImGui.PopTextWrapPos();

        return changed;
    }

    /// <summary>ImGui's text field with a hint.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="text">The text.</param>
    /// <param name="hint">The hint shown while empty.</param>
    /// <param name="width">The width in pixels.</param>
    /// <returns>True on the frame the text changes.</returns>
    public virtual bool Search(string id, ref string text, string hint, float width)
    {
        ImGui.PushID(id);
        ImGui.SetNextItemWidth(width);
        var changed = ImGui.InputTextWithHint("##search", hint, ref text, 256);
        ImGui.PopID();
        return changed;
    }

    /// <summary>ImGui's tab bar, counts after the labels.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="tabs">The tabs.</param>
    /// <param name="index">The selected tab.</param>
    /// <returns>True on the frame the selection changes.</returns>
    public virtual bool Tabs(string id, ReadOnlySpan<TabItem> tabs, ref int index)
    {
        ImGui.PushID(id);

        if (!ImGui.BeginTabBar("##tabs"))
        {
            ImGui.PopID();
            return false;
        }

        var bar = ImGui.GetID("##tabs");
        var external = !tabIndexSeen.TryGetValue(bar, out var seen) || seen != index;
        var changed = false;

        for (var i = 0; i < tabs.Length; i++)
        {
            var flags = external && i == index ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

            ImGui.PushID(i);
            var open = ImGui.BeginTabItem(TabLabel(tabs[i]), flags);
            ImGui.PopID();

            if (!open)
                continue;

            if (index != i && !external)
            {
                index = i;
                changed = true;
            }

            ImGui.EndTabItem();
        }

        tabIndexSeen[bar] = index;
        ImGui.EndTabBar();
        ImGui.PopID();
        return changed;
    }

    /// <summary>A muted info glyph showing the text on hover.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="text">The text.</param>
    public virtual void HelpMark(string id, string text)
    {
        NoireIcons.Draw(NoireIcon.Info, NoireUI.Unscaled(ImGui.GetTextLineHeight()), NoireTheme.Current.Resolve(ThemeColor.TextMuted));

        if (ImGui.IsItemHovered())
            PlainTooltip(text);
    }

    /// <summary>The title in the accent colour over a separator.</summary>
    /// <param name="title">The title.</param>
    public virtual void Section(string title)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.Accent));
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    /// <summary>Wrapped text in the tone's colour, the detail muted below.</summary>
    /// <param name="tone">The tone.</param>
    /// <param name="text">The text.</param>
    /// <param name="detail">The second paragraph.</param>
    public virtual void Notice(NoticeTone tone, string text, string? detail = null)
    {
        var theme = NoireTheme.Current;
        var color = theme.Resolve(tone switch
        {
            NoticeTone.Warning => ThemeColor.Warning,
            NoticeTone.Danger => ThemeColor.Danger,
            NoticeTone.Success => ThemeColor.Success,
            _ => ThemeColor.Info,
        });

        ImGui.PushTextWrapPos(0f);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        if (detail != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, theme.Resolve(ThemeColor.TextMuted));
            ImGui.TextUnformatted(detail);
            ImGui.PopStyleColor();
        }

        ImGui.PopTextWrapPos();
    }

    /// <summary>A small button filled with the colour while selected.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="label">The label.</param>
    /// <param name="selected">Whether it is selected.</param>
    /// <param name="color">Its colour, or <see langword="null"/> for the accent.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public virtual bool Chip(string id, string label, bool selected, Vector4? color = null)
    {
        var accent = color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

        ImGui.PushID(id);
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? ColorHelper.ScaleAlpha(accent, 0.45f) : Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorHelper.ScaleAlpha(accent, 0.6f));
        var clicked = ImGui.SmallButton(label);
        ImGui.PopStyleColor(2);
        ImGui.PopID();
        return clicked;
    }

    /// <summary>The text in the tone's colour.</summary>
    /// <param name="text">The text.</param>
    /// <param name="tone">The tone.</param>
    public virtual void Badge(string text, BadgeTone tone)
    {
        var theme = NoireTheme.Current;

        ImGui.PushStyleColor(ImGuiCol.Text, theme.Resolve(tone switch
        {
            BadgeTone.Accent => ThemeColor.Accent,
            BadgeTone.Success => ThemeColor.Success,
            _ => ThemeColor.TextMuted,
        }));

        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>The game icon, or an outlined square with a question mark.</summary>
    /// <param name="iconId">The game icon id.</param>
    /// <param name="size">The side in pixels.</param>
    public virtual void GameIcon(uint iconId, float size)
    {
        var square = new Vector2(size, size);

        if (iconId != 0 && NoireService.IsInitialized() && GameIconSource(iconId).GetWrap() is { } wrap)
        {
            ImGui.Image(wrap.Handle, square);
            return;
        }

        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(square);

        using var draw = UiDraw.Begin();

        if (draw.List.IsNull)
            return;

        var color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        var mark = NoireText.CalcSizeInCurrentFont("?");
        draw.List.AddRect(min, min + square, color, MathF.Min(3f, size * 0.15f));
        draw.List.AddText(min + ((square - mark) * 0.5f), color, "?");
    }

    /// <summary>Muted text centred in the width.</summary>
    /// <param name="text">The message.</param>
    public virtual void Empty(string text)
    {
        var width = NoireText.CalcSizeInCurrentFont(text).X;
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ((ImGui.GetContentRegionAvail().X - width) * 0.5f)));
        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>ImGui's separator.</summary>
    public virtual void Separator() => ImGui.Separator();

    /// <summary>A child region scrolled by the list state; the rows it shows are laid out by <see cref="NoireListState"/>.</summary>
    /// <param name="id">An id unique among its siblings.</param>
    /// <param name="size">The list's size in pixels.</param>
    /// <param name="state">The list's scroll state.</param>
    /// <param name="count">How many rows the list has.</param>
    /// <returns>True when the rows should be drawn.</returns>
    public virtual bool BeginList(string id, Vector2 size, NoireListState state, int count)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ImGui.BeginChild(id, size, false))
        {
            ImGui.EndChild();
            return false;
        }

        var stride = RowHeight + ImGui.GetStyle().ItemSpacing.Y;
        var imguiScroll = ImGui.GetScrollY();

        // Only a scroll the user made is handed to the state; handing it every frame would stop a reveal after one step.
        if (MathF.Abs(imguiScroll - state.Scroll) > 0.5f)
            state.ScrollTo(imguiScroll);

        state.Layout(count, stride, ImGui.GetWindowHeight());

        if (MathF.Abs(state.Scroll - imguiScroll) > 0.5f)
            ImGui.SetScrollY(state.Scroll);

        ImGui.SetCursorPosY(state.First * stride);
        return true;
    }

    /// <summary>A selectable row with the icon, the label and the detail or note on the right.</summary>
    /// <param name="id">An id unique within the list.</param>
    /// <param name="row">What the row shows.</param>
    /// <returns>What the mouse did to it.</returns>
    public virtual RowResult Row(string id, in RowInfo row)
    {
        var start = ImGui.GetCursorScreenPos();
        var tinted = row.Tint.HasValue;

        if (row.Tint is { } tint)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, ColorHelper.ScaleAlpha(tint, 0.35f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColorHelper.ScaleAlpha(tint, 0.25f));
        }

        ImGui.PushID(id);
        var clicked = ImGui.Selectable("##row", row.Selected, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0f, RowHeight));
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemActivated();
        var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.PopID();

        if (tinted)
            ImGui.PopStyleColor(2);

        ImGui.SetCursorScreenPos(start);

        if (row.GameIcon != 0)
        {
            GameIcon(row.GameIcon, RowHeight);
            ImGui.SameLine();
        }

        var theme = NoireTheme.Current;
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, theme.Resolve(row.Marked ? ThemeColor.TextDisabled : ThemeColor.Text));
        ImGui.TextUnformatted(row.Label);
        ImGui.PopStyleColor();

        var right = row.Marked ? row.Note : row.Detail;

        if (right != null)
        {
            ImGui.SameLine(max.X - min.X - NoireText.CalcSizeInCurrentFont(right).X - ImGui.GetStyle().FramePadding.X);
            ImGui.PushStyleColor(ImGuiCol.Text, theme.Resolve(ThemeColor.TextMuted));
            ImGui.TextUnformatted(right);
            ImGui.PopStyleColor();
        }

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y + ImGui.GetStyle().ItemSpacing.Y));
        return new RowResult(clicked && !row.Marked, rightClicked, hovered, pressed, min, max);
    }

    /// <summary>Reserves the list's full height and ends the child region.</summary>
    /// <param name="state">The list's scroll state.</param>
    public virtual void EndList(NoireListState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ImGui.SetCursorPosY(state.ContentHeight);
        ImGui.Dummy(Vector2.Zero);
        ImGui.EndChild();
    }

    // A text in ImGui's own tooltip, wrapped.
    private static void PlainTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // Widgets that keep state by label need one unique to the ImGui scope; the scope's hash tells two ids apart.
    private static string ScopedLabel(string prefix, string id) => UiIds.For(prefix, id, (int)ImGui.GetID(id));

    private ButtonStyle PrepareButton(ButtonTone tone, bool enabled)
    {
        var style = buttonScratch;
        style.CopyFrom(Blank);
        style.Tone = tone;

        if (!enabled)
        {
            var theme = NoireTheme.Current;
            style.Tone = ButtonTone.Neutral;
            style.Color = theme.Resolve(ThemeColor.SurfaceSunken);
            style.TextColor = theme.Resolve(ThemeColor.TextDisabled);
        }

        return style;
    }

    // The label and count, formatted once per pair, with an id that does not move with the count.
    private string TabLabel(in TabItem tab)
    {
        var key = (tab.Label, tab.Count);

        if (tabLabels.TryGetValue(key, out var cached))
            return cached;

        if (tabLabels.Count >= MaxCachedLabels)
            tabLabels.Clear();

        var text = tab.Count >= 0 ? tab.Label + " (" + NoireLanguages.Number(tab.Count) + ")###tab" : tab.Label + "###tab";
        tabLabels[key] = text;
        return text;
    }

    private UiImageSource GameIconSource(uint iconId)
    {
        if (!gameIcons.TryGetValue(iconId, out var source))
            gameIcons[iconId] = source = UiImageSource.FromGameIcon(iconId);

        return source;
    }
}
