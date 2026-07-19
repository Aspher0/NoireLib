using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// One palette the whole library follows.<br/>
/// Every NoireUI widget resolves the colors it was not explicitly given through <see cref="Current"/>, so setting an
/// accent here re-tints buttons, toggles, toasts, modals and section headers at once. Nothing is required: a theme that
/// sets no colors resolves everything to the host's ImGui style, and a plugin that never touches this looks exactly as
/// it did before.
/// </summary>
/// <remarks>
/// Resolution runs in three steps, in this order: the value the widget was given, then this theme, then the ImGui style.
/// A theme color left <see langword="null"/> falls through rather than forcing a default, which is what lets one theme
/// override two colors and inherit the rest.<br/>
/// <see cref="Hover"/> and <see cref="Active"/> derive their states from the base color rather than storing separate
/// values, and <see cref="TintSource"/> decides which way they move. By default each color decides for itself, so a dark
/// button brightens and a pale accent one darkens rather than washing out.
/// </remarks>
/// <example>
/// <code>
/// NoireTheme.Current = NoireTheme.FromAccent(ColorHelper.HexToVector4("#C8A96A"));
/// NoireTheme.Current.Danger = ColorHelper.HexToVector4("#B4443A");   // override one token, keep the rest
/// </code>
/// </example>
public sealed class NoireTheme
{
    private static NoireTheme current = new();

    private static readonly Dictionary<ThemeColor, Vector4> ShippedDefaults = new()
    {
        [ThemeColor.Accent] = new Vector4(0.35f, 0.60f, 0.90f, 1f),
        [ThemeColor.Success] = new Vector4(0.35f, 0.72f, 0.44f, 1f),
        [ThemeColor.Warning] = new Vector4(0.90f, 0.68f, 0.28f, 1f),
        [ThemeColor.Danger] = new Vector4(0.87f, 0.36f, 0.33f, 1f),
        [ThemeColor.Info] = new Vector4(0.45f, 0.68f, 0.85f, 1f),
        [ThemeColor.Surface] = new Vector4(0.08f, 0.08f, 0.09f, 0.94f),
        [ThemeColor.SurfaceRaised] = new Vector4(0.14f, 0.14f, 0.16f, 1f),
        [ThemeColor.SurfaceSunken] = new Vector4(0.05f, 0.05f, 0.06f, 1f),
        [ThemeColor.Border] = new Vector4(0.30f, 0.30f, 0.33f, 0.60f),
        [ThemeColor.Text] = new Vector4(0.94f, 0.94f, 0.94f, 1f),
        [ThemeColor.TextMuted] = new Vector4(0.62f, 0.62f, 0.65f, 1f),
        [ThemeColor.TextDisabled] = new Vector4(0.45f, 0.45f, 0.48f, 1f),
        [ThemeColor.Shadow] = new Vector4(0f, 0f, 0f, 0.55f),
    };

    /// <summary>
    /// The theme every widget resolves against. Never <see langword="null"/>: assigning <see langword="null"/> restores
    /// an empty theme, which resolves everything to the ImGui style.
    /// </summary>
    /// <remarks>
    /// Static per plugin, not per process. NoireLib is compiled into each plugin rather than shared, so setting this
    /// re-themes your own interface and cannot reach another plugin's.
    /// </remarks>
    public static NoireTheme Current
    {
        get => current;
        set => current = value ?? new NoireTheme();
    }

    /// <summary>
    /// Every color this theme overrides. The named color properties read and write here, and a color left out is
    /// resolved from the ImGui style instead.
    /// </summary>
    public Dictionary<ThemeColor, Vector4> Colors { get; } = new();

    /// <summary>
    /// Extra named colors this theme carries, for tokens a bespoke skin needs and the library does not define.<br/>
    /// Read them back with <see cref="Resolve(string, Vector4)"/>. Nothing in NoireUI reads these; they exist so a skin
    /// can keep its whole palette in one place instead of half here and half in its own fields.
    /// </summary>
    public Dictionary<string, Vector4> CustomColors { get; } = new();

    #region Palette

    /// <summary>The one color the interface is built around. See <see cref="ThemeColor.Accent"/>.</summary>
    public Vector4? Accent { get => Get(ThemeColor.Accent); set => Set(ThemeColor.Accent, value); }

    /// <summary>The color a completed or healthy state is shown in.</summary>
    public Vector4? Success { get => Get(ThemeColor.Success); set => Set(ThemeColor.Success, value); }

    /// <summary>The color a state that needs attention but is not an error is shown in.</summary>
    public Vector4? Warning { get => Get(ThemeColor.Warning); set => Set(ThemeColor.Warning, value); }

    /// <summary>The color a failure or a destructive action is shown in.</summary>
    public Vector4? Danger { get => Get(ThemeColor.Danger); set => Set(ThemeColor.Danger, value); }

    /// <summary>The color neutral, purely informational emphasis is shown in.</summary>
    public Vector4? Info { get => Get(ThemeColor.Info); set => Set(ThemeColor.Info, value); }

    /// <summary>The background everything sits on.</summary>
    public Vector4? Surface { get => Get(ThemeColor.Surface); set => Set(ThemeColor.Surface, value); }

    /// <summary>A surface that reads as lifted off the background: cards, popups, toasts.</summary>
    public Vector4? SurfaceRaised { get => Get(ThemeColor.SurfaceRaised); set => Set(ThemeColor.SurfaceRaised, value); }

    /// <summary>A surface that reads as recessed into the background: input fields, wells, tracks.</summary>
    public Vector4? SurfaceSunken { get => Get(ThemeColor.SurfaceSunken); set => Set(ThemeColor.SurfaceSunken, value); }

    /// <summary>The hairline color separating one surface from another.</summary>
    public Vector4? Border { get => Get(ThemeColor.Border); set => Set(ThemeColor.Border, value); }

    /// <summary>The main text color.</summary>
    public Vector4? Text { get => Get(ThemeColor.Text); set => Set(ThemeColor.Text, value); }

    /// <summary>Secondary text: descriptions, captions, units, anything supporting.</summary>
    public Vector4? TextMuted { get => Get(ThemeColor.TextMuted); set => Set(ThemeColor.TextMuted, value); }

    /// <summary>The text color of something switched off or unavailable.</summary>
    public Vector4? TextDisabled { get => Get(ThemeColor.TextDisabled); set => Set(ThemeColor.TextDisabled, value); }

    /// <summary>The color of drop shadows and scrims.</summary>
    public Vector4? Shadow { get => Get(ThemeColor.Shadow); set => Set(ThemeColor.Shadow, value); }

    #endregion

    #region Shape

    // Every value in this region is a pixel measurement at 100%, scaled by NoireUI.Scale when it is resolved. Set what
    // looks right at 100% and it holds at any scale.

    /// <summary>
    /// The corner radius of buttons, toggles and framed widgets, at 100%. When <see langword="null"/>, the ImGui frame
    /// rounding is used.
    /// </summary>
    public float? Rounding { get; set; }

    /// <summary>
    /// The corner radius of raised surfaces at 100%: toasts, modals, cards. When <see langword="null"/>, the ImGui
    /// window rounding is used.
    /// </summary>
    public float? SurfaceRounding { get; set; }

    /// <summary>
    /// The thickness of widget borders at 100%. When <see langword="null"/>, the ImGui frame border size is used.<br/>
    /// Zero is a real value here, and the way to ask for a flat, borderless look.
    /// </summary>
    public float? BorderSize { get; set; }

    /// <summary>
    /// The padding inside widgets at 100%. When <see langword="null"/>, the ImGui frame padding is used.
    /// </summary>
    public Vector2? FramePadding { get; set; }

    /// <summary>
    /// The spacing between consecutive items at 100%. When <see langword="null"/>, the ImGui item spacing is used.
    /// </summary>
    public Vector2? ItemSpacing { get; set; }

    #endregion

    #region Type

    // Sizes here are logical pixels at 100%, like every other measurement on this theme. NoireText scales them when it
    // builds the font, so the number set here is the number you would have measured on a 100% monitor.

    /// <summary>
    /// The shipped proportions of the type scale, as multiples of <see cref="BodySize"/>.
    /// </summary>
    /// <remarks>
    /// A size left unset derives from the body size through these rather than carrying an absolute default, which is
    /// what makes moving <see cref="BodySize"/> move the whole scale. Setting a size explicitly opts that one step out
    /// of the proportion and leaves the others following.
    /// </remarks>
    private static readonly Dictionary<TextSize, float> SizeRatios = new()
    {
        [TextSize.Display] = 2.2f,
        [TextSize.Heading] = 1.5f,
        [TextSize.Body] = 1f,
        [TextSize.Caption] = 0.85f,
    };

    /// <summary>
    /// Every text size this theme overrides. The named size properties read and write here, and a size left out is
    /// derived from <see cref="BodySize"/> instead.
    /// </summary>
    public Dictionary<TextSize, float> TextSizes { get; } = new();

    /// <summary>The masthead size at 100%. When <see langword="null"/>, it is derived from <see cref="BodySize"/>.</summary>
    public float? DisplaySize { get => GetSize(TextSize.Display); set => SetSize(TextSize.Display, value); }

    /// <summary>The section-heading size at 100%. When <see langword="null"/>, it is derived from <see cref="BodySize"/>.</summary>
    public float? HeadingSize { get => GetSize(TextSize.Heading); set => SetSize(TextSize.Heading, value); }

    /// <summary>
    /// The running-text size at 100%, and the root of the scale. When <see langword="null"/>, the host's own default
    /// font size is used, so an untouched theme matches the interface around it exactly.
    /// </summary>
    public float? BodySize { get => GetSize(TextSize.Body); set => SetSize(TextSize.Body, value); }

    /// <summary>The supporting-text size at 100%. When <see langword="null"/>, it is derived from <see cref="BodySize"/>.</summary>
    public float? CaptionSize { get => GetSize(TextSize.Caption); set => SetSize(TextSize.Caption, value); }

    /// <summary>
    /// Resolves a named step of the type scale to a size in logical pixels: this theme's override when it has one, and
    /// a proportion of the body size otherwise.
    /// </summary>
    /// <param name="size">The step to resolve.</param>
    /// <returns>The size at 100%, which is what <see cref="NoireText"/> takes.</returns>
    public float ResolveTextSize(TextSize size)
    {
        if (TextSizes.TryGetValue(size, out var explicitSize))
            return MathF.Max(1f, explicitSize);

        var body = TextSizes.TryGetValue(TextSize.Body, out var bodySize) ? bodySize : DefaultBodySize;
        var ratio = SizeRatios.TryGetValue(size, out var value) ? value : 1f;

        return MathF.Max(1f, body * ratio);
    }

    /// <summary>
    /// The body size a theme that sets none falls back to: the host's own default font size, so text drawn through
    /// <see cref="NoireText"/> at <see cref="TextSize.Body"/> is indistinguishable from an ordinary
    /// <c>ImGui.TextUnformatted</c> beside it.
    /// </summary>
    /// <remarks>
    /// A logical size, not a scaled one. Dalamud builds its own default font from this constant into a global-scaled
    /// atlas, which is exactly what NoireText does, so the two agree at every scale.<br/>
    /// Public because a settings screen offering the type scale has to show the user what "unset" currently means.
    /// </remarks>
    public static float DefaultBodySize => NoireService.IsInitialized() ? UiBuilder.DefaultFontSizePx : 17f;

    #endregion

    #region Derivation

    /// <summary>
    /// How far <see cref="Hover"/> moves a color, from 0 (no change) to 1. Defaults to a light touch.
    /// </summary>
    public float HoverShift { get; set; } = 0.12f;

    /// <summary>
    /// How far <see cref="Active"/> moves a color, from 0 (no change) to 1. Larger than <see cref="HoverShift"/> by
    /// default, so pressing reads as a further step in the same direction rather than a different state.
    /// </summary>
    public float ActiveShift { get; set; } = 0.22f;

    /// <summary>
    /// The opacity <see cref="Muted"/> applies, for secondary and inactive elements.
    /// </summary>
    public float MutedAlpha { get; set; } = 0.65f;

    /// <summary>
    /// The opacity applied to a widget that is switched off or unavailable.
    /// </summary>
    public float DisabledAlpha { get; set; } = 0.45f;

    /// <summary>
    /// What decides which way <see cref="Hover"/> and <see cref="Active"/> move a color.<br/>
    /// Defaults to <see cref="ThemeTintSource.Item"/>, so each color decides for itself: a dark button brightens and a
    /// pale accent one darkens, and both visibly respond. See <see cref="ThemeTintSource"/> for the alternatives.
    /// </summary>
    public ThemeTintSource TintSource { get; set; } = ThemeTintSource.Item;

    /// <summary>
    /// Whether this theme reads as dark, taken from the brightness of its surface.
    /// </summary>
    public bool IsDark => ColorHelper.IsDark(Resolve(ThemeColor.Surface));

    /// <summary>
    /// The hovered form of a color, moved by <see cref="HoverShift"/> in the direction
    /// <see cref="TintSource"/> chooses.
    /// </summary>
    /// <param name="color">The base color.</param>
    /// <returns>The hovered color.</returns>
    public Vector4 Hover(Vector4 color) => Shift(color, HoverShift);

    /// <summary>
    /// The held form of a color, a further step in the same direction as <see cref="Hover"/>.
    /// </summary>
    /// <param name="color">The base color.</param>
    /// <returns>The held color.</returns>
    public Vector4 Active(Vector4 color) => Shift(color, ActiveShift);

    /// <summary>
    /// Moves a color towards white or towards black, whichever <see cref="TintSource"/> calls for.
    /// </summary>
    /// <param name="color">The base color.</param>
    /// <param name="amount">How far to move it.</param>
    /// <returns>The shifted color.</returns>
    public Vector4 Shift(Vector4 color, float amount)
    {
        var lighten = TintSource switch
        {
            ThemeTintSource.Lighten => true,
            ThemeTintSource.Darken => false,
            ThemeTintSource.Surface => IsDark,
            _ => ColorHelper.IsDark(color),
        };

        return lighten ? ColorHelper.Lighten(color, amount) : ColorHelper.Darken(color, amount);
    }

    /// <summary>
    /// The muted form of a color, faded to <see cref="MutedAlpha"/>.
    /// </summary>
    /// <param name="color">The base color.</param>
    /// <returns>The muted color.</returns>
    public Vector4 Muted(Vector4 color) => ColorHelper.ScaleAlpha(color, MutedAlpha);

    /// <summary>
    /// A text color that stays legible on top of a filled widget, for a button painted in the accent or in danger red.
    /// </summary>
    /// <param name="background">The color the text sits on.</param>
    /// <returns>The legible text color.</returns>
    public Vector4 On(Vector4 background)
        => ColorHelper.Readable(background, Resolve(ThemeColor.Text), new Vector4(0.06f, 0.06f, 0.07f, 1f));

    #endregion

    #region Resolution

    /// <summary>
    /// Resolves a color to an actual value: this theme's override when it has one, the matching ImGui style color
    /// otherwise, and a shipped default when there is no ImGui context or the token has no ImGui equivalent.
    /// </summary>
    /// <param name="token">The color to resolve.</param>
    /// <returns>The resolved color.</returns>
    public Vector4 Resolve(ThemeColor token)
    {
        if (Colors.TryGetValue(token, out var value))
            return value;

        var slot = MapToImGui(token);
        if (slot.HasValue && NoireService.IsInitialized())
            return ImGui.GetStyle().Colors[(int)slot.Value];

        return ShippedDefaults[token];
    }

    /// <summary>
    /// Resolves one of this theme's <see cref="CustomColors"/>.
    /// </summary>
    /// <param name="token">The custom token name.</param>
    /// <param name="fallback">The color to use when the theme does not define that token.</param>
    /// <returns>The resolved color.</returns>
    public Vector4 Resolve(string token, Vector4 fallback)
        => CustomColors.TryGetValue(token, out var value) ? value : fallback;

    // The shape values below are logical units, authored at 100% and scaled here so no widget has to remember to. The
    // ImGui branch of each is returned untouched: Dalamud has already scaled the style, and scaling it a second time is
    // the one way this goes wrong. Only a number NoireUI ships is ever passed through NoireUI.Scaled.

    /// <summary>
    /// Resolves the corner radius of framed widgets.
    /// </summary>
    /// <returns>The rounding in pixels, at the user's scale.</returns>
    public float ResolveRounding()
        => Rounding.HasValue
            ? NoireUI.Scaled(Rounding.Value)
            : NoireService.IsInitialized() ? ImGui.GetStyle().FrameRounding : NoireUI.Scaled(3f);

    /// <summary>
    /// Resolves the corner radius of raised surfaces.
    /// </summary>
    /// <returns>The rounding in pixels, at the user's scale.</returns>
    public float ResolveSurfaceRounding()
        => SurfaceRounding.HasValue
            ? NoireUI.Scaled(SurfaceRounding.Value)
            : NoireService.IsInitialized() ? ImGui.GetStyle().WindowRounding : NoireUI.Scaled(6f);

    /// <summary>
    /// Resolves the border thickness of widgets.
    /// </summary>
    /// <returns>The border size in pixels, at the user's scale.</returns>
    public float ResolveBorderSize()
        => BorderSize.HasValue
            ? NoireUI.Scaled(BorderSize.Value)
            : NoireService.IsInitialized() ? ImGui.GetStyle().FrameBorderSize : 0f;

    /// <summary>
    /// Resolves the padding inside widgets.
    /// </summary>
    /// <returns>The padding in pixels, at the user's scale.</returns>
    public Vector2 ResolveFramePadding()
        => FramePadding.HasValue
            ? NoireUI.Scaled(FramePadding.Value)
            : NoireService.IsInitialized() ? ImGui.GetStyle().FramePadding : NoireUI.Scaled(new Vector2(4f, 3f));

    /// <summary>
    /// Resolves the spacing between consecutive items.
    /// </summary>
    /// <returns>The spacing in pixels, at the user's scale.</returns>
    public Vector2 ResolveItemSpacing()
        => ItemSpacing.HasValue
            ? NoireUI.Scaled(ItemSpacing.Value)
            : NoireService.IsInitialized() ? ImGui.GetStyle().ItemSpacing : NoireUI.Scaled(new Vector2(8f, 4f));

    /// <summary>
    /// Builds a <see cref="UiStyle"/> that paints raw ImGui with this theme, so your own <c>ImGui.Button</c> calls match
    /// the widgets around them.
    /// </summary>
    /// <remarks>
    /// Hand it to <see cref="NoireStyle.With(UiStyle, Action)"/> around a block, or around a whole window body. NoireUI
    /// widgets do not need it: they already resolve through the theme.
    /// </remarks>
    /// <returns>The style to apply.</returns>
    public UiStyle ToStyle()
    {
        var accent = Resolve(ThemeColor.Accent);
        var raised = Resolve(ThemeColor.SurfaceRaised);
        var sunken = Resolve(ThemeColor.SurfaceSunken);

        var style = new UiStyle
        {
            TextColor = Resolve(ThemeColor.Text),
            TextDisabledColor = Resolve(ThemeColor.TextDisabled),
            BorderColor = Resolve(ThemeColor.Border),
            SeparatorColor = Resolve(ThemeColor.Border),
            ChildColor = Resolve(ThemeColor.Surface),
            PopupColor = raised,
            FrameColor = sunken,
            FrameHoveredColor = Hover(sunken),
            FrameActiveColor = Active(sunken),
            ButtonColor = raised,
            ButtonHoveredColor = Hover(raised),
            ButtonActiveColor = Active(raised),
            HeaderColor = ColorHelper.ScaleAlpha(accent, 0.35f),
            FrameRounding = ResolveRounding(),
            PopupRounding = ResolveSurfaceRounding(),
            ChildRounding = ResolveSurfaceRounding(),
            FrameBorderSize = ResolveBorderSize(),
            FramePadding = ResolveFramePadding(),
            ItemSpacing = ResolveItemSpacing(),
        };

        style.With(ImGuiCol.CheckMark, accent);
        style.With(ImGuiCol.SliderGrab, accent);
        style.With(ImGuiCol.SliderGrabActive, Active(accent));
        style.With(ImGuiCol.HeaderHovered, ColorHelper.ScaleAlpha(accent, 0.50f));
        style.With(ImGuiCol.HeaderActive, ColorHelper.ScaleAlpha(accent, 0.65f));
        style.With(ImGuiCol.PlotHistogram, accent);

        return style;
    }

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public NoireTheme Clone()
    {
        var clone = new NoireTheme
        {
            Rounding = Rounding,
            SurfaceRounding = SurfaceRounding,
            BorderSize = BorderSize,
            FramePadding = FramePadding,
            ItemSpacing = ItemSpacing,
            HoverShift = HoverShift,
            ActiveShift = ActiveShift,
            MutedAlpha = MutedAlpha,
            DisabledAlpha = DisabledAlpha,
            TintSource = TintSource,
        };

        foreach (var entry in Colors)
            clone.Colors[entry.Key] = entry.Value;

        foreach (var entry in TextSizes)
            clone.TextSizes[entry.Key] = entry.Value;

        foreach (var entry in CustomColors)
            clone.CustomColors[entry.Key] = entry.Value;

        return clone;
    }

    #endregion

    #region Building

    /// <summary>
    /// Builds a complete palette from a single accent color.<br/>
    /// The surface neutrals are tinted very slightly towards the accent rather than left pure grey, which is what makes
    /// a generated palette read as chosen instead of default, and the text colors are picked for legibility against the
    /// surface that results.
    /// </summary>
    /// <param name="accent">The color to build around.</param>
    /// <param name="dark">Whether to build a dark palette. Defaults to dark, matching the game.</param>
    /// <returns>The built theme.</returns>
    public static NoireTheme FromAccent(Vector4 accent, bool dark = true)
    {
        var tint = new Vector4(accent.X, accent.Y, accent.Z, 1f);

        var surface = dark
            ? ColorHelper.Mix(new Vector4(0.07f, 0.07f, 0.08f, 0.94f), ColorHelper.WithAlpha(tint, 0.94f), 0.06f)
            : ColorHelper.Mix(new Vector4(0.95f, 0.95f, 0.96f, 0.96f), ColorHelper.WithAlpha(tint, 0.96f), 0.05f);

        var raised = dark ? ColorHelper.Lighten(surface, 0.07f) : ColorHelper.Darken(surface, 0.05f);
        var sunken = dark ? ColorHelper.Darken(surface, 0.35f) : ColorHelper.Darken(surface, 0.08f);
        var text = ColorHelper.Readable(surface);

        return new NoireTheme
        {
            Accent = accent,
            Surface = ColorHelper.WithAlpha(surface, surface.W),
            SurfaceRaised = ColorHelper.WithAlpha(raised, 1f),
            SurfaceSunken = ColorHelper.WithAlpha(sunken, 1f),
            Border = ColorHelper.ScaleAlpha(ColorHelper.Mix(text, tint, 0.35f), 0.35f),
            Text = text,
            TextMuted = ColorHelper.Mix(text, surface, 0.35f),
            TextDisabled = ColorHelper.Mix(text, surface, 0.60f),
            Shadow = new Vector4(0f, 0f, 0f, dark ? 0.55f : 0.30f),
            Success = ShippedDefaults[ThemeColor.Success],
            Warning = ShippedDefaults[ThemeColor.Warning],
            Danger = ShippedDefaults[ThemeColor.Danger],
            Info = ShippedDefaults[ThemeColor.Info],
        };
    }

    /// <summary>
    /// Builds a complete palette from a single accent color given as a HEX string.
    /// </summary>
    /// <param name="accentHex">The accent color, for example "#C8A96A". The leading "#" is optional.</param>
    /// <param name="dark">Whether to build a dark palette.</param>
    /// <returns>The built theme.</returns>
    /// <exception cref="ArgumentException">Thrown when the HEX string is not a valid color.</exception>
    public static NoireTheme FromAccent(string accentHex, bool dark = true)
        => FromAccent(ColorHelper.HexToVector4(accentHex), dark);

    #endregion

    #region Sharing

    /// <summary>
    /// The share-code kind themes are tagged with, so a code meant for something else is refused by name rather than
    /// half-read. See <see cref="ShareCodeHelper"/>.
    /// </summary>
    public const string ShareCodeKind = "noire.theme";

    /// <summary>
    /// Encodes this theme as a share code that can be pasted anywhere.
    /// </summary>
    /// <returns>The share code.</returns>
    public string ToShareCode() => ShareCodeHelper.Encode(ShareCodeKind, ThemeSnapshot.From(this));

    /// <summary>
    /// Reads a theme back from a share code written by <see cref="ToShareCode"/>.<br/>
    /// Never throws on a bad paste: a code that is damaged, truncated, meant for something else or simply not a share
    /// code comes back as a failed result carrying a message you can show the user.
    /// </summary>
    /// <param name="code">The pasted code.</param>
    /// <returns>The decoded theme, or a failure describing what was wrong with the code.</returns>
    public static ShareCodeResult<NoireTheme> FromShareCode(string code)
    {
        var decoded = ShareCodeHelper.Decode<ThemeSnapshot>(code, ShareCodeKind);

        if (!decoded.Success || decoded.Value == null)
            return ShareCodeResult<NoireTheme>.Fail(decoded.Error, decoded.Message, decoded.Kind);

        return ShareCodeResult<NoireTheme>.Ok(decoded.Value.ToTheme(), ShareCodeKind);
    }

    #endregion

    private float? GetSize(TextSize size) => TextSizes.TryGetValue(size, out var value) ? value : null;

    private void SetSize(TextSize size, float? value)
    {
        if (value.HasValue)
            TextSizes[size] = value.Value;
        else
            TextSizes.Remove(size);
    }

    private Vector4? Get(ThemeColor token) => Colors.TryGetValue(token, out var value) ? value : null;

    private void Set(ThemeColor token, Vector4? value)
    {
        if (value.HasValue)
            Colors[token] = value.Value;
        else
            Colors.Remove(token);
    }

    /// <summary>
    /// Maps a theme color onto the ImGui style color it falls back to, or <see langword="null"/> for the tokens ImGui
    /// has no equivalent for (the semantic colors and the shadow), which fall back to shipped defaults instead.
    /// </summary>
    private static ImGuiCol? MapToImGui(ThemeColor token) => token switch
    {
        ThemeColor.Accent => ImGuiCol.CheckMark,
        ThemeColor.Surface => ImGuiCol.WindowBg,
        ThemeColor.SurfaceRaised => ImGuiCol.PopupBg,
        ThemeColor.SurfaceSunken => ImGuiCol.FrameBg,
        ThemeColor.Border => ImGuiCol.Border,
        ThemeColor.Text => ImGuiCol.Text,
        ThemeColor.TextMuted => ImGuiCol.TextDisabled,
        ThemeColor.TextDisabled => ImGuiCol.TextDisabled,
        _ => null,
    };
}
