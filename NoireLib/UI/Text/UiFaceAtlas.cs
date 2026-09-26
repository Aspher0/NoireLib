using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

// Draw thread only, except Stale.
internal sealed class UiFaceEntry
{
    public UiFaceEntry(NoireFont face, float emPx, UiFacePage page)
    {
        Face = face;
        EmPx = emPx;
        Page = page;
    }

    public NoireFont Face { get; }

    public float EmPx { get; }

    public UiFacePage Page { get; }

    public IFontHandle? Handle { get; set; }

    public ILockedImFont? Locked { get; set; }

    public volatile bool Stale;

    public long LastUsedTicks = Stopwatch.GetTimestamp();

    public bool Removed { get; set; }
}

internal sealed class UiFacePage
{
    public UiFacePage(string name, bool loose)
    {
        Name = name;
        Loose = loose;
    }

    public string Name { get; }

    public bool Loose { get; }

    public List<UiFaceEntry> Entries { get; } = new();

    public IFontAtlas? Atlas { get; set; }

    public bool Dirty { get; set; }

    public bool Building { get; set; }

    public int DirtyFrame { get; set; } = int.MinValue;

    public int BuiltScripts { get; set; } = -1;

    public int BuildingScripts { get; set; }

    public long LastUsedTicks { get; set; } = Stopwatch.GetTimestamp();

    public bool Removed { get; set; }

    public UiFaceBuild? Build { get; set; }

    public (string Key, UiFaceCacheData Data)? HandOff { get; set; }

    public bool SkipCacheNext { get; set; }
}

internal static class UiFaceAtlas
{
    private const string DisposeCallbackKey = "NoireLib.UI.UiFaceAtlas";

    internal const int TextureWidth = 2048;

    internal const int RestoredTextureWidth = 1024;

    private static readonly ushort[] SpaceOnly = [0x20, 0x20, 0];

    private static readonly TimeSpan ColdLifetime = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan HotLifetime = TimeSpan.FromSeconds(1);

    internal static int MaxEntries { get; set; } = 1024;

    internal static float? Gamma { get; set; }

    internal static int MaxConcurrentBuilds { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 2, 4);

    private static readonly object SyncRoot = new();
    private static readonly List<UiFacePage> Pages = new();
    private static readonly Dictionary<NoireFont, UiFacePage> LoosePages = new();

    // Released one frame later, once the frame that last drew with them has rendered.
    private static readonly List<(ILockedImFont Locked, int Frame)> Retired = new();
    private static readonly List<(IFontAtlas Atlas, int Frame)> RetiredAtlases = new();

    private static int entryCount;
    private static int buildingCount;
    private static int tickedFrame = int.MinValue;
    private static bool warnedFull;
    private static int buildScheduled;
    private static int generation;
    private static bool disposeRegistered;

    internal static int Generation => Volatile.Read(ref generation);

    internal static bool Busy
    {
        get
        {
            lock (SyncRoot)
            {
                foreach (var page in Pages)
                {
                    if (page.Building || (page.Dirty && page.Entries.Count > 0))
                        return true;
                }
            }

            return false;
        }
    }

    internal static int EntryCount
    {
        get
        {
            lock (SyncRoot)
                return entryCount;
        }
    }

    internal static UiFacePage LoosePage(NoireFont face)
    {
        lock (SyncRoot)
        {
            if (LoosePages.TryGetValue(face, out var page))
                return page;

            page = new UiFacePage(face.Name, loose: true);
            LoosePages[face] = page;
            Pages.Add(page);
            return page;
        }
    }

    internal static UiFacePage NewPage(string name)
    {
        var page = new UiFacePage(name, loose: false);

        lock (SyncRoot)
            Pages.Add(page);

        return page;
    }

    internal static UiFaceEntry? Register(NoireFont face, float emPx, UiFacePage page, bool buildNow)
    {
        if (!NoireService.IsInitialized())
            return null;

        UiFaceEntry entry;

        lock (SyncRoot)
        {
            if (page.Removed)
                return null;

            if (entryCount >= MaxEntries)
            {
                WarnFull();
                return null;
            }

            entry = new UiFaceEntry(face, emPx, page);

            try
            {
                entry.Handle = CreateHandle(EnsureAtlas(page), entry);
            }
            catch (Exception ex)
            {
                NoireUI.Diagnostics.ReportFault(nameof(NoireFont), $"Failed to register {face.Name} at {emPx:0.##} px.", ex);
                return null;
            }

            page.Entries.Add(entry);
            page.Dirty = true;
            page.DirtyFrame = NoireUI.FrameCount;
            entryCount++;
        }

        if (buildNow)
            ScheduleBuild();

        return entry;
    }

    // A lambda capturing the entry would allocate on every Register call.
    private static IFontHandle CreateHandle(IFontAtlas target, UiFaceEntry entry)
    {
        var handle = target.NewDelegateFontHandle(e => e
            .OnPreBuild(toolkit => BuildEntry(toolkit, entry))
            .OnPostBuild(toolkit => entry.Page.Build?.AfterBuild(toolkit, entry)));
        handle.ImFontChanged += (_, _) => entry.Stale = true;
        return handle;
    }

    internal static ImFontPtr Resolve(UiFaceEntry entry)
    {
        Tick();

        var now = Stopwatch.GetTimestamp();
        entry.LastUsedTicks = now;
        entry.Page.LastUsedTicks = now;

        if (entry.Locked is { } locked && !entry.Stale)
            return locked.ImFont;

        return Relock(entry);
    }

    internal static bool IsBuilt(UiFaceEntry entry)
        => !entry.Removed && (entry.Locked != null || entry.Handle is { Available: true });

    internal static bool IsCurrent(UiFacePage page, int scripts)
        => !page.Removed && !page.Dirty && !page.Building && page.BuiltScripts == scripts;

    internal static bool IsReady(IReadOnlyList<UiFacePage> pages, IReadOnlyList<UiFaceEntry> entries)
    {
        var scripts = NoireScriptFonts.Generation;
        var ready = true;
        var stale = false;

        lock (SyncRoot)
        {
            foreach (var page in pages)
            {
                if (page.Entries.Count == 0 || IsCurrent(page, scripts))
                    continue;

                ready = false;
                stale |= MarkStaleLocked(page, scripts);
            }
        }

        if (stale)
            ScheduleBuild();

        if (!ready)
            return false;

        lock (SyncRoot)
        {
            foreach (var entry in entries)
            {
                if (!IsBuilt(entry))
                    return false;
            }
        }

        return true;
    }

    internal static bool AllBuilt(IReadOnlyList<UiFaceEntry> entries)
    {
        lock (SyncRoot)
        {
            foreach (var entry in entries)
            {
                if (!IsBuilt(entry))
                    return false;
            }
        }

        return entries.Count > 0;
    }

    internal static bool HotPagesCurrent()
    {
        var scripts = NoireScriptFonts.Generation;
        var now = Stopwatch.GetTimestamp();
        var current = true;
        var stale = false;

        lock (SyncRoot)
        {
            foreach (var page in Pages)
            {
                if (page.Entries.Count == 0 || Stopwatch.GetElapsedTime(page.LastUsedTicks, now) > HotLifetime)
                    continue;

                if (IsCurrent(page, scripts))
                    continue;

                current = false;
                stale |= MarkStaleLocked(page, scripts);
            }
        }

        if (stale)
            ScheduleBuild();

        return current;
    }

    internal static void RebuildAll()
    {
        var any = false;

        lock (SyncRoot)
        {
            foreach (var page in Pages)
            {
                if (page.Entries.Count == 0 || page.Removed)
                    continue;

                page.Dirty = true;
                page.DirtyFrame = int.MinValue;
                any = true;
            }
        }

        if (any)
            ScheduleBuild();
    }

    internal static void MarkAllStale()
    {
        var scripts = NoireScriptFonts.Generation;
        var stale = false;

        lock (SyncRoot)
        {
            foreach (var page in Pages)
            {
                if (page.Entries.Count > 0)
                    stale |= MarkStaleLocked(page, scripts);
            }
        }

        if (stale)
            ScheduleBuild();
    }

    private static bool MarkStaleLocked(UiFacePage page, int scripts)
    {
        if (page.Removed || page.Building || page.Dirty || page.BuiltScripts < 0 || page.BuiltScripts == scripts)
            return false;

        page.Dirty = true;
        page.DirtyFrame = int.MinValue;
        return true;
    }

    private static ImFontPtr Relock(UiFaceEntry entry)
    {
        if (entry.Removed || entry.Handle is not { Available: true } handle)
            return entry.Locked?.ImFont ?? default;

        entry.Stale = false;

        var fresh = handle.TryLock(out _);

        if (fresh == null)
            return entry.Locked?.ImFont ?? default;

        if (entry.Locked is { } previous)
        {
            lock (SyncRoot)
                Retired.Add((previous, NoireUI.FrameCount));
        }

        entry.Locked = fresh;
        Interlocked.Increment(ref generation);

        return fresh.ImFont;
    }

    internal static void Maintain()
    {
        if (tickedFrame == int.MinValue || NoireUI.FrameCount - tickedFrame > 1)
            Tick();
    }

    private static void Tick()
    {
        var frame = NoireUI.FrameCount;

        if (frame == tickedFrame)
            return;

        tickedFrame = frame;

        var scripts = NoireScriptFonts.Generation;

        lock (SyncRoot)
        {
            foreach (var page in Pages)
            {
                if (page.Entries.Count > 0)
                    MarkStaleLocked(page, scripts);
            }

            for (var index = Retired.Count - 1; index >= 0; index--)
            {
                if (Retired[index].Frame >= frame)
                    continue;

                Release(Retired[index].Locked);
                Retired.RemoveAt(index);
            }

            for (var index = RetiredAtlases.Count - 1; index >= 0; index--)
            {
                if (RetiredAtlases[index].Frame >= frame)
                    continue;

                DisposeAtlas(RetiredAtlases[index].Atlas);
                RetiredAtlases.RemoveAt(index);
            }

            // A size nobody draws must not keep an old atlas alive.
            foreach (var page in Pages)
            {
                foreach (var entry in page.Entries)
                {
                    if (entry.Stale && entry.Locked != null)
                        Relock(entry);
                }
            }
        }

        Pump(frame);
    }

    internal static void ScheduleBuild()
    {
        if (!NoireService.IsInitialized() || Interlocked.Exchange(ref buildScheduled, 1) == 1)
            return;

        try
        {
            NoireService.Framework.RunOnTick(static () =>
            {
                Volatile.Write(ref buildScheduled, 0);
                Pump(null);
            });
        }
        catch (Exception ex)
        {
            Volatile.Write(ref buildScheduled, 0);
            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), "Failed to schedule a font build.", ex);
        }
    }

    private static void Pump(int? frame)
    {
        List<(UiFacePage Page, IFontAtlas Atlas)>? kicks = null;
        var now = Stopwatch.GetTimestamp();

        lock (SyncRoot)
        {
            while (buildingCount < MaxConcurrentBuilds)
            {
                UiFacePage? next = null;
                var nextHot = false;

                foreach (var page in Pages)
                {
                    if (!page.Dirty || page.Building || page.Removed || page.Atlas == null || page.Entries.Count == 0)
                        continue;

                    if (frame is { } current && page.DirtyFrame == current)
                        continue;

                    var hot = Stopwatch.GetElapsedTime(page.LastUsedTicks, now) <= HotLifetime;

                    if (next == null || (hot && !nextHot))
                    {
                        next = page;
                        nextHot = hot;
                    }
                }

                if (next == null)
                    break;

                PruneLocked(next);

                next.Dirty = false;

                if (next.Entries.Count == 0)
                    continue;

                next.Build = UiFaceCache.Prepare(next, next.SkipCacheNext);
                next.SkipCacheNext = false;
                next.Building = true;
                next.BuildingScripts = next.Build.Scripts;
                buildingCount++;

                (kicks ??= new()).Add((next, next.Atlas!));
            }
        }

        if (kicks == null)
            return;

        foreach (var (page, atlas) in kicks)
            Kick(page, atlas);
    }

    private static void Kick(UiFacePage page, IFontAtlas target)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            target.BuildFontsAsync().ContinueWith(task => Built(page, task, started), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                page.Building = false;
                buildingCount--;
            }

            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), $"Failed to start building the {page.Name} fonts.", ex);
        }
    }

    private static void Built(UiFacePage page, Task task, long started)
    {
        int sizes;
        var build = page.Build;

        lock (SyncRoot)
        {
            page.Building = false;
            page.Build = null;
            buildingCount--;
            sizes = page.Entries.Count;

            if (task.Exception == null)
                page.BuiltScripts = page.BuildingScripts;

            if (build is { Failed: true } && !page.Removed)
            {
                page.Dirty = true;
                page.DirtyFrame = int.MinValue;
                page.SkipCacheNext = true;
            }
            else if (task.Exception == null && !page.Removed && UiFaceCache.FullAlpha
                && build is { Restore: null, Key: { } handedKey } && build.Captured is { } handed)
            {
                page.HandOff = (handedKey, handed);
                page.Dirty = true;
                page.DirtyFrame = int.MinValue;
            }

            if (page.Removed)
                RetireAtlasLocked(page);
        }

        if (build is { Failed: true, Key: { } broken })
            UiFaceCache.Delete(broken);

        ScheduleBuild();

        if (task.Exception is { } failure)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), $"Failed to build the {page.Name} fonts.", failure.GetBaseException());
            return;
        }

        var source = "rasterized";

        if (build is { Failed: false })
        {
            if (build.Restore != null)
                source = "from cache";
            else if (build.Key is { } key && build.Captured is { } captured)
                UiFaceCache.Save(key, captured);
        }

        Interlocked.Increment(ref generation);
        NoireLogger.LogDebug($"Built {page.Name}: {sizes} font size(s) {source} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms.", "[NoireFont] ");
    }

    private static void BuildEntry(IFontAtlasBuildToolkitPreBuild toolkit, UiFaceEntry entry)
    {
        var face = entry.Face;
        var linePx = entry.EmPx * face.LineRatio;
        var build = entry.Page.Build;

        if (build != null && build.Knows(entry) && build.Restoring)
        {
            toolkit.NewImAtlas.TexDesiredWidth = RestoredTextureWidth;
            toolkit.AddFontFromMemory(face.Data, new SafeFontConfig { SizePx = linePx, GlyphRanges = SpaceOnly }, face.Name);
            return;
        }

        toolkit.NewImAtlas.TexDesiredWidth = TextureWidth;

        var config = new SafeFontConfig
        {
            SizePx = linePx,
            GlyphRanges = face.GlyphRanges ?? UiFontCache.DefaultGlyphRanges,
        };

        if (Gamma is { } gamma)
            config.RasterizerGamma = gamma;

        if (face.Oversample is { } oversample)
        {
            config.OversampleH = oversample;
            config.OversampleV = 1;
        }

        var font = toolkit.AddFontFromMemory(face.Data, config, face.Name);

        if (face.MergeLanguageGlyphs)
        {
            if (face.MergeDalamudLanguageGlyphs)
            {
                var extra = new SafeFontConfig { SizePx = linePx, MergeFont = font };

                if (Gamma is { } extraGamma)
                    extra.RasterizerGamma = extraGamma;

                toolkit.AttachExtraGlyphsForDalamudLanguage(ref extra);
            }

            NoireScriptFonts.Merge(toolkit, font, linePx, build != null ? build.Ranges : NoireScriptFonts.Ranges,
                build?.FontNumber ?? NoireScriptFonts.FontNumber, Gamma);
        }

        face.OnBuild?.Invoke(toolkit, font, linePx);
    }

    private static void PruneLocked(UiFacePage page)
    {
        if (!page.Loose)
            return;

        var now = Stopwatch.GetTimestamp();

        for (var index = page.Entries.Count - 1; index >= 0; index--)
        {
            var entry = page.Entries[index];

            if (Stopwatch.GetElapsedTime(entry.LastUsedTicks, now) < ColdLifetime || entry.Face.Keeps(entry.EmPx))
                continue;

            RemoveLocked(entry);
            page.Entries.RemoveAt(index);
        }
    }

    internal static void Remove(UiFaceEntry entry)
    {
        lock (SyncRoot)
        {
            if (entry.Removed)
                return;

            RemoveLocked(entry);
            entry.Page.Entries.Remove(entry);
        }
    }

    internal static void RemovePages(IReadOnlyList<UiFacePage> pages)
    {
        lock (SyncRoot)
        {
            foreach (var page in pages)
            {
                if (page.Removed)
                    continue;

                page.Removed = true;

                foreach (var entry in page.Entries)
                {
                    if (!entry.Removed)
                        RemoveLocked(entry);
                }

                page.Entries.Clear();
                Pages.Remove(page);

                if (!page.Building)
                    RetireAtlasLocked(page);
            }
        }
    }

    private static void RetireAtlasLocked(UiFacePage page)
    {
        if (page.Atlas is { } atlas)
            RetiredAtlases.Add((atlas, NoireUI.FrameCount));

        page.Atlas = null;
    }

    private static void RemoveLocked(UiFaceEntry entry)
    {
        entry.Removed = true;
        entry.Face.Forget(entry);
        entryCount--;

        if (entry.Locked is { } locked)
            Retired.Add((locked, NoireUI.FrameCount));

        entry.Locked = null;

        try
        {
            entry.Handle?.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to dispose the {entry.EmPx:0.##} px handle of {entry.Face.Name}.", "[NoireFont] ");
        }

        entry.Handle = null;
    }

    private static IFontAtlas EnsureAtlas(UiFacePage page)
    {
        if (page.Atlas != null)
            return page.Atlas;

        // Not global scaled. A scale change is new sizes.
        page.Atlas = NoireService.PluginInterface.UiBuilder.CreateFontAtlas(
            FontAtlasAutoRebuildMode.Disable,
            isGlobalScaled: false,
            debugName: "NoireFont " + page.Name);

        UiFontPump.Ensure();

        if (!disposeRegistered && !NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
        {
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);
            disposeRegistered = true;
        }

        return page.Atlas;
    }

    private static void Release(ILockedImFont locked)
    {
        try
        {
            locked.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to release a locked font.", "[NoireFont] ");
        }
    }

    private static void DisposeAtlas(IFontAtlas atlas)
    {
        try
        {
            atlas.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to dispose a NoireFont atlas.", "[NoireFont] ");
        }
    }

    private static void WarnFull()
    {
        if (warnedFull)
            return;

        warnedFull = true;

        NoireLogger.LogWarning(
            $"{MaxEntries} NoireFont sizes are built and no more will be; further sizes draw with the current font. Raise {nameof(NoireFont)}.{nameof(NoireFont.MaxBuiltSizes)} if this is expected.",
            "[NoireFont] ");
    }

    internal static void Cleanup()
    {
        lock (SyncRoot)
        {
            foreach (var page in Pages)
            {
                foreach (var entry in page.Entries)
                {
                    entry.Face.Forget(entry);
                    entry.Removed = true;

                    if (entry.Locked is { } locked)
                        Release(locked);

                    try
                    {
                        entry.Handle?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        NoireLogger.LogError(ex, "Failed to dispose a NoireFont handle.", "[NoireFont] ");
                    }
                }

                page.Entries.Clear();
                page.Removed = true;

                if (page.Atlas is { } atlas)
                    DisposeAtlas(atlas);

                page.Atlas = null;
            }

            Pages.Clear();
            LoosePages.Clear();

            foreach (var (locked, _) in Retired)
                Release(locked);

            Retired.Clear();

            foreach (var (atlas, _) in RetiredAtlases)
                DisposeAtlas(atlas);

            RetiredAtlases.Clear();

            UiFaceCache.Shutdown();

            entryCount = 0;
            buildingCount = 0;
            warnedFull = false;
            disposeRegistered = false;
            tickedFrame = int.MinValue;
            Interlocked.Increment(ref generation);
        }
    }
}
