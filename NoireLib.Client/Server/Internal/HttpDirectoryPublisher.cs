using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace NoireLib.Remote.Internal;

// The file holds the loopback credential. The folder is locked to the current user.
internal sealed class HttpDirectoryPublisher : IDisposable
{
    private readonly NoireRemoteOptions options;
    private readonly Func<NoireRemoteInstanceRecord> build;
    private readonly Guid instance;
    private readonly INoireRemoteHost host;
    private Timer? heartbeat;
    private int disposed;

    public HttpDirectoryPublisher(NoireRemoteOptions options, Guid instance, Func<NoireRemoteInstanceRecord> build, INoireRemoteHost host)
    {
        this.options = options;
        this.instance = instance;
        this.build = build;
        this.host = host;
    }

    public void Start()
    {
        PrepareDirectory(options.RegistryDirectory, host);
        Write();

        var interval = options.HeartbeatInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : options.HeartbeatInterval;
        heartbeat = new Timer(static state => ((HttpDirectoryPublisher)state!).Write(), this, interval, interval);
    }

    private void Write()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        try
        {
            NoireRemoteDirectory.Write(options.RegistryDirectory, build());
        }
        catch (Exception exception)
        {
            host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] could not write the instance record", exception);
        }
    }

    public static void PrepareDirectory(string directory, INoireRemoteHost host)
    {
        try
        {
            Directory.CreateDirectory(directory);

            // The access control API is Windows only.
            if (!OperatingSystem.IsWindows())
                return;

            var identity = WindowsIdentity.GetCurrent().User;

            if (identity == null)
                return;

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            new DirectoryInfo(directory).SetAccessControl(security);
        }
        catch (Exception exception)
        {
            // A default install already restricts the profile folder to the current user.
            host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] could not lock the instance folder to the current user", exception);
        }
    }

    public static NoireRemoteInstanceRecord BuildRecord(
        Guid instance,
        string address,
        int port,
        string token,
        DateTime startedUtc,
        string plugin,
        string pluginVersion,
        string libraryVersion,
        string label,
        IReadOnlyList<string> endpoints,
        string machine,
        string secretId,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? metadata = null,
        bool allowFleetControl = false)
    {
        using var process = Process.GetCurrentProcess();

        return new NoireRemoteInstanceRecord
        {
            Protocol = NoireRemotePaths.Protocol,
            Instance = instance,
            Address = address,
            Port = port,
            Token = token,
            Pid = process.Id,
            ProcessStartUtc = process.StartTime.ToUniversalTime(),
            StartedUtc = startedUtc,
            HeartbeatUtc = DateTime.UtcNow,
            Metadata = metadata ?? new System.Collections.Generic.Dictionary<string, string>(),
            AllowFleetControl = allowFleetControl,
            Plugin = plugin,
            PluginVersion = pluginVersion,
            LibraryVersion = libraryVersion,
            Label = label,
            Endpoints = endpoints,
            Machine = machine,
            SecretId = secretId,
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        heartbeat?.Dispose();
        heartbeat = null;

        try
        {
            NoireRemoteDirectory.Delete(options.RegistryDirectory, instance);
        }
        catch (Exception exception)
        {
            host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] could not remove the instance record", exception);
        }
    }
}
