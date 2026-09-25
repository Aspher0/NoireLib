using Dalamud.Bindings.ImGui;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

// A table per group: the name column fits the group's longest name, then the control, then the help mark and, on a
// modified row, the reset button. Tabs, sections and controls come from the active skin's controls.
internal sealed class StockSettingsSkin : ISettingsSkin
{
    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings;
    private const float ControlMaxWidth = 280f;
    private const float PickerCardWidth = 168f;

    internal static readonly StockSettingsSkin Instance = new();

    private bool inTable;
    private int group;
    private int groupFrame = -1;

    public int Tabs(ReadOnlySpan<TabItem> pages, int current)
    {
        var index = current;
        NoireSkins.Active.Controls.Tabs("settings-tabs", pages, ref index);
        return index;
    }

    public void Section(string title) => NoireSkins.Active.Controls.Section(title);

    public void BeginRows(ReadOnlySpan<SettingRowInfo> rows)
    {
        var nameWidth = 0f;

        foreach (var row in rows)
            nameWidth = MathF.Max(nameWidth, NoireText.CalcSizeInCurrentFont(row.Name).X);

        // Numbered from 0 each frame to keep each group's ImGui id, and its controls' state, stable.
        var frame = ImGui.GetFrameCount();

        if (frame != groupFrame)
        {
            groupFrame = frame;
            group = 0;
        }

        var style = ImGui.GetStyle();
        ImGui.PushID(group++);
        inTable = ImGui.BeginTable("##rows", 3, TableFlags);

        if (!inTable)
            return;

        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, nameWidth + style.ItemSpacing.X);
        ImGui.TableSetupColumn("##control", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##extra", ImGuiTableColumnFlags.WidthFixed, (ImGui.GetFrameHeight() * 2f) + style.ItemSpacing.X);
    }

    public SettingRowArea Row(in SettingRowInfo row, SettingControl control, out bool resetClicked)
    {
        resetClicked = false;

        if (!inTable)
            return new SettingRowArea(ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight(), row.Disabled);

        var theme = NoireTheme.Current;
        var controls = NoireSkins.Active.Controls;

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, theme.Resolve(row.Alarm != null ? ThemeColor.Danger : row.Disabled ? ThemeColor.TextDisabled : ThemeColor.Text));
        ImGui.TextUnformatted(row.Name);
        ImGui.PopStyleColor();

        if (row.Alarm is { } alarm && ImGui.IsItemHovered())
            PlainTooltip(alarm);

        ImGui.TableSetColumnIndex(2);

        if (row.Help is { } help)
        {
            ImGui.AlignTextToFramePadding();
            controls.HelpMark(row.Id, help);
        }

        if (row.Modified)
        {
            if (row.Help != null)
                ImGui.SameLine();

            ImGui.PushID(row.Id);
            resetClicked = controls.IconButton("reset", NoireIcon.Refresh, NoireStrings.ResetToDefault.Text, enabled: !row.Disabled);
            ImGui.PopID();
        }

        ImGui.TableSetColumnIndex(1);

        var min = ImGui.GetCursorScreenPos();
        var width = MathF.Min(ImGui.GetContentRegionAvail().X, NoireUI.Scaled(ControlMaxWidth));
        var height = ImGui.GetFrameHeight();

        if (row.Disabled && row.DisabledReason is { } reason && ImGui.IsMouseHoveringRect(min, min + new Vector2(width, height)))
            PlainTooltip(reason);

        return new SettingRowArea(min, width, height, row.Disabled);
    }

    public void EndRows()
    {
        if (inTable)
            ImGui.EndTable();

        inTable = false;
        ImGui.PopID();
    }

    public bool Toggle(in SettingRowArea area, string id, ref bool value)
    {
        ImGui.BeginDisabled(area.Disabled);
        var changed = NoireSkins.Active.Controls.Switch(id, ref value);
        ImGui.EndDisabled();
        return changed && !area.Disabled;
    }

    public bool Choice(in SettingRowArea area, string id, ReadOnlySpan<string> options, ref int index)
    {
        ImGui.BeginDisabled(area.Disabled);
        var changed = NoireSkins.Active.Controls.Combo(id, options, ref index, area.Width);
        ImGui.EndDisabled();
        return changed && !area.Disabled;
    }

    public bool Number(in SettingRowArea area, string id, ref int value, int min, int max)
    {
        ImGui.BeginDisabled(area.Disabled);
        var changed = NoireSkins.Active.Controls.Stepper(id, ref value, min, max, area.Width);
        ImGui.EndDisabled();
        return changed && !area.Disabled;
    }

    public bool Duration(in SettingRowArea area, string id, ref TimeSpan value, TimeSpan min, TimeSpan max)
    {
        ImGui.BeginDisabled(area.Disabled);
        var changed = NoireSkins.Active.Controls.Duration(id, ref value, min, max, area.Width);
        ImGui.EndDisabled();
        return changed && !area.Disabled;
    }

    public bool SkinPicker(IReadOnlyList<NoireSkin> skins, NoireSkin active, out NoireSkin picked)
    {
        picked = active;

        if (skins.Count == 0)
            return false;

        var style = ImGui.GetStyle();
        var theme = NoireTheme.Current;
        var gap = style.ItemSpacing.X;
        var available = ImGui.GetContentRegionAvail().X;
        var width = MathF.Min(NoireUI.Scaled(PickerCardWidth), (available - (gap * (skins.Count - 1))) / skins.Count);
        var pad = style.FramePadding.X;
        var previewHeight = width * 0.6f;
        var size = new Vector2(width, previewHeight + ImGui.GetTextLineHeight() + (pad * 3f));
        var changed = false;

        for (var i = 0; i < skins.Count; i++)
        {
            var skin = skins[i];
            var on = ReferenceEquals(skin, active);

            if (i > 0)
                ImGui.SameLine(0f, gap);

            var min = ImGui.GetCursorScreenPos();
            var max = min + size;

            ImGui.PushID(skin.Id);
            var clicked = ImGui.InvisibleButton("##skin", size);
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();

            skin.DrawPreview(new UiRect(min + new Vector2(pad, pad), new Vector2(width - (pad * 2f), previewHeight)), NoireSkins.ThemeOf(skin));

            using (var draw = UiDraw.Begin())
            {
                if (!draw.List.IsNull)
                {
                    var name = skin.Name.Text;
                    var nameSize = NoireText.CalcSizeInCurrentFont(name);
                    var namePos = new Vector2(min.X + ((width - nameSize.X) * 0.5f), max.Y - pad - nameSize.Y);
                    var text = theme.Resolve(on || hovered ? ThemeColor.Text : ThemeColor.TextMuted);
                    var ring = theme.Resolve(on ? ThemeColor.Accent : hovered ? ThemeColor.Border : ThemeColor.SurfaceSunken);

                    draw.List.AddText(namePos, ImGui.GetColorU32(text), name);
                    draw.List.AddRect(min, max, ImGui.GetColorU32(ring), style.FrameRounding, ImDrawFlags.None, on ? NoireUI.Scaled(2f) : 1f);
                }
            }

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (clicked && !on)
            {
                picked = skin;
                changed = true;
            }
        }

        return changed;
    }

    private static void PlainTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
