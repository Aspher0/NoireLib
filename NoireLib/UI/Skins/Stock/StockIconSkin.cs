using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

// NoireLib's own icons: FontAwesome glyphs, and artwork for the brand marks.
internal sealed class StockIconSkin : IIconSkin
{
    internal static readonly StockIconSkin Instance = new();

    public void Draw(NoireIcon icon, Vector2 topLeft, float size, Vector4 color)
    {
        using var draw = UiDraw.Begin();

        if (!draw.List.IsNull)
            NoireIcons.DrawAt(draw.List, icon, topLeft, size, ColorHelper.Vector4ToUint(color));
    }
}
