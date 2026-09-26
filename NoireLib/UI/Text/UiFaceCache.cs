using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

internal sealed class UiFaceCacheEntry
{
    public ushort FallbackChar { get; init; }

    public ushort EllipsisChar { get; init; }

    public required ImGuiHelpers.ImFontGlyphReal[] Glyphs { get; init; }

    public required ImFontKerningPair[] Kerning { get; init; }
}

internal sealed class UiFaceCacheData
{
    public int Width { get; init; }

    public int Height { get; init; }

    public List<byte[]> Textures { get; } = new();

    public required UiFaceCacheEntry?[] Entries { get; init; }
}

internal sealed class UiFaceBuild
{
    private readonly object sync = new();
    private readonly Dictionary<UiFaceEntry, int> indices = new();
    private bool loadTried;
    private int[]? stored;
    private UiFaceCacheData? capture;
    private bool captureBroken;

    public UiFaceBuild(string? key, UiFaceEntry[] entries, ushort[]? ranges, int scripts)
    {
        Key = key;
        Entries = entries;
        Ranges = ranges;
        Scripts = scripts;

        for (var index = 0; index < entries.Length; index++)
            indices[entries[index]] = index;
    }

    public string? Key { get; }

    public UiFaceEntry[] Entries { get; }

    public ushort[]? Ranges { get; }

    public int Scripts { get; }

    public bool SkipCache { get; init; }

    public UiFaceCacheData? Preloaded { get; init; }

    public bool Knows(UiFaceEntry entry) => indices.ContainsKey(entry);

    public UiFaceCacheData? Restore { get; private set; }

    public bool Failed { get; private set; }

    public bool Restoring
    {
        get
        {
            lock (sync)
            {
                if (!loadTried)
                {
                    loadTried = true;

                    if (Preloaded != null && Preloaded.Entries.Length == Entries.Length)
                        Restore = Preloaded;
                    else if (Key != null && !SkipCache)
                        Restore = UiFaceCache.Load(Key, Entries.Length);
                }

                return Restore != null;
            }
        }
    }

    public UiFaceCacheData? Captured
    {
        get
        {
            lock (sync)
            {
                if (captureBroken || capture == null)
                    return null;

                foreach (var entry in capture.Entries)
                {
                    if (entry == null)
                        return null;
                }

                return capture;
            }
        }
    }

    public void AfterBuild(IFontAtlasBuildToolkitPostBuild toolkit, UiFaceEntry entry)
    {
        try
        {
            lock (sync)
            {
                if (!indices.TryGetValue(entry, out var index))
                {
                    captureBroken = true;
                    return;
                }

                if (Restore != null)
                    Inject(toolkit, toolkit.Font, index);
                else if (Key != null)
                    Capture(toolkit, toolkit.Font, index);
            }
        }
        catch (Exception ex)
        {
            Failed = true;
            captureBroken = true;
            NoireLogger.LogError(ex, $"Font cache failed on {entry.Face.Name} {entry.EmPx:0.##} px", nameof(NoireFont));
        }
    }

    private unsafe void Inject(IFontAtlasBuildToolkitPostBuild toolkit, ImFontPtr font, int index)
    {
        var data = Restore!;

        if (stored == null)
        {
            stored = new int[data.Textures.Count];

            for (var texture = 0; texture < data.Textures.Count; texture++)
                stored[texture] = toolkit.StoreTexture(UiFaceCache.CreateTexture(data, texture), disposeOnError: true);
        }

        var record = data.Entries[index]!;
        var glyphs = font.GlyphsWrapped();

        glyphs.Clear();

        foreach (var cached in record.Glyphs)
        {
            var glyph = cached;

            if ((uint)glyph.TextureIndex < (uint)stored.Length)
                glyph.TextureIndex = stored[glyph.TextureIndex];

            glyphs.Add(in glyph);
        }

        foreach (var pair in record.Kerning)
            font.AddKerningPair(pair.Left, pair.Right, pair.AdvanceXAdjustment);

        font.EllipsisChar = record.EllipsisChar;

        var native = font.Handle;
        native->FallbackGlyph = null;
        font.BuildLookupTable();

        var fallback = native->FindGlyphNoFallback(record.FallbackChar);

        if (fallback == null)
            return;

        native->FallbackChar = record.FallbackChar;
        native->FallbackGlyph = fallback;
        native->FallbackHotData = (ImFontGlyphHotData*)((byte*)native->IndexedHotData.Data + (record.FallbackChar * sizeof(ImGuiHelpers.ImFontGlyphHotDataReal)));
    }

    private unsafe void Capture(IFontAtlasBuildToolkitPostBuild toolkit, ImFontPtr font, int index)
    {
        if (captureBroken)
            return;

        if (capture == null)
        {
            var atlas = toolkit.NewImAtlas;
            var created = new UiFaceCacheData
            {
                Width = atlas.TexWidth,
                Height = atlas.TexHeight,
                Entries = new UiFaceCacheEntry?[Entries.Length],
            };

            var pixels = created.Width * created.Height;

            for (var texture = 0; texture < atlas.Textures.Size; texture++)
            {
                var source = atlas.Textures[texture];
                var alpha = new byte[pixels];

                if (source.TexPixelsAlpha8 != null)
                {
                    new ReadOnlySpan<byte>(source.TexPixelsAlpha8, pixels).CopyTo(alpha);
                }
                else if (source.TexPixelsRGBA32 != null)
                {
                    var rgba = new ReadOnlySpan<uint>(source.TexPixelsRGBA32, pixels);

                    for (var at = 0; at < pixels; at++)
                        alpha[at] = (byte)(rgba[at] >> 24);
                }
                else
                {
                    captureBroken = true;
                    return;
                }

                created.Textures.Add(alpha);
            }

            capture = created;
        }

        var glyphs = font.GlyphsWrapped();
        var copied = new ImGuiHelpers.ImFontGlyphReal[glyphs.Length];

        for (var at = 0; at < copied.Length; at++)
        {
            copied[at] = glyphs[at];

            if (copied[at].Colored)
            {
                captureBroken = true;
                return;
            }
        }

        var pairs = font.KerningPairs;
        var kerning = new ImFontKerningPair[pairs.Size];

        for (var at = 0; at < kerning.Length; at++)
            kerning[at] = pairs[at];

        capture.Entries[index] = new UiFaceCacheEntry
        {
            FallbackChar = font.FallbackChar,
            EllipsisChar = font.EllipsisChar,
            Glyphs = copied,
            Kerning = kerning,
        };
    }
}

internal static class UiFaceCache
{
    private const int Magic = 0x3143464E;
    private const int FormatVersion = 1;
    private const string Extension = ".nfc";

    private static readonly TimeSpan SweepDelay = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> Used = new(StringComparer.OrdinalIgnoreCase);

    private static string? directory;
    private static Timer? sweepTimer;

    internal static bool Enabled { get; set; } = true;

    internal static bool FullAlpha { get; set; }

    private static string? Directory
    {
        get
        {
            if (directory != null)
                return directory;

            if (!NoireService.IsInitialized())
                return null;

            directory = Path.Combine(NoireService.PluginInterface.GetPluginConfigDirectory(), "NoireFontCache");
            return directory;
        }
    }

    internal static UiFaceBuild Prepare(UiFacePage page, bool skipCache)
    {
        var (ranges, scripts) = NoireScriptFonts.Snapshot;
        var entries = page.Entries.ToArray();

        Array.Sort(entries, static (a, b) =>
        {
            var byName = string.CompareOrdinal(a.Face.Name, b.Face.Name);
            return byName != 0 ? byName : a.EmPx.CompareTo(b.EmPx);
        });

        var key = Enabled ? KeyOf(entries, ranges) : null;
        var handOff = page.HandOff;
        page.HandOff = null;

        return new UiFaceBuild(key, entries, ranges, scripts)
        {
            SkipCache = skipCache,
            Preloaded = handOff is { } given && given.Key == key ? given.Data : null,
        };
    }

    private static string? KeyOf(UiFaceEntry[] entries, ushort[]? ranges)
    {
        if (entries.Length == 0 || !NoireService.IsInitialized())
            return null;

        var text = new StringBuilder(4096);

        text.Append("NFC").Append(FormatVersion)
            .Append('|').Append(typeof(IFontAtlas).Assembly.GetName().Version)
            .Append('|').Append(ImGui.GetVersion())
            .Append('|').Append(NoireService.PluginInterface.UiLanguage)
            .Append('|').Append(UiFaceAtlas.TextureWidth)
            .Append('|').Append(UiFaceAtlas.Gamma?.ToString("R", CultureInfo.InvariantCulture) ?? "-")
            .Append('|');

        AppendRanges(text, ranges);

        foreach (var entry in entries)
        {
            var face = entry.Face;

            if (face.OnBuild != null)
                return null;

            text.Append('|').Append(face.DataHash)
                .Append(',').Append(entry.EmPx.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(face.LineRatio.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(face.Oversample?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(',').Append(face.MergeLanguageGlyphs ? '1' : '0')
                .Append(',');

            AppendRanges(text, face.GlyphRanges ?? UiFontCache.DefaultGlyphRanges);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AppendRanges(StringBuilder text, ushort[]? ranges)
    {
        if (ranges == null)
        {
            text.Append('-');
            return;
        }

        foreach (var value in ranges)
            text.Append(value.ToString("X", CultureInfo.InvariantCulture)).Append(' ');
    }

    private static string? PathOf(string key) => Directory is { } root ? Path.Combine(root, key + Extension) : null;

    internal static UiFaceCacheData? Load(string key, int entryCount)
    {
        if (PathOf(key) is not { } path || !File.Exists(path))
            return null;

        try
        {
            using var file = File.OpenRead(path);
            using var head = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            if (head.ReadInt32() != Magic || head.ReadInt32() != FormatVersion || head.ReadString() != key)
                return Discard(path, "stale header");

            using var zip = new ZLibStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);

            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var textureCount = reader.ReadInt32();
            var count = reader.ReadInt32();

            if (width <= 0 || height <= 0 || textureCount <= 0 || count != entryCount)
                return Discard(path, "wrong shape");

            var data = new UiFaceCacheData
            {
                Width = width,
                Height = height,
                Entries = new UiFaceCacheEntry?[count],
            };

            for (var texture = 0; texture < textureCount; texture++)
            {
                var pixels = new byte[width * height];
                reader.BaseStream.ReadExactly(pixels);
                data.Textures.Add(pixels);
            }

            for (var index = 0; index < count; index++)
            {
                var fallback = reader.ReadUInt16();
                var ellipsis = reader.ReadUInt16();
                var glyphs = new ImGuiHelpers.ImFontGlyphReal[reader.ReadInt32()];
                reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(glyphs.AsSpan()));
                var kerning = new ImFontKerningPair[reader.ReadInt32()];
                reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(kerning.AsSpan()));

                foreach (var glyph in glyphs)
                {
                    if (glyph.Visible && (uint)glyph.TextureIndex >= (uint)textureCount)
                        return Discard(path, "bad texture index");
                }

                data.Entries[index] = new UiFaceCacheEntry
                {
                    FallbackChar = fallback,
                    EllipsisChar = ellipsis,
                    Glyphs = glyphs,
                    Kerning = kerning,
                };
            }

            NoteUsed(key);
            return data;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"Font cache unreadable, rebuilding: {Path.GetFileName(path)} ({ex.Message})", nameof(NoireFont));
            TryDelete(path);
            return null;
        }
    }

    internal static void Save(string key, UiFaceCacheData data)
    {
        if (PathOf(key) is not { } path)
            return;

        NoteUsed(key);

        _ = Task.Run(() =>
        {
            var temporary = path + ".tmp" + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture);

            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                using (var file = File.Create(temporary))
                {
                    using (var head = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
                    {
                        head.Write(Magic);
                        head.Write(FormatVersion);
                        head.Write(key);
                    }

                    using var zip = new ZLibStream(file, CompressionLevel.Fastest);
                    using var writer = new BinaryWriter(zip);

                    writer.Write(data.Width);
                    writer.Write(data.Height);
                    writer.Write(data.Textures.Count);
                    writer.Write(data.Entries.Length);

                    foreach (var texture in data.Textures)
                        writer.Write(texture);

                    foreach (var entry in data.Entries)
                    {
                        writer.Write(entry!.FallbackChar);
                        writer.Write(entry.EllipsisChar);
                        writer.Write(entry.Glyphs.Length);
                        writer.Write(MemoryMarshal.AsBytes(entry.Glyphs.AsSpan()));
                        writer.Write(entry.Kerning.Length);
                        writer.Write(MemoryMarshal.AsBytes(entry.Kerning.AsSpan()));
                    }
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex)
            {
                NoireLogger.LogWarning($"Font cache not saved: {Path.GetFileName(path)} ({ex.Message})", nameof(NoireFont));
                TryDelete(temporary);
            }
        });
    }

    internal static void Delete(string key)
    {
        if (PathOf(key) is { } path)
            TryDelete(path);
    }

    internal static IDalamudTextureWrap CreateTexture(UiFaceCacheData data, int texture)
    {
        var provider = NoireService.TextureProvider;
        var compact = !FullAlpha && provider.IsDxgiFormatSupported(115);
        var alpha = data.Textures[texture];
        var bytesPerPixel = compact ? 2 : 4;
        var raw = new byte[alpha.Length * bytesPerPixel];

        if (compact)
        {
            var target = MemoryMarshal.Cast<byte, ushort>(raw.AsSpan());

            for (var at = 0; at < alpha.Length; at++)
                target[at] = (ushort)((alpha[at] << 8) | 0xFFF);
        }
        else
        {
            var target = MemoryMarshal.Cast<byte, uint>(raw.AsSpan());

            for (var at = 0; at < alpha.Length; at++)
                target[at] = ((uint)alpha[at] << 24) | 0xFFFFFFu;
        }

        return provider.CreateFromRaw(new RawImageSpecification(data.Width, data.Height, compact ? 115 : 87, data.Width * bytesPerPixel), raw, "NoireFont cache");
    }

    private static UiFaceCacheData? Discard(string path, string reason)
    {
        NoireLogger.LogDebug($"Font cache dropped: {Path.GetFileName(path)} ({reason})", nameof(NoireFont));
        TryDelete(path);
        return null;
    }

    private static void NoteUsed(string key)
    {
        lock (Used)
            Used.Add(key);

        ArmSweep();
    }

    internal static void ForgetUsed()
    {
        lock (Used)
            Used.Clear();
    }

    internal static void ArmSweep()
    {
        lock (Used)
        {
            sweepTimer ??= new Timer(static _ => SweepUnused(), null, Timeout.Infinite, Timeout.Infinite);
            sweepTimer.Change(SweepDelay, Timeout.InfiniteTimeSpan);
        }
    }

    internal static void Shutdown()
    {
        lock (Used)
        {
            sweepTimer?.Dispose();
            sweepTimer = null;
            Used.Clear();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void SweepUnused()
    {
        if (UiFaceAtlas.Busy)
        {
            ArmSweep();
            return;
        }

        if (Directory is not { } root || !System.IO.Directory.Exists(root))
            return;

        HashSet<string> keep;

        lock (Used)
        {
            if (Used.Count == 0)
                return;

            keep = new HashSet<string>(Used, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var now = DateTime.UtcNow;

            foreach (var file in System.IO.Directory.EnumerateFiles(root))
            {
                if (file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!keep.Contains(Path.GetFileNameWithoutExtension(file)))
                        TryDelete(file);
                }
                else if (now - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1))
                {
                    TryDelete(file);
                }
            }
        }
        catch
        {
        }
    }
}
