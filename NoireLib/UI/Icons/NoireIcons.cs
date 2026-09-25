using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Icons drawn like glyphs: every <see cref="NoireIcon"/> and any artwork a plugin registers by name. Registered
/// names are private to the plugin.
/// </summary>
[NoireFacade]
public static class NoireIcons
{
    private static readonly int[] BuiltSizes = [20, 24, 32, 40, 48, 64, 128];

    // Only the brand marks have artwork. Every later NoireIcon value is a glyph.
    private const int BrandMarks = (int)NoireIcon.Kofi + 1;

    private static readonly UiImageSource?[,] BuiltIn = new UiImageSource?[BrandMarks, BuiltSizes.Length];

    private static readonly Dictionary<string, UiImageSource> Registered = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReportedUnknown = new(StringComparer.Ordinal);
    private static readonly object SyncRoot = new();

    #region Registry

    /// <summary>Registers artwork under a name, replacing whatever the name held.</summary>
    /// <param name="name">The name to draw it by.</param>
    /// <param name="source">The artwork.</param>
    public static void Register(string name, UiImageSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);

        lock (SyncRoot)
        {
            Registered[name] = source;
            ReportedUnknown.Remove(name);
        }
    }

    /// <summary>Removes a registered name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the name was registered.</returns>
    public static bool Unregister(string name)
    {
        lock (SyncRoot)
            return name != null && Registered.Remove(name);
    }

    /// <summary>Whether a name is registered.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsRegistered(string name)
    {
        lock (SyncRoot)
            return name != null && Registered.ContainsKey(name);
    }

    /// <summary>The artwork registered under a name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The artwork, or <see langword="null"/> when the name is not registered.</returns>
    public static UiImageSource? Source(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        lock (SyncRoot)
            return Registered.TryGetValue(name, out var source) ? source : null;
    }

    /// <summary>
    /// The built-in artwork for a brand mark, at the rasterized size that draws a given pixel size most sharply.
    /// </summary>
    /// <param name="icon">The mark.</param>
    /// <param name="pixelSize">The size it will be drawn at, in real pixels.</param>
    /// <returns>The artwork, or <see langword="null"/> for an icon drawn as a glyph.</returns>
    public static UiImageSource? Source(NoireIcon icon, float pixelSize)
    {
        if ((uint)icon >= BrandMarks)
            return null;

        var slot = BuiltSizes.Length - 1;

        for (var index = 0; index < BuiltSizes.Length; index++)
        {
            if (BuiltSizes[index] + 0.5f >= pixelSize)
            {
                slot = index;
                break;
            }
        }

        return BuiltIn[(int)icon, slot] ??= UiImageSource.FromManifestResource(
            typeof(NoireIcons).Assembly,
            $"NoireLib.UI.Icons.{ResourceStem(icon)}_{BuiltSizes[slot]}.png");
    }

    private static string ResourceStem(NoireIcon icon) => icon switch
    {
        NoireIcon.Kofi => "kofi",
        _ => "discord",
    };

    /// <summary>The FontAwesome glyph an icon is drawn with.</summary>
    /// <param name="icon">The icon.</param>
    /// <returns>The glyph, or <see langword="null"/> for a brand mark, which is drawn from its artwork.</returns>
    public static FontAwesomeIcon? Glyph(NoireIcon icon) => icon switch
    {
        NoireIcon.Play => FontAwesomeIcon.Play,
        NoireIcon.Hotbar => FontAwesomeIcon.Th,
        NoireIcon.Star => FontAwesomeIcon.Star,
        NoireIcon.Block => FontAwesomeIcon.Ban,
        NoireIcon.Info => FontAwesomeIcon.InfoCircle,
        NoireIcon.Copy => FontAwesomeIcon.Copy,
        NoireIcon.Settings => FontAwesomeIcon.Cog,
        NoireIcon.Logs => FontAwesomeIcon.List,
        NoireIcon.Changelog => FontAwesomeIcon.Book,
        NoireIcon.Search => FontAwesomeIcon.Search,
        NoireIcon.Close => FontAwesomeIcon.Times,
        NoireIcon.Menu => FontAwesomeIcon.Bars,
        NoireIcon.Collapse => FontAwesomeIcon.ChevronUp,
        NoireIcon.Expand => FontAwesomeIcon.ChevronDown,
        NoireIcon.Refresh => FontAwesomeIcon.Redo,
        NoireIcon.Sync => FontAwesomeIcon.Sync,
        NoireIcon.Plus => FontAwesomeIcon.Plus,
        NoireIcon.Trash => FontAwesomeIcon.Trash,
        NoireIcon.Export => FontAwesomeIcon.FileExport,
        NoireIcon.Check => FontAwesomeIcon.Check,
        NoireIcon.Chevron => FontAwesomeIcon.ChevronDown,
        NoireIcon.Warning => FontAwesomeIcon.ExclamationTriangle,
        NoireIcon.Swap => FontAwesomeIcon.ExchangeAlt,
        NoireIcon.Cube => FontAwesomeIcon.Cube,
        NoireIcon.Tag => FontAwesomeIcon.Tag,
        NoireIcon.Overrides => FontAwesomeIcon.LayerGroup,
        NoireIcon.First => FontAwesomeIcon.AngleDoubleLeft,
        NoireIcon.Prev => FontAwesomeIcon.AngleLeft,
        NoireIcon.Next => FontAwesomeIcon.AngleRight,
        NoireIcon.Last => FontAwesomeIcon.AngleDoubleRight,
        NoireIcon.Language => FontAwesomeIcon.Globe,
        NoireIcon.Palette => FontAwesomeIcon.Palette,
        NoireIcon.Layout => FontAwesomeIcon.ThLarge,
        NoireIcon.Grip => FontAwesomeIcon.GripLines,
        NoireIcon.Person => FontAwesomeIcon.User,
        _ => null,
    };

    #endregion

    #region Drawing

    /// <summary>Draws a built-in icon at the cursor as an ImGui item.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="size">The square's side at 100%.</param>
    /// <param name="tint">A color multiplied into the artwork. White when <see langword="null"/>.</param>
    public static void Draw(NoireIcon icon, float size, Vector4? tint = null)
    {
        if (!UiDraw.Available)
            return;

        var side = NoireUI.Scaled(size);
        var min = ImGui.GetCursorScreenPos();

        using (var draw = UiDraw.Begin())
            DrawAt(draw.List, icon, min, side, ColorHelper.Vector4ToUint(tint ?? Vector4.One));

        ImGui.Dummy(new Vector2(side, side));
    }

    /// <summary>Draws registered artwork at the cursor as an ImGui item. An unknown name reserves the space and reports a fault.</summary>
    /// <param name="name">The registered name.</param>
    /// <param name="size">The square's side at 100%.</param>
    /// <param name="tint">A color multiplied into the artwork. White when <see langword="null"/>.</param>
    public static void Draw(string name, float size, Vector4? tint = null)
    {
        if (!UiDraw.Available)
            return;

        var side = NoireUI.Scaled(size);
        var min = ImGui.GetCursorScreenPos();

        using (var draw = UiDraw.Begin())
            DrawAt(draw.List, name, min, side, ColorHelper.Vector4ToUint(tint ?? Vector4.One));

        ImGui.Dummy(new Vector2(side, side));
    }

    /// <summary>Paints a built-in icon into a draw list, fitted into a square, without submitting an item.</summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="min">The square's top left, in screen pixels.</param>
    /// <param name="side">The square's side in real pixels.</param>
    /// <param name="tint">A packed color, multiplied into artwork and used as a glyph's color.</param>
    /// <returns><see langword="true"/> when the icon was painted.</returns>
    public static bool DrawAt(ImDrawListPtr drawList, NoireIcon icon, Vector2 min, float side, uint tint = 0xFFFFFFFFu)
    {
        if (Glyph(icon) is { } glyph)
            return PaintGlyph(drawList, glyph, min, side, tint);

        return Source(icon, side) is { } source && Paint(drawList, source, min, side, tint);
    }

    /// <summary>Paints registered artwork into a draw list, fitted into a square, without submitting an item.</summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="name">The registered name.</param>
    /// <param name="min">The square's top left, in screen pixels.</param>
    /// <param name="side">The square's side in real pixels.</param>
    /// <param name="tint">A packed color multiplied into the artwork.</param>
    /// <returns><see langword="true"/> when the artwork was loaded and painted.</returns>
    public static bool DrawAt(ImDrawListPtr drawList, string name, Vector2 min, float side, uint tint = 0xFFFFFFFFu)
    {
        var source = Source(name);

        if (source == null)
        {
            ReportUnknown(name);
            return false;
        }

        return Paint(drawList, source, min, side, tint);
    }

    // Nothing is painted while the texture loads.
    private static bool Paint(ImDrawListPtr drawList, UiImageSource source, Vector2 min, float side, uint tint)
    {
        if (drawList.IsNull || side <= 0f || !NoireService.IsInitialized())
            return false;

        var wrap = source.GetWrap();

        if (wrap == null)
            return false;

        var native = wrap.Size;

        if (native.X <= 4f && native.Y <= 4f)
            return false;

        var fit = side / MathF.Max(native.X, native.Y);
        var size = native * fit;
        var at = min + ((new Vector2(side, side) - size) * 0.5f);

        drawList.AddImage(wrap.Handle, at, at + size, Vector2.Zero, Vector2.One, tint);
        return true;
    }

    // Without Dalamud the glyph falls back to the current font.
    private static bool PaintGlyph(ImDrawListPtr drawList, FontAwesomeIcon glyph, Vector2 min, float side, uint tint)
    {
        if (drawList.IsNull || side <= 0f)
            return false;

        var font = UiIconFont.Current;
        var text = UiValueText.Icon(glyph);
        Vector2 natural;

        using (UiPush.Font(font))
            natural = NoireText.CalcSizeInCurrentFont(text) * (side / MathF.Max(1f, ImGui.GetFontSize()));

        var fit = MathF.Min(1f, side / MathF.Max(1f, MathF.Max(natural.X, natural.Y)));
        var at = min + ((new Vector2(side, side) - (natural * fit)) * 0.5f);

        drawList.AddText(font, side * fit, at, tint, text);
        return true;
    }

    private static void ReportUnknown(string? name)
    {
        var key = name ?? string.Empty;

        lock (SyncRoot)
        {
            if (!ReportedUnknown.Add(key))
                return;
        }

        NoireUI.Diagnostics.ReportFault(nameof(NoireIcons), $"No icon is registered as '{key}'.", null);
    }

    #endregion
}
