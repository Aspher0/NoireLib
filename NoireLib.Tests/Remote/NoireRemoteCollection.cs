using NoireLib.Remote;
using System;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>The listener is a process-wide static. The suites that publish to it or bind it run one at a time.</summary>
[CollectionDefinition("NoireRemote")]
public sealed class NoireRemoteCollection
{
}

/// <summary>
/// Takes the listener back to its launch state and points its instance record at a folder of its own. A test run
/// never writes into the folder a real game client publishes to.
/// </summary>
public abstract class NoireRemoteTestBase : IDisposable
{
    protected NoireRemoteTestBase()
    {
        NoireRemote.DisposeAll();

        RegistryDirectory = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

        NoireRemote.Options.RegistryDirectory = RegistryDirectory;
        NoireRemote.Options.PublishDirectoryRecord = false;
        NoireRemote.Options.PublishCharacterIdentity = false;
        NoireRemote.Options.AutoStart = false;
        NoireRemote.Options.EnableLogging = false;
        NoireRemote.Options.EnableRemote = false;
        NoireRemote.Options.RemoteSecret = null;
        NoireRemote.Options.EnableManifest = true;
        NoireRemote.Options.SendStackTraces = false;
        NoireRemote.Options.Port = 0;
        NoireRemote.Options.DefaultCallTimeout = TimeSpan.FromSeconds(10);
        NoireRemote.Options.MaxCallTimeout = TimeSpan.FromMinutes(5);
        NoireRemote.Options.MaxConcurrentCalls = 8;
        NoireRemote.Options.MaxQueuedCalls = 64;
        NoireRemote.Options.MaxEventStreams = 4;
        NoireRemote.Options.EventBufferSize = 256;
        NoireRemote.Options.JobRetention = TimeSpan.FromMinutes(5);
        NoireRemote.Options.HeartbeatInterval = TimeSpan.FromSeconds(15);
        NoireRemote.Options.MaxConnections = 32;
        NoireRemote.Options.SendExceptionType = true;

        // Options outlive a Reset. Traffic left on makes every later socket send take the hub's lock.
        NoireRemote.Options.PublishTraffic = true;
        NoireRemote.Options.PublishTrafficFrames = false;
        NoireRemote.Options.BindRetry = NoireLib.Websocket.NoireRetryPolicy.None;
        NoireRemote.Options.FallBackToAnyPort = true;
        NoireRemote.Options.AllowedHosts.Clear();

        // The facade excludes its own listener from discovery. These suites are caller and listener at once.
        NoireRemoteClient.Options.ExcludeInstance = null;
    }

    protected string RegistryDirectory { get; }

    public virtual void Dispose()
    {
        NoireRemote.DisposeAll();

        try
        {
            if (Directory.Exists(RegistryDirectory))
                Directory.Delete(RegistryDirectory, true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
