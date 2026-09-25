using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

public sealed partial class NoireGradient
{
    private static NoireGradient? rainbow;
    private static NoireGradient? pastel;
    private static NoireGradient? fire;
    private static NoireGradient? ocean;
    private static NoireGradient? sunset;
    private static NoireGradient? gold;
    private static NoireGradient? silver;
    private static NoireGradient? neon;
    private static NoireGradient? holographic;
    private static NoireGradient? aurora;
    private static NoireGradient? lava;
    private static NoireGradient? ice;

    /// <summary>
    /// Every hue in turn, one step per character, flowing along the text. Built for text: on a shape it shows one hue per piece.
    /// </summary>
    public static NoireGradient Rainbow => rainbow ??= PerGlyph(Hues(0.62f, 1f))
        .WithGlyphStep(0.045f)
        .WithRepeat(GradientRepeat.Repeat)
        .Scrolling(0.35f)
        .Reversed();

    /// <summary>Soft pastel hues, left to right.</summary>
    public static NoireGradient Pastel => pastel ??= Linear(GradientDirection.LeftToRight, Hues(0.32f, 1f)).WithSpace(GradientColorSpace.Oklab);

    /// <summary>Deep red rising through orange to yellow, bottom to top.</summary>
    public static NoireGradient Fire => fire ??= Linear(GradientDirection.BottomToTop,
        Hex("#7A0A00"), Hex("#E0301E"), Hex("#FF8A00"), Hex("#FFD84D"));

    /// <summary>Navy through blue to aqua, top to bottom.</summary>
    public static NoireGradient Ocean => ocean ??= Linear(GradientDirection.TopToBottom,
        Hex("#0A1F44"), Hex("#1565C0"), Hex("#00A6A6"), Hex("#7FE7F2")).WithSpace(GradientColorSpace.Oklab);

    /// <summary>Purple through magenta and orange to gold, left to right.</summary>
    public static NoireGradient Sunset => sunset ??= Linear(GradientDirection.LeftToRight,
        Hex("#4B1D6B"), Hex("#C2185B"), Hex("#FF6F3C"), Hex("#FFC857")).WithSpace(GradientColorSpace.Oklab);

    /// <summary>Metallic gold bands, top to bottom.</summary>
    public static NoireGradient Gold => gold ??= Linear(GradientDirection.TopToBottom,
        Hex("#FFF3B0"), Hex("#E6B84A"), Hex("#9C6B12"), Hex("#E6B84A"), Hex("#FFE58A"));

    /// <summary>Metallic silver bands, top to bottom.</summary>
    public static NoireGradient Silver => silver ??= Linear(GradientDirection.TopToBottom,
        Hex("#FFFFFF"), Hex("#C9CED6"), Hex("#7D8590"), Hex("#C9CED6"), Hex("#F2F4F7"));

    /// <summary>Bright magenta to cyan, left to right.</summary>
    public static NoireGradient Neon => neon ??= Linear(GradientDirection.LeftToRight,
        Hex("#FF2BD6"), Hex("#8A5CFF"), Hex("#00E5FF")).WithSpace(GradientColorSpace.Oklab);

    /// <summary>Pastel pink, cyan, lavender and mint, mirrored diagonally and drifting.</summary>
    public static NoireGradient Holographic => holographic ??= Linear(30f,
        Hex("#FFC6F0"), Hex("#B8F3FF"), Hex("#D9C6FF"), Hex("#C6FFE0"))
        .WithSpace(GradientColorSpace.Oklab)
        .WithRepeat(GradientRepeat.Mirror, 2f)
        .Scrolling(0.08f);

    /// <summary>Green through teal to violet, bottom to top.</summary>
    public static NoireGradient Aurora => aurora ??= Linear(GradientDirection.BottomToTop,
        Hex("#1DE9B6"), Hex("#00B8D4"), Hex("#7C4DFF"), Hex("#B388FF")).WithSpace(GradientColorSpace.Oklab);

    /// <summary>Dark red and glowing orange in slow moving blotches.</summary>
    public static NoireGradient Lava => lava ??= Noise(36f, Hex("#2B0000"), Hex("#B31200"), Hex("#FF6A00"), Hex("#FFC400"))
        .Scrolling(0.05f);

    /// <summary>White through pale cyan to blue, top to bottom.</summary>
    public static NoireGradient Ice => ice ??= Linear(GradientDirection.TopToBottom,
        Hex("#FFFFFF"), Hex("#CDEFFF"), Hex("#6EC6FF"), Hex("#2F80ED")).WithSpace(GradientColorSpace.Oklab);

    /// <summary>
    /// The current theme's accent, lightening left to right. Call it again after the theme changes.
    /// </summary>
    /// <returns>The gradient.</returns>
    public static NoireGradient Theme()
    {
        var accent = NoireTheme.Current.Resolve(ThemeColor.Accent);
        return Linear(GradientDirection.LeftToRight, accent, ColorHelper.Lighten(accent, 0.35f)).WithSpace(GradientColorSpace.Oklab);
    }

    private static GradientStop[] Hues(float saturation, float value)
    {
        var stops = new GradientStop[7];

        for (var i = 0; i < stops.Length; i++)
            stops[i] = ColorHelper.FromHsv(i / 6f, saturation, value);

        return stops;
    }

    private static GradientStop Hex(string hex) => ColorHelper.HexToVector4(hex);
}
