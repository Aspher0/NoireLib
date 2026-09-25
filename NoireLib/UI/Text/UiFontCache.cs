using Dalamud.Interface;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

internal static class UiFontCache
{
    // Raise it before the first text is drawn.
    public static int MaxSizes { get; set; } = 16;

    private const int SizePrecision = 0;

    private const string DisposeCallbackKey = "NoireLib.UI.UiFontCache";

    private static readonly TimeSpan ColdSizeLifetime = TimeSpan.FromSeconds(20);

    // Pairs of first and last codepoint, terminated by zero.
    internal static readonly ushort[] DefaultGlyphRanges =
    [
        0x0020, 0x00FF,
        0x0100, 0x017F,
        0x2000, 0x206F,
        0x20A0, 0x20CF,
        0x2190, 0x21FF,
        0x2600, 0x26FF,
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

    internal static int Generation => GenerationOverride?.Invoke() ?? generation;

    internal static Func<int>? GenerationOverride { get; set; }

    private static int generation;

    private static bool dirty;

    private static int pendingScaleKey;

    private static long pendingSinceTicks;

    // NaN before the first build. Claimed when a build starts.
    private static float builtScale = float.NaN;

    private static int builtScripts;

    // An unbuilt handle pushes as a no-op. Check IFontHandle.Available first.
    internal static IFontHandle? Get(float logicalSizePx)
    {
        if (!NoireService.IsInitialized())
            return null;

        var size = Normalize(logicalSizePx);
        var scripts = NoireScriptFonts.Generation;

        // The host font lacks a merged script's glyphs.
        if (IsHostDefault(size) && NoireScriptFonts.Ranges == null)
            return null;

        using var draw = UiDraw.Begin();

        IFontHandle? handle;
        bool needsBuild;

        lock (SyncRoot)
        {
            if (scripts != builtScripts)
            {
                builtScripts = scripts;
                dirty |= Handles.Count > 0;
            }

            var stale = ScaleMovedLocked();

            if (!stale && !dirty && Handles.TryGetValue(size, out var existing))
            {
                existing.LastUsedTicks = Stopwatch.GetTimestamp();
                return existing.Handle;
            }

            // A size being dragged is a different size next frame. The stretched stand-in draws until the scale settles.
            if (!ScaleSettledLocked())
                return null;

            PruneLocked();
            RegisterScaleLocked();

            handle = Handles.TryGetValue(size, out var built) ? built.Handle : CreateLocked(size);
            needsBuild = dirty || stale;
        }

        // Never blocking. This runs mid-frame.
        if (needsBuild)
            Rebuild(blocking: false);

        return handle;
    }

    internal static void BuildScale(bool waitForCompletion = false)
    {
        if (!NoireService.IsInitialized())
            return;

        bool needsBuild;

        lock (SyncRoot)
        {
            MarkScaleSettledLocked();
            PruneLocked();
            RegisterScaleLocked();
            needsBuild = dirty || ScaleMovedLocked();
        }

        if (needsBuild)
            Rebuild(waitForCompletion);
    }

    private static bool ScaleMovedLocked()
        => !float.IsNaN(builtScale) && MathF.Abs(NoireUI.Scale - builtScale) > 0.001f;

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

    private static void MarkScaleSettledLocked()
    {
        pendingScaleKey = CurrentScaleKey();
        pendingSinceTicks = 0;
    }

    private static int CurrentScaleKey()
    {
        var theme = NoireTheme.Current;
        var key = new HashCode();

        foreach (var step in Enum.GetValues<TextSize>())
            key.Add(Normalize(theme.ResolveTextSize(step)));

        return key.ToHashCode();
    }

    // Only sizes both out of the scale and cold are dropped. Nothing being drawn with is disposed.
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

            // Claimed up front. An asynchronous build takes seconds and every draw meanwhile would ask again.
            dirty = false;
            builtScale = NoireUI.Scale;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            if (blocking)
            {
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
        // Bumped when the font becomes available.
        Interlocked.Increment(ref generation);

        NoireLogger.LogInformation(
            $"Built {sizes} text size(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms. "
            + "The time scales with the glyph ranges the Dalamud language settings ask for. "
            + $"Call {nameof(NoireText)}.{nameof(NoireText.Prewarm)}(wait: true) from your plugin's constructor to spend it at load instead.",
            nameof(NoireText));
    }

    private static void RegisterScaleLocked()
    {
        var theme = NoireTheme.Current;
        var scripts = NoireScriptFonts.Ranges != null;

        foreach (var step in Enum.GetValues<TextSize>())
        {
            var size = Normalize(theme.ResolveTextSize(step));

            if ((IsHostDefault(size) && !scripts) || Handles.ContainsKey(size))
                continue;

            CreateLocked(size);
        }
    }

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

    private static void BuildFont(IFontAtlasBuildToolkitPreBuild toolkit, float size)
    {
        if (NoireText.FontBuilder is { } custom)
        {
            custom(toolkit, size);
            return;
        }

        if (NoireService.PluginInterface.UiBuilder.DefaultFontSpec is not SingleFontSpec spec)
        {
            NoireScriptFonts.Merge(toolkit, toolkit.AddDalamudDefaultFont(size), size);
            return;
        }

        var resized = spec with { SizePx = size, GlyphRanges = NoireText.GlyphRanges ?? DefaultGlyphRanges };
        var font = resized.AddToBuildToolkit(toolkit, default);

        var extra = new SafeFontConfig { SizePx = size, MergeFont = font };
        toolkit.AttachExtraGlyphsForDalamudLanguage(ref extra);
        NoireScriptFonts.Merge(toolkit, font, size);
    }

    private static float Normalize(float logicalSizePx)
        => MathF.Round(MathF.Max(1f, logicalSizePx), SizePrecision);

    private static bool IsHostDefault(float normalizedSize)
        => Math.Abs(normalizedSize - Normalize(NoireTheme.DefaultBodySize)) < float.Epsilon;

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
            $"NoireText reached its limit of {MaxSizes} font sizes. {size:0.#} px is drawn at the nearest built size. "
            + $"Each size is a full glyph atlas. "
            + $"Ask for text by {nameof(TextSize)}, or set the sizes you need on {nameof(NoireTheme)}.",
            nameof(NoireText));
    }
}
