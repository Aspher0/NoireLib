using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>
/// Changelog window that displays changelog entries using ImGui.
/// </summary>
public class ChangelogWindow : Window, IDisposable
{
    private NoireChangelogManager ChangelogManager { get; init; }
    private Version? selectedVersion = null;
    private Version? previousSelectedVersion = null;
    private ChangelogVersion? currentChangelog = null;
    private Version[] availableVersions = [];

    /// <summary>
    /// Gets the unique window name based on the <see cref="NoireChangelogManager.WindowName"/> and <see cref="NoireModuleBase.GetUniqueIdentifier"/>.
    /// </summary>
    /// <param name="noireChangelogManager"></param>
    /// <returns></returns>
    private static string GetWindowName(NoireChangelogManager noireChangelogManager)
    {
        var windowName = noireChangelogManager.WindowName + "##";
        windowName += noireChangelogManager.GetUniqueIdentifier();
        return windowName;
    }

    public ChangelogWindow(NoireChangelogManager noireChangelogManager) : base(GetWindowName(noireChangelogManager), ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(750, 500);

        ChangelogManager = noireChangelogManager;

        UpdateVersions();
        UpdateTitleBarButtons(noireChangelogManager.TitleBarButtons);
    }

    /// <summary>
    /// Updates the list of available versions from the ChangelogManager.
    /// </summary>
    public void UpdateVersions()
    {
        selectedVersion = null;
        currentChangelog = null;
        availableVersions = Array.Empty<Version>();

        var versions = ChangelogManager.GetAllVersions();
        availableVersions = versions.Select(v => v.Version).ToArray();

        if (availableVersions.Length > 0)
        {
            selectedVersion = availableVersions[0];
            currentChangelog = ChangelogManager.GetVersion(selectedVersion);
        }

        if (availableVersions.Length == 0)
            CloseWindow();
    }

    /// <summary>
    /// Updates the title bar buttons of the window.
    /// </summary>
    /// <param name="titleBarButtons">The new list of title bar buttons.</param>
    public void UpdateTitleBarButtons(List<TitleBarButton> titleBarButtons)
    {
        TitleBarButtons.Clear();
        foreach (var titleBarButton in titleBarButtons)
            TitleBarButtons.Add(titleBarButton);
    }

    /// <summary>
    /// Updates the window name, keeping its uniqueness.
    /// </summary>
    public void UpdateWindowName() => WindowName = GetWindowName(ChangelogManager);

    /// <summary>
    /// Shows the changelog window for a specific version. If no version is provided, it shows the latest version.
    /// </summary>
    /// <param name="version">The Version object to show.</param>
    public void ShowChangelogForVersion(Version? version = null)
    {
        if (availableVersions.Length == 0)
        {
            NoireService.NotificationManager.AddNotification(new Notification
            {
                Content = "There are no changelogs available",
                Title = "No changelog available",
                InitialDuration = TimeSpan.FromMilliseconds(3000),
                Type = NotificationType.Warning,
            });
            return;
        }

        if (version != null && availableVersions.Contains(version))
            selectedVersion = version;
        else
            selectedVersion = availableVersions[0];

        currentChangelog = ChangelogManager.GetVersion(selectedVersion);
        previousSelectedVersion = selectedVersion;

        IsOpen = true;

        // Notify manager that window was opened
        ChangelogManager.OnWindowOpened(selectedVersion);
    }

    /// <summary>
    /// Closes the changelog window.
    /// </summary>
    public void CloseWindow()
    {
        if (IsOpen)
        {
            IsOpen = false;
            ChangelogManager.OnWindowClosed();
        }
    }

    public override void Draw()
    {
        DrawVersionSelector();
        ImGui.Dummy(new Vector2(0, 3));
        DrawChangelogContent();
        ImGui.Dummy(new Vector2(0, 3));
        DrawFooter();
    }

    private void DrawVersionSelector()
    {
        ImGui.Text("Select Version:");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(200f);
        var selectedVersionString = selectedVersion?.ToString(4) ?? string.Empty;
        if (ImGui.BeginCombo("##VersionSelector", selectedVersionString))
        {
            foreach (var version in availableVersions)
            {
                bool isSelected = version == selectedVersion;
                var versionString = version.ToString(4);
                if (ImGui.Selectable($"{versionString}##version_{versionString}", isSelected))
                {
                    var oldVersion = selectedVersion;
                    selectedVersion = version;
                    currentChangelog = ChangelogManager.GetVersion(selectedVersion);

                    // Notify manager that version changed
                    if (oldVersion != selectedVersion)
                    {
                        ChangelogManager.OnVersionChanged(oldVersion, selectedVersion);
                        previousSelectedVersion = selectedVersion;
                    }
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Show selected version info
        if (currentChangelog != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({currentChangelog.Date})");

            if (!string.IsNullOrWhiteSpace(currentChangelog.Title))
            {
                var availableWidth = ImGui.GetContentRegionAvail().X;

                // Start on a new line if there's not enough space
                if (availableWidth < 100f)
                {
                    ImGui.NewLine();
                }
                else
                {
                    ImGui.SameLine();
                }

                var titleColor = currentChangelog.TitleColor ?? new Vector4(1f, 1f, 1f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, titleColor);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                ImGui.TextWrapped($"- {currentChangelog.Title}");
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }
    }

    private void DrawChangelogContent()
    {
        if (currentChangelog == null)
        {
            ImGui.TextDisabled("No changelog available for this version.");
            return;
        }

        var availHeight = ImGui.GetContentRegionAvail().Y - 40f; // Reserve space for footer

        var bgColor = new Vector4(0.5f, 0.5f, 0.5f, 0.05f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        ImGui.BeginChild("##ChangelogContent", new Vector2(0, availHeight), false);

        var padding = 5f;
        ImGui.Dummy(new Vector2(0, padding));
        ImGui.Indent(padding);

        // Set wrap position accounting for padding
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - padding);

        if (!string.IsNullOrWhiteSpace(currentChangelog.Description))
        {
            ImGui.TextWrapped(currentChangelog.Description);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        foreach (var entry in currentChangelog.Entries)
        {
            DrawChangelogEntry(entry);
        }

        ImGui.PopTextWrapPos();
        ImGui.Unindent(padding);
        ImGui.Dummy(new Vector2(0, padding));

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawChangelogEntry(ChangelogEntry entry)
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

            // Apply indentation for headers
            var headerIndent = 20f;
            var headerTotalIndent = entry.IndentLevel * headerIndent;

            if (headerTotalIndent > 0)
            {
                var currentPosX = ImGui.GetCursorPosX();
                ImGui.SetCursorPosX(currentPosX + headerTotalIndent);
            }

            // Headers with bullets
            if (entry.HasBullet)
            {
                ImGui.Bullet();
                ImGui.SameLine();
            }
            // Headers with icons (optional)
            else if (entry.Icon.HasValue)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                var iconColor = entry.IconColor ?? new Vector4(1f, 1f, 1f, 1f);
                ImGui.TextColored(iconColor, entry.Icon.Value.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
            }

            var headerTextColor = entry.TextColor ?? new Vector4(1f, 1f, 1f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Text, headerTextColor);

            var originalPos = ImGui.GetCursorPos();

            ImGui.SetCursorPos(new Vector2(originalPos.X + 0.5f, originalPos.Y));
            ImGui.TextUnformatted(entry.Text);
            ImGui.SetCursorPos(originalPos);
            ImGui.TextUnformatted(entry.Text);

            ImGui.PopStyleColor();

            ImGui.Spacing();
            return;
        }

        // Regular entry
        var startPos = ImGui.GetCursorPos();
        var levelIndent = 20f;
        var totalIndent = entry.IndentLevel * levelIndent;

        if (totalIndent > 0)
        {
            ImGui.SetCursorPosX(startPos.X + totalIndent);
        }

        var entryTextColor = entry.TextColor ?? new Vector4(1f, 1f, 1f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, entryTextColor);

        // Check if we have a button to determine text wrapping behavior
        bool hasButton = !string.IsNullOrWhiteSpace(entry.ButtonText) && entry.ButtonAction != null;
        bool shouldPlaceButtonOnNewLine = false;

        // Calculate prefix width (bullet or icon)
        float prefixWidth = 0f;

        // Determine what prefix we're using
        bool willShowBullet = entry.HasBullet;
        bool willShowIcon = !entry.HasBullet && entry.Icon.HasValue;

        if (willShowIcon)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            prefixWidth = ImGui.CalcTextSize(entry.Icon!.Value.ToIconString()).X + ImGui.GetStyle().ItemSpacing.X;
            ImGui.PopFont();
        }
        else if (willShowBullet)
        {
            prefixWidth = ImGui.CalcTextSize("• ").X;
        }

        if (hasButton)
        {
            // Calculate if text will wrap
            var buttonWidth = ImGui.CalcTextSize(entry.ButtonText).X + 35f;
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var textWidth = ImGui.CalcTextSize(entry.Text ?? string.Empty).X;

            // If text + button doesn't fit on one line, put button on new line
            if (textWidth + prefixWidth + buttonWidth + 10f > availableWidth)
            {
                shouldPlaceButtonOnNewLine = true;
            }
        }

        // Store position after bullet/icon for button alignment
        var textStartPosX = 0f;

        // Draw prefix (bullet or icon) + text
        if (willShowBullet)
        {
            // Entry with bullet
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
            // Entry with icon
            var iconColor = entry.IconColor ?? new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(iconColor, entry.Icon!.Value.ToIconString());
            ImGui.PopFont();
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
            // Entry with no prefix (no bullet, no icon)
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

        ImGui.PopStyleColor();

        // Draw button
        if (hasButton)
        {
            if (!shouldPlaceButtonOnNewLine)
            {
                ImGui.SameLine();
            }
            else
            {
                // Position button at the same X position as the text (after bullet/icon)
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

    private void DrawFooter()
    {
        var buttonWidth = 100f;
        var windowWidth = ImGui.GetWindowWidth();

        ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

        if (ImGui.Button("Close", new Vector2(buttonWidth, 0)))
            CloseWindow();
    }

    public void Dispose() { }
}
