using Dalamud.Interface;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

// Builds and keeps the fonts NoireText draws with: one atlas entry per distinct size, built once and reused for the
// life of the plugin.
internal static class UiFontCache
{
    // Raise it before the first text is drawn; lowering it below what is already built changes nothing.
    public static int MaxSizes { get; set; } = 16;

    // Without this, a scale of 12.999 and one of 13.001 would each take an atlas entry.
    private const int SizePrecision = 0;

    private const string DisposeCallbackKey = "NoireLib.UI.UiFontCache";

    // How long a size no longer in the type scale is kept before it is dropped, in case the scale comes back to it.
    private static readonly TimeSpan ColdSizeLifetime = TimeSpan.FromSeconds(20);

    // Pairs of first and last codepoint, terminated by zero. NoireText.GlyphRanges widens it.
    private static readonly ushort[] DefaultGlyphRanges =
    [
        0x0020, 0x00FF,   // Basic Latin and Latin-1 Supplement
        0x0100, 0x017F,   // Latin Extended-A
        0x2000, 0x206F,   // General punctuation: real quotes, dashes, ellipsis
        0x20A0, 0x20CF,   // Currency symbols
        0x2190, 0x21FF,   // Arrows
        0x2600, 0x26FF,   // Miscellaneous symbols
        0,
    ];

    private sealed class Entry(IFontHandle handle)
    {
        public IFontHandle Handle { get; } = handle;

        public long LastUsedTicks { get; set; } = Stopwatch.GetTimestamp();
    }

    private static readonly Dictionary<float, Entry> Handles = new();
    private static readonly object SyncRoot = new();

    private static IFontAtlas? atlas;
    private static bool warnedFull;

    // Moves whenever the set of built fonts changes, so anything that remembered a measurement can tell that the font
    // it measured with is no longer the font that would draw.
    internal static int Generation => GenerationOverride?.Invoke() ?? generation;

    // Null outside a test, where the real counter answers.
    internal static Func<int>? GenerationOverride { get; set; }

    private static int generation;

    // Whether a size has been registered that the last build did not cover.
    private static bool dirty;

    // The type scale that was last asked for, and when it last changed.
    private static int pendingScaleKey;

    private static long pendingSinceTicks;

    // The NoireUI.Scale the current fonts were rasterized for, or NaN before the first build. Claimed when a build
    // starts rather than when it finishes.
    private static float builtScale = float.NaN;

    // Returns null to draw with the font already current. The handle may not be built yet: check IFontHandle.Available
    // before pushing it, since an unbuilt handle pushes as a no-op and draws at the wrong size.
    internal static IFontHandle? Get(float logicalSizePx)
    {
        if (!NoireService.IsInitialized())
            return null;

        var size = Normalize(logicalSizePx);

        if (IsHostDefault(size))
            return null;

        // Broken out from NoireText, so registering a size, pruning cold ones and asking for a rasterization is its
        // own row rather than time charged to whatever text happened to be first.
        using var draw = UiDraw.Begin();

        IFontHandle? handle;
        bool needsBuild;

        lock (SyncRoot)
        {
            var stale = ScaleMovedLocked();

            if (!stale && Handles.TryGetValue(size, out var existing))
            {
                existing.LastUsedTicks = Stopwatch.GetTimestamp();
                return existing.Handle;
            }

            // Nothing built yet. If the scale is still moving, do not build: a size being dragged is a different size
            // next frame, and rasterizing every step would fill the cache with sizes nobody keeps. Falls back to the
            // stretched stand-in instead.
            if (!ScaleSettledLocked())
                return null;

            PruneLocked();
            RegisterScaleLocked();

            handle = Handles.TryGetValue(size, out var built) ? built.Handle : CreateLocked(size);
            needsBuild = dirty || stale;
        }

        // Asked for outside the lock, and never blocking: this path runs while a frame is being drawn, and the time
        // would come out of that frame.
        if (needsBuild)
            Rebuild(blocking: false);

        return handle;
    }

    // Builds every size the current theme's type scale resolves to.
    internal static void BuildScale(bool waitForCompletion = false)
    {
        if (!NoireService.IsInitialized())
            return;

        bool needsBuild;

        lock (SyncRoot)
        {
            // Asked for outright rather than inferred from a draw, so it is not held back to see whether the scale
            // settles. A caller that says "build this now" means now.
            MarkScaleSettledLocked();
            PruneLocked();
            RegisterScaleLocked();
            needsBuild = dirty || ScaleMovedLocked();
        }

        if (needsBuild)
            Rebuild(waitForCompletion);
    }

    // Callers hold SyncRoot. A moved UI scale makes every built size the wrong one.
    private static bool ScaleMovedLocked()
        => !float.IsNaN(builtScale) && MathF.Abs(NoireUI.Scale - builtScale) > 0.001f;

    // Callers hold SyncRoot. Keyed on the whole scale rather than one size, and reads false while it is moving.
    private static bool ScaleSettledLocked()
    {
        var key = CurrentScaleKey();
        var now = Stopwatch.GetTimestamp();

        if (key != pendingScaleKey)
        {
            pendingScaleKey = key;
            pendingSinceTicks = now;
            return false;
        }

        return Stopwatch.GetElapsedTime(pendingSinceTicks, now) >= NoireText.RebuildSettleDelay;
    }

    // Callers hold SyncRoot. Treats the current scale as settled, for a caller that asked for a build directly.
    private static void MarkScaleSettledLocked()
    {
        pendingScaleKey = CurrentScaleKey();
        pendingSinceTicks = 0;
    }

    // A value that changes whenever any step of the current theme's type scale changes.
    private static int CurrentScaleKey()
    {
        var theme = NoireTheme.Current;
        var key = new HashCode();

        foreach (var step in Enum.GetValues<TextSize>())
            key.Add(Normalize(theme.ResolveTextSize(step)));

        return key.ToHashCode();
    }

    // Callers hold SyncRoot. Only sizes both out of the current scale and cold are dropped, so nothing being drawn
    // with is disposed.
    private static void PruneLocked()
    {
        if (Handles.Count == 0)
            return;

        var theme = NoireTheme.Current;
        var live = new HashSet<float>();

        foreach (var step in Enum.GetValues<TextSize>())
            live.Add(Normalize(theme.ResolveTextSize(step)));

        var now = Stopwatch.GetTimestamp();
        List<float>? cold = null;

        foreach (var entry in Handles)
        {
            if (live.Contains(entry.Key) || Stopwatch.GetElapsedTime(entry.Value.LastUsedTicks, now) < ColdSizeLifetime)
                continue;

            (cold ??= []).Add(entry.Key);
        }

        if (cold == null)
            return;

        foreach (var size in cold)
        {
            try
            {
                Handles[size].Handle.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to dispose the unused {size:0.#} px text font.", nameof(NoireText));
            }

            Handles.Remove(size);
        }

        // A dropped size measures with the stand-in again from here on, so anything holding its numbers has to stop.
        Interlocked.Increment(ref generation);
    }

    internal static int BuiltSizeCount
    {
        get
        {
            lock (SyncRoot)
                return Handles.Count;
        }
    }

    // Releases every built font and the atlas holding them. Registered with NoireLib's own disposal.
    internal static void Cleanup()
    {
        lock (SyncRoot)
        {
            foreach (var entry in Handles.Values)
            {
                try
                {
                    entry.Handle.Dispose();
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

            UiTextMeasureCache.Clear();

            atlas = null;
            warnedFull = false;
            dirty = false;
            builtScale = float.NaN;
            pendingScaleKey = 0;
            pendingSinceTicks = 0;
        }
    }

    // Rasterizes everything registered since the last build. Blocking rasterizes on this thread.
    private static void Rebuild(bool blocking)
    {
        IFontAtlas? target;
        int sizes;

        lock (SyncRoot)
        {
            target = atlas;

            if (target == null)
                return;

            sizes = Handles.Count;

            // Claimed before the work starts, not after it finishes: an asynchronous build takes seconds, and every
            // draw during those seconds would otherwise see the same staleness and ask for the same build again.
            dirty = false;
            builtScale = NoireUI.Scale;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            if (blocking)
            {
                // Everything happens on this thread, so it needs nothing from a frame that is not running yet.
                target.BuildFontsImmediately();
                Report(sizes, started);
                return;
            }

            target.BuildFontsAsync().ContinueWith(_ => Report(sizes, started), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireText), "Failed to build the text fonts.", ex);
        }
    }

    private static void Report(int sizes, long started)
    {
        // Bumped here rather than when the build was asked for: what invalidates a remembered measurement is the
        // font becoming available, not the build being queued.
        Interlocked.Increment(ref generation);

        NoireLogger.LogInformation(
            $"Built {sizes} text size(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms. "
            + "Sizes are rasterized as real glyphs, so this scales with how many glyph ranges the Dalamud language settings ask for. "
            + $"Call {nameof(NoireText)}.{nameof(NoireText.Prewarm)}(wait: true) from your plugin's constructor to spend it at load instead.",
            nameof(NoireText));
    }

    // Callers hold SyncRoot. Registers every step of the current theme's scale that is not registered yet, without
    // building anything.
    private static void RegisterScaleLocked()
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

    // Callers hold SyncRoot. Registers one size, or reports why it could not.
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
                e => e.OnPreBuild(tk => BuildFont(tk, size)));

            Handles[size] = new Entry(handle);
            dirty = true;
            return handle;
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireText), $"Failed to register a font at {size:0.#} px.", ex);
            return null;
        }
    }

    // Icons and the rest of Unicode are dropped; NoireText.FontBuilder puts either back.
    private static void BuildFont(IFontAtlasBuildToolkitPreBuild toolkit, float size)
    {
        if (NoireText.FontBuilder is { } custom)
        {
            custom(toolkit, size);
            return;
        }

        // A font specification NoireLib does not recognise is not worth guessing at: fall back to the complete font,
        // slow and certainly correct, rather than rendering someone's chosen typeface as something else.
        if (NoireService.PluginInterface.UiBuilder.DefaultFontSpec is not SingleFontSpec spec)
        {
            toolkit.AddDalamudDefaultFont(size);
            return;
        }

        var resized = spec with { SizePx = size, GlyphRanges = NoireText.GlyphRanges ?? DefaultGlyphRanges };
        var font = resized.AddToBuildToolkit(toolkit, default);

        var extra = new SafeFontConfig { SizePx = size, MergeFont = font };
        toolkit.AttachExtraGlyphsForDalamudLanguage(ref extra);
    }

    // Rounds a requested size to the precision sizes are cached at.
    private static float Normalize(float logicalSizePx)
        => MathF.Round(MathF.Max(1f, logicalSizePx), SizePrecision);

    // The host's own size is already built and needs nothing from this cache.
    private static bool IsHostDefault(float normalizedSize)
        => Math.Abs(normalizedSize - Normalize(NoireTheme.DefaultBodySize)) < float.Epsilon;

    // Callers hold SyncRoot. Global-scaled, so every size in the public surface stays a logical one, and
    // auto-rebuilding is off.
    private static IFontAtlas EnsureAtlas()
    {
        if (atlas != null)
            return atlas;

        atlas = NoireService.PluginInterface.UiBuilder.CreateFontAtlas(
            FontAtlasAutoRebuildMode.Disable,
            isGlobalScaled: true,
            debugName: "NoireText");

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);

        return atlas;
    }

    // The closest size already built, for a request that arrived after the cache filled up.
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
            nearest = entry.Value.Handle;
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
