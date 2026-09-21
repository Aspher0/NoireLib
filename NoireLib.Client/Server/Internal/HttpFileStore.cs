using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace NoireLib.Remote.Internal;

// A base64 body would cost a third more bytes and hold the file twice in a game process's memory, past the 1 MB request cap.
internal sealed class HttpFileStore : IDisposable
{
    private readonly NoireRemoteOptions options;
    private readonly INoireRemoteHost host;
    private readonly object gate = new();
    private readonly Dictionary<string, Transfer> transfers = new(StringComparer.Ordinal);

    public HttpFileStore(NoireRemoteOptions options, INoireRemoteHost host)
    {
        this.options = options;
        this.host = host;

        SweepSpool();
    }

    internal sealed class Transfer : IDisposable
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public long Size;
        public string Sha256 = string.Empty;
        public string? ContentType;
        public long Received;
        public bool Complete;
        public DateTime TouchedUtc = DateTime.UtcNow;
        public byte[]? Buffer;
        public string? SpoolPath;

        public byte[] ReadAll()
        {
            if (Buffer != null)
                return Buffer;

            if (SpoolPath != null && File.Exists(SpoolPath))
                return File.ReadAllBytes(SpoolPath);

            return [];
        }

        public void Dispose()
        {
            Buffer = null;

            if (SpoolPath == null)
                return;

            try
            {
                File.Delete(SpoolPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public int ChunkBytes => Math.Max(1, options.MaxRequestBytes);

    public Transfer? Get(string id)
    {
        lock (gate)
        {
            Sweep();

            if (!transfers.TryGetValue(id, out var transfer))
                return null;

            transfer.TouchedUtc = DateTime.UtcNow;

            return transfer;
        }
    }

    public HttpFailure? Declare(NoireRemoteFileRequest request, out Transfer? transfer)
    {
        transfer = null;

        if (request.Size < 0 || request.Size > options.MaxFileBytes)
            return new HttpFailure(413, NoireRemoteErrorCodes.TooLarge,
                "This listener accepts a file of at most " + options.MaxFileBytes + " bytes.");

        lock (gate)
        {
            Sweep();

            if (transfers.Count >= Math.Max(1, options.MaxOpenTransfers))
                return new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many transfers as it accepts.") { RetryAfterSeconds = 2 };

            var held = new Transfer
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = request.Name ?? string.Empty,
                Size = request.Size,
                Sha256 = request.Sha256 ?? string.Empty,
                ContentType = request.ContentType,
            };

            // Above the threshold the bytes go to disk.
            if (request.Size > options.MaxSpooledBytes)
                held.SpoolPath = SpoolPathFor(held.Id);
            else
                held.Buffer = new byte[request.Size];

            transfers[held.Id] = held;
            transfer = held;

            return null;
        }
    }

    public Transfer Park(string name, byte[] bytes, string? contentType, string sha256)
    {
        lock (gate)
        {
            Sweep();

            var held = new Transfer
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Size = bytes.LongLength,
                Sha256 = sha256,
                ContentType = contentType,
                Buffer = bytes,
                Received = bytes.LongLength,
                Complete = true,
            };

            transfers[held.Id] = held;

            return held;
        }
    }

    public HttpFailure? Write(Transfer transfer, long offset, byte[] chunk)
    {
        if (offset < 0 || offset + chunk.LongLength > transfer.Size)
            return new HttpFailure(400, NoireRemoteErrorCodes.BadRequest,
                "The chunk at offset " + offset + " runs past the " + transfer.Size + " bytes this transfer declared.");

        lock (gate)
        {
            if (transfer.Buffer != null)
            {
                Array.Copy(chunk, 0, transfer.Buffer, offset, chunk.Length);
            }
            else if (transfer.SpoolPath != null)
            {
                try
                {
                    using var file = new FileStream(transfer.SpoolPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                    file.Seek(offset, SeekOrigin.Begin);
                    file.Write(chunk, 0, chunk.Length);
                }
                catch (IOException exception)
                {
                    host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a transfer could not be spooled", exception);

                    return new HttpFailure(500, NoireRemoteErrorCodes.HandlerFault, "The transfer could not be written to disk.");
                }
            }

            // A resumed chunk rewrites bytes that already arrived.
            transfer.Received = Math.Max(transfer.Received, offset + chunk.LongLength);
            transfer.TouchedUtc = DateTime.UtcNow;

            if (transfer.Received < transfer.Size)
                return null;

            if (transfer.Sha256.Length > 0)
            {
                var digest = Convert.ToHexString(SHA256.HashData(transfer.ReadAll()));

                if (!string.Equals(digest, transfer.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Drop(transfer.Id);

                    return new HttpFailure(400, NoireRemoteErrorCodes.ChecksumMismatch,
                        "The bytes received hash to " + digest + " and the transfer declared " + transfer.Sha256 + ".");
                }
            }

            transfer.Complete = true;

            return null;
        }
    }

    public bool Drop(string id)
    {
        lock (gate)
        {
            if (!transfers.TryGetValue(id, out var transfer))
                return false;

            transfer.Dispose();

            return transfers.Remove(id);
        }
    }

    public NoireRemoteFileStatus StatusOf(Transfer transfer)
        => new()
        {
            Id = transfer.Id,
            ChunkBytes = ChunkBytes,
            Received = transfer.Received,
            Size = transfer.Size,
            Complete = transfer.Complete,
            ExpiresAtUtc = transfer.TouchedUtc + Retention,
        };

    private TimeSpan Retention => options.FileRetention <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : options.FileRetention;

    private string SpoolPathFor(string id)
    {
        Directory.CreateDirectory(options.FileSpoolDirectory);

        return Path.Combine(options.FileSpoolDirectory, id + ".part");
    }

    // A crash leaves spool files behind.
    private void SweepSpool()
    {
        try
        {
            if (!Directory.Exists(options.FileSpoolDirectory))
                return;

            foreach (var path in Directory.GetFiles(options.FileSpoolDirectory, "*.part"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > Retention)
                    File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - Retention;
        List<string>? expired = null;

        foreach (var pair in transfers)
        {
            if (pair.Value.TouchedUtc < cutoff)
                (expired ??= []).Add(pair.Key);
        }

        if (expired == null)
            return;

        foreach (var key in expired)
        {
            transfers[key].Dispose();
            transfers.Remove(key);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var transfer in transfers.Values)
                transfer.Dispose();

            transfers.Clear();
        }
    }
}
