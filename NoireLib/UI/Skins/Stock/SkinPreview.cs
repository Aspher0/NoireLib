using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

// The miniature a skin without a preview of its own shows in the skin picker: a window, a title, a few lines of text
// and an accent button, in the theme's colours.
internal static class SkinPreview
{
    private static readonly float[] LineWidths = [0.78f, 0.62f, 0.7f];

    internal static void Draw(UiRect area, NoireTheme theme)
    {
        if (area.Size.X <= 0f || area.Size.Y <= 0f)
            return;

        using var draw = UiDraw.Begin();
        var list = draw.List;

        if (list.IsNull)
            return;

        var min = area.Position;
        var max = area.Max;
        var size = area.Size;
        var radius = MathF.Min(6f * NoireUI.Scale, size.Y * 0.08f);
        var header = size.Y * 0.2f;
        var pad = size.X * 0.07f;
        var line = MathF.Max(2f, header * 0.26f);

        list.AddRectFilled(min, max, ImGui.GetColorU32(theme.Resolve(ThemeColor.Surface)), radius);
        list.AddRectFilled(min, new Vector2(max.X, min.Y + header), ImGui.GetColorU32(theme.Resolve(ThemeColor.SurfaceRaised)), radius, ImDrawFlags.RoundCornersTop);

        var titleY = min.Y + ((header - line) * 0.5f);
        list.AddRectFilled(new Vector2(min.X + pad, titleY), new Vector2(min.X + (size.X * 0.42f), titleY + line), ImGui.GetColorU32(theme.Resolve(ThemeColor.Text)), line * 0.5f);

        var muted = ImGui.GetColorU32(theme.Resolve(ThemeColor.TextMuted));
        var y = min.Y + header + pad;

        for (var i = 0; i < LineWidths.Length; i++)
        {
            list.AddRectFilled(new Vector2(min.X + pad, y), new Vector2(min.X + (size.X * LineWidths[i]), y + line), muted, line * 0.5f);
            y += line * 2.2f;
        }

        var button = new Vector2(size.X * 0.3f, header * 0.7f);
        var buttonMax = max - new Vector2(pad, pad);
        list.AddRectFilled(buttonMax - button, buttonMax, ImGui.GetColorU32(theme.Resolve(ThemeColor.Accent)), radius * 0.6f);
        list.AddRect(min, max, ImGui.GetColorU32(theme.Resolve(ThemeColor.Border)), radius);
    }
}
