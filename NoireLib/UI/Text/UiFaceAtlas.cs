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
    public UiFaceEntry(NoireFont face, float emPx)
    {
        Face = face;
        EmPx = emPx;
    }

    public NoireFont Face { get; }

    public float EmPx { get; }

    public IFontHandle? Handle { get; set; }

    public ILockedImFont? Locked { get; set; }

    public volatile bool Stale;

    public long LastUsedTicks = Stopwatch.GetTimestamp();

    public bool Removed { get; set; }
}

// Sizes asked for during one frame are rasterized together. A frame never waits on one.
internal static class UiFaceAtlas
{
    private const string DisposeCallbackKey = "NoireLib.UI.UiFaceAtlas";

    private static readonly TimeSpan ColdLifetime = TimeSpan.FromSeconds(30);

    internal static int MaxEntries { get; set; } = 256;

    private static readonly object SyncRoot = new();
    private static readonly List<UiFaceEntry> Entries = new();

    // Released one frame later, once the frame that last drew with them has rendered.
    private static readonly List<(ILockedImFont Locked, int Frame)> Retired = new();

    private static IFontAtlas? atlas;
    private static bool dirty;
    private static bool building;
    private static int dirtyFrame = int.MinValue;
    private static int tickedFrame = int.MinValue;
    private static bool warnedFull;
    private static int buildScheduled;
    private static int generation;

    internal static int Generation => Volatile.Read(ref generation);

    internal static int EntryCount
    {
        get
        {
            lock (SyncRoot)
                return Entries.Count;
        }
    }

    internal static UiFaceEntry? Register(NoireFont face, float emPx, bool buildNow)
    {
        if (!NoireService.IsInitialized())
            return null;

        UiFaceEntry entry;

        lock (SyncRoot)
        {
            if (Entries.Count >= MaxEntries)
            {
                WarnFull();
                return null;
            }

            entry = new UiFaceEntry(face, emPx);

            try
            {
                entry.Handle = CreateHandle(EnsureAtlas(), entry);
            }
            catch (Exception ex)
            {
                NoireUI.Diagnostics.ReportFault(nameof(NoireFont), $"Failed to register {face.Name} at {emPx:0.##} px.", ex);
                return null;
            }

            Entries.Add(entry);
            dirty = true;
            dirtyFrame = NoireUI.FrameCount;
        }

        if (buildNow)
            ScheduleBuild();

        return entry;
    }

    // A lambda capturing the entry would allocate on every Register call.
    private static IFontHandle CreateHandle(IFontAtlas target, UiFaceEntry entry)
    {
        var handle = target.NewDelegateFontHandle(e => e.OnPreBuild(toolkit => BuildEntry(toolkit, entry)));
        handle.ImFontChanged += (_, _) => entry.Stale = true;
        return handle;
    }

    internal static ImFontPtr Resolve(UiFaceEntry entry)
    {
        Tick();

        entry.LastUsedTicks = Stopwatch.GetTimestamp();

        if (entry.Locked is { } locked && !entry.Stale)
            return locked.ImFont;

        return Relock(entry);
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

    private static void Tick()
    {
        var frame = NoireUI.FrameCount;

        if (frame == tickedFrame)
            return;

        tickedFrame = frame;

        var kick = false;

        lock (SyncRoot)
        {
            for (var index = Retired.Count - 1; index >= 0; index--)
            {
                if (Retired[index].Frame >= frame)
                    continue;

                Release(Retired[index].Locked);
                Retired.RemoveAt(index);
            }

            // A size nobody draws must not keep an old atlas alive.
            for (var index = 0; index < Entries.Count; index++)
            {
                var entry = Entries[index];

                if (entry.Stale && entry.Locked != null)
                    Relock(entry);
            }

            kick = dirty && !building && frame != dirtyFrame;
        }

        if (kick)
            KickBuild();
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
                KickBuild();
            });
        }
        catch (Exception ex)
        {
            Volatile.Write(ref buildScheduled, 0);
            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), "Failed to schedule a font build.", ex);
        }
    }

    internal static void KickBuild()
    {
        IFontAtlas? target;

        lock (SyncRoot)
        {
            if (building || !dirty || atlas == null)
                return;

            PruneLocked();

            dirty = false;
            building = true;
            target = atlas;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            target.BuildFontsAsync().ContinueWith(task => Built(task, started), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
                building = false;

            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), "Failed to start building the fonts.", ex);
        }
    }

    private static void Built(Task task, long started)
    {
        lock (SyncRoot)
            building = false;

        if (task.Exception is { } failure)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireFont), "Failed to build the fonts.", failure.GetBaseException());
            return;
        }

        Interlocked.Increment(ref generation);
        NoireLogger.LogDebug($"Built {EntryCount} font size(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms.", nameof(NoireFont));
    }

    private static void BuildEntry(IFontAtlasBuildToolkitPreBuild toolkit, UiFaceEntry entry)
    {
        var face = entry.Face;
        var linePx = entry.EmPx * face.LineRatio;

        var config = new SafeFontConfig
        {
            SizePx = linePx,
            GlyphRanges = face.GlyphRanges ?? UiFontCache.DefaultGlyphRanges,
        };

        if (face.Oversample is { } oversample)
        {
            config.OversampleH = oversample;
            config.OversampleV = 1;
        }

        var font = toolkit.AddFontFromMemory(face.Data, config, face.Name);

        if (face.MergeLanguageGlyphs)
        {
            var extra = new SafeFontConfig { SizePx = linePx, MergeFont = font };
            toolkit.AttachExtraGlyphsForDalamudLanguage(ref extra);
        }

        face.OnBuild?.Invoke(toolkit, font, linePx);
    }

    private static void PruneLocked()
    {
        var now = Stopwatch.GetTimestamp();

        for (var index = Entries.Count - 1; index >= 0; index--)
        {
            var entry = Entries[index];

            if (Stopwatch.GetElapsedTime(entry.LastUsedTicks, now) < ColdLifetime)
                continue;

            RemoveLocked(entry);
            Entries.RemoveAt(index);
        }
    }

    internal static void Remove(UiFaceEntry entry)
    {
        lock (SyncRoot)
        {
            if (entry.Removed)
                return;

            RemoveLocked(entry);
            Entries.Remove(entry);
        }
    }

    private static void RemoveLocked(UiFaceEntry entry)
    {
        entry.Removed = true;
        entry.Face.Forget(entry);

        if (entry.Locked is { } locked)
            Retired.Add((locked, NoireUI.FrameCount));

        entry.Locked = null;

        try
        {
            entry.Handle?.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to dispose the {entry.EmPx:0.##} px handle of {entry.Face.Name}.", nameof(NoireFont));
        }

        entry.Handle = null;
    }

    private static IFontAtlas EnsureAtlas()
    {
        if (atlas != null)
            return atlas;

        // Not global scaled. A scale change is new sizes.
        atlas = NoireService.PluginInterface.UiBuilder.CreateFontAtlas(
            FontAtlasAutoRebuildMode.Disable,
            isGlobalScaled: false,
            debugName: "NoireFont");

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);

        return atlas;
    }

    private static void Release(ILockedImFont locked)
    {
        try
        {
            locked.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to release a locked font.", nameof(NoireFont));
        }
    }

    private static void WarnFull()
    {
        if (warnedFull)
            return;

        warnedFull = true;

        NoireLogger.LogWarning(
            $"{MaxEntries} NoireFont sizes are built and no more will be; further sizes draw with the current font. Raise {nameof(NoireFont)}.{nameof(NoireFont.MaxBuiltSizes)} if this is expected.",
            nameof(NoireFont));
    }

    internal static void Cleanup()
    {
        lock (SyncRoot)
        {
            foreach (var entry in Entries)
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
                    NoireLogger.LogError(ex, "Failed to dispose a NoireFont handle.", nameof(NoireFont));
                }
            }

            Entries.Clear();

            foreach (var (locked, _) in Retired)
                Release(locked);

            Retired.Clear();

            try
            {
                atlas?.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "Failed to dispose the NoireFont atlas.", nameof(NoireFont));
            }

            atlas = null;
            dirty = false;
            building = false;
            warnedFull = false;
            dirtyFrame = int.MinValue;
            tickedFrame = int.MinValue;
            Interlocked.Increment(ref generation);
        }
    }
}
