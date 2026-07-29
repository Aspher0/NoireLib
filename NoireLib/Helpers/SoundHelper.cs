using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Plays a sound, either one the game has or one from a file on disk.
/// <br/>
/// Game sounds follow the client's volume and mute; files go through Windows and do not, so a file is heard even
/// with the game muted.
/// </summary>
public static unsafe class SoundHelper
{
    private static readonly object PlaybackLock = new();
    private static readonly Dictionary<string, SoundPlayback> Playing = [];
    private static readonly StringBuilder ReturnBuffer = new(256);
    private static int nextAlias;

    #region Game sounds

    /// <summary>Plays one of the game's interface sound effects. Runs on the framework thread.</summary>
    /// <param name="soundEffectId">The sound effect id.</param>
    public static void PlayEffect(uint soundEffectId)
    {
        if (soundEffectId == 0)
            return;

        OnFrameworkThread(() => UIGlobals.PlaySoundEffect(soundEffectId, null, null, 0));
    }

    /// <summary>Plays a chat sound effect, numbered as in <c>&lt;se.1&gt;</c> through <c>&lt;se.16&gt;</c>.</summary>
    /// <param name="chatSoundNumber">The number as written in the chat token.</param>
    public static void PlayChatEffect(uint chatSoundNumber)
    {
        if (chatSoundNumber == 0)
            return;

        OnFrameworkThread(() => UIGlobals.PlayChatSoundEffect(chatSoundNumber));
    }

    #endregion

    #region Files

    /// <summary>Plays an audio file. Does not block.</summary>
    /// <param name="path">Full path to the file.</param>
    /// <param name="volumePercent">Volume, 0 to 100, independent of the game's.</param>
    /// <returns>The playback, or null when the file is missing or Windows could not open it.</returns>
    public static SoundPlayback? PlayFile(string path, int volumePercent = 100)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            NoireLogger.LogWarning($"SoundHelper cannot play '{path}': the file does not exist.");
            return null;
        }

        return SafeExecutor.ExecuteSafely<SoundPlayback?>(() =>
        {
            lock (PlaybackLock)
            {
                PruneFinished();

                var alias = $"noirelib_sound_{nextAlias++}";

                // No device named, so MCI picks one from the extension.
                if (!Send($"open \"{path}\" alias {alias}"))
                    return null;

                Send($"setaudio {alias} volume to {Math.Clamp(volumePercent, 0, 100) * 10}");

                if (!Send($"play {alias}"))
                {
                    Send($"close {alias}");
                    return null;
                }

                var playback = new SoundPlayback(alias, path);
                Playing[alias] = playback;

                return playback;
            }
        }, null);
    }

    /// <summary>Stops every file this helper is playing. Game sounds are unaffected.</summary>
    public static void StopAllFiles()
    {
        lock (PlaybackLock)
        {
            foreach (var playback in new List<SoundPlayback>(Playing.Values))
            {
                Send($"close {playback.Alias}");
                playback.MarkReleased();
            }

            Playing.Clear();
        }
    }

    #endregion

    #region Playback plumbing

    internal static bool IsAliasPlaying(string alias)
    {
        lock (PlaybackLock)
        {
            return string.Equals(Query($"status {alias} mode"), "playing", StringComparison.Ordinal);
        }
    }

    internal static void ReleaseAlias(string alias)
    {
        lock (PlaybackLock)
        {
            Send($"close {alias}");
            Playing.Remove(alias);
        }
    }

    /// <summary>Closes devices whose sound has ended. MCI holds one open until told otherwise.</summary>
    private static void PruneFinished()
    {
        List<string>? finished = null;

        foreach (var (alias, playback) in Playing)
        {
            if (playback.Released || !string.Equals(Query($"status {alias} mode"), "playing", StringComparison.Ordinal))
                (finished ??= []).Add(alias);
        }

        if (finished == null)
            return;

        foreach (var alias in finished)
        {
            Send($"close {alias}");

            if (Playing.Remove(alias, out var playback))
                playback.MarkReleased();
        }
    }

    private static bool Send(string command)
        => mciSendStringW(command, null, 0, IntPtr.Zero) == 0;

    private static string Query(string command)
    {
        ReturnBuffer.Clear();

        return mciSendStringW(command, ReturnBuffer, ReturnBuffer.Capacity, IntPtr.Zero) == 0
            ? ReturnBuffer.ToString()
            : string.Empty;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(
        string command,
        StringBuilder? returnBuffer,
        int returnBufferLength,
        IntPtr callbackWindow);

    #endregion

    private static void OnFrameworkThread(Action action)
    {
        if (!NoireService.IsInitialized())
            return;

        // Direct when already there, so a draw-time call is not delayed a frame.
        if (NoireService.Framework.IsInFrameworkUpdateThread)
        {
            SafeExecutor.ExecuteSafely(action);
            return;
        }

        _ = AsyncHelper.RunOnFrameworkThreadAsync(() => SafeExecutor.ExecuteSafely(action));
    }
}
