using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using NoireLib.Core.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.TweakManager;

/// <summary>
/// Window for the Tweak Manager module that displays available tweaks,
/// their status, and configuration panels.
/// </summary>
public class TweakManagerWindow : NoireModuleWindowBase<NoireTweakManager>
{
    private string? selectedTweakKey;
    private string searchFilter = string.Empty;
    private bool showFavoritesOnly;
    private readonly HashSet<string> selectedTagFilters = [];
    private bool tagFilterDropdownOpen;

    private static readonly Vector4 ErrorColor = new(0.9f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 EnabledColor = new(0.1f, 0.8f, 0.1f, 1f);
    private static readonly Vector4 EnableButtonColor = new(0.18f, 0.56f, 0.34f, 1f);
    private static readonly Vector4 EnableButtonHoveredColor = new(0.22f, 0.64f, 0.39f, 1f);
    private static readonly Vector4 EnableButtonActiveColor = new(0.14f, 0.48f, 0.29f, 1f);
    private static readonly Vector4 DisableButtonColor = new(0.68f, 0.24f, 0.24f, 1f);
    private static readonly Vector4 DisableButtonHoveredColor = new(0.77f, 0.29f, 0.29f, 1f);
    private static readonly Vector4 DisableButtonActiveColor = new(0.58f, 0.19f, 0.19f, 1f);
    private static readonly Vector4 DisabledColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 DisabledCheckboxBgColor = new(0.22f, 0.12f, 0.12f, 0.9f);
    private static readonly Vector4 HeaderColor = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 DescriptionColor = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Vector4 GoldColor = new(0.95f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 SelectedBgColor = new(0.3f, 0.3f, 0.6f, 0.4f);
    private static readonly Vector4 HoverBgColor = new(0.3f, 0.3f, 0.5f, 0.2f);
    private static readonly Vector4 TitleColor = new(0.55f, 0.7f, 1f, 1f);
    private static readonly Vector4 TagBgColor = new(0.3f, 0.35f, 0.5f, 0.6f);
    private static readonly Vector4 TagTextColor = new(0.85f, 0.85f, 0.95f, 1f);
    private static readonly Vector4 ConfigHeaderColor = new(0.7f, 0.8f, 1f, 1f);

    /// <summary>
    /// Gets or sets the name of the display window.
    /// </summary>
    public override string DisplayWindowName { get; set; } = "Tweak Manager";

    /// <summary>
    /// Creates a new instance of the <see cref="TweakManagerWindow"/>.
    /// </summary>
    /// <param name="parentModule">The parent <see cref="NoireTweakManager"/> module.</param>
    public TweakManagerWindow(NoireTweakManager parentModule)
        : base(parentModule, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(850, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        UpdateTitleBarButtons();
    }

    /// <summary>
    /// Draws the tweak manager window content.
    /// </summary>
    public override void Draw()
    {
        DrawSearchBar();
        ImGui.Spacing();
        DrawMainContent();
    }

    /// <summary>
    /// Called when the window is opened.
    /// </summary>
    public new void OpenWindow()
    {
        IsOpen = true;
        ParentModule.OnWindowOpened();
    }

    /// <summary>
    /// Called when the window is closed.
    /// </summary>
    public new void CloseWindow()
    {
        if (IsOpen)
        {
            IsOpen = false;
            ParentModule.OnWindowClosed();
        }
    }

    /// <summary>
    /// Clears the currently selected tweak.
    /// </summary>
    internal void ClearSelection()
    {
        selectedTweakKey = null;
    }

    #region Drawing

    private void DrawSearchBar()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var favWidth = 120f;
        var tagBtnWidth = 90f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var searchWidth = MathF.Max(100f, availableWidth - favWidth - tagBtnWidth - spacing * 2f);

        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##TweakSearch", "Search tweaks...", ref searchFilter, 256);

        ImGui.SameLine();
        ImGui.Checkbox("Favorites", ref showFavoritesOnly);

        ImGui.SameLine();
        DrawTagFilterCombo(tagBtnWidth);
    }

    private void DrawTagFilterCombo(float width)
    {
        var allTags = ParentModule.GetAllTweaks()
            .SelectMany(t => t.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var label = selectedTagFilters.Count == 0
            ? "Tags"
            : $"Tags ({selectedTagFilters.Count})";

        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo("##TagFilter", label))
        {
            tagFilterDropdownOpen = true;
            foreach (var tag in allTags)
            {
                var selected = selectedTagFilters.Contains(tag);
                if (ImGui.Checkbox(tag, ref selected))
                {
                    if (selected)
                        selectedTagFilters.Add(tag);
                    else
                        selectedTagFilters.Remove(tag);
                }
            }

            if (selectedTagFilters.Count > 0)
            {
                ImGui.Separator();
                if (ImGui.Selectable("Clear all"))
                    selectedTagFilters.Clear();
            }

            ImGui.EndCombo();
        }
        else
        {
            tagFilterDropdownOpen = false;
        }
    }

    private void DrawMainContent()
    {
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var leftPanelWidth = 325f;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(3, 3)))
        using (ImRaii.Child("##TweakListPanel", new Vector2(leftPanelWidth, availableHeight), true))
        {
            DrawTweakList();
        }

        ImGui.SameLine();

        using (ImRaii.Child("##TweakDetailsPanel", new Vector2(0, availableHeight), true))
        {
            DrawDetailsPanel();
        }
    }

    private void DrawTweakList()
    {
        var visibleTweaks = GetVisibleTweaks();

        if (visibleTweaks.Count == 0)
        {
            ImGui.TextDisabled("No tweaks available.");
            return;
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            foreach (var tweak in visibleTweaks)
            {
                DrawTweakListEntry(tweak);
            }
        }
    }

    private void DrawTweakListEntry(TweakBase tweak)
    {
        var isSelected = selectedTweakKey == tweak.InternalKey;
        var isGloballyDisabled = tweak.IsGloballyDisabled;
        var showWhenDisabled = tweak.ShowWhenDisabled;
        var hasError = tweak.HasError;
        var isFavorite = ParentModule.IsFavorite(tweak.InternalKey);

        var cursorPos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var leftPad = 4f;
        var topPad = 4f;
        var rightPad = 4f;
        var contentSpacing = 8f;
        var checkboxWidth = ImGui.GetFrameHeight();
        var favoriteButtonWidth = 22f;
        var textRightReserve = favoriteButtonWidth + rightPad + 3f;
        var textStartX = cursorPos.X + leftPad + checkboxWidth + contentSpacing;
        var textWrapWidth = MathF.Max(1f, availWidth - (textStartX - cursorPos.X) - textRightReserve);
        var textSize = ImGui.CalcTextSize(tweak.Name, false, textWrapWidth);
        var entryHeight = MathF.Max(checkboxWidth + topPad * 2f, textSize.Y + topPad * 2f);
        var rowSize = new Vector2(availWidth, entryHeight);
        var nextRowPos = new Vector2(cursorPos.X, cursorPos.Y + entryHeight);

        // Full-row invisible button for selection
        ImGui.SetCursorScreenPos(cursorPos);
        if (ImGui.InvisibleButton($"##tweak_{tweak.InternalKey}", rowSize))
        {
            selectedTweakKey = tweak.InternalKey;
            ParentModule.OnTweakSelected(tweak);
        }

        var rowHovered = ImGui.IsItemHovered();
        ImGui.SetItemAllowOverlap();

        // Row background
        if (isSelected || rowHovered)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(
                cursorPos,
                cursorPos + rowSize,
                ImGui.GetColorU32(isSelected ? SelectedBgColor : HoverBgColor),
                0f);
        }

        // Checkbox (centered vertically, left padding)
        var checkboxY = cursorPos.Y + (entryHeight - checkboxWidth) * 0.5f;
        var checkboxPos = new Vector2(cursorPos.X + leftPad, checkboxY);
        ImGui.SetCursorScreenPos(checkboxPos);

        if (isGloballyDisabled)
        {
            DrawDisabledCheckbox(checkboxPos, checkboxWidth);
        }
        else
        {
            var enabled = tweak.Enabled;
            if (ImGui.Checkbox($"##chk_{tweak.InternalKey}", ref enabled))
            {
                if (enabled)
                    ParentModule.EnableTweak(tweak.InternalKey);
                else
                    ParentModule.DisableTweak(tweak.InternalKey);
            }
        }

        // Tweak name (centered vertically)
        Vector4 textColor;
        if (isGloballyDisabled && showWhenDisabled)
            textColor = ErrorColor;
        else if (isGloballyDisabled)
            textColor = DisabledColor;
        else if (hasError)
            textColor = ErrorColor;
        else
            textColor = HeaderColor;

        var textPos = new Vector2(textStartX, cursorPos.Y + (entryHeight - textSize.Y) * 0.5f);
        ImGui.SetCursorScreenPos(textPos);
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        using (ImRaii.Child($"##tweak_text_{tweak.InternalKey}", new Vector2(textWrapWidth, textSize.Y), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs))
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            ImGui.TextWrapped(tweak.Name);
        }

        // Favorite star (centered vertically, right padding)
        var starY = cursorPos.Y + (entryHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var favoritePos = new Vector2(cursorPos.X + availWidth - favoriteButtonWidth - rightPad, starY);
        ImGui.SetCursorScreenPos(favoritePos);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, isFavorite ? GoldColor : DisabledColor))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        ImGui.SetCursorScreenPos(new Vector2(favoritePos.X, cursorPos.Y));
        if (ImGui.InvisibleButton($"##favorite_{tweak.InternalKey}", new Vector2(favoriteButtonWidth, entryHeight)))
            ParentModule.ToggleFavorite(tweak.InternalKey);

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(isFavorite ? "Remove from favorites" : "Add to favorites");
            }
        }
        else if (isGloballyDisabled && showWhenDisabled && rowHovered)
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextColored(ErrorColor, "Globally Disabled");
                if (!tweak.GloballyDisabledReason.IsNullOrWhitespace())
                {
                    ImGui.Separator();
                    ImGui.Text(tweak.GloballyDisabledReason);
                }
            }
        }
        else if (hasError && rowHovered)
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextColored(ErrorColor, "Error");
                if (tweak.LastError != null)
                {
                    ImGui.Separator();
                    ImGui.TextWrapped(tweak.LastError.Message);
                }
            }
        }

        ImGui.SetCursorScreenPos(nextRowPos);
    }

    private void DrawDetailsPanel()
    {
        if (string.IsNullOrEmpty(selectedTweakKey))
        {
            DrawWelcomeUI();
            return;
        }

        var tweak = ParentModule.GetTweak(selectedTweakKey);
        if (tweak == null)
        {
            selectedTweakKey = null;
            DrawWelcomeUI();
            return;
        }

        DrawTweakDetails(tweak);
    }

    private void DrawWelcomeUI()
    {
        var availableSize = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize("Select a tweak from the list to view details.");

        ImGui.SetCursorPos(new Vector2(
            (availableSize.X - textSize.X) * 0.5f + ImGui.GetCursorPosX(),
            (availableSize.Y - textSize.Y) * 0.5f));

        ImGui.TextDisabled("Select a tweak from the list to view details.");
    }

    private void DrawTweakDetails(TweakBase tweak)
    {
        var isGloballyDisabled = tweak.IsGloballyDisabled;
        var favoriteLabel = FontAwesomeIcon.Star.ToIconString();
        var buttonWidth = 60f;
        var favoriteWidth = 24f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var headerStart = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonsWidth = favoriteWidth + spacing + buttonWidth;
        var titleRightReserve = buttonsWidth + spacing + 8f;
        var titleWrapWidth = MathF.Max(1f, availableWidth - titleRightReserve);
        var titleSize = ImGui.CalcTextSize(tweak.Name, false, titleWrapWidth);
        var headerHeight = MathF.Max(titleSize.Y, ImGui.GetFrameHeight());
        var buttonsX = headerStart.X + availableWidth - buttonsWidth;
        var buttonsY = headerStart.Y + (headerHeight - ImGui.GetFrameHeight()) * 0.5f;

        ImGui.SetCursorScreenPos(headerStart);
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        using (ImRaii.Child($"##tweak_header_text_{tweak.InternalKey}", new Vector2(titleWrapWidth, titleSize.Y), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
        using (ImRaii.PushColor(ImGuiCol.Text, TitleColor))
        using (ImRaii.PushFont(UiBuilder.DefaultFont))
        {
            ImGui.TextWrapped(tweak.Name);
        }

        ImGui.SetCursorScreenPos(new Vector2(buttonsX, buttonsY));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, ParentModule.IsFavorite(tweak.InternalKey) ? GoldColor : DescriptionColor))
        {
            if (ImGui.Button($"{favoriteLabel}##favorite_toggle_{tweak.InternalKey}", new Vector2(favoriteWidth, 0)))
                ParentModule.ToggleFavorite(tweak.InternalKey);
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(ParentModule.IsFavorite(tweak.InternalKey)
                    ? "Remove from favorites"
                    : "Add to favorites");
            }
        }

        ImGui.SameLine(0f, spacing);
        ImGui.SetCursorScreenPos(new Vector2(buttonsX + favoriteWidth + spacing, buttonsY));
        using (ImRaii.Disabled(isGloballyDisabled))
        {
            var toggleLabel = tweak.Enabled ? "Disable" : "Enable";
            var buttonColor = tweak.Enabled ? DisableButtonColor : EnableButtonColor;
            var buttonHoveredColor = tweak.Enabled ? DisableButtonHoveredColor : EnableButtonHoveredColor;
            var buttonActiveColor = tweak.Enabled ? DisableButtonActiveColor : EnableButtonActiveColor;
            using (ImRaii.PushColor(ImGuiCol.Button, buttonColor))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, buttonHoveredColor))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, buttonActiveColor))
            {
                if (ImGui.Button($"{toggleLabel}##toggle_{tweak.InternalKey}", new Vector2(buttonWidth, 0)))
                {
                    if (tweak.Enabled)
                        ParentModule.DisableTweak(tweak.InternalKey);
                    else
                        ParentModule.EnableTweak(tweak.InternalKey);
                }
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(headerStart.X, headerStart.Y + headerHeight));
        if (tweak.Tags.Count > 0)
        {
            ImGui.Spacing();
            DrawTagPills(tweak.Tags);
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (isGloballyDisabled)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ErrorColor))
            {
                ImGui.TextUnformatted("This tweak is globally disabled.");
            }

            if (!tweak.GloballyDisabledReason.IsNullOrWhitespace())
            {
                using (ImRaii.PushColor(ImGuiCol.Text, DescriptionColor))
                using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X))
                {
                    ImGui.TextWrapped(tweak.GloballyDisabledReason);
                }
            }

            ImGui.Spacing();
        }

        // Description with brighter text
        using (ImRaii.PushColor(ImGuiCol.Text, DescriptionColor))
        using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X))
        {
            ImGui.TextWrapped(tweak.Description);
        }

        ImGui.Spacing();

        if (tweak.HasError)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, ErrorColor))
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
                }
                ImGui.SameLine();
                ImGui.TextUnformatted("Error:");
            }

            using (ImRaii.PushColor(ImGuiCol.Text, ErrorColor))
            using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X))
            {
                ImGui.TextWrapped(tweak.LastError?.Message ?? "Unknown error");
            }

            using (ImRaii.Disabled(isGloballyDisabled))
            {
                ImGui.Spacing();
                if (ImGui.Button($"Clear Error##clearerr_{tweak.InternalKey}"))
                {
                    tweak.ClearError();
                }
                ImGui.SameLine();
                if (ImGui.Button($"Retry Enable##retry_{tweak.InternalKey}"))
                {
                    tweak.ClearError();
                    ParentModule.EnableTweak(tweak.InternalKey);
                }
            }

            ImGui.Spacing();
        }

        if (tweak.HasConfigurationUi)
        {
            ImGui.Separator();
            ImGui.Spacing();

            using (ImRaii.PushColor(ImGuiCol.Text, ConfigHeaderColor))
            {
                ImGui.TextUnformatted("Configuration");
            }
            ImGui.Separator();
            ImGui.Spacing();

            using (ImRaii.Child("##TweakConfigContent", new Vector2(0, 0), false))
            {
                try
                {
                    using (ImRaii.Disabled(isGloballyDisabled))
                    {
                        tweak.DrawConfigUI();
                    }
                }
                catch (Exception ex)
                {
                    tweak.HasError = true;
                    tweak.LastError = ex;
                    NoireLogger.LogError<TweakManagerWindow>(ex, $"Error drawing config UI for tweak '{tweak.Name}'.");
                    ImGui.TextColored(ErrorColor, $"Error rendering configuration: {ex.Message}");
                }
            }
        }
    }

    private List<TweakBase> GetVisibleTweaks()
    {
        var allTweaks = ParentModule.GetAllTweaks();

        return allTweaks
            .Where(t =>
            {
                // Allow globally disabled tweaks through if ShowWhenDisabled is set
                if (t.IsGloballyDisabled && !t.ShowWhenDisabled)
                    return false;

                if (!t.ShouldShow)
                    return false;

                if (showFavoritesOnly && !ParentModule.IsFavorite(t.InternalKey))
                    return false;

                if (selectedTagFilters.Count > 0 &&
                    !selectedTagFilters.Any(tag => t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    return false;

                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    var filter = searchFilter.Trim();
                    if (!t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !t.Tags.Any(tag => tag.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }

                return true;
            })
            .OrderBy(t => t.Name)
            .ToList();
    }

    private void DrawDisabledCheckbox(Vector2 position, float size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = position;
        var max = new Vector2(position.X + size, position.Y + size);
        var inset = MathF.Max(2f, size * 0.22f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(DisabledCheckboxBgColor));
        drawList.AddRect(min, max, ImGui.GetColorU32(ErrorColor));
        drawList.AddLine(
            new Vector2(min.X + inset, min.Y + inset),
            new Vector2(max.X - inset, max.Y - inset),
            ImGui.GetColorU32(ErrorColor),
            2f);
        drawList.AddLine(
            new Vector2(min.X + inset, max.Y - inset),
            new Vector2(max.X - inset, min.Y + inset),
            ImGui.GetColorU32(ErrorColor),
            2f);
    }

    private void DrawTagPills(IReadOnlyList<string> tags)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padX = 8f;
        var padY = 3f;
        var rounding = 6f;
        var tagSpacing = 4f;
        var lineHeight = ImGui.GetTextLineHeight();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var startX = ImGui.GetCursorScreenPos().X;
        var currentX = startX;

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var textSize = ImGui.CalcTextSize(tag);
            var pillWidth = textSize.X + padX * 2f;
            var pillHeight = lineHeight + padY * 2f;

            // Wrap to next line if needed
            if (i > 0 && currentX + pillWidth > startX + availWidth)
            {
                ImGui.NewLine();
                currentX = ImGui.GetCursorScreenPos().X;
            }

            var pos = ImGui.GetCursorScreenPos();

            // Draw pill background
            drawList.AddRectFilled(
                pos,
                new Vector2(pos.X + pillWidth, pos.Y + pillHeight),
                ImGui.GetColorU32(TagBgColor),
                rounding);

            // Draw pill text
            drawList.AddText(
                new Vector2(pos.X + padX, pos.Y + padY),
                ImGui.GetColorU32(TagTextColor),
                tag);

            // Advance cursor
            ImGui.Dummy(new Vector2(pillWidth, pillHeight));
            currentX += pillWidth + tagSpacing;

            if (i < tags.Count - 1)
                ImGui.SameLine(0f, tagSpacing);
        }
    }

    #endregion

    /// <summary>
    /// Disposes resources used by the TweakManagerWindow.
    /// </summary>
    public override void Dispose() { /* no-op */ }
}
