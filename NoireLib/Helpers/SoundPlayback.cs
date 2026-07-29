using System;

namespace NoireLib.Helpers;

/// <summary>
/// One sound playing from a file. Disposing it stops the sound; a playback that finishes on its own releases itself.
/// </summary>
public sealed class SoundPlayback : IDisposable
{
    private bool released;

    internal SoundPlayback(string alias, string path)
    {
        Alias = alias;
        Path = path;
    }

    /// <summary>The file being played.</summary>
    public string Path { get; }

    /// <summary>Whether the sound is still playing.</summary>
    public bool IsPlaying => !released && SoundHelper.IsAliasPlaying(Alias);

    /// <summary>Stops the sound. Safe to call more than once.</summary>
    public void Stop() => Dispose();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (released)
            return;

        released = true;
        SoundHelper.ReleaseAlias(Alias);
    }

    internal string Alias { get; }

    internal bool Released => released;

    internal void MarkReleased() => released = true;
}
