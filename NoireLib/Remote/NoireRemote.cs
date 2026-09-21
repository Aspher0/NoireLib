using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Remote;

/// <summary>
/// Publishes a plugin's surface over HTTP so a program outside the game can drive it. A class carrying
/// <see cref="NoireRemoteClassAttribute"/> is published during <see cref="NoireLibMain.Initialize"/>. Anything else
/// is published by hand.<br/>
/// A published member runs on the framework thread by default. A member that blocks drops frames.
/// </summary>
public static class NoireRemote
{
    private const string DisposeKey = "NoireLib.Internal.NoireRemote.Dispose";

    private static readonly NoireRemoteServer Server = new(new NoireRemotePluginHost());

    private static bool disposeHookRegistered;

    static NoireRemote()
    {
        // A plugin resolving its own listener from the framework thread would deadlock.
        NoireRemoteClient.Options.ExcludeInstance = Server.InstanceId;
    }

    /// <summary>
    /// Gets the listener this plugin publishes through. A console, a fleet call and anything else taking a server
    /// takes this one.
    /// </summary>
    public static NoireRemoteServer Instance => Server;

    /// <summary>
    /// Gets the listener's settings. Change them before the listener starts. A running listener keeps the values it
    /// bound with.
    /// </summary>
    public static NoireRemoteOptions Options => Server.Options;

    /// <summary>
    /// Gets what this instance tags itself with. A caller outside the game aims at a tag. A tag is the only thing
    /// that works before a character is logged in.
    /// </summary>
    public static NoireRemoteSelf Self => Server.Self;

    /// <summary>
    /// Gets the instance that sent the event being handled, or null outside an event handler. It is the NoireRemote
    /// half of NoireIPC's caller identity.
    /// </summary>
    public static NoireRemoteInstance? Sender => RemoteConsumerBinder.Sender;

    /// <summary>
    /// Binds a type's consumer members to a published surface: a property of delegate or
    /// <see cref="NoireRemoteConsumer{TDelegate}"/> type calls a member, and an annotated event receives one.
    /// </summary>
    /// <param name="type">The type whose static consumer members are bound.</param>
    /// <param name="api">The surface name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A binding that releases everything it did when disposed.</returns>
    /// <exception cref="InvalidOperationException">If the type both publishes and consumes, or has nothing to bind.</exception>
    public static NoireRemoteBinding Bind(Type type, string? api = null)
        => RemoteConsumerBinder.Bind(type, null, api);

    /// <summary>
    /// Binds a live object's consumer members to a published surface.
    /// </summary>
    /// <param name="instance">The object whose members are bound.</param>
    /// <param name="api">The surface name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A binding that releases everything it did when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the instance is null.</exception>
    public static NoireRemoteBinding Bind(object instance, string? api = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return RemoteConsumerBinder.Bind(instance.GetType(), instance, api);
    }

    /// <summary>
    /// Gets the listener's state.
    /// </summary>
    public static NoireRemoteListenerState State => Server.State;

    /// <summary>
    /// Gets whether the listener is bound and accepting connections.
    /// </summary>
    public static bool IsListening => Server.IsListening;

    /// <summary>
    /// Gets the port the listener is bound to, or zero when it is not listening.
    /// </summary>
    public static int Port => Server.Port;

    /// <summary>
    /// Gets the id identifying this listener. It is generated once per launch and carried by every answer.
    /// </summary>
    public static Guid InstanceId => Server.InstanceId;

    /// <summary>
    /// Gets the loopback credential. It is generated per launch, written into the instance record and accepted on a
    /// loopback connection only.
    /// </summary>
    public static string Token => Server.Token;

    /// <summary>
    /// Gets or sets the label telling this game client from another in a caller's listing. It is refreshed from the
    /// character name and world while <see cref="NoireRemoteOptions.PublishCharacterIdentity"/> is set.
    /// </summary>
    public static string Label
    {
        get => Server.Label;
        set => Server.Label = value;
    }

    /// <summary>
    /// Gets the published endpoints.
    /// </summary>
    public static IReadOnlyList<NoireRemoteApiInfo> Endpoints => Server.Endpoints;

    /// <summary>
    /// Raised when the listener's state changes.
    /// </summary>
    public static event Action<NoireRemoteListenerState>? StateChanged
    {
        add => Server.StateChanged += value;
        remove => Server.StateChanged -= value;
    }

    /// <summary>
    /// Raised when a call finishes, on the thread that answered it.
    /// </summary>
    public static event Action<NoireRemoteCallReport>? CallCompleted
    {
        add => Server.CallCompleted += value;
        remove => Server.CallCompleted -= value;
    }

    /// <summary>
    /// Finds a published endpoint by name, ignoring case.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The endpoint, or null when nothing publishes it.</returns>
    public static NoireRemoteApiInfo? GetEndpoint(string name)
        => Server.GetEndpoint(name);

    /// <summary>
    /// Binds the listener with the settings on <see cref="Options"/>. Calling it twice is a no-op.
    /// </summary>
    public static void Start()
    {
        RegisterDisposeHook();
        NoireRemoteGameMetrics.Register();
        Server.Start();
    }

    /// <summary>
    /// Binds the listener with the settings given, copying them onto <see cref="Options"/>.
    /// </summary>
    /// <param name="options">The settings to bind with.</param>
    /// <exception cref="ArgumentNullException">If the options are null.</exception>
    public static void Start(NoireRemoteOptions options)
    {
        RegisterDisposeHook();
        NoireRemoteGameMetrics.Register();
        Server.Start(options);
    }

    /// <summary>
    /// Brings the running listener in line with <see cref="Options"/> without rebinding it or moving its port.
    /// Assigning a setting already does this. This is only needed after replacing several at once.
    /// </summary>
    public static void Apply()
        => Server.Apply();

    /// <summary>
    /// Closes the listener, removes the instance record and cancels every running job. The published surface is
    /// kept. A later <see cref="Start()"/> serves the same routes.
    /// </summary>
    public static void Stop()
        => Server.Stop();

    /// <summary>
    /// Replaces the loopback credential and rewrites the instance record. Every caller holding the old one is
    /// rejected once and re-reads the record.
    /// </summary>
    /// <returns>The credential now in force.</returns>
    public static string RotateToken()
        => Server.RotateToken();

    /// <summary>
    /// Publishes an event to every caller watching the topic. A caller collects it by polling the event route.
    /// </summary>
    /// <param name="topic">The topic, matched by prefix against a poll's topic list.</param>
    /// <param name="payload">The payload. It has to survive JSON the way a member's return value does.</param>
    /// <exception cref="ArgumentException">If the topic is null or blank.</exception>
    public static void PublishEvent(string topic, object? payload)
        => Server.PublishEvent(topic, payload);

    /// <summary>
    /// Publishes an event onto a named channel.
    /// </summary>
    /// <param name="channel">The channel. An undeclared name is declared on the spot at the default buffer size.</param>
    /// <param name="topic">The topic, matched by prefix against a poll topic list.</param>
    /// <param name="payload">The payload. It has to survive JSON the way a member return value does.</param>
    /// <exception cref="ArgumentException">If the channel or the topic is null or blank.</exception>
    public static void PublishEvent(string channel, string topic, object? payload)
        => Server.PublishEvent(channel, topic, payload);

    /// <summary>
    /// Declares a channel of your own. What you publish on it is bounded apart from everything else.
    /// </summary>
    /// <param name="name">The channel name. Declaring one twice keeps the first buffer size.</param>
    /// <param name="bufferSize">How many events the channel keeps before the oldest are dropped.</param>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public static void DeclareChannel(string name, int bufferSize)
        => Server.DeclareChannel(name, bufferSize);

    /// <summary>
    /// Reads the published surface as the manifest route serves it.
    /// </summary>
    /// <returns>The manifest. It is rebuilt when the route table changes and shared otherwise.</returns>
    public static NoireRemoteManifest Manifest()
        => Server.Manifest();

    /// <summary>
    /// Reports how far along the member running this job is. It does nothing outside a job.
    /// </summary>
    /// <param name="progress">A value between zero and one. It is clamped.</param>
    public static void ReportProgress(double progress)
        => NoireRemoteProgress.Report(progress);

    /// <summary>
    /// Publishes a live object's surface, taking the endpoint name from its type.
    /// </summary>
    /// <param name="instance">The object whose public methods are published.</param>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the instance is null.</exception>
    /// <exception cref="InvalidOperationException">If another type already claims the endpoint name, or the endpoint already publishes one of these members.</exception>
    public static NoireRemotePublication Publish(object instance, string? name = null)
    {
        RegisterDisposeHook();
        return Server.Publish(instance, name);
    }

    /// <summary>
    /// Publishes a type's static surface.
    /// </summary>
    /// <param name="type">The type whose public static methods are published.</param>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the type is null.</exception>
    /// <exception cref="InvalidOperationException">If another type already claims the endpoint name, or the endpoint already publishes one of these members.</exception>
    public static NoireRemotePublication PublishType(Type type, string? name = null)
    {
        RegisterDisposeHook();
        return Server.PublishType(type, name);
    }

    /// <summary>
    /// Publishes a type's static surface.
    /// </summary>
    /// <typeparam name="T">The type whose public static methods are published.</typeparam>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    public static NoireRemotePublication PublishType<T>(string? name = null)
    {
        RegisterDisposeHook();
        return Server.PublishType<T>(name);
    }

    /// <summary>
    /// Publishes one member from a delegate. This is the primitive the attributes are sugar over. A plugin building
    /// its route table from a file uses it directly.
    /// </summary>
    /// <param name="endpoint">The endpoint name.</param>
    /// <param name="member">The member name.</param>
    /// <param name="handler">The delegate to call. Its parameters and return type go through the same JSON check.</param>
    /// <param name="options">The per-member thread, readiness, mode, access and deadline. Null takes the listener defaults.</param>
    /// <returns>A handle that removes the published member when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the handler is null.</exception>
    /// <exception cref="InvalidOperationException">If the signature does not survive JSON, or the endpoint already publishes the member.</exception>
    public static NoireRemotePublication Publish(string endpoint, string member, Delegate handler, NoireRemoteMemberOptions? options = null)
    {
        RegisterDisposeHook();
        return Server.Publish(endpoint, member, handler, options);
    }

    /// <summary>
    /// Publishes every type in an assembly carrying <see cref="NoireRemoteClassAttribute"/>. This is what
    /// <see cref="NoireLibMain.Initialize"/> calls on the plugin's own assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The publications made, disposable together.</returns>
    /// <exception cref="ArgumentNullException">If the assembly is null.</exception>
    public static NoireRemoteGroup PublishAttributedTypes(Assembly assembly)
    {
        RegisterDisposeHook();
        return Server.PublishAttributedTypes(assembly);
    }

    internal static DateTime StartedUtc => Server.StartedUtc;

    private static void RegisterDisposeHook()
    {
        if (disposeHookRegistered)
            return;

        disposeHookRegistered = NoireLibMain.RegisterOnDispose(DisposeKey, DisposeAll);

        // A handler left on Dalamud's static sink would call into an unloaded plugin.
        if (disposeHookRegistered)
            DalamudLogTap.Start();
    }

    internal static void DisposeAll()
    {
        DalamudLogTap.Stop();
        NoireRemoteGameMetrics.Unregister();
        Server.Reset();
    }
}
