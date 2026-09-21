using NoireLib.Remote.Internal;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// One listener and the surface it serves. A plugin reaches it through <see cref="NoireRemote"/>. A program that is not
/// a plugin constructs one and owns it.
/// </summary>
public sealed partial class NoireRemoteServer : IDisposable
{
    private readonly object syncRoot = new();
    private readonly object manifestGate = new();
    private readonly HttpRouteTable routes = new();
    private readonly List<NoireRemotePublication> ownedPublications = [];
    private readonly Dictionary<string, int> declaredChannels = new(StringComparer.OrdinalIgnoreCase);

    private HttpServer? server;
    private RemoteCallDispatcher? dispatcher;
    private RemoteJobStore? jobs;
    private RemoteEventHub? events;
    private HttpDirectoryPublisher? directory;
    private RemoteMetricsSampler? metrics;
    private HttpFileStore? files;
    private HttpConsoleSocket? console;
    private HttpDiscoveryResponder? discovery;
    private HttpRouterContext? context;
    private NoireRemoteManifest? manifest;
    private int manifestVersion = -1;
    private string label = string.Empty;
    private bool disposed;
    private bool applying;

    // Reused on restart. A moving port breaks every saved script and open browser tab.
    private int lastBoundPort;
    private System.Threading.Timer? rebind;
    private Websocket.Internal.BackoffClock? rebindClock;
    private int lastConsolePort;
    private int consoleBoundPort;
    private bool consoleBoundRemote;

    /// <summary>Initializes a new instance of the <see cref="NoireRemoteServer"/> class under the standalone host. Members run on the thread that read the request, with no gate.</summary>
    public NoireRemoteServer() : this(new NoireRemoteStandaloneHost())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteServer"/> class under a host of your own.
    /// </summary>
    /// <param name="host">What supplies the identity, the thread and the readiness gate.</param>
    /// <exception cref="ArgumentNullException">If the host is null.</exception>
    public NoireRemoteServer(INoireRemoteHost host) : this(host, new NoireRemoteOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteServer"/> class under a host and a set of settings.
    /// </summary>
    /// <param name="host">What supplies the identity, the thread and the readiness gate.</param>
    /// <param name="options">The settings the listener binds with. They are held directly, without a copy.</param>
    /// <exception cref="ArgumentNullException">If the host or the options are null.</exception>
    public NoireRemoteServer(INoireRemoteHost host, NoireRemoteOptions options)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Console = new NoireRemoteConsole(this);
        Options.Changed += OnOptionsChanged;
    }

    /// <summary>
    /// Gets what supplies the identity, the thread and the readiness gate.
    /// </summary>
    public INoireRemoteHost Host { get; }

    /// <summary>
    /// Gets the browser console this listener serves. It is bound only while
    /// <see cref="NoireRemoteOptions.EnableConsole"/> is set.
    /// </summary>
    public NoireRemoteConsole Console { get; private set; } = null!;

    /// <summary>
    /// Gets the listener's settings. Assigning one while the listener runs takes effect on the spot: a setting that
    /// decides which sockets and services exist reconciles itself, and nothing rebinds the listener or moves its port.
    /// The five that name the socket itself are the exception, and they say so.
    /// </summary>
    public NoireRemoteOptions Options { get; }

    /// <summary>
    /// Gets what this instance tags itself with. A caller can aim at a tag as well as at a character name. A tag
    /// is the only thing that works before a character is logged in.
    /// </summary>
    public NoireRemoteSelf Self { get; } = new();

    /// <summary>
    /// Gets the listener's state.
    /// </summary>
    public NoireRemoteListenerState State { get; private set; } = NoireRemoteListenerState.Stopped;

    /// <summary>
    /// Gets whether the listener is bound and accepting connections.
    /// </summary>
    public bool IsListening => State == NoireRemoteListenerState.Listening;

    /// <summary>
    /// Gets the port the listener is bound to, or zero when it is not listening.
    /// </summary>
    public int Port => server?.Port ?? 0;

    /// <summary>
    /// Gets the id identifying this listener. It is generated once per launch and carried by every answer.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets the loopback credential. It is generated per launch, written into the instance record and accepted on a
    /// loopback connection only.
    /// </summary>
    public string Token { get; private set; } = NoireRemoteSignature.CreateToken();

    /// <summary>
    /// Gets or sets the label telling this host from another in a caller's listing. It is refreshed from the host
    /// while <see cref="NoireRemoteOptions.PublishCharacterIdentity"/> is set.
    /// </summary>
    public string Label
    {
        get => label;
        set => label = value ?? string.Empty;
    }

    /// <summary>
    /// Gets the published endpoints.
    /// </summary>
    public IReadOnlyList<NoireRemoteApiInfo> Endpoints => routes.Snapshot();

    /// <summary>
    /// Raised when the listener's state changes.
    /// </summary>
    public event Action<NoireRemoteListenerState>? StateChanged;

    /// <summary>
    /// Raised when a call finishes, on the thread that answered it.
    /// </summary>
    public event Action<NoireRemoteCallReport>? CallCompleted;

    /// <summary>
    /// Finds a published endpoint by name, ignoring case.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The endpoint, or null when nothing publishes it.</returns>
    public NoireRemoteApiInfo? GetEndpoint(string name)
    {
        foreach (var endpoint in routes.Snapshot())
        {
            if (string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                return endpoint;
        }

        return null;
    }

    /// <summary>
    /// Binds the listener with the settings on <see cref="Options"/>. Calling it twice is a no-op.
    /// </summary>
    /// <exception cref="ObjectDisposedException">If the server has been disposed.</exception>
    public void Start()
        => Start(Options);

    /// <summary>
    /// Binds the listener with the settings given, copying them onto <see cref="Options"/>.
    /// </summary>
    /// <param name="options">The settings to bind with.</param>
    /// <exception cref="ArgumentNullException">If the options are null.</exception>
    /// <exception cref="ObjectDisposedException">If the server has been disposed.</exception>
    public void Start(NoireRemoteOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        lock (syncRoot)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NoireRemoteServer));

            if (server != null)
                return;

            Options.CopyFrom(options);

            if (Options.EnableRemote && string.IsNullOrWhiteSpace(Options.RemoteSecret))
            {
                Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] remote access needs a secret. Staying on loopback.", null);
                Options.EnableRemote = false;
            }

            SetState(NoireRemoteListenerState.Starting);

            try
            {
                StartedUtc = DateTime.UtcNow;
                dispatcher = new RemoteCallDispatcher(Options, Host);
                jobs = new RemoteJobStore(Options.JobRetention);
                files = new HttpFileStore(Options, Host);

                var hub = Channels;
                var identity = Host.Identity;

                context = new HttpRouterContext
                {
                    Routes = routes,
                    Dispatcher = dispatcher,
                    Jobs = jobs,
                    Files = files,
                    Events = hub,
                    Options = Options,
                    Host = Host,
                    Instance = InstanceId,
                    Plugin = identity.Name,
                    PluginVersion = identity.Version,
                    StartedUtc = StartedUtc,
                    Manifest = BuildManifest,
                    BaseUrl = () => server == null ? null : NoireRemotePaths.BaseUrl("127.0.0.1", server.Port).TrimEnd('/'),
                    Label = () => Label,
                    Metadata = () => Self.All,
                    Token = () => Token,
                    Report = report => CallCompleted?.Invoke(report),
                };

                server = Bind();
                lastBoundPort = server.Port;

                if (Options.PublishDirectoryRecord)
                {
                    directory = new HttpDirectoryPublisher(Options, InstanceId, BuildRecord, Host);
                    directory.Start();

                    Self.Changed -= OnSelfChanged;
                    Self.Changed += OnSelfChanged;
                }

                PublishApiSocket();

                metrics = new RemoteMetricsSampler(context);
                NoireRemoteLog.Attach(this);

                if (Options.EnableConsole)
                    StartConsole();

                if (Options.AnnounceOnNetwork)
                {
                    discovery = new HttpDiscoveryResponder(Options, Host, InstanceId);
                    discovery.Start();
                }

                var waited = rebindClock != null;

                CancelRebind();
                SetState(NoireRemoteListenerState.Listening);

                if (waited)
                    Host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] port " + server.Port + " is free. Listening on it.", null);
                else if (Options.EnableLogging)
                    Host.Log(NoireRemoteLogLevel.Info, "[NoireRemote] listening on " + server.BoundAddress + ":" + server.Port, null);
            }
            catch (System.Net.Sockets.SocketException exception)
            {
                StopInternal();
                SetState(NoireRemoteListenerState.Failed);
                ScheduleRebind(exception);
            }
            catch (Exception exception)
            {
                Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] could not bind the listener", exception);
                StopInternal();
                SetState(NoireRemoteListenerState.Failed);
            }
        }
    }

    /// <summary>
    /// Closes the listener, removes the instance record and cancels every running job. The published surface is kept,
    /// so a later <see cref="Start()"/> serves the same routes.
    /// </summary>
    public void Stop()
    {
        lock (syncRoot)
        {
            CancelRebind();

            if (server == null && State == NoireRemoteListenerState.Stopped)
                return;

            StopInternal();
            SetState(NoireRemoteListenerState.Stopped);
        }
    }

    /// <summary>
    /// Replaces the loopback credential and rewrites the instance record. Every caller holding the old one is
    /// rejected once and re-reads the record.
    /// </summary>
    /// <returns>The credential now in force.</returns>
    public string RotateToken()
    {
        lock (syncRoot)
        {
            Token = NoireRemoteSignature.CreateToken();

            if (directory != null && Options.PublishDirectoryRecord)
                NoireRemoteDirectory.Write(Options.RegistryDirectory, BuildRecord());

            return Token;
        }
    }

    /// <summary>
    /// Publishes an event to every caller watching the topic. A caller collects it by polling the event route.
    /// </summary>
    /// <param name="topic">The topic, matched by prefix against a poll's topic list.</param>
    /// <param name="payload">The payload. It has to survive JSON the way a member's return value does.</param>
    /// <exception cref="ArgumentException">If the topic is null or blank.</exception>
    public void PublishEvent(string topic, object? payload)
        => Channels.Publish(NoireRemoteChannels.Events, topic, payload);

    /// <summary>
    /// Publishes an event onto a named channel.
    /// </summary>
    /// <param name="channel">The channel. An undeclared name is declared on the spot at the default buffer size.</param>
    /// <param name="topic">The topic, matched by prefix against a poll topic list.</param>
    /// <param name="payload">The payload. It has to survive JSON the way a member return value does.</param>
    /// <exception cref="ArgumentException">If the channel or the topic is null or blank.</exception>
    public void PublishEvent(string channel, string topic, object? payload)
        => Channels.Publish(channel, topic, payload);

    /// <summary>
    /// Declares a channel of your own. What you publish on it is bounded apart from everything else.
    /// </summary>
    /// <param name="name">The channel name. Declaring one twice keeps the first buffer size.</param>
    /// <param name="bufferSize">How many events the channel keeps before the oldest are dropped.</param>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public void DeclareChannel(string name, int bufferSize)
    {
        declaredChannels[name] = Math.Max(1, bufferSize);
        events?.Declare(name, bufferSize);
    }

    /// <summary>
    /// Reads the published surface as the manifest route serves it. It is what the OpenAPI document and the
    /// generated stubs are built from, and it needs no listener.
    /// </summary>
    /// <returns>The manifest. It is rebuilt when the route table changes and shared otherwise.</returns>
    public NoireRemoteManifest Manifest()
        => BuildManifest();

    /// <summary>
    /// Gets when the listener last started.
    /// </summary>
    public DateTime StartedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Takes the server back to the state it launched in: no publication, no listener, no record. It stays usable.
    /// </summary>
    public void Reset()
    {
        NoireRemotePublication[] publications;

        lock (syncRoot)
            publications = [.. ownedPublications];

        foreach (var publication in publications)
            publication.Dispose();

        Self.Changed -= OnSelfChanged;
        Self.Clear();

        lock (syncRoot)
        {
            StopInternal();
            events?.Dispose();
            events = null;
            declaredChannels.Clear();
            routes.Clear();
            ownedPublications.Clear();

            // A reset forgets the port. A stop keeps it.
            CancelRebind();
            lastBoundPort = 0;
            lastConsolePort = 0;
            State = NoireRemoteListenerState.Stopped;
        }
    }

    /// <summary>
    /// Resets the server and refuses any later start.
    /// </summary>
    public void Dispose()
    {
        Options.Changed -= OnOptionsChanged;

        Reset();

        lock (syncRoot)
            disposed = true;
    }

    // Never creates the hub. The send path reads this per frame.
    internal RemoteEventHub? ChannelsIfAny => events;

    internal RemoteEventHub Channels
    {
        get
        {
            var existing = events;

            if (existing != null)
                return existing;

            lock (syncRoot)
            {
                if (events != null)
                    return events;

                var hub = new RemoteEventHub(Options);

                foreach (var pair in declaredChannels)
                    hub.Declare(pair.Key, pair.Value);

                events = hub;

                return hub;
            }
        }
    }

    // Asks for the last bound port. The operating system picks a new one when it has been taken.
    private HttpServer Bind()
    {
        var wanted = Options.Port > 0 ? Options.Port : lastBoundPort;

        if (wanted > 0)
        {
            var sticky = new HttpServer(Options, context!, Token, Host, new HttpSecurityGate(Host), null, Options.EnableRemote, wanted);

            try
            {
                sticky.Start();

                return sticky;
            }
            catch (System.Net.Sockets.SocketException)
            {
                sticky.Dispose();

                // A refusal here means another program is accepting on the port. Leftover connections do not block a bind.
                if (Options.Port > 0 && !Options.FallBackToAnyPort)
                    throw;

                Host.Log(
                    NoireRemoteLogLevel.Warning,
                    "[NoireRemote] port " + wanted + " is in use: " + WhatHolds(wanted) + ". Taking any free port instead.",
                    null);
            }
        }

        var bound = new HttpServer(Options, context!, Token, Host, new HttpSecurityGate(Host), null, Options.EnableRemote, 0);

        bound.Start();

        return bound;
    }

    // A refused bind usually means connections left by a reloaded plugin, gone within minutes. Retries on the BindRetry schedule.
    private void ScheduleRebind(Exception reason)
    {
        var policy = Options.BindRetry ?? Websocket.NoireRetryPolicy.None;

        rebindClock ??= new Websocket.Internal.BackoffClock(policy);

        if (!rebindClock.ShouldRetry)
        {
            Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] could not bind the listener", reason);
            rebindClock = null;

            return;
        }

        var delay = rebindClock.Next();
        var port = Options.Port > 0 ? Options.Port : lastBoundPort;

        // Names what holds the port. A second listener and leftover connections need opposite responses.
        Host.Log(
            NoireRemoteLogLevel.Warning,
            "[NoireRemote] port " + port + " is in use: " + WhatHolds(port) + ". Trying again in "
                + Math.Max(1, (int)Math.Round(delay.TotalSeconds)) + " s.",
            null);

        rebind?.Dispose();
        rebind = new System.Threading.Timer(static state => ((NoireRemoteServer)state!).RetryBind(), this, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private static string WhatHolds(int port)
    {
        try
        {
            foreach (var listener in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (listener.Port == port)
                    return "another listener is accepting on " + listener.Address;
            }

            var connections = 0;

            foreach (var connection in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
            {
                if (connection.LocalEndPoint.Port == port)
                    connections++;
            }

            return connections > 0
                ? connections + " connection(s) from a previous listener are still closing"
                : "nothing this process can see holds it";
        }
        catch (Exception)
        {
            return "the reason could not be read";
        }
    }

    private void RetryBind()
    {
        lock (syncRoot)
        {
            if (disposed || server != null || State != NoireRemoteListenerState.Failed)
                return;
        }

        Start();
    }

    private void CancelRebind()
    {
        rebind?.Dispose();
        rebind = null;
        rebindClock = null;
    }

    private void StartConsole()
    {
        console = new HttpConsoleSocket(this, context!);
        console.Start(lastConsolePort);
        Console.Socket = console;
        consoleBoundPort = Options.ConsolePort;
        consoleBoundRemote = Options.EnableRemoteConsole;
        lastConsolePort = console.Port;
    }

    private void StopConsole()
    {
        Console.Socket = null;
        console?.Dispose();
        console = null;
    }

    private static readonly HashSet<string> Reconciled = new(StringComparer.Ordinal)
    {
        nameof(NoireRemoteOptions.BindAddress), nameof(NoireRemoteOptions.Port), nameof(NoireRemoteOptions.EnableRemote),
        nameof(NoireRemoteOptions.RemoteSecret), nameof(NoireRemoteOptions.EnableDualStack),
        nameof(NoireRemoteOptions.PublishDirectoryRecord), nameof(NoireRemoteOptions.PublishCharacterIdentity),
        nameof(NoireRemoteOptions.EnableConsole), nameof(NoireRemoteOptions.ConsolePort),
        nameof(NoireRemoteOptions.EnableRemoteConsole), nameof(NoireRemoteOptions.AnnounceOnNetwork),
        nameof(NoireRemoteOptions.DiscoveryPort), nameof(NoireRemoteOptions.AllowFleetControl),
    };

    private void OnOptionsChanged(string? name)
    {
        if (name == null || Reconciled.Contains(name))
            Apply();

        InvalidateConsole(ConsoleScopes.Settings);
    }

    // Coalesced: a burst of setting changes or connections produces one redraw.
    internal static class ConsoleScopes
    {
        public const string Settings = "settings";
        public const string Sockets = "sockets";
        public const string Fleet = "fleet";
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> pendingInvalidations = new(StringComparer.Ordinal);

    internal void InvalidateConsole(string scope)
    {
        var socket = console;

        if (socket == null || !socket.HasLiveSessions || !pendingInvalidations.TryAdd(scope, 0))
            return;

        _ = Task.Delay(100).ContinueWith(_ =>
        {
            pendingInvalidations.TryRemove(scope, out byte _);
            console?.Invalidate(scope);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Brings the running listener in line with <see cref="Options"/> without rebinding it.<br/>
    /// A setting that decides whether a service exists is started or stopped, and the console socket rebinds on
    /// its own port if its settings moved. <see cref="NoireRemoteOptions.BindAddress"/>,
    /// <see cref="NoireRemoteOptions.Port"/>, <see cref="NoireRemoteOptions.EnableRemote"/>,
    /// <see cref="NoireRemoteOptions.RemoteSecret"/> and <see cref="NoireRemoteOptions.EnableDualStack"/> name the
    /// socket itself and take effect only at the next <see cref="Start()"/>. Call this yourself only after several
    /// settings changed at once through <see cref="NoireRemoteOptions.CopyFrom"/>. A single assignment calls it
    /// automatically.
    /// </summary>
    public void Apply()
    {
        lock (syncRoot)
        {
            if (disposed || applying || server == null || context == null || State != NoireRemoteListenerState.Listening)
                return;

            applying = true;

            try
            {
                ApplyInternal();
            }
            catch (Exception exception)
            {
                Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a setting could not be applied", exception);
            }
            finally
            {
                applying = false;
            }
        }
    }

    private void ApplyInternal()
    {
        if (Options.EnableConsole && console == null)
        {
            StartConsole();
        }
        else if (!Options.EnableConsole && console != null)
        {
            StopConsole();
        }
        else if (console != null && (consoleBoundPort != Options.ConsolePort || consoleBoundRemote != Options.EnableRemoteConsole))
        {
            StopConsole();
            StartConsole();
        }

        if (Options.AnnounceOnNetwork && discovery == null)
        {
            discovery = new HttpDiscoveryResponder(Options, Host, InstanceId);
            discovery.Start();
        }
        else if (!Options.AnnounceOnNetwork && discovery != null)
        {
            discovery.Dispose();
            discovery = null;
        }

        if (Options.PublishDirectoryRecord && directory == null)
        {
            directory = new HttpDirectoryPublisher(Options, InstanceId, BuildRecord, Host);
            directory.Start();

            Self.Changed -= OnSelfChanged;
            Self.Changed += OnSelfChanged;
        }
        else if (!Options.PublishDirectoryRecord && directory != null)
        {
            Self.Changed -= OnSelfChanged;
            directory.Dispose();
            directory = null;
        }
        else if (directory != null)
        {
            RefreshRecord();
        }

        if (Options.Port > 0 && Options.Port != server!.Port)
            Host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] the port is bound at " + server.Port + "; " + Options.Port + " takes effect at the next Start", null);

        AnnounceSurface();
    }

    private void StopInternal()
    {
        NoireRemoteLog.Detach(this);

        discovery?.Dispose();
        discovery = null;

        StopConsole();

        metrics?.Dispose();
        metrics = null;

        directory?.Dispose();
        directory = null;

        server?.Dispose();
        server = null;

        jobs?.Dispose();
        jobs = null;

        files?.Dispose();
        files = null;

        dispatcher?.Dispose();
        dispatcher = null;

        context = null;
    }

    private void SetState(NoireRemoteListenerState next)
    {
        if (State == next)
            return;

        State = next;

        try
        {
            StateChanged?.Invoke(next);
        }
        catch (Exception exception)
        {
            Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a state handler threw", exception);
        }
    }

    private NoireRemoteManifest BuildManifest()
    {
        // Sockets count into the version. Publishing a socket changes the surface without touching the route table.
        lock (manifestGate)
        {
            var version = routes.Version + Websocket.Internal.WebsocketDiscovery.Revision(InstanceId);

            if (manifest != null && manifestVersion == version)
                return manifest;

            var identity = Host.Identity;
            manifest = RemoteManifestBuilder.Build(routes, InstanceId, identity.Name, identity.Version, Options, Channels.Describe());
            manifestVersion = version;

            return manifest;
        }
    }

    private NoireRemoteInstanceRecord BuildRecord()
    {
        if (Options.PublishCharacterIdentity)
            Host.RefreshLabel(value => label = value ?? string.Empty);
        else
            label = string.Empty;

        var identity = Host.Identity;

        return HttpDirectoryPublisher.BuildRecord(
            InstanceId,
            server?.BoundAddress.Equals(System.Net.IPAddress.Any) == true
                ? NoireRemoteAddress.LocalAddress()
                : server?.BoundAddress.ToString() ?? "127.0.0.1",
            Port,
            Token,
            StartedUtc,
            identity.Name,
            identity.Version,
            identity.LibraryVersion,
            Label,
            routes.EndpointNames(),
            Environment.MachineName,
            NoireRemoteDiscovery.SecretId(Options.RemoteSecret),
            Self.All,
            Options.AllowFleetControl);
    }

    private void OnSelfChanged(System.Collections.Generic.IReadOnlyDictionary<string, string> tags)
    {
        RefreshRecord();

        if (State == NoireRemoteListenerState.Listening)
            Channels.Publish(RemoteSubscriptionTable.ReservedPrefix + "metadata", tags);
    }

    internal void RefreshRecord()
    {
        if (!Options.PublishDirectoryRecord || State != NoireRemoteListenerState.Listening)
            return;

        NoireRemoteDirectory.Write(Options.RegistryDirectory, BuildRecord());
    }

    internal void AnnounceSurface()
    {
        if (State != NoireRemoteListenerState.Listening)
            return;

        var manifest = BuildManifest();

        Channels.Publish(RemoteSubscriptionTable.ReservedPrefix + "surface", new NoireRemoteSurfaceChange
        {
            Revision = manifest.Revision,
            Endpoints = manifest.Endpoints.Count,
            Sockets = manifest.Sockets?.Count ?? 0,
        });
    }

    private void PublishApiSocket()
    {
        if (context == null)
            return;

        var sockets = Websocket.NoireWebsocketServer.For(this);

        // The websocket server is cached per instance and outlives a stop. An endpoint from an earlier start holds a disposed context.
        sockets.GetEndpoint(NoireRemotePaths.ApiSocket)?.Dispose();

        var dispatch = new RemoteSocketDispatcher(context);
        // The drain only reads frames. The dispatcher applies each member's thread and readiness per call.
        var endpoint = sockets.PublishReserved(NoireRemotePaths.ApiSocket, new Websocket.NoireWebsocketEndpointOptions
        {
            Thread = NoireRemoteThread.Background,
            Requires = NoireRemoteReadiness.None,
        });

        endpoint.OnClose((client, close) => dispatch.Subscriptions.RemoveAll(client.Id));

        dispatch.Push = (connection, pushed) =>
        {
            foreach (var client in endpoint.Clients)
            {
                if (client.Id == connection)
                    client.Send(pushed.Write());
            }
        };

        // Never blocks. The publisher is often the framework thread. The send queue handles a slow connection.
        Channels.SocketPush = (topic, data) =>
        {
            var frame = new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Event,
                Member = topic,
                Payload = data,
            }.Write();

            foreach (var client in endpoint.Clients)
            {
                if (dispatch.Subscriptions.Matches(client.Id, topic))
                    client.Send(frame);
            }
        };

        endpoint.OnMessage(async (client, message) =>
        {
            NoireRemoteFrame frame;

            try
            {
                frame = NoireRemoteFrame.Parse(message.Text ?? string.Empty);
            }
            catch (Exception exception)
            {
                client.Send(dispatch.Malformed(exception).Write());
                return;
            }

            var answer = await dispatch.HandleAsync(client.Id, frame, System.Threading.CancellationToken.None).ConfigureAwait(false);

            if (answer != null)
                client.Send(answer.Write());
        });
    }
}
