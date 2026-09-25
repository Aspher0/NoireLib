using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

// ImGui's own menu and popup look, NoireTooltip for tooltips.
internal sealed class StockOverlaySkin : IOverlaySkin
{
    private const int MaxCachedTooltips = 256;

    private const ImGuiWindowFlags ConfirmFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav;

    private static readonly TooltipStyle TooltipAbove = new() { Placement = TooltipPlacement.AboveItem };

    internal static readonly StockOverlaySkin Instance = new();

    private readonly Dictionary<uint, UiImageSource> gameIcons = new();

    // Per text instance: texts are cached strings and come back every frame.
    private readonly Dictionary<string, NoireContent> tooltips = new(StringInstanceComparer.Instance);

    public ToastStyle? Toasts => null;

    public void MenuStyle()
    {
    }

    public void MenuTitle(string title, uint gameIcon)
    {
        if (gameIcon != 0 && NoireService.IsInitialized() && GameIcon(gameIcon).GetWrap() is { } wrap)
        {
            var side = ImGui.GetTextLineHeight();
            ImGui.Image(wrap.Handle, new Vector2(side, side));
            ImGui.SameLine();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    public bool MenuItem(string label, NoireIcon? icon, bool enabled, string? hint, bool requiresCtrl)
    {
        var usable = enabled && (!requiresCtrl || ImGui.GetIO().KeyCtrl);
        var side = ImGui.GetTextLineHeight();
        var start = ImGui.GetCursorScreenPos();
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;

        ImGui.SetCursorScreenPos(new Vector2(start.X + side + spacing, start.Y));
        var clicked = ImGui.MenuItem(label, string.Empty, false, usable);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

        if (icon is { } glyph)
        {
            using var draw = UiDraw.Begin();
            NoireIcons.DrawAt(draw.List, glyph, start, side, ImGui.GetColorU32(usable ? ImGuiCol.Text : ImGuiCol.TextDisabled));
        }

        if (hint != null && hovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(hint);
            ImGui.EndTooltip();
        }

        return clicked && usable;
    }

    public void MenuSeparator() => ImGui.Separator();

    public void Tooltip(string text, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (!tooltips.TryGetValue(text, out var content))
        {
            if (tooltips.Count >= MaxCachedTooltips)
                tooltips.Clear();

            tooltips[text] = content = text;
        }

        NoireTooltip.Show(content, TooltipAbove, null, (anchorMin, anchorMax));
    }

    public bool CardBegin(string id, Vector2 anchorMin, Vector2 anchorMax, float width)
    {
        ImGui.SetNextWindowPos(new Vector2(anchorMin.X, anchorMax.Y + NoireUI.Scaled(4f)));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));
        ImGui.BeginTooltip();
        UiWindowOrder.KeepInFront();
        ImGui.PushTextWrapPos(0f);
        return true;
    }

    public void CardEnd()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    public void Pill(NoirePill pill, Vector2 windowMin, Vector2 windowMax)
    {
        if (!pill.Active || pill.Text.Length == 0)
            return;

        using var draw = UiDraw.Begin();

        if (draw.List.IsNull)
            return;

        var theme = NoireTheme.Current;
        var padding = ImGui.GetStyle().FramePadding;
        var size = NoireText.CalcSizeInCurrentFont(pill.Text) + (padding * 2f);
        var min = new Vector2(MathF.Round(((windowMin.X + windowMax.X) - size.X) * 0.5f), MathF.Round(windowMax.Y - size.Y - (padding.Y * 3f)));
        var max = min + size;
        var radius = size.Y * 0.5f;
        var accent = ImGui.GetColorU32(pill.Accent ?? theme.Resolve(ThemeColor.Accent));

        draw.List.AddRectFilled(min, max, ImGui.GetColorU32(theme.Resolve(ThemeColor.SurfaceRaised)), radius);
        draw.List.AddRect(min, max, accent, radius);
        draw.List.AddText(min + padding, ImGui.GetColorU32(theme.Resolve(ThemeColor.Text)), pill.Text);

        var left = min.X + radius;
        var right = left + ((size.X - (radius * 2f)) * (1f - pill.Progress));
        draw.List.AddLine(new Vector2(left, max.Y - 1f), new Vector2(right, max.Y - 1f), accent);
    }

    public ConfirmAnswer Confirm(in ConfirmState state, Vector2 bodyMin, Vector2 bodyMax)
    {
        var confirm = state.Confirm;
        var theme = NoireTheme.Current;
        var width = MathF.Min(NoireUI.Scaled(420f), (bodyMax.X - bodyMin.X) * 0.9f);

        ImGui.SetNextWindowPos((bodyMin + bodyMax) * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

        var name = UiIds.For("##noire.confirm", string.Empty, (int)ImGui.GetID("confirm"));
        var answer = ConfirmAnswer.Pending;

        ImGui.Begin(name, ConfirmFlags);

        try
        {
            UiWindowOrder.KeepInFront();

            ImGui.TextUnformatted(confirm.Title.Text);
            ImGui.Separator();
            ImGui.PushTextWrapPos(0f);

            foreach (var paragraph in confirm.Paragraphs)
            {
                var color = paragraph.Tone switch
                {
                    ParagraphTone.Muted => theme.Resolve(ThemeColor.TextMuted),
                    ParagraphTone.Danger => theme.Resolve(ThemeColor.Danger),
                    ParagraphTone.Strong => theme.Resolve(ThemeColor.Accent),
                    _ => theme.Resolve(ThemeColor.Text),
                };

                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.TextUnformatted(paragraph.Text.Text);
                ImGui.PopStyleColor();
            }

            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            var controls = NoireSkins.Active.Controls;
            var label = state.SecondsLeft > 0
                ? NoireStrings.CountdownLabel.With("label", confirm.ConfirmLabel.Text, "seconds", NoireLanguages.Number(state.SecondsLeft))
                : confirm.ConfirmLabel.Text;

            if (controls.Button("confirm", label, null, confirm.Danger ? ButtonTone.Danger : ButtonTone.Accent, state.SecondsLeft == 0))
                answer = ConfirmAnswer.Confirmed;

            ImGui.SameLine();

            if (controls.Button("cancel", (confirm.CancelLabel ?? NoireStrings.Cancel).Text) || ImGui.IsKeyPressed(ImGuiKey.Escape))
                answer = ConfirmAnswer.Cancelled;
        }
        finally
        {
            ImGui.End();
        }

        return answer;
    }

    private UiImageSource GameIcon(uint id)
    {
        if (!gameIcons.TryGetValue(id, out var source))
            gameIcons[id] = source = UiImageSource.FromGameIcon(id);

        return source;
    }
}
