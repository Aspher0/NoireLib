using NoireLib.Core.Reflection;
using NoireLib.Remote.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Remote;

public sealed partial class NoireRemoteServer
{
    /// <summary>
    /// Publishes a live object's surface, taking the endpoint name from its type.
    /// </summary>
    /// <param name="instance">The object whose public methods are published.</param>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the instance is null.</exception>
    /// <exception cref="InvalidOperationException">If another type already claims the endpoint name, or the endpoint already publishes one of these members.</exception>
    public NoireRemotePublication Publish(object instance, string? name = null)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        return PublishCore(instance.GetType(), instance, name);
    }

    /// <summary>
    /// Publishes a type's static surface.
    /// </summary>
    /// <param name="type">The type whose public static methods are published.</param>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the type is null.</exception>
    /// <exception cref="InvalidOperationException">If another type already claims the endpoint name, or the endpoint already publishes one of these members.</exception>
    public NoireRemotePublication PublishType(Type type, string? name = null)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        return PublishCore(type, null, name);
    }

    /// <summary>
    /// Publishes a type's static surface.
    /// </summary>
    /// <typeparam name="T">The type whose public static methods are published.</typeparam>
    /// <param name="name">The endpoint name. Null takes it from the type's attribute, then from the type name.</param>
    /// <returns>A handle that removes the published members when disposed.</returns>
    public NoireRemotePublication PublishType<T>(string? name = null)
        => PublishCore(typeof(T), null, name);

    /// <summary>
    /// Publishes one member from a delegate. This is the primitive the attributes are sugar over. A host building its
    /// route table from a file uses it directly.
    /// </summary>
    /// <param name="endpoint">The endpoint name.</param>
    /// <param name="member">The member name.</param>
    /// <param name="handler">The delegate to call. Its parameters and return type go through the same JSON check.</param>
    /// <param name="options">The per-member thread, readiness, mode, access and deadline. Null takes the listener defaults.</param>
    /// <returns>A handle that removes the published member when disposed.</returns>
    /// <exception cref="ArgumentNullException">If the handler is null.</exception>
    /// <exception cref="InvalidOperationException">If the signature does not survive JSON, or the endpoint already publishes the member.</exception>
    public NoireRemotePublication Publish(string endpoint, string member, Delegate handler, NoireRemoteMemberOptions? options = null)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("An endpoint name is required.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(member))
            throw new ArgumentException("A member name is required.", nameof(member));

        if (!NoireRemotePaths.IsValidName(member))
            throw new InvalidOperationException("'" + member + "' is not a usable member name.");

        var memberOptions = options ?? new NoireRemoteMemberOptions();

        var info = RemoteTypeScanner.Build(
            handler.Method,
            handler.Target,
            endpoint,
            member,
            new NoireRemoteAttribute
            {
                Thread = memberOptions.Thread,
                Requires = memberOptions.Requires,
                Access = memberOptions.Access,
                Mode = memberOptions.Mode,
                TimeoutSeconds = memberOptions.Timeout.TotalSeconds,
            },
            null,
            Options,
            Host,
            out var reason);

        if (info == null)
            throw new InvalidOperationException("'" + endpoint + "/" + member + "' cannot be published: " + reason + ".");

        return Register(
            endpoint,
            handler.Method.DeclaringType ?? typeof(object),
            RemoteTypeScanner.ResolveAccess(memberOptions.Access, NoireRemoteAccess.Inherit),
            [info],
            false,
            null,
            null);
    }

    /// <summary>
    /// Publishes every type in an assembly carrying <see cref="NoireRemoteClassAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The publications made, disposable together.</returns>
    /// <exception cref="ArgumentNullException">If the assembly is null.</exception>
    public NoireRemoteGroup PublishAttributedTypes(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var publications = new List<NoireRemotePublication>();

        foreach (var type in AttributedTypes.WithAttribute<NoireRemoteClassAttribute>(assembly))
        {
            if (type.IsInterface)
                continue;

            try
            {
                publications.Add(PublishCore(type, null, null));
            }
            catch (Exception exception)
            {
                Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] " + type.FullName + " was not published", exception);
            }
        }

        return new NoireRemoteGroup(publications);
    }

    private NoireRemotePublication PublishCore(Type type, object? target, string? name)
    {
        var attribute = type.GetCustomAttribute<NoireRemoteClassAttribute>();
        var endpoint = RemoteTypeScanner.EndpointNameOf(type, attribute, name);
        var members = RemoteTypeScanner.Scan(type, target, endpoint, attribute, Options, Host);
        var events = RemoteEventScanner.Scan(type, target, endpoint, PublishEvent, Host);

        if (members.Count == 0 && events.Count == 0)
            Host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] " + type.FullName + " publishes the endpoint '" + endpoint + "' with no member.", null);

        var access = RemoteTypeScanner.ResolveAccess(NoireRemoteAccess.Inherit, attribute?.Access ?? NoireRemoteAccess.Inherit);

        return Register(endpoint, type, access, members, true, events, attribute?.Description);
    }

    private NoireRemotePublication Register(
        string endpoint,
        Type declaringType,
        NoireRemoteAccess access,
        IReadOnlyList<NoireRemoteMemberInfo> members,
        bool fromType,
        IReadOnlyList<NoireRemoteEventInfo>? events,
        string? summary)
    {
        routes.Add(endpoint, declaringType, access, members, fromType, events, summary);

        var publication = new NoireRemotePublication(endpoint, members, Unregister, events);

        lock (syncRoot)
            ownedPublications.Add(publication);

        if (Options.AutoStart && (members.Count > 0 || (events != null && events.Count > 0)))
            Start();

        // The record lists the published surfaces. Written now, ahead of the next heartbeat.
        RefreshRecord();
        AnnounceSurface();

        return publication;
    }

    private void Unregister(NoireRemotePublication publication)
    {
        routes.Remove(publication.Name, publication.Members, publication.Events);
        RefreshRecord();
        AnnounceSurface();

        lock (syncRoot)
            ownedPublications.Remove(publication);
    }
}
