using System;
using System.Collections.Generic;
using System.Net;

namespace NoireLib.Remote;

/// <summary>
/// The listener's settings. Nothing here has to be set for a caller on the same machine to work: the defaults are
/// loopback, an ephemeral port and a credential generated per launch.
/// </summary>
public sealed class NoireRemoteOptions
{
    /// <summary>
    /// Gets or sets the address the socket binds to. Defaults to loopback. Setting anything else also requires
    /// <see cref="EnableRemote"/> and <see cref="RemoteSecret"/>.
    /// </summary>
    public IPAddress BindAddress
    {
        get => bindAddressField;
        set => Set(ref bindAddressField, value);
    }

    private IPAddress bindAddressField = IPAddress.Loopback;

    /// <summary>Gets or sets the port. Zero lets the operating system pick one. Defaults to zero.</summary>
    public int Port
    {
        get => portField;
        set => Set(ref portField, value);
    }

    private int portField = 0;

    /// <summary>
    /// Gets or sets whether the listener binds every address. Without <see cref="RemoteSecret"/> the listener
    /// refuses and stays on loopback.
    /// </summary>
    public bool EnableRemote
    {
        get => enableRemoteField;
        set => Set(ref enableRemoteField, value);
    }

    private bool enableRemoteField = false;

    /// <summary>
    /// Gets or sets the shared secret a caller on another machine signs with. It never crosses the wire.
    /// </summary>
    public string? RemoteSecret
    {
        get => remoteSecretField;
        set => Set(ref remoteSecretField, value);
    }

    private string? remoteSecretField = null;

    /// <summary>
    /// Gets the host header values accepted on top of the bind address and the loopback names.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>
    /// Gets or sets whether the socket also accepts IPv6. Defaults to false.
    /// </summary>
    public bool EnableDualStack
    {
        get => enableDualStackField;
        set => Set(ref enableDualStackField, value);
    }

    private bool enableDualStackField = false;

    /// <summary>Gets or sets whether the instance record is written. Discovery needs it.</summary>
    public bool PublishDirectoryRecord
    {
        get => publishDirectoryRecordField;
        set => Set(ref publishDirectoryRecordField, value);
    }

    private bool publishDirectoryRecordField = true;

    /// <summary>
    /// Gets or sets whether the record carries the character name and world. It is the only thing telling two running
    /// game clients apart by eye, and it writes that identity to a file any process of the same user can read.
    /// </summary>
    public bool PublishCharacterIdentity
    {
        get => publishCharacterIdentityField;
        set => Set(ref publishCharacterIdentityField, value);
    }

    private bool publishCharacterIdentityField = true;

    /// <summary>
    /// Gets or sets whether the manifest route serves. It hands the whole published surface to anyone holding the
    /// credential.
    /// </summary>
    public bool EnableManifest
    {
        get => enableManifestField;
        set => Set(ref enableManifestField, value);
    }

    private bool enableManifestField = true;

    /// <summary>
    /// Gets or sets whether a failed call reports the exception type name.
    /// </summary>
    public bool SendExceptionType
    {
        get => sendExceptionTypeField;
        set => Set(ref sendExceptionTypeField, value);
    }

    private bool sendExceptionTypeField = true;

    /// <summary>
    /// Gets or sets whether a failed call reports the stack trace. It carries file paths from this machine.
    /// </summary>
    public bool SendStackTraces
    {
        get => sendStackTracesField;
        set => Set(ref sendStackTracesField, value);
    }

    private bool sendStackTracesField = false;

    /// <summary>Gets or sets the thread a member runs on when neither it nor its endpoint names one. Inherit asks the host: the framework thread in a plugin, the background one elsewhere.</summary>
    public NoireRemoteThread DefaultThread
    {
        get => defaultThreadField;
        set => Set(ref defaultThreadField, value);
    }

    private NoireRemoteThread defaultThreadField = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the call deadline when neither the member nor the caller sets one. Defaults to ten seconds.
    /// </summary>
    public TimeSpan DefaultCallTimeout
    {
        get => defaultCallTimeoutField;
        set => Set(ref defaultCallTimeoutField, value);
    }

    private TimeSpan defaultCallTimeoutField = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the ceiling a caller's requested deadline is clamped to. Defaults to five minutes.
    /// </summary>
    public TimeSpan MaxCallTimeout
    {
        get => maxCallTimeoutField;
        set => Set(ref maxCallTimeoutField, value);
    }

    private TimeSpan maxCallTimeoutField = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long a connection has to send a complete request line and header block. Defaults to five seconds.
    /// </summary>
    public TimeSpan HeaderReadTimeout
    {
        get => headerReadTimeoutField;
        set => Set(ref headerReadTimeoutField, value);
    }

    private TimeSpan headerReadTimeoutField = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long a finished job is kept before a poll gets a not found. Defaults to five minutes.
    /// </summary>
    public TimeSpan JobRetention
    {
        get => jobRetentionField;
        set => Set(ref jobRetentionField, value);
    }

    private TimeSpan jobRetentionField = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how far a signed request's timestamp may be from this machine's clock. Defaults to two minutes.
    /// </summary>
    public TimeSpan RemoteClockSkew
    {
        get => remoteClockSkewField;
        set => Set(ref remoteClockSkewField, value);
    }

    private TimeSpan remoteClockSkewField = TimeSpan.FromSeconds(120);

    /// <summary>Gets or sets how long a signed request's nonce is remembered. This is the replay window. Defaults to five minutes.</summary>
    public TimeSpan RemoteNonceRetention
    {
        get => remoteNonceRetentionField;
        set => Set(ref remoteNonceRetentionField, value);
    }

    private TimeSpan remoteNonceRetentionField = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Gets or sets how often the instance record is rewritten with a fresh heartbeat. Defaults to fifteen seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval
    {
        get => heartbeatIntervalField;
        set => Set(ref heartbeatIntervalField, value);
    }

    private TimeSpan heartbeatIntervalField = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the heartbeat age a reader treats as abandoned, written into nothing and used only to size
    /// <see cref="HeartbeatInterval"/>. Defaults to a minute.
    /// </summary>
    public TimeSpan StaleAfter
    {
        get => staleAfterField;
        set => Set(ref staleAfterField, value);
    }

    private TimeSpan staleAfterField = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the largest request body accepted. Defaults to one megabyte.
    /// </summary>
    public int MaxRequestBytes
    {
        get => maxRequestBytesField;
        set => Set(ref maxRequestBytesField, value);
    }

    private int maxRequestBytesField = 1024 * 1024;

    /// <summary>
    /// Gets or sets the largest header block accepted. Defaults to sixteen kilobytes.
    /// </summary>
    public int MaxHeaderBytes
    {
        get => maxHeaderBytesField;
        set => Set(ref maxHeaderBytesField, value);
    }

    private int maxHeaderBytesField = 16 * 1024;

    /// <summary>
    /// Gets or sets the largest number of headers accepted. Defaults to sixty-four.
    /// </summary>
    public int MaxHeaderCount
    {
        get => maxHeaderCountField;
        set => Set(ref maxHeaderCountField, value);
    }

    private int maxHeaderCountField = 64;

    /// <summary>
    /// Gets or sets the largest request line accepted. Defaults to two kilobytes.
    /// </summary>
    public int MaxRequestLineBytes
    {
        get => maxRequestLineBytesField;
        set => Set(ref maxRequestLineBytesField, value);
    }

    private int maxRequestLineBytesField = 2048;

    /// <summary>
    /// Gets or sets how many sockets may be accepted at once. Defaults to thirty-two.
    /// </summary>
    public int MaxConnections
    {
        get => maxConnectionsField;
        set => Set(ref maxConnectionsField, value);
    }

    private int maxConnectionsField = 32;

    /// <summary>
    /// Gets or sets how many member invocations may be in flight. Defaults to eight.
    /// </summary>
    public int MaxConcurrentCalls
    {
        get => maxConcurrentCallsField;
        set => Set(ref maxConcurrentCallsField, value);
    }

    private int maxConcurrentCallsField = 8;

    /// <summary>
    /// Gets or sets how many calls may wait for a slot before the listener answers busy. Defaults to sixty-four.
    /// </summary>
    public int MaxQueuedCalls
    {
        get => maxQueuedCallsField;
        set => Set(ref maxQueuedCallsField, value);
    }

    private int maxQueuedCallsField = 64;

    /// <summary>
    /// Gets or sets how many event polls and held streams may run at once, counted together. Defaults to eight.
    /// </summary>
    public int MaxEventStreams
    {
        get => maxEventStreamsField;
        set => Set(ref maxEventStreamsField, value);
    }

    private int maxEventStreamsField = 8;

    /// <summary>
    /// Gets or sets how many events the events channel keeps before the oldest are dropped. Defaults to two hundred
    /// and fifty-six.
    /// </summary>
    public int EventBufferSize
    {
        get => eventBufferSizeField;
        set => Set(ref eventBufferSizeField, value);
    }

    private int eventBufferSizeField = 256;

    /// <summary>
    /// Gets or sets how many reports the progress channel keeps. Defaults to a hundred and twenty-eight.
    /// </summary>
    public int ProgressBufferSize
    {
        get => progressBufferSizeField;
        set => Set(ref progressBufferSizeField, value);
    }

    private int progressBufferSizeField = 128;

    /// <summary>
    /// Gets or sets how many lines the log channel keeps. Defaults to five hundred and twelve.
    /// </summary>
    public int LogBufferSize
    {
        get => logBufferSizeField;
        set => Set(ref logBufferSizeField, value);
    }

    private int logBufferSizeField = 2000;

    /// <summary>Gets or sets how many samples the metrics channel keeps. Defaults to 240, four minutes at the default interval.</summary>
    public int MetricsBufferSize
    {
        get => metricsBufferSizeField;
        set => Set(ref metricsBufferSizeField, value);
    }

    private int metricsBufferSizeField = 240;

    /// <summary>
    /// Gets or sets how many clauses one poll filter may carry. Defaults to sixteen.
    /// </summary>
    public int MaxFilterClauses
    {
        get => maxFilterClausesField;
        set => Set(ref maxFilterClausesField, value);
    }

    private int maxFilterClausesField = 16;

    /// <summary>
    /// Gets or sets how often a held stream writes a blank line. It notices a peer that vanished without closing.
    /// Defaults to fifteen seconds.
    /// </summary>
    public TimeSpan StreamKeepAlive
    {
        get => streamKeepAliveField;
        set => Set(ref streamKeepAliveField, value);
    }

    private TimeSpan streamKeepAliveField = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets how often a member progress report is published at most. Defaults to 250 milliseconds.
    /// </summary>
    public TimeSpan ProgressInterval
    {
        get => progressIntervalField;
        set => Set(ref progressIntervalField, value);
    }

    private TimeSpan progressIntervalField = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets which lines of the host's log the log channel carries: none, this plugin's (the default), or every
    /// line the host writes. The console's Dalamud Logs view reads it, and the channel keeps the most recent
    /// <see cref="LogBufferSize"/> lines so a page opened later still sees what came before it.
    /// </summary>
    public NoireRemoteLogScope LogScope
    {
        get => logScopeField;
        set => Set(ref logScopeField, value);
    }

    private NoireRemoteLogScope logScopeField = NoireRemoteLogScope.Plugin;

    /// <summary>
    /// Gets or sets whether what the listener serves is published onto the traffic channel as it serves it: one
    /// event per finished call, and one per connection opened or closed on a published socket. Defaults to true.
    /// Nothing is built while nothing is watching the channel. An unwatched listener pays nothing for it.
    /// </summary>
    public bool PublishTraffic
    {
        get => publishTrafficField;
        set => Set(ref publishTrafficField, value);
    }

    private bool publishTrafficField = true;

    /// <summary>
    /// Gets or sets whether each frame in and out of a published socket joins the traffic channel. Off by default:
    /// frames arrive at rate and carry whatever a caller sent, and a preview of that crosses to every watcher.
    /// Turning it on also puts a lock on the send path, since every frame then asks whether the channel is watched.
    /// </summary>
    public bool PublishTrafficFrames
    {
        get => publishTrafficFramesField;
        set => Set(ref publishTrafficFramesField, value);
    }

    private bool publishTrafficFramesField = false;

    /// <summary>
    /// Gets or sets how much of a frame a traffic event carries before it is cut. Defaults to 256 characters.
    /// </summary>
    public int MaxTrafficPreview
    {
        get => maxTrafficPreviewField;
        set => Set(ref maxTrafficPreviewField, value);
    }

    private int maxTrafficPreviewField = 256;

    /// <summary>
    /// Gets or sets how many traffic events the channel keeps before the oldest are dropped. Defaults to two
    /// hundred and fifty-six.
    /// </summary>
    public int TrafficBufferSize
    {
        get => trafficBufferSizeField;
        set => Set(ref trafficBufferSizeField, value);
    }

    private int trafficBufferSizeField = 256;

    /// <summary>
    /// Gets or sets how often the registered gauges are sampled while something is watching the metrics channel.
    /// Defaults to one second.
    /// </summary>
    public TimeSpan MetricsInterval
    {
        get => metricsIntervalField;
        set => Set(ref metricsIntervalField, value);
    }

    private TimeSpan metricsIntervalField = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether the file transfer routes serve. It is the only feature that writes to disk on behalf of
    /// a caller. It is off by default.
    /// </summary>
    public bool EnableFiles
    {
        get => enableFilesField;
        set => Set(ref enableFilesField, value);
    }

    private bool enableFilesField = false;

    /// <summary>
    /// Gets or sets the largest file a transfer may carry. Defaults to 256 megabytes.
    /// </summary>
    public long MaxFileBytes
    {
        get => maxFileBytesField;
        set => Set(ref maxFileBytesField, value);
    }

    private long maxFileBytesField = 256L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the size above which a transfer spools to disk. Below it, the transfer stays in memory.
    /// Defaults to four megabytes.
    /// </summary>
    public int MaxSpooledBytes
    {
        get => maxSpooledBytesField;
        set => Set(ref maxSpooledBytesField, value);
    }

    private int maxSpooledBytesField = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the folder a spooled transfer is written to.
    /// </summary>
    public string FileSpoolDirectory
    {
        get => fileSpoolDirectoryField;
        set => Set(ref fileSpoolDirectoryField, value);
    }

    private string fileSpoolDirectoryField = NoireRemoteDirectory.DefaultSpoolDirectory();

    /// <summary>
    /// Gets or sets how long an idle transfer is kept before it is dropped. Defaults to ten minutes.
    /// </summary>
    public TimeSpan FileRetention
    {
        get => fileRetentionField;
        set => Set(ref fileRetentionField, value);
    }

    private TimeSpan fileRetentionField = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets how many transfers may be open at once. Defaults to eight.
    /// </summary>
    public int MaxOpenTransfers
    {
        get => maxOpenTransfersField;
        set => Set(ref maxOpenTransfersField, value);
    }

    private int maxOpenTransfersField = 8;

    /// <summary>
    /// Gets or sets whether the browser console is served, on a socket of its own. Defaults to false.
    /// </summary>
    public bool EnableConsole
    {
        get => enableConsoleField;
        set => Set(ref enableConsoleField, value);
    }

    private bool enableConsoleField = false;

    /// <summary>
    /// Gets or sets the port the console socket binds to. Zero lets the operating system pick one.
    /// </summary>
    public int ConsolePort
    {
        get => consolePortField;
        set => Set(ref consolePortField, value);
    }

    private int consolePortField = 0;

    /// <summary>
    /// Gets or sets whether the console socket binds every address. That lets a phone on the same network open
    /// it. Defaults to false.
    /// </summary>
    public bool EnableRemoteConsole
    {
        get => enableRemoteConsoleField;
        set => Set(ref enableRemoteConsoleField, value);
    }

    private bool enableRemoteConsoleField = false;

    /// <summary>
    /// Gets or sets a file the console page is read from, in place of the one embedded in this assembly. Null
    /// uses the embedded page.
    /// </summary>
    public string? ConsolePagePath
    {
        get => consolePagePathField;
        set => Set(ref consolePagePathField, value);
    }

    private string? consolePagePathField = null;

    /// <summary>
    /// Gets or sets how much the console asks a browser for before it hands out a session.<br/>
    /// Defaults to <see cref="NoireRemoteConsoleAccess.LoopbackOpen"/>: a browser on this machine is asked for nothing.
    /// </summary>
    public NoireRemoteConsoleAccess ConsoleAccess
    {
        get => consoleAccessField;
        set => Set(ref consoleAccessField, value);
    }

    private NoireRemoteConsoleAccess consoleAccessField = NoireRemoteConsoleAccess.LoopbackOpen;

    /// <summary>
    /// Gets or sets the password a networked browser presents to open the console. Null generates one at start when
    /// <see cref="EnableRemoteConsole"/> is on, readable as <see cref="NoireRemoteConsole.Key"/>.
    /// </summary>
    public string? ConsoleKey
    {
        get => consoleKeyField;
        set => Set(ref consoleKeyField, value);
    }

    private string? consoleKeyField = null;

    /// <summary>
    /// Gets or sets whether the console remembers the last values typed into a member. Defaults to true.
    /// </summary>
    public bool RememberValues
    {
        get => rememberValuesField;
        set => Set(ref rememberValuesField, value);
    }

    private bool rememberValuesField = true;

    /// <summary>
    /// Gets or sets whether the console draws a code the owner scans to open it from a phone. Defaults to true.
    /// </summary>
    public bool EnableQrCode
    {
        get => enableQrCodeField;
        set => Set(ref enableQrCodeField, value);
    }

    private bool enableQrCodeField = true;

    /// <summary>
    /// Gets or sets whether another listener's console may list this one in its fleet and drive it. Off by default.<br/>
    /// Anyone reaching that console reaches this plugin through it. A program on this machine can still read the record and call this listener directly.
    /// </summary>
    public bool AllowFleetControl
    {
        get => allowFleetControlField;
        set => Set(ref allowFleetControlField, value);
    }

    private bool allowFleetControlField = false;

    /// <summary>
    /// Gets or sets whether the OpenAPI document route serves. Defaults to true.
    /// </summary>
    public bool EnableOpenApi
    {
        get => enableOpenApiField;
        set => Set(ref enableOpenApiField, value);
    }

    private bool enableOpenApiField = true;

    /// <summary>
    /// Gets or sets whether the listener answers a signed discovery probe on the local network. Nothing unsolicited
    /// ever leaves the host. A host nobody asks about stays silent. Defaults to false.
    /// </summary>
    public bool AnnounceOnNetwork
    {
        get => announceOnNetworkField;
        set => Set(ref announceOnNetworkField, value);
    }

    private bool announceOnNetworkField = false;

    /// <summary>
    /// Gets or sets the port discovery probes arrive on. Zero derives it from the secret. Nothing needs
    /// configuring on either side.
    /// </summary>
    public int DiscoveryPort
    {
        get => discoveryPortField;
        set => Set(ref discoveryPortField, value);
    }

    private int discoveryPortField = 0;

    /// <summary>
    /// Gets or sets how long one source address waits between answered probes. Defaults to one second.
    /// </summary>
    public TimeSpan DiscoveryAnswerCooldown
    {
        get => discoveryAnswerCooldownField;
        set => Set(ref discoveryAnswerCooldownField, value);
    }

    private TimeSpan discoveryAnswerCooldownField = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how many rejected credentials from one address trigger a block. Defaults to ten.
    /// </summary>
    public int FailedAuthLockoutThreshold
    {
        get => failedAuthLockoutThresholdField;
        set => Set(ref failedAuthLockoutThresholdField, value);
    }

    private int failedAuthLockoutThresholdField = 10;

    /// <summary>
    /// Gets or sets how long a blocked address stays blocked. Defaults to five minutes.
    /// </summary>
    public TimeSpan FailedAuthLockout
    {
        get => failedAuthLockoutField;
        set => Set(ref failedAuthLockoutField, value);
    }

    private TimeSpan failedAuthLockoutField = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the folder the instance record is written to.
    /// </summary>
    public string RegistryDirectory
    {
        get => registryDirectoryField;
        set => Set(ref registryDirectoryField, value);
    }

    private string registryDirectoryField = NoireRemoteDirectory.DefaultDirectory();

    /// <summary>
    /// Gets or sets whether the first publication binds the listener on its own. Turning it off means nothing opens a
    /// socket until <see cref="NoireRemote.Start()"/> runs.
    /// </summary>
    public bool AutoStart
    {
        get => autoStartField;
        set => Set(ref autoStartField, value);
    }

    private bool autoStartField = true;

    /// <summary>
    /// Gets or sets whether informational logging is written. Warnings and errors report regardless.
    /// </summary>
    public bool EnableLogging
    {
        get => enableLoggingField;
        set => Set(ref enableLoggingField, value);
    }

    private bool enableLoggingField = true;

    /// <summary>
    /// Gets or sets whether a refused <see cref="Port"/> is answered by binding any free one. Defaults to true:
    /// the instance record carries the port. A caller finds the listener whatever number it ends up on, and a
    /// listener that is up on the wrong port is worth more than one that is down on the right one. Set it false
    /// when the number itself is the contract, and <see cref="BindRetry"/> then waits for it.
    /// </summary>
    public bool FallBackToAnyPort
    {
        get => fallBackToAnyPortField;
        set => Set(ref fallBackToAnyPortField, value);
    }

    private bool fallBackToAnyPortField = true;

    /// <summary>
    /// Gets or sets how a refused bind is retried. Only consulted while <see cref="FallBackToAnyPort"/> is false,
    /// since a listener that fell back is already up. A plugin that reloads finds its own port still held by the
    /// connections the previous instance left behind, for as long as the operating system keeps them, and a listener
    /// that gave up there would stay down for minutes with nothing said. Set
    /// <see cref="NoireLib.Websocket.NoireRetryPolicy.None"/> to fail at the first refusal instead.
    /// </summary>
    public NoireLib.Websocket.NoireRetryPolicy BindRetry
    {
        get => bindRetryField;
        set => Set(ref bindRetryField, value);
    }

    private NoireLib.Websocket.NoireRetryPolicy bindRetryField = NoireLib.Websocket.NoireRetryPolicy.Default;

    // Raised with the name of the setting that changed. The server reconciles from it. No setting needs a restart.
    internal event Action<string?>? Changed;

    private bool quiet;
    private bool missed;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;

        if (quiet)
        {
            missed = true;

            return;
        }

        Changed?.Invoke(name);
    }

    /// <summary>
    /// Creates a shallow copy of these options. The allowed host list's elements are copied. The two objects
    /// never share one list instance.
    /// </summary>
    /// <returns>The copied options.</returns>
    public NoireRemoteOptions Clone()
    {
        var copy = new NoireRemoteOptions();
        copy.CopyFrom(this);

        return copy;
    }

    /// <summary>
    /// Overwrites every setting on this object with the one on another. The allowed host list's elements are
    /// replaced. The two objects never share one list instance.
    /// </summary>
    /// <param name="source">The options to read from.</param>
    /// <exception cref="ArgumentNullException">If the source is null.</exception>
    public void CopyFrom(NoireRemoteOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (ReferenceEquals(source, this))
            return;

        quiet = true;
        missed = false;

        try
        {
            // Every settable property is copied here. NoireRemoteOptionsTests fails when one is missing.
            BindAddress = source.BindAddress;
            Port = source.Port;
            EnableRemote = source.EnableRemote;
            RemoteSecret = source.RemoteSecret;
            EnableDualStack = source.EnableDualStack;
            PublishDirectoryRecord = source.PublishDirectoryRecord;
            PublishCharacterIdentity = source.PublishCharacterIdentity;
            EnableManifest = source.EnableManifest;
            SendExceptionType = source.SendExceptionType;
            SendStackTraces = source.SendStackTraces;
            DefaultThread = source.DefaultThread;
            DefaultCallTimeout = source.DefaultCallTimeout;
            MaxCallTimeout = source.MaxCallTimeout;
            HeaderReadTimeout = source.HeaderReadTimeout;
            JobRetention = source.JobRetention;
            RemoteClockSkew = source.RemoteClockSkew;
            RemoteNonceRetention = source.RemoteNonceRetention;
            HeartbeatInterval = source.HeartbeatInterval;
            StaleAfter = source.StaleAfter;
            MaxRequestBytes = source.MaxRequestBytes;
            MaxHeaderBytes = source.MaxHeaderBytes;
            MaxHeaderCount = source.MaxHeaderCount;
            MaxRequestLineBytes = source.MaxRequestLineBytes;
            MaxConnections = source.MaxConnections;
            MaxConcurrentCalls = source.MaxConcurrentCalls;
            MaxQueuedCalls = source.MaxQueuedCalls;
            MaxEventStreams = source.MaxEventStreams;
            EventBufferSize = source.EventBufferSize;
            ProgressBufferSize = source.ProgressBufferSize;
            LogBufferSize = source.LogBufferSize;
            MetricsBufferSize = source.MetricsBufferSize;
            MaxFilterClauses = source.MaxFilterClauses;
            StreamKeepAlive = source.StreamKeepAlive;
            ProgressInterval = source.ProgressInterval;
            LogScope = source.LogScope;
            PublishTraffic = source.PublishTraffic;
            PublishTrafficFrames = source.PublishTrafficFrames;
            MaxTrafficPreview = source.MaxTrafficPreview;
            TrafficBufferSize = source.TrafficBufferSize;
            MetricsInterval = source.MetricsInterval;
            EnableFiles = source.EnableFiles;
            MaxFileBytes = source.MaxFileBytes;
            MaxSpooledBytes = source.MaxSpooledBytes;
            FileSpoolDirectory = source.FileSpoolDirectory;
            FileRetention = source.FileRetention;
            MaxOpenTransfers = source.MaxOpenTransfers;
            EnableConsole = source.EnableConsole;
            ConsolePort = source.ConsolePort;
            EnableRemoteConsole = source.EnableRemoteConsole;
            ConsolePagePath = source.ConsolePagePath;
            ConsoleAccess = source.ConsoleAccess;
            ConsoleKey = source.ConsoleKey;
            RememberValues = source.RememberValues;
            EnableQrCode = source.EnableQrCode;
            AllowFleetControl = source.AllowFleetControl;
            EnableOpenApi = source.EnableOpenApi;
            AnnounceOnNetwork = source.AnnounceOnNetwork;
            DiscoveryPort = source.DiscoveryPort;
            DiscoveryAnswerCooldown = source.DiscoveryAnswerCooldown;
            FailedAuthLockoutThreshold = source.FailedAuthLockoutThreshold;
            FailedAuthLockout = source.FailedAuthLockout;
            RegistryDirectory = source.RegistryDirectory;
            AutoStart = source.AutoStart;
            EnableLogging = source.EnableLogging;
            BindRetry = source.BindRetry;
            FallBackToAnyPort = source.FallBackToAnyPort;

            if (ReferenceEquals(source.AllowedHosts, AllowedHosts))
                return;

            AllowedHosts.Clear();

            foreach (var host in source.AllowedHosts)
                AllowedHosts.Add(host);
        }
        finally
        {
            quiet = false;

            // Null means several settings changed at once.
            if (missed)
                Changed?.Invoke(null);
        }
    }
}
