using System;
using System.Collections.Generic;

namespace NoireLib.UI;

public sealed class NoireFontSet : IDisposable
{
    private const float GlyphArea = 0.5f;
    private const int LatinGlyphs = 300;
    private const int MaxPages = 4;

    private readonly string name;
    private readonly List<(NoireFont Face, float EmPx, float Weight)> planned = new();
    private readonly List<UiFaceEntry> entries = new();
    private UiFacePage[] pages = [];
    private bool built;
    private bool disposed;

    public NoireFontSet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
    }

    public NoireFontSet Add(NoireFont face, ReadOnlySpan<float> sizesPx)
    {
        ArgumentNullException.ThrowIfNull(face);

        if (built)
            throw new InvalidOperationException("Sizes cannot be added to a font set once it is built");

        foreach (var size in sizesPx)
        {
            var em = face.EmPixels(size);

            if (planned.Exists(p => ReferenceEquals(p.Face, face) && p.EmPx == em))
                continue;

            var line = em * face.LineRatio;
            planned.Add((face, em, line * line));
        }

        return this;
    }

    public void Build()
    {
        if (built || disposed)
            return;

        built = true;

        var total = 0f;

        foreach (var item in planned)
            total += item.Weight;

        var textures = (NoireScriptFonts.GlyphCount + LatinGlyphs) * total * GlyphArea / ((float)UiFaceAtlas.TextureWidth * UiFaceAtlas.TextureWidth);
        var count = Math.Clamp((int)MathF.Ceiling(textures), 1, MaxPages);

        pages = new UiFacePage[count];

        for (var index = 0; index < count; index++)
            pages[index] = UiFaceAtlas.NewPage(count == 1 ? name : $"{name} {index + 1}/{count}");

        planned.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));

        var loads = new float[count];

        foreach (var (face, em, weight) in planned)
        {
            var lightest = 0;

            for (var index = 1; index < count; index++)
            {
                if (loads[index] < loads[lightest])
                    lightest = index;
            }

            loads[lightest] += weight;

            if (face.Add(em, pages[lightest], buildNow: false) is { } entry)
                entries.Add(entry);
        }

        UiFaceAtlas.ScheduleBuild();
    }

    public bool IsReady => built && !disposed && UiFaceAtlas.IsReady(pages, entries);

    public bool IsBuilt => built && !disposed && UiFaceAtlas.AllBuilt(entries);

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        UiFaceAtlas.RemovePages(pages);
        entries.Clear();
    }
}
