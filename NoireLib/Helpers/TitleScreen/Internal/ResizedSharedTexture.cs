using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

// Reports "not ready" while the GPU scales the copy. Dalamud removes an entry whose texture reports an exception.
internal sealed class ResizedSharedTexture : ISharedImmediateTexture, IDisposable
{
    private readonly ISharedImmediateTexture source;
    private readonly int longestSide;
    private readonly Task<IDalamudTextureWrap> work;

    private IDalamudTextureWrap? resized;
    private Exception? failure;
    private bool disposed;

    internal ResizedSharedTexture(ISharedImmediateTexture source, int longestSide)
    {
        this.source = source;
        this.longestSide = longestSide;
        work = Resize();
    }

    public bool TryGetWrap(out IDalamudTextureWrap? texture, out Exception? exception)
    {
        Settle();
        texture = resized;
        exception = failure;
        return texture != null;
    }

    // Until the copy exists the original draws.
    public IDalamudTextureWrap GetWrapOrEmpty()
    {
        Settle();
        return resized ?? source.GetWrapOrEmpty();
    }

    public IDalamudTextureWrap GetWrapOrDefault(IDalamudTextureWrap defaultWrap)
    {
        Settle();
        return resized ?? source.GetWrapOrDefault(defaultWrap);
    }

    public async Task<IDalamudTextureWrap> RentAsync(CancellationToken cancellationToken = default)
    {
        var wrap = await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        return wrap.CreateWrapSharingLowLevelResource();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        resized?.Dispose();
        resized = null;
    }

    private void Settle()
    {
        if (disposed || resized != null || failure != null || !work.IsCompleted)
            return;

        if (work.IsCompletedSuccessfully)
            resized = work.Result;
        else
            failure = work.Exception?.GetBaseException() ?? new InvalidOperationException("The icon could not be scaled.");
    }

    private async Task<IDalamudTextureWrap> Resize()
    {
        using var original = await source.RentAsync().ConfigureAwait(false);

        // Dalamud accepts the required size on either side. The aspect ratio is kept.
        var scale = (float)longestSide / Math.Max(original.Width, original.Height);
        var width = Math.Max(1, (int)MathF.Round(original.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(original.Height * scale));

        if (original.Width >= original.Height)
            width = longestSide;
        else
            height = longestSide;

        return await NoireService.TextureProvider.CreateFromExistingTextureAsync(
            original,
            new TextureModificationArgs { NewWidth = width, NewHeight = height },
            true,
            $"NoireLib title screen icon {width}x{height}",
            CancellationToken.None).ConfigureAwait(false);
    }
}
