using Dalamud.Interface.ManagedFontAtlas;
using System;

namespace NoireLib.UI;

// The host font through NoireLib's size cache. Body text at the host size pushes nothing unless the active language
// needs a merged script; every other size costs the one font push NoireText already pays.
internal sealed class StockFontSkin : IFontSkin
{
    private const int MaxDepth = 16;

    private static readonly float[] Scales = [1f, 0.88f, 1.15f, 1.4f, 1f];

    internal static readonly StockFontSkin Instance = new();

    private readonly IDisposable?[] pushed = new IDisposable?[MaxDepth];
    private int depth;

    public void Load()
    {
    }

    public void Unload()
    {
    }

    public void Push(TextRole role)
    {
        var scale = Scales[(int)role] * NoireSkinnedWindowBase.CurrentTextScale;
        IFontHandle? handle = null;

        if (scale != 1f || NoireScriptFonts.Ranges != null)
            handle = UiFontCache.Get(NoireTheme.DefaultBodySize * scale);

        var entry = handle is { Available: true } ? handle.Push() : null;

        if (depth < MaxDepth)
            pushed[depth] = entry;

        depth++;
    }

    public void Pop()
    {
        if (depth == 0)
            return;

        depth--;

        if (depth < MaxDepth)
        {
            pushed[depth]?.Dispose();
            pushed[depth] = null;
        }
    }
}
