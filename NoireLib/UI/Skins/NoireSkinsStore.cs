using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

// Keys in the plugin's NoireUiState file: noireui.skin, noireui.colors.<skin>, noireui.layout.<skin>.<window> and
// noireui.chrome.<key>, shared by every skin.
internal static class NoireSkinsStore
{
    internal const string SkinKey = "noireui.skin";
    internal const string ColorsPrefix = "noireui.colors.";
    internal const string LayoutPrefix = "noireui.layout.";
    internal const string ChromePrefix = "noireui.chrome.";

    // One instance per key: windows naming the same key edit the same options.
    private static readonly Dictionary<string, WindowMenuSettings> Chrome = new();

    // Read once per skin: a read deserializes, and the theme editor asks every frame.
    private static readonly Dictionary<string, Dictionary<string, Vector4>> Colors = new();

    internal static string? LoadSkin() => NoireUiState.Get<string?>(SkinKey, null);

    internal static void SaveSkin(string id) => NoireUiState.Set(SkinKey, id);

    // Moves on every save of any skin's colours.
    internal static int ColorsRevision { get; private set; }

    internal static Dictionary<string, Vector4> LoadColors(string skin)
    {
        if (Colors.TryGetValue(skin, out var cached))
            return cached;

        var loaded = NoireUiState.Get<Dictionary<string, Vector4>>(ColorsPrefix + skin, null) ?? new Dictionary<string, Vector4>();
        Colors[skin] = loaded;
        return loaded;
    }

    internal static void SaveColors(string skin, Dictionary<string, Vector4> colors)
    {
        Colors[skin] = colors;
        ColorsRevision++;

        if (colors.Count == 0)
            NoireUiState.Remove(ColorsPrefix + skin);
        else
            NoireUiState.Set(ColorsPrefix + skin, colors);
    }

    internal static ComponentLayout LoadLayout(string skin, string window)
        => NoireUiState.Get<ComponentLayout>(LayoutPrefix + skin + "." + window, null) ?? new ComponentLayout();

    internal static void SaveLayout(string skin, string window, ComponentLayout layout)
    {
        if (layout.IsDefault)
            NoireUiState.Remove(LayoutPrefix + skin + "." + window);
        else
            NoireUiState.Set(LayoutPrefix + skin + "." + window, layout);
    }

    internal static WindowMenuSettings LoadChrome(string key)
    {
        if (Chrome.TryGetValue(key, out var cached))
            return cached;

        var loaded = NoireUiState.Get<WindowMenuSettings>(ChromePrefix + key, null) ?? new WindowMenuSettings();
        Chrome[key] = loaded;
        return loaded;
    }

    internal static void SaveChrome(string key, WindowMenuSettings settings) => NoireUiState.Set(ChromePrefix + key, settings);

    // Tests reload the store between cases.
    internal static void ForgetCache()
    {
        Chrome.Clear();
        Colors.Clear();
        ColorsRevision++;
    }
}
