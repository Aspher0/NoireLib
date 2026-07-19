using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// Builds and keeps the fonts <see cref="NoireText"/> draws with: one atlas entry per distinct size, built once and
/// reused for the life of the plugin.
/// </summary>
/// <remarks>
/// The reason this is a cache and not a helper that builds a font per call is texture memory. Every distinct size is a
/// full glyph atlas, and a plugin that quietly built one per frame would grow until the game ran out of it. So the
/// cache is bounded, refuses to grow past its limit rather than evicting something a caller may be drawing with, and
/// says so once by name.<br/>
/// NoireLib builds into its own atlas rather than the plugin's shared one so that adding a heading never forces the
/// host's own fonts to rebuild alongside it.
/// </remarks>
internal static class UiFontCache
{
    /// <summary>
    /// How many distinct sizes may be built. An interface with more genuinely different text sizes than this is a type
    /// scale that has stopped being one, and the atlas is the wrong place to discover that.
    /// </summary>
    private const int MaxSizes = 16;

    /// <summary>
    /// How close two requested sizes must be to share a font. Sizes arrive as products of a ratio and a body size, so
    /// without this a scale of 12.999 and one of 13.001 would each take an atlas entry.
    /// </summary>
    private const int SizePrecision = 1;

    private const string DisposeCallbackKey = "NoireLib.UI.UiFontCache";

    private static readonly Dictionary<float, IFontHandle> Handles = new();
    private static readonly object SyncRoot = new();

    private static IFontAtlas? atlas;
    private static bool warnedFull;

    /// <summary>
    /// The font to draw a given logical size with, or <see langword="null"/> to use the font already current.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for the host's own default size rather than building a copy of a font that
    /// already exists, so an interface that only ever draws body text costs no atlas space at all.<br/>
    /// A miss builds the whole of the current theme's type scale rather than only the size asked for. Registering a
    /// handle asks the atlas to rebuild, and building the four steps as they were first drawn meant four rebuilds one
    /// after another, which is seconds of an interface at the wrong size. One miss, one rebuild, every step.<br/>
    /// The handle it returns may still not be built. Callers must check <see cref="IFontHandle.Available"/> rather than
    /// pushing blindly: an unbuilt handle pushes as a no-op, which draws the text at the wrong size instead of the
    /// approximation <see cref="NoireText"/> falls back to.
    /// </remarks>
    /// <param name="logicalSizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The font handle, or <see langword="null"/> to draw with the current font.</returns>
    internal static IFontHandle? Get(float logicalSizePx)
    {
        if (!NoireService.IsInitialized())
            return null;

        var size = Normalize(logicalSizePx);

        if (IsHostDefault(size))
            return null;

        lock (SyncRoot)
        {
            if (Handles.TryGetValue(size, out var existing))
                return existing;

            BuildScaleLocked();

            return Handles.TryGetValue(size, out var built) ? built : CreateLocked(size);
        }
    }

    /// <summary>
    /// Builds every size the current theme's type scale resolves to, so they arrive in one atlas rebuild.
    /// </summary>
    /// <remarks>
    /// Run on every miss rather than once at startup, because the scale moves: a plugin that sets
    /// <see cref="NoireTheme.BodySize"/> from its settings shifts all four steps at once, and rebuilding them one at a
    /// time as each is next drawn would put the delay back.
    /// </remarks>
    internal static void BuildScale()
    {
        if (!NoireService.IsInitialized())
            return;

        lock (SyncRoot)
            BuildScaleLocked();
    }

    /// <summary>
    /// How many distinct sizes have been built. Reported by the diagnostics so an atlas filling up is visible before it
    /// is a problem.
    /// </summary>
    internal static int BuiltSizeCount
    {
        get
        {
            lock (SyncRoot)
                return Handles.Count;
        }
    }

    /// <summary>
    /// Releases every built font and the atlas holding them. Registered with NoireLib's own disposal.
    /// </summary>
    internal static void Cleanup()
    {
        lock (SyncRoot)
        {
            foreach (var handle in Handles.Values)
            {
                try
                {
                    handle.Dispose();
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, "Failed to dispose a NoireText font handle.", nameof(NoireText));
                }
            }

            Handles.Clear();

            try
            {
                atlas?.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "Failed to dispose the NoireText font atlas.", nameof(NoireText));
            }

            atlas = null;
            warnedFull = false;
        }
    }

    /// <summary>
    /// Builds every step of the current theme's scale that is not built yet. Callers hold <see cref="SyncRoot"/>.
    /// </summary>
    private static void BuildScaleLocked()
    {
        var theme = NoireTheme.Current;

        foreach (var step in Enum.GetValues<TextSize>())
        {
            var size = Normalize(theme.ResolveTextSize(step));

            if (IsHostDefault(size) || Handles.ContainsKey(size))
                continue;

            CreateLocked(size);
        }
    }

    /// <summary>
    /// Builds one size, or reports why it could not. Callers hold <see cref="SyncRoot"/>.
    /// </summary>
    private static IFontHandle? CreateLocked(float size)
    {
        if (Handles.Count >= MaxSizes)
        {
            WarnFull(size);
            return NearestBuilt(size);
        }

        try
        {
            var handle = EnsureAtlas().NewDelegateFontHandle(
                e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(size)));

            Handles[size] = handle;
            return handle;
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireText), $"Failed to build a font at {size:0.#} px.", ex);
            return null;
        }
    }

    /// <summary>
    /// Rounds a requested size to the precision sizes are cached at, so two requests a hundredth of a pixel apart do
    /// not each take an atlas entry.
    /// </summary>
    private static float Normalize(float logicalSizePx)
        => MathF.Round(MathF.Max(1f, logicalSizePx), SizePrecision);

    /// <summary>
    /// Whether a size is the host's own, which is already built and needs nothing from this cache.
    /// </summary>
    private static bool IsHostDefault(float normalizedSize)
        => Math.Abs(normalizedSize - Normalize(NoireTheme.DefaultBodySize)) < float.Epsilon;

    /// <summary>
    /// Creates the atlas on first use. Callers hold <see cref="SyncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Global-scaled on purpose, which is what makes every size in the public surface a logical one: Dalamud multiplies
    /// the size by the user's scale when it rasterizes, and rebuilds the atlas by itself when that scale changes. A
    /// plain atlas would mean scaling the size here and rebuilding every font whenever the user moved the slider.
    /// </remarks>
    private static IFontAtlas EnsureAtlas()
    {
        if (atlas != null)
            return atlas;

        atlas = NoireService.PluginInterface.UiBuilder.CreateFontAtlas(
            FontAtlasAutoRebuildMode.Async,
            isGlobalScaled: true,
            debugName: "NoireText");

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);

        return atlas;
    }

    /// <summary>
    /// The closest size already built, for a request that arrived after the cache filled up. Drawing at nearly the
    /// right size reads as a rounding error; falling back to the body size would read as the request being ignored.
    /// </summary>
    private static IFontHandle? NearestBuilt(float size)
    {
        IFontHandle? nearest = null;
        var best = float.MaxValue;

        foreach (var entry in Handles)
        {
            var distance = MathF.Abs(entry.Key - size);
            if (distance >= best)
                continue;

            best = distance;
            nearest = entry.Value;
        }

        return nearest;
    }

    private static void WarnFull(float size)
    {
        if (warnedFull)
            return;

        warnedFull = true;

        NoireLogger.LogWarning(
            $"NoireText has built {MaxSizes} distinct font sizes and will not build more, so {size:0.#} px is being drawn at the nearest size already built. "
            + $"Each size is a full glyph atlas, so this limit is what stops an interface from exhausting texture memory. "
            + $"Ask for text by {nameof(TextSize)} rather than by number, or set the sizes you need on {nameof(NoireTheme)}.",
            nameof(NoireText));
    }
}
