using System;
using System.IO;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Writes files through a temporary sibling that replaces the target only once it is fully on disk, so a failure
/// part way through never leaves a truncated file where a reader expects a complete one.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically, through a temporary sibling that
    /// replaces the target only once fully written. When another process holds the target open and the atomic swap
    /// fails, the written temporary is moved or copied over it instead, which is no longer atomic.
    /// </summary>
    /// <param name="path">The file to write; its directory is created when missing.</param>
    /// <param name="contents">The text to write, encoded as UTF-8 without a byte-order mark.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> has no directory component.</exception>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException($"Path '{path}' has no directory component.", nameof(path));

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, contents, Utf8NoBom);

            if (File.Exists(path))
                ReplaceWithFallback(
                    replace: () => File.Replace(temporaryPath, path, null, true),
                    deleteThenMove: () =>
                    {
                        File.Delete(path);
                        File.Move(temporaryPath, path);
                    },
                    copyOver: () =>
                    {
                        File.Copy(temporaryPath, path, true);
                        TryDelete(temporaryPath);
                    });
            else
                File.Move(temporaryPath, path);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="replace"/>, and on an <see cref="IOException"/> (the destination held open by another
    /// process) tries <paramref name="deleteThenMove"/> then <paramref name="copyOver"/> before rethrowing the
    /// original failure. Never waits between attempts, since callers include the framework thread.
    /// </summary>
    /// <param name="replace">The atomic replacement attempt.</param>
    /// <param name="deleteThenMove">First non-atomic fallback.</param>
    /// <param name="copyOver">Second non-atomic fallback.</param>
    internal static void ReplaceWithFallback(Action replace, Action deleteThenMove, Action copyOver)
    {
        try
        {
            replace();
        }
        catch (IOException)
        {
            try
            {
                deleteThenMove();
                return;
            }
            catch
            {
                // The destination may still be held open; one fallback left to try.
            }

            try
            {
                copyOver();
                return;
            }
            catch
            {
                // Out of options; fall through and surface the failure the replace itself reported.
            }

            throw;
        }
    }

    /// <summary>Cleans up a stray temporary file after a failed write.</summary>
    /// <param name="path">The temporary file to remove.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stray temp file is not worth failing the operation over.
        }
    }
}
