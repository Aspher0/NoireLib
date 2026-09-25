using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Changelog;

// The changelog's drawing, shared by the built-in window and the skinned one.
internal static class ChangelogDraw
{
    private const int MaxCachedLabels = 64;

    // Labels built once per version: a frame that draws the selector formats nothing.
    private static readonly Dictionary<ChangelogVersion, Labels> LabelCache = new(ReferenceEqualityComparer.Instance);

    internal static void VersionSelector(NoireChangelogManager manager)
    {
        ImGui.Text(NoireStrings.ChangelogSelectVersion.Text);
        ImGui.SameLine();

        var currentChangelog = manager.SelectedVersion;
        var selectedVersion = currentChangelog?.Version;

        ImGui.SetNextItemWidth(200f);

        if (ImGui.BeginCombo("##VersionSelector", currentChangelog != null ? LabelsOf(currentChangelog).Version : string.Empty, ImGuiComboFlags.HeightRegular))
        {
            foreach (var changelog in manager.Versions)
            {
                var version = changelog.Version;
                var isSelected = version == selectedVersion;

                if (ImGui.Selectable(LabelsOf(changelog).Item, isSelected))
                {
                    manager.SelectVersion(version);
                    currentChangelog = manager.SelectedVersion;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (currentChangelog != null)
        {
            var labels = LabelsOf(currentChangelog);
            ImGui.SameLine();
            ImGui.TextDisabled(labels.Date);

            if (labels.Title != null)
            {
                var availableWidth = ImGui.GetContentRegionAvail().X;

                if (availableWidth < 100f)
                {
                    ImGui.NewLine();
                }
                else
                {
                    ImGui.SameLine();
                }

                var titleColor = currentChangelog.TitleColor ?? new Vector4(1f, 1f, 1f, 1f);
                using (var pushed = UiPush.Color(ImGuiCol.Text, titleColor))
                {
                    pushed.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                    ImGui.TextWrapped(labels.Title);
                }
            }
        }
    }

    internal static void Content(NoireChangelogManager manager, float height)
    {
        var currentChangelog = manager.SelectedVersion;
        if (currentChangelog == null)
        {
            ImGui.TextDisabled(NoireStrings.ChangelogNone.Text);
            return;
        }


        var bgColor = new Vector4(0.5f, 0.5f, 0.5f, 0.05f);
        using (UiPush.Color(ImGuiCol.Border, bgColor))
        {
            if (ImGui.BeginChild("##ChangelogContentChild", new Vector2(0, height), false))
            {
                var padding = 5f;
                ImGui.Dummy(new Vector2(0, padding));
                // Scales with the global UI scale.
                ImGui.Indent(padding);

                using (UiPush.TextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - padding))
                {
                    if (!string.IsNullOrWhiteSpace(currentChangelog.Description))
                    {
                        ImGui.TextWrapped(currentChangelog.Description);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                    }

                    foreach (var entry in currentChangelog.Entries)
                    {
                        // Effects move and recolor the entry as drawn, never the entries after it.
                        using var effects = entry.Gradient != null || entry.Motion != null
                            ? NoireEffects.Begin(entry.Gradient, entry.Motion)
                            : default;

                        DrawChangelogEntry(entry);
                    }
                }

                ImGui.Unindent(padding);
                ImGui.Dummy(new Vector2(0, padding));
            }

            ImGui.EndChild();
        }
    }

    private static void DrawChangelogEntry(ChangelogEntry entry)
    {
        if (entry.IsSeparator)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            return;
        }

        if (entry.IsRaw)
        {
            entry.RawAction?.Invoke();
            return;
        }

        if (entry.IsHeader)
        {
            ImGui.Spacing();

            var headerIndent = 20f;
            var headerTotalIndent = entry.IndentLevel * headerIndent;

            if (headerTotalIndent > 0)
            {
                var currentPosX = ImGui.GetCursorPosX();
                ImGui.SetCursorPosX(currentPosX + headerTotalIndent);
            }

            if (entry.HasBullet)
            {
                using (UiPush.Color(ImGuiCol.Text, entry.TextColor ?? new Vector4(1f, 1f, 1f, 1f)))
                    ImGui.Bullet();

                ImGui.SameLine();
            }
            else if (entry.Icon.HasValue)
            {
                using (UiPush.Font(UiIconFont.Current))
                {
                    var iconColor = entry.IconColor ?? new Vector4(1f, 1f, 1f, 1f);
                    ImGui.TextColored(iconColor, UiValueText.Icon(entry.Icon.Value));
                }
                ImGui.SameLine();
            }

            var headerTextColor = entry.TextColor ?? new Vector4(1f, 1f, 1f, 1f);
            using (UiPush.Color(ImGuiCol.Text, headerTextColor))
            {
                var originalPos = ImGui.GetCursorPos();

                ImGui.SetCursorPos(new Vector2(originalPos.X + 0.5f, originalPos.Y));
                ImGui.TextUnformatted(entry.Text);
                ImGui.SetCursorPos(originalPos);
                ImGui.TextUnformatted(entry.Text);
            }

            ImGui.Spacing();
            return;
        }

        var startPos = ImGui.GetCursorPos();
        var levelIndent = 20f;
        var totalIndent = entry.IndentLevel * levelIndent;

        if (totalIndent > 0)
        {
            ImGui.SetCursorPosX(startPos.X + totalIndent);
        }

        var entryTextColor = entry.TextColor ?? new Vector4(1f, 1f, 1f, 1f);

        bool hasButton = !string.IsNullOrWhiteSpace(entry.ButtonText) && entry.ButtonAction != null;
        bool shouldPlaceButtonOnNewLine = false;
        var textStartPosX = 0f;

        using (UiPush.Color(ImGuiCol.Text, entryTextColor))
        {

            float prefixWidth = 0f;

            bool willShowBullet = entry.HasBullet;
            bool willShowIcon = !entry.HasBullet && entry.Icon.HasValue;

            if (willShowIcon)
            {
                using (UiPush.Font(UiIconFont.Current))
                {
                    prefixWidth = ImGui.CalcTextSize(UiValueText.Icon(entry.Icon!.Value)).X + ImGui.GetStyle().ItemSpacing.X;
                }
            }
            else if (willShowBullet)
            {
                prefixWidth = ImGui.CalcTextSize("• ").X;
            }

            if (hasButton)
            {
                var buttonWidth = ImGui.CalcTextSize(entry.ButtonText).X + 35f;
                var availableWidth = ImGui.GetContentRegionAvail().X;
                var textWidth = ImGui.CalcTextSize(entry.Text ?? string.Empty).X;

                if (textWidth + prefixWidth + buttonWidth + 10f > availableWidth)
                {
                    shouldPlaceButtonOnNewLine = true;
                }
            }

            if (willShowBullet)
            {
                ImGui.Bullet();
                ImGui.SameLine();

                textStartPosX = ImGui.GetCursorPosX();

                if (shouldPlaceButtonOnNewLine)
                {
                    ImGui.TextWrapped(entry.Text);
                }
                else
                {
                    ImGui.TextUnformatted(entry.Text);
                }
            }
            else if (willShowIcon)
            {
                var iconColor = entry.IconColor ?? new Vector4(0.7f, 0.7f, 0.7f, 1f);
                using (UiPush.Font(UiIconFont.Current))
                {
                    ImGui.TextColored(iconColor, UiValueText.Icon(entry.Icon!.Value));
                }
                ImGui.SameLine();

                textStartPosX = ImGui.GetCursorPosX();

                if (shouldPlaceButtonOnNewLine)
                {
                    ImGui.TextWrapped(entry.Text);
                }
                else
                {
                    ImGui.TextUnformatted(entry.Text);
                }
            }
            else
            {
                textStartPosX = ImGui.GetCursorPosX();

                if (shouldPlaceButtonOnNewLine)
                {
                    ImGui.TextWrapped(entry.Text);
                }
                else
                {
                    ImGui.TextUnformatted(entry.Text);
                }
            }
        }

        if (hasButton)
        {
            if (!shouldPlaceButtonOnNewLine)
            {
                ImGui.SameLine();
            }
            else
            {
                ImGui.SetCursorPosX(textStartPosX);
            }

            var colorCount = 0;

            if (entry.ButtonColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, entry.ButtonColor.Value);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, entry.ButtonColor.Value * 1.1f);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, entry.ButtonColor.Value * 0.9f);
                colorCount += 3;
            }

            if (entry.ButtonTextColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, entry.ButtonTextColor.Value);
                colorCount++;
            }

            var buttonSize = new Vector2(ImGui.CalcTextSize(entry.ButtonText).X + 20f, 0);

            ImGui.Button(entry.ButtonText, buttonSize);

            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    entry.ButtonAction?.Invoke(ImGuiMouseButton.Left);
                }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    entry.ButtonAction?.Invoke(ImGuiMouseButton.Right);
                }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    entry.ButtonAction?.Invoke(ImGuiMouseButton.Middle);
                }
            }

            if (colorCount > 0)
            {
                ImGui.PopStyleColor(colorCount);
            }
        }

        ImGui.Spacing();
    }

    internal static void Footer(NoireChangelogManager manager)
    {
        // Built once per language: a frame that draws the footer formats nothing.
        if (closeRevision != NoireLanguages.Revision)
        {
            closeRevision = NoireLanguages.Revision;
            closeLabel = NoireStrings.Close.Text + "##changelogclose";
        }

        var buttonWidth = MathF.Max(100f, ImGui.CalcTextSize(closeLabel, true).X + (ImGui.GetStyle().FramePadding.X * 2f));
        var windowWidth = ImGui.GetWindowWidth();

        ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

        if (ImGui.Button(closeLabel, new Vector2(buttonWidth, 0)))
            manager.CloseWindow();
    }

    private static string closeLabel = string.Empty;
    private static int closeRevision = -1;

    private static Labels LabelsOf(ChangelogVersion changelog)
    {
        if (LabelCache.TryGetValue(changelog, out var labels))
            return labels;

        if (LabelCache.Count >= MaxCachedLabels)
            LabelCache.Clear();

        var version = changelog.Version.ToString(4);
        labels = new Labels(
            version,
            version + "##version_" + version,
            "(" + changelog.Date + ")",
            string.IsNullOrWhiteSpace(changelog.Title) ? null : "- " + changelog.Title);
        LabelCache[changelog] = labels;
        return labels;
    }

    private sealed record Labels(string Version, string Item, string Date, string? Title);
}
