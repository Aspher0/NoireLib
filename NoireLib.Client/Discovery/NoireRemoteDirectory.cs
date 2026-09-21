using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NoireLib.Remote;

/// <summary>
/// Reads and writes the folder of instance records both halves agree on. A listener publishes one file per instance
/// and a caller enumerates the folder to find it.
/// </summary>
public static class NoireRemoteDirectory
{
    /// <summary>
    /// The folder records are written to and read from unless a caller names another.
    /// </summary>
    /// <returns>An absolute folder path.</returns>
    public static string DefaultDirectory()
        => NoireRemotePaths.DefaultRegistryDirectory();

    /// <summary>
    /// The folder a spooled file transfer is written to unless a caller names another.
    /// </summary>
    /// <returns>An absolute folder path. It is not created by this call.</returns>
    public static string DefaultSpoolDirectory()
        => Path.Combine(Path.GetDirectoryName(NoireRemotePaths.DefaultRegistryDirectory()) ?? Path.GetTempPath(), "spool");

    /// <summary>
    /// The path of one record inside a folder.
    /// </summary>
    /// <param name="directory">The folder holding the records.</param>
    /// <param name="instance">The instance id.</param>
    /// <returns>The full file path.</returns>
    public static string RecordPath(string directory, Guid instance)
        => Path.Combine(directory, instance.ToString("D") + ".json");

    /// <summary>
    /// Writes a record, replacing any previous one for the same instance. The bytes land in a temporary file that is
    /// then moved over the target. A reader never sees a half-written record.
    /// </summary>
    /// <param name="directory">The folder to write into. It is created when it does not exist.</param>
    /// <param name="record">The record to write.</param>
    /// <exception cref="ArgumentNullException">If the record is null.</exception>
    public static void Write(string directory, NoireRemoteInstanceRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        Directory.CreateDirectory(directory);

        var target = RecordPath(directory, record.Instance);
        var temporary = target + ".tmp";

        File.WriteAllBytes(temporary, NoireRemoteJson.WriteBytes(record));
        File.Move(temporary, target, true);
    }

    /// <summary>
    /// Deletes one record, ignoring a file that is already gone.
    /// </summary>
    /// <param name="directory">The folder holding the records.</param>
    /// <param name="instance">The instance id.</param>
    public static void Delete(string directory, Guid instance)
    {
        try
        {
            File.Delete(RecordPath(directory, instance));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads every record in a folder, skipping anything unreadable, malformed or of another protocol version.
    /// </summary>
    /// <param name="directory">The folder holding the records.</param>
    /// <returns>The records found, in no particular order.</returns>
    public static IReadOnlyList<NoireRemoteInstanceRecord> ReadAll(string directory)
    {
        var records = new List<NoireRemoteInstanceRecord>();

        string[] files;

        try
        {
            files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception)
        {
            return records;
        }

        foreach (var file in files)
        {
            var record = ReadFile(file);

            if (record != null && record.Protocol == NoireRemotePaths.Protocol && record.Port > 0)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Reads one record file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The record, or null when the file is missing, locked or malformed.</returns>
    public static NoireRemoteInstanceRecord? ReadFile(string path)
    {
        try
        {
            return NoireRemoteJson.TryRead<NoireRemoteInstanceRecord>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether the process a record names is still the one that wrote it.
    /// </summary>
    /// <param name="record">The record to check.</param>
    /// <returns>True when a process of that id is running and started when the record says. True as well when the
    /// process cannot be inspected, since a refused query is not evidence of death.</returns>
    public static bool ProcessIsAlive(NoireRemoteInstanceRecord record)
    {
        if (record == null || record.Pid <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(record.Pid);

            if (process.HasExited)
                return false;

            if (record.ProcessStartUtc == default)
                return true;

            var difference = process.StartTime.ToUniversalTime() - record.ProcessStartUtc;
            return Math.Abs(difference.TotalSeconds) <= 2;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Checks whether a record's heartbeat is recent enough to believe.
    /// </summary>
    /// <param name="record">The record to check.</param>
    /// <param name="staleAfter">How old a heartbeat may be before the record is treated as abandoned.</param>
    /// <param name="nowUtc">The moment to compare against.</param>
    /// <returns>True when the heartbeat is within the window.</returns>
    public static bool HeartbeatIsFresh(NoireRemoteInstanceRecord record, TimeSpan staleAfter, DateTime nowUtc)
        => record != null && record.HeartbeatUtc != default && nowUtc - record.HeartbeatUtc <= staleAfter;
}
