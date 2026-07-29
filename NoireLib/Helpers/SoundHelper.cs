using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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

    /// <summary>
    /// The longest path the multimedia interface accepts when opening a file. The limit belongs to the command string
    /// the file name is pasted into, so a longer path is refused outright and no audio is ever read. A plugin's own
    /// configuration folder already spends more than half of this budget, so the short form of the path is what makes
    /// a normal layout fit.
    /// </summary>
    private const int MaxOpenPathLength = 126;

    /// <summary>Marks a path as extended-length, which is what lets Windows read one past 260 characters.</summary>
    private const string ExtendedLengthPrefix = @"\\?\";

    /// <summary>
    /// How far into an mp3 Windows will look for the first audio frame. A tag longer than this hides the audio from
    /// it completely, and the error it gives back names the driver rather than the tag that caused it. Measured
    /// against files whose tag was grown a few kilobytes at a time: the last size that plays puts the first frame at
    /// 129,034 bytes and the first that fails puts it at 131,082.
    /// </summary>
    private const int Mp3FirstFrameWindow = 131072;

    /// <summary>How long a tag-stripped copy is kept before it is treated as rubbish and deleted.</summary>
    private static readonly TimeSpan StrippedLifetime = TimeSpan.FromDays(7);

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

    /// <summary>
    /// Plays an audio file. Does not block.
    /// <br/>
    /// Windows chooses the device from the extension, so .wav and .mp3 play everywhere while a format it has no codec
    /// for is refused. Anything that stops a file from playing is logged with the reason Windows gave.
    /// <br/>
    /// A .wav plays from any thread. Every other format reaches a driver that only loads for a caller in a
    /// single-threaded apartment, which the framework thread is and a background task is not, so those belong on the
    /// framework thread.
    /// </summary>
    /// <param name="path">Full path to the file.</param>
    /// <param name="volumePercent">
    /// Volume, 0 to 100, independent of the game's. Applies only to the devices that carry a volume control, which
    /// .wav does not; a .wav plays at the system volume.
    /// </param>
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

                if (!TryResolveOpenPath(path, out var openPath))
                    return null;

                var alias = $"noirelib_sound_{nextAlias++}";

                // No device named, so MCI picks one from the extension.
                var error = Send($"open \"{openPath}\" alias {alias}");
                var rewritten = false;

                // An mp3 whose audio begins past the window Windows scans is refused above. The same audio without
                // the tag sitting in front of it is a file Windows reads normally, so that is what gets opened.
                if (error != 0 && TryStripTag(path, out var strippedPath) && TryResolveOpenPath(strippedPath, out var strippedOpenPath))
                {
                    error = Send($"open \"{strippedOpenPath}\" alias {alias}");
                    rewritten = true;

                    if (error == 0)
                    {
                        NoireLogger.LogDebug(
                            $"SoundHelper is playing '{path}' from a copy without its front tag, which is what puts " +
                            "the audio where Windows looks for it.");
                    }
                }

                if (error != 0)
                {
                    var diagnosis = rewritten ? string.Empty : Diagnose(path);

                    NoireLogger.LogWarning($"SoundHelper could not open '{path}': {Describe(error)}{diagnosis}");

                    return null;
                }

                ApplyVolume(alias, volumePercent);

                error = Send($"play {alias}");

                if (error != 0)
                {
                    NoireLogger.LogWarning($"SoundHelper opened '{path}' but could not play it: {Describe(error)}");
                    Send($"close {alias}");

                    return null;
                }

                // The playback keeps the path the caller gave, since the short form is an implementation detail.
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

    /// <summary>
    /// Chooses which form of the path to open with. Windows keeps a short 8.3 form of most paths, and that form is
    /// what brings a file living under a normal user folder back inside the length the interface accepts.
    /// </summary>
    /// <param name="path">The path the caller asked for.</param>
    /// <param name="openPath">The form to open, which is the caller's path whenever it already fits.</param>
    /// <returns>Whether a usable form was found.</returns>
    private static bool TryResolveOpenPath(string path, out string openPath)
    {
        openPath = path;

        if (path.Length > MaxOpenPathLength)
        {
            var shortPath = GetShortPath(path);

            if (shortPath.Length < openPath.Length)
                openPath = shortPath;
        }

        if (openPath.Length <= MaxOpenPathLength)
            return true;

        NoireLogger.LogWarning(
            $"SoundHelper cannot play '{path}': the path is {path.Length} characters and the multimedia interface " +
            $"refuses anything longer than {MaxOpenPathLength}. The short form of the path came to {openPath.Length} " +
            "and does not fit either, which happens when the folders along it are already short or when 8.3 name " +
            "creation is turned off for the drive. Moving the file nearer the drive root is what gets it playing.");

        return false;
    }

    /// <summary>Asks Windows for the short 8.3 form of a path, falling back to the path when there is none.</summary>
    private static string GetShortPath(string path)
    {
        if (TryGetShortPath(path, out var shortPath))
            return shortPath;

        // Past 260 characters the plain call will not even look at the path, and the extended-length prefix is what
        // gets it to. The prefix belongs to that call alone, so it comes back off before the path is opened.
        if (!IsExtendedLengthEligible(path) || !TryGetShortPath(ExtendedLengthPrefix + path, out shortPath))
            return path;

        return shortPath.StartsWith(ExtendedLengthPrefix, StringComparison.Ordinal)
            ? shortPath[ExtendedLengthPrefix.Length..]
            : shortPath;
    }

    private static bool TryGetShortPath(string path, out string shortPath)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathNameW(path, buffer, buffer.Capacity);

        // Zero reports failure, and a length past the buffer means the answer did not fit in it.
        if (length <= 0 || length >= buffer.Capacity)
        {
            shortPath = path;

            return false;
        }

        shortPath = buffer.ToString();

        return true;
    }

    /// <summary>
    /// Whether the extended-length prefix can be put in front of a path. It accepts a plain drive letter and
    /// backslashes only, so a network path or one written with forward slashes is left alone.
    /// </summary>
    private static bool IsExtendedLengthEligible(string path)
        => path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\';

    /// <summary>
    /// Applies the requested volume. Only some of the devices Windows picks by extension carry a volume control: the
    /// wave device behind .wav has none, so those files play at the system volume and the refusal is logged rather
    /// than swallowed.
    /// </summary>
    private static void ApplyVolume(string alias, int volumePercent)
    {
        // The device scale runs to 1000 rather than 100.
        var error = Send($"setaudio {alias} volume to {Math.Clamp(volumePercent, 0, 100) * 10}");

        if (error != 0)
        {
            NoireLogger.LogDebug(
                $"SoundHelper could not set the volume on '{alias}': {Describe(error)}. The device has no volume " +
                "control of its own, so the sound plays at the system volume.");
        }
    }

    /// <summary>
    /// Copies an mp3 without the tag that hides its audio, and answers where the copy is. Everything else is turned
    /// away, so an ordinary failure to open a file never turns into a file copy.
    /// </summary>
    /// <param name="path">The file the caller asked to play.</param>
    /// <param name="strippedPath">The copy, which is the same audio starting at its first frame.</param>
    /// <returns>Whether a copy is available to play.</returns>
    internal static bool TryStripTag(string path, out string strippedPath)
    {
        strippedPath = string.Empty;

        if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return false;

        var tagLength = ReadId3Length(path);

        if (tagLength < Mp3FirstFrameWindow)
            return false;

        try
        {
            var directory = StrippedDirectory;
            Directory.CreateDirectory(directory);

            var target = Path.Combine(directory, $"{StrippedName(path)}.mp3");

            // The same sound is usually played more than once, and the copy is the expensive part of playing it.
            if (!File.Exists(target))
            {
                PruneStripped(directory);
                WriteWithoutTag(path, tagLength, target);
            }

            strippedPath = target;

            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"SoundHelper could not copy '{path}' without its tag: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    /// Where the copies live. The folder name is kept short because the copy has to be opened through the same
    /// interface that refuses a long path, and it is shared so one file is only ever copied once.
    /// </summary>
    private static string StrippedDirectory => Path.Combine(Path.GetTempPath(), "NoireLib", "Sound");

    /// <summary>
    /// Names a copy after what it was made from. The write time and length are part of the name, so editing the
    /// original produces a different name rather than a stale copy that plays the old audio.
    /// </summary>
    private static string StrippedName(string path)
    {
        var file = new FileInfo(path);
        var key = $"{path.ToLowerInvariant()}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>Writes the audio without the tag in front of it.</summary>
    private static void WriteWithoutTag(string path, int tagLength, string target)
    {
        using var source = File.OpenRead(path);

        source.Position = FindFirstFrame(source, tagLength);

        // Written under another name first, so a copy cut short is never mistaken for a finished one.
        var partial = $"{target}.partial";

        using (var destination = File.Create(partial))
            source.CopyTo(destination);

        File.Move(partial, target, true);
    }

    /// <summary>
    /// Finds where the audio starts. The tag length already points at it in a well-formed file, and the scan that
    /// follows covers the ones that pad past the length they declare.
    /// </summary>
    private static long FindFirstFrame(FileStream stream, int tagLength)
    {
        const int scanLength = 8192;

        if (tagLength >= stream.Length)
            return tagLength;

        stream.Position = tagLength;

        var window = new byte[(int)Math.Min(scanLength, stream.Length - tagLength)];
        var read = stream.ReadAtLeast(window, window.Length, false);

        for (var i = 0; i + 3 < read; i++)
        {
            if (IsFrameHeader(window, i))
                return tagLength + i;
        }

        return tagLength;
    }

    /// <summary>
    /// Whether four bytes open an audio frame. The sync bits alone appear inside cover art often enough to be worth
    /// checking the fields behind them, none of which may hold the value reserved as meaningless.
    /// </summary>
    private static bool IsFrameHeader(byte[] window, int offset)
    {
        if (window[offset] != 0xFF || (window[offset + 1] & 0xE0) != 0xE0)
            return false;

        var version = (window[offset + 1] >> 3) & 0x03;
        var layer = (window[offset + 1] >> 1) & 0x03;
        var bitrate = (window[offset + 2] >> 4) & 0x0F;
        var sampleRate = (window[offset + 2] >> 2) & 0x03;

        return version != 1 && layer != 0 && bitrate is not (0 or 15) && sampleRate != 3;
    }

    /// <summary>Drops copies old enough that nothing is likely to want them, so the folder does not grow forever.</summary>
    private static void PruneStripped(string directory)
    {
        var cutoff = DateTime.UtcNow - StrippedLifetime;

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch
            {
                // A copy being played right now cannot be deleted, and the next prune will find it again.
            }
        }
    }

    /// <summary>
    /// Adds what Windows leaves out. The error it returns for an mp3 whose audio begins too late in the file names
    /// the driver rather than the cause, which sends the reader looking in the wrong place entirely, so the one shape
    /// of file that produces it is measured here and named.
    /// </summary>
    private static string Diagnose(string path)
    {
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        // Every format other than wav reaches a driver built on COM, and that driver only loads for a caller in a
        // single-threaded apartment. The framework thread is one and a background task is not, so the very same call
        // plays from a draw and refuses from a task.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return " The call came from a thread outside a single-threaded apartment, and the driver behind this " +
                   "format only loads inside one. Calling from the framework thread is what lets it load; a wav " +
                   "has no such condition and plays from anywhere.";
        }

        if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var tagLength = ReadId3Length(path);

        return tagLength < Mp3FirstFrameWindow
            ? string.Empty
            : $" The file opens with a {tagLength:N0} byte ID3 tag, and Windows looks for the audio only inside the " +
              $"first {Mp3FirstFrameWindow:N0} bytes, so it never reaches it. Large embedded cover art is what " +
              "usually makes a tag this big, and saving the file with a smaller image makes it playable.";
    }

    /// <summary>Reads the length of the ID3 tag a file opens with, or zero when it does not open with one.</summary>
    internal static int ReadId3Length(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            Span<byte> header = stackalloc byte[10];

            if (stream.ReadAtLeast(header, header.Length, false) < header.Length)
                return 0;

            if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
                return 0;

            // The four length bytes carry seven bits each, so the tag length never sets a high bit of its own.
            var length = ((header[6] & 0x7F) << 21)
                         | ((header[7] & 0x7F) << 14)
                         | ((header[8] & 0x7F) << 7)
                         | (header[9] & 0x7F);

            return length + header.Length;
        }
        catch
        {
            // The tag is only ever read to explain a failure, so failing to read it explains nothing and says nothing.
            return 0;
        }
    }

    /// <summary>Turns a multimedia error code into the sentence Windows has for it.</summary>
    private static string Describe(int error)
    {
        var buffer = new StringBuilder(256);

        return mciGetErrorStringW(error, buffer, buffer.Capacity)
            ? $"{buffer} (code {error})"
            : $"multimedia error {error}";
    }

    private static int Send(string command)
        => mciSendStringW(command, null, 0, IntPtr.Zero);

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

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorStringW(int error, StringBuilder buffer, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathNameW(string longPath, StringBuilder shortPath, int shortPathLength);

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
