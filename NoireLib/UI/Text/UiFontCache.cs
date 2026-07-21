using Dalamud.Interface;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
    /// <summary>
    /// How many distinct pixel sizes may be built before the cache refuses more.
    /// </summary>
    /// <remarks>
    /// Every size is an atlas entry, and every atlas rebuild re-rasterizes all of them, so this is a real budget rather
    /// than a formality. The default suits a plugin with one type scale; a host offering the reader several scales has
    /// as many sizes as steps times scale, and should raise it deliberately rather than have its largest heading
    /// silently drawn at its second-largest size.<br/>
    /// Raise it before the first text is drawn. Lowering it below what is already built changes nothing that exists.
    /// </remarks>
    public static int MaxSizes { get; set; } = 16;

    /// <summary>
    /// How close two requested sizes must be to share a font. Sizes arrive as products of a ratio and a body size, so
    /// without this a scale of 12.999 and one of 13.001 would each take an atlas entry.
    /// </summary>
    /// <remarks>
    /// Whole pixels. Glyphs are rasterized onto a pixel grid, so a tenth of a pixel is not a different font, it is a
    /// different cache entry for the same one. It matters most while a size is being dragged: at a tenth of a pixel a
    /// slider sweep asks for hundreds of distinct sizes, and at whole pixels it asks for a couple of dozen.
    /// </remarks>
    private const int SizePrecision = 0;

    private const string DisposeCallbackKey = "NoireLib.UI.UiFontCache";

    /// <summary>
    /// How long a size no longer in the type scale is kept before it is dropped, in case the scale comes back to it.
    /// </summary>
    private static readonly TimeSpan ColdSizeLifetime = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The glyphs a size is rasterized with when nothing says otherwise: Latin with its accents, the punctuation real
    /// prose uses, currency, arrows and the common symbols.
    /// </summary>
    /// <remarks>
    /// Pairs of first and last codepoint, terminated by zero, which is the shape ImGui wants.<br/>
    /// Around seven hundred glyphs rather than the several thousand a complete font carries. Anything outside this and
    /// the user's own language is a deliberate trade for a type scale that is ready when the window opens: see
    /// <see cref="NoireText.GlyphRanges"/> to widen it.
    /// </remarks>
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

    /// <summary>
    /// A built size, and when something last drew with it.
    /// </summary>
    private sealed class Entry(IFontHandle handle)
    {
        public IFontHandle Handle { get; } = handle;

        public long LastUsedTicks { get; set; } = Stopwatch.GetTimestamp();
    }

    private static readonly Dictionary<float, Entry> Handles = new();
    private static readonly object SyncRoot = new();

    private static IFontAtlas? atlas;
    private static bool warnedFull;

    /// <summary>
    /// Moves whenever the set of built fonts changes, so anything that remembered a measurement can tell that the font
    /// it measured with is no longer the font that would draw.
    /// </summary>
    /// <remarks>
    /// The case this exists for is the handover: a size that is still building is drawn with the stretched stand-in and
    /// measures as the stand-in, and the frame the real font arrives it measures differently. Without a generation, a
    /// measurement cache would keep answering with the stand-in's numbers for as long as the label went unchanged.
    /// </remarks>
    internal static int Generation => GenerationOverride?.Invoke() ?? generation;

    /// <summary>
    /// Stands in for the real generation, so the handover can be driven without building a font atlas.
    /// </summary>
    /// <remarks>
    /// Building a font needs a Dalamud atlas, which a test does not have, so the frame the real font arrives cannot be
    /// reached by asking for a size and waiting. What a test needs to assert is that a measurement taken under one
    /// generation is unreachable under the next, and that is exactly what moving this proves.<br/>
    /// Null outside a test, where the real counter answers. Matches <see cref="NoireUI.FrameOverride"/> and
    /// <see cref="NoireUI.ScaleOverride"/>, which exist for the same reason.
    /// </remarks>
    internal static Func<int>? GenerationOverride { get; set; }

    private static int generation;

    /// <summary>
    /// Whether a size has been registered that the last build did not cover.
    /// </summary>
    private static bool dirty;

    /// <summary>
    /// The type scale that was last asked for, and when it last changed. See <see cref="ScaleSettledLocked"/>.
    /// </summary>
    private static int pendingScaleKey;

    private static long pendingSinceTicks;

    /// <summary>
    /// The <see cref="NoireUI.Scale"/> the current fonts were rasterized for, or <see cref="float.NaN"/> before the
    /// first build.
    /// </summary>
    /// <remarks>
    /// Claimed when a build starts rather than when it finishes, so the draws happening during an asynchronous one do
    /// not each queue another build of the same thing.
    /// </remarks>
    private static float builtScale = float.NaN;

    /// <summary>
    /// The font to draw a given logical size with, or <see langword="null"/> to use the font already current.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for the host's own default size rather than building a copy of a font that
    /// already exists, so an interface that only ever draws body text costs no atlas space at all.<br/>
    /// A miss registers the whole of the current theme's type scale and asks for one build, rather than registering
    /// only the size asked for. A build re-rasterizes <em>every</em> font in the atlas, so a size added later
    /// re-rasterizes everything beside it: taking the scale in one pass is the cheaper order.<br/>
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

        // Broken out from NoireText so a first draw that has to register a size, prune the cold ones and ask for a
        // rasterization is a row of its own rather than time charged to whatever text happened to be first.
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

            // Nothing is built for this size yet. If the scale is still moving, do not build one: a size being dragged
            // is a different size next frame, and rasterizing each step of the drag would spend a build on every one of
            // them and fill the cache with sizes nobody keeps. Drawing falls back to the stretched stand-in, which
            // tracks the size exactly and is what a live slider wants to show anyway.
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

    /// <summary>
    /// Builds every size the current theme's type scale resolves to.
    /// </summary>
    /// <remarks>
    /// Run on every miss rather than once at startup, because the scale moves: a plugin that sets
    /// <see cref="NoireTheme.BodySize"/> from its settings shifts all four steps at once, and rebuilding them one at a
    /// time as each is next drawn would put the delay back.
    /// </remarks>
    /// <param name="waitForCompletion">
    /// Whether to block until the sizes are rasterized, rather than letting them arrive over the following frames.
    /// </param>
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

    /// <summary>
    /// Whether the user's UI scale has moved since the fonts were rasterized, which makes every built size the wrong
    /// one. Callers hold <see cref="SyncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Checked here, on the way to drawing, rather than handled from <see cref="IFontAtlas.RebuildRecommend"/>. That
    /// event also fires for every handle registered, including the ones this cache registered a line earlier, so
    /// acting on it rebuilds the atlas once per size and then once more on the first frame. Text is only wrong if it
    /// is about to be drawn, and this runs exactly then.
    /// </remarks>
    private static bool ScaleMovedLocked()
        => !float.IsNaN(builtScale) && MathF.Abs(NoireUI.Scale - builtScale) > 0.001f;

    /// <summary>
    /// Whether the type scale has held still long enough to be worth rasterizing. Callers hold <see cref="SyncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Keyed on the whole scale rather than on one size, because every step moves together: a body size dragged from 8
    /// to 28 is one gesture, not sixty type scales, and treating it as sixty is sixty builds and sixty cache entries.
    /// While it is moving this reads false and nothing is built; when it stops, one build covers where it landed.
    /// </remarks>
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

    /// <summary>
    /// Treats the current scale as settled, for a caller that asked for a build directly. Callers hold
    /// <see cref="SyncRoot"/>.
    /// </summary>
    private static void MarkScaleSettledLocked()
    {
        pendingScaleKey = CurrentScaleKey();
        pendingSinceTicks = 0;
    }

    /// <summary>
    /// A value that changes whenever any step of the current theme's type scale changes.
    /// </summary>
    private static int CurrentScaleKey()
    {
        var theme = NoireTheme.Current;
        var key = new HashCode();

        foreach (var step in Enum.GetValues<TextSize>())
            key.Add(Normalize(theme.ResolveTextSize(step)));

        return key.ToHashCode();
    }

    /// <summary>
    /// Drops sizes the current scale no longer contains and nothing has drawn with for a while. Callers hold
    /// <see cref="SyncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Without this, a plugin offering a live font-size setting fills the cache simply by being used: every value the
    /// user settles on leaves its whole scale behind. Only sizes that are both out of the current scale and cold are
    /// dropped, so nothing being drawn with is ever disposed.
    /// </remarks>
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

    /// <summary>
    /// Rasterizes everything registered since the last build.
    /// </summary>
    /// <remarks>
    /// The build is asked for explicitly rather than left to the atlas, and that is the whole reason this cache drives
    /// its own rebuilds. An atlas set to rebuild itself does so from <see cref="IFontAtlas.RebuildRecommend"/>, which
    /// Dalamud raises <em>on the main thread</em>: registered from a plugin constructor, before any frame has run, that
    /// event has not fired, so nothing has been scheduled and there is no build to wait for. Waiting there returns
    /// immediately and the rasterization silently starts later, on the first frame, which is exactly the delay a
    /// blocking prewarm exists to avoid.
    /// </remarks>
    /// <param name="blocking">Whether to rasterize on this thread and return only once it is done.</param>
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

    /// <summary>
    /// Reports what a build actually cost, so a slow one is a number rather than a guess.
    /// </summary>
    private static void Report(int sizes, long started)
    {
        // Bumped here rather than when the build was asked for, because what invalidates a remembered measurement is a
        // font becoming available, which is this moment and not the one the build was queued at.
        Interlocked.Increment(ref generation);

        NoireLogger.LogInformation(
            $"Built {sizes} text size(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms. "
            + "Sizes are rasterized as real glyphs, so this scales with how many glyph ranges the Dalamud language settings ask for. "
            + $"Call {nameof(NoireText)}.{nameof(NoireText.Prewarm)}(wait: true) from your plugin's constructor to spend it at load instead.",
            nameof(NoireText));
    }

    /// <summary>
    /// Registers every step of the current theme's scale that is not registered yet, without building anything.
    /// Callers hold <see cref="SyncRoot"/>.
    /// </summary>
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

    /// <summary>
    /// Registers one size, or reports why it could not. Callers hold <see cref="SyncRoot"/>.
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

    /// <summary>
    /// Rasterizes one size, as few glyphs as it can get away with.
    /// </summary>
    /// <remarks>
    /// The obvious call here is <c>AddDalamudDefaultFont</c>, and it is what shipped first. It is also why building a
    /// type scale took seconds: it rasterizes the font's entire default range, then merges FontAwesome's ~1400 icons,
    /// then the extra glyphs for the user's language, and it does that for every size. Roughly four thousand glyphs per
    /// size, to draw a heading that says "Settings".<br/>
    /// Instead the user's own font specification is re-sized and given a glyph range, which is the one thing
    /// <c>AddDalamudDefaultFont</c> takes but then ignores for everything it merges. The extra glyphs for the user's
    /// language are attached on top, so this stays correct for someone reading Japanese rather than only being fast for
    /// someone reading English. What is dropped is the icon font and the parts of Unicode nobody is about to render in
    /// a heading, and <see cref="NoireText.FontBuilder"/> puts either back.
    /// </remarks>
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
    /// the size by the user's scale when it rasterizes. A plain atlas would mean scaling the size here instead.<br/>
    /// Auto-rebuilding is off, and <see cref="Rebuild"/> says why. The cost of driving it here is that a change in the
    /// user's scale has to be noticed rather than handled for us, which is what <see cref="ScaleMovedLocked"/> does on
    /// the way to drawing.
    /// </remarks>
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
