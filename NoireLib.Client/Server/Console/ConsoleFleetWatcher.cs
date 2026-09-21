using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NoireLib.Remote.Internal;

// The instance records are the only place listener changes are written. Their folder is watched.
// Every record rewrites its heartbeat every few seconds. Only a change outside the heartbeat counts.
internal sealed class ConsoleFleetWatcher : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly string folder;
    private readonly Action changed;
    private readonly Timer settle;
    private readonly FileSystemWatcher? watcher;
    private string fingerprint;
    private int disposed;

    public ConsoleFleetWatcher(NoireRemoteOptions options, Action changed)
    {
        folder = options.RegistryDirectory;
        this.changed = changed;
        settle = new Timer(static state => ((ConsoleFleetWatcher)state!).Compare(), this, Timeout.Infinite, Timeout.Infinite);
        fingerprint = Read();

        try
        {
            Directory.CreateDirectory(folder);

            watcher = new FileSystemWatcher(folder, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };

            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Without a watcher the fleet is refreshed by hand.
            watcher?.Dispose();
            watcher = null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (Volatile.Read(ref disposed) == 0)
            settle.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    private void Compare()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        var next = Read();

        if (string.Equals(next, fingerprint, StringComparison.Ordinal))
            return;

        fingerprint = next;
        changed();
    }

    // Minus the parts that change on their own.
    private string Read()
    {
        var text = new StringBuilder();

        try
        {
            var files = Directory.GetFiles(folder, "*.json");

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var record = JObject.Parse(File.ReadAllText(file));

                    record.Remove("heartbeatUtc");
                    record.Remove("token");
                    text.Append(record.ToString(Newtonsoft.Json.Formatting.None)).Append('\n');
                }
                catch (Exception)
                {
                    // A half-written record is read again on the next change.
                }
            }
        }
        catch (Exception)
        {
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        watcher?.Dispose();
        settle.Dispose();
    }
}
