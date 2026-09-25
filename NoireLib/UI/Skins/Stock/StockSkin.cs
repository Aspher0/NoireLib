using NoireLib.Localizer;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>Plain NoireUI and ImGui inside Dalamud's own window frame. Complete, and the last fallback of every skin.</summary>
public sealed class StockSkin : NoireSkin
{
    private static readonly ThemeRole[] Roles =
    [
        new(nameof(ThemeColor.Accent), NoireStrings.ColorAccent),
        new(nameof(ThemeColor.Danger), NoireStrings.ColorDanger),
    ];

    private StockSkin()
        : base("stock", NoireStrings.SkinStock)
    {
    }

    /// <summary>The single instance.</summary>
    public static StockSkin Instance { get; } = new();

    /// <summary>The plugin's own <see cref="NoireTheme.Current"/>.</summary>
    public override NoireTheme Theme => NoireSkins.HostTheme;

    /// <summary>The accent and danger colours.</summary>
    public override IReadOnlyList<ThemeRole> EditableColors => Roles;

    /// <summary><see cref="NativeChrome"/>.</summary>
    public override IChromeSkin Chrome => NativeChrome.Instance;

    /// <summary>A <see cref="StockControlSkin"/>.</summary>
    public override IControlSkin Controls { get; } = new StockControlSkin();

    /// <summary>ImGui's popups and NoireLib's tooltips.</summary>
    public override IOverlaySkin Overlays => StockOverlaySkin.Instance;

    /// <summary>NoireLib's own icons.</summary>
    public override IIconSkin Icons => StockIconSkin.Instance;

    /// <summary>The host font, sized by the window's text size.</summary>
    public override IFontSkin Fonts => StockFontSkin.Instance;

    /// <summary>A table of name, control and help per row, under ImGui's tab bar.</summary>
    public override ISettingsSkin Settings => StockSettingsSkin.Instance;
}
