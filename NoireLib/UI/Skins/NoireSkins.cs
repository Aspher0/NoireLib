using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The plugin's skins, the active one and its theme. Remembers the user's choice and colour edits in
/// <see cref="NoireUiState"/>. Draw thread only.
/// </summary>
public static class NoireSkins
{
    private static readonly Dictionary<string, NoireSkin> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NoireSkin> Ordered = [];
    private static readonly Dictionary<NoireSkin, NoireTheme> EditedThemes = new();

    private static NoireTheme? theme;
    private static NoireSkin? themeSkin;
    private static NoireTheme? themeHost;
    private static int themeDepth;
    private static NoireTheme? hostWhileDrawing;
    private static ToastStyle? pluginToasts;
    private static bool toastsApplied;
    private static int editedThemesRevision = -1;

    /// <summary>Every registered skin, in registration order.</summary>
    public static IReadOnlyList<NoireSkin> All => Ordered;

    /// <summary>The skin used when nothing was picked yet or the remembered one no longer exists: the first registered.</summary>
    public static NoireSkin Default => Ordered.Count > 0 ? Ordered[0] : StockSkin.Instance;

    /// <summary>The active skin.</summary>
    public static NoireSkin Active { get; private set; } = StockSkin.Instance;

    /// <summary>The active skin's theme with the user's edits on top; skinned windows draw with it as <see cref="NoireTheme.Current"/>.</summary>
    public static NoireTheme Theme
    {
        get
        {
            var host = HostTheme;

            if (theme == null || !ReferenceEquals(themeSkin, Active) || !ReferenceEquals(themeHost, host))
            {
                theme = BuildTheme(Active);
                themeSkin = Active;
                themeHost = host;
                ThemeRevision++;
            }

            return theme;
        }
    }

    /// <summary>Moves on every theme change, switch or edit. A skin refreshes the colours it derives.</summary>
    public static int ThemeRevision { get; private set; }

    // The plugin's own NoireTheme.Current, which a skinned window replaces while it draws.
    internal static NoireTheme HostTheme => themeDepth > 0 ? hostWhileDrawing! : NoireTheme.Current;

    /// <summary>Registers the plugin's skins, the first being the default, then activates the one the user picked last.</summary>
    /// <param name="skins">The skins.</param>
    public static void Register(params NoireSkin[] skins)
    {
        ArgumentNullException.ThrowIfNull(skins);

        foreach (var skin in skins)
        {
            ArgumentNullException.ThrowIfNull(skin);

            if (ById.TryAdd(skin.Id, skin))
                Ordered.Add(skin);
        }

        Use(Get(NoireSkinsStore.LoadSkin() ?? string.Empty) ?? Default, remember: false);
    }

    /// <summary>The registered skin with an id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The skin, or <see langword="null"/>.</returns>
    public static NoireSkin? Get(string id) => ById.GetValueOrDefault(id);

    /// <summary>Activates the registered skin with an id, or <see cref="Default"/> when there is none, and remembers it.</summary>
    /// <param name="id">The id.</param>
    public static void Use(string id) => Use(Get(id) ?? Default);

    /// <summary>Activates a skin and remembers it. Open windows stay open and redraw with it.</summary>
    /// <param name="skin">The skin.</param>
    public static void Use(NoireSkin skin) => Use(skin, remember: true);

    /// <summary>The user's colour for a role of a skin.</summary>
    /// <param name="skin">The skin.</param>
    /// <param name="role">The role.</param>
    /// <returns>The colour, or <see langword="null"/> when unchanged.</returns>
    public static Vector4? EditedColor(NoireSkin skin, ThemeRole role)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(role);

        return NoireSkinsStore.LoadColors(skin.Id).TryGetValue(role.Key, out var color) ? color : null;
    }

    /// <summary>Changes one colour of a skin, or restores it with <see langword="null"/>. Applies at once.</summary>
    /// <param name="skin">The skin.</param>
    /// <param name="role">The role.</param>
    /// <param name="color">The colour.</param>
    public static void EditColor(NoireSkin skin, ThemeRole role, Vector4? color)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(role);

        var colors = NoireSkinsStore.LoadColors(skin.Id);

        if (color is { } value)
            colors[role.Key] = value;
        else
            colors.Remove(role.Key);

        NoireSkinsStore.SaveColors(skin.Id, colors);

        if (ReferenceEquals(skin, Active))
            theme = null;
    }

    /// <summary>Restores every colour of a skin.</summary>
    /// <param name="skin">The skin.</param>
    public static void ResetColors(NoireSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        NoireSkinsStore.SaveColors(skin.Id, new Dictionary<string, Vector4>());

        if (ReferenceEquals(skin, Active))
            theme = null;
    }

    /// <summary>A skin's theme with the user's colours, as a share code.</summary>
    /// <param name="skin">The skin.</param>
    /// <returns>The code.</returns>
    public static string ExportColors(NoireSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        return BuildTheme(skin).ToShareCode();
    }

    /// <summary>Applies a colour share code to the colours a skin lets the user change; every other colour in it is ignored.</summary>
    /// <param name="skin">The skin.</param>
    /// <param name="code">The code, as written by <see cref="ExportColors"/>.</param>
    /// <returns>How many colours were applied, or why the code could not be read.</returns>
    public static ShareCodeResult<int> ImportColors(NoireSkin skin, string? code)
    {
        ArgumentNullException.ThrowIfNull(skin);

        var decoded = NoireTheme.FromShareCode(code ?? string.Empty);

        if (!decoded.Success || decoded.Value is not { } shared)
            return ShareCodeResult<int>.Fail(decoded.Error, decoded.Message, decoded.Kind);

        var colors = NoireSkinsStore.LoadColors(skin.Id);
        var applied = 0;

        foreach (var role in skin.EditableColors)
        {
            var found = Enum.TryParse<ThemeColor>(role.Key, out var token)
                ? shared.Colors.TryGetValue(token, out var color)
                : shared.CustomColors.TryGetValue(role.Key, out color);

            if (!found)
                continue;

            colors[role.Key] = color;
            applied++;
        }

        NoireSkinsStore.SaveColors(skin.Id, colors);

        if (ReferenceEquals(skin, Active))
            theme = null;

        return ShareCodeResult<int>.Ok(applied, decoded.Kind);
    }

    // A skin's theme with the user's edits, for previews of skins that are not active. Rebuilt after an edit.
    internal static NoireTheme ThemeOf(NoireSkin skin)
    {
        if (ReferenceEquals(skin, Active))
            return Theme;

        if (editedThemesRevision != NoireSkinsStore.ColorsRevision)
        {
            EditedThemes.Clear();
            editedThemesRevision = NoireSkinsStore.ColorsRevision;
        }

        if (NoireSkinsStore.LoadColors(skin.Id).Count == 0)
            return skin.Theme;

        if (!EditedThemes.TryGetValue(skin, out var built))
            EditedThemes[skin] = built = BuildTheme(skin);

        return built;
    }

    // Inside, NoireTheme.Current is the skin's theme: every widget a skin draws follows it.
    internal static void EnterTheme()
    {
        if (themeDepth++ == 0)
        {
            hostWhileDrawing = NoireTheme.Current;
            NoireTheme.Current = Theme;
        }
    }

    internal static void LeaveTheme()
    {
        if (themeDepth == 0 || --themeDepth > 0)
            return;

        NoireTheme.Current = hostWhileDrawing;
        hostWhileDrawing = null;
    }

    // Tests start from Stock with nothing registered.
    internal static void Reset()
    {
        ById.Clear();
        Ordered.Clear();
        Active = StockSkin.Instance;
        theme = null;
        themeSkin = null;
        themeHost = null;
        themeDepth = 0;
        hostWhileDrawing = null;
        EditedThemes.Clear();
        NoireSkinsStore.ForgetCache();
    }

    private static void Use(NoireSkin skin, bool remember)
    {
        ArgumentNullException.ThrowIfNull(skin);

        if (remember)
            NoireSkinsStore.SaveSkin(skin.Id);

        if (ReferenceEquals(skin, Active))
            return;

        var previous = Active;
        previous.Deactivate();
        Active = skin;
        skin.Activate();
        theme = null;
        NoireSkinnedWindowBase.SkinChanged();
        ApplyToasts();
    }

    // Without edits the skin's own theme is used: a plugin changing it in place is followed live.
    private static NoireTheme BuildTheme(NoireSkin skin)
    {
        var colors = NoireSkinsStore.LoadColors(skin.Id);

        if (colors.Count == 0)
            return skin.Theme;

        var built = skin.Theme.Clone();

        foreach (var (key, color) in colors)
        {
            if (Enum.TryParse<ThemeColor>(key, out var token))
                built.Colors[token] = color;
            else
                built.CustomColors[key] = color;
        }

        return built;
    }

    // The default toast area takes the active skin's toast look, and gets the plugin's own back under a skin without one.
    private static void ApplyToasts()
    {
        var style = Active.Overlays.Toasts;

        if ((style == null && !toastsApplied) || !NoireService.IsInitialized())
            return;

        var area = NoireToastArea.Default;

        if (!toastsApplied)
        {
            pluginToasts = area.Style;
            toastsApplied = true;
        }

        area.Style = style ?? pluginToasts ?? new ToastStyle();
    }
}
