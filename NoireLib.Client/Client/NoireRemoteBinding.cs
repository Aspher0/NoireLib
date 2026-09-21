using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace NoireLib.Remote;

/// <summary>
/// What a bound consumer type holds: the client it calls through, the properties it filled and the events it
/// subscribed. Disposing it takes all of that back.
/// </summary>
public sealed class NoireRemoteBinding : IDisposable
{
    private readonly List<Action> undo = [];
    private readonly NoireRemoteClient client;

    internal NoireRemoteBinding(NoireRemoteClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Gets the surface the bound type calls.
    /// </summary>
    public string Api => client.Api;

    internal void Add(Action release) => undo.Add(release);

    /// <summary>
    /// Releases every property and subscription this binding made.
    /// </summary>
    public void Dispose()
    {
        foreach (var release in undo)
        {
            try
            {
                release();
            }
            catch (Exception)
            {
            }
        }

        undo.Clear();
        client.Dispose();
    }
}

// Binds a type's consumer members: a delegate or NoireRemoteConsumer property calls a member, an event receives one.
internal static class RemoteConsumerBinder
{
    private static readonly AsyncLocal<NoireRemoteInstance?> CurrentSender = new();

    internal static NoireRemoteInstance? Sender
    {
        get => CurrentSender.Value;
        set => CurrentSender.Value = value;
    }

    internal static NoireRemoteBinding Bind(Type type, object? target, string? api)
    {
        ArgumentNullException.ThrowIfNull(type);

        var attribute = type.GetCustomAttribute<NoireRemoteClassAttribute>();
        var name = RemoteNameOf(type, attribute, api);
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                    | (target == null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);

        var properties = type.GetProperties(flags)
            .Where(property => property.GetCustomAttribute<NoireRemoteAttribute>() != null)
            .ToArray();

        var events = type.GetEvents(flags)
            .Where(member => member.GetCustomAttribute<NoireRemoteAttribute>() != null)
            .ToArray();

        var publishing = type.GetMethods(flags)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NoireRemoteAttribute>() != null)
            .ToArray();

        if (publishing.Length > 0 && properties.Length > 0)
        {
            throw new InvalidOperationException(
                "'" + type.FullName + "' both publishes and consumes: " + string.Join(", ", publishing.Select(method => method.Name))
                + " publish, while " + string.Join(", ", properties.Select(property => property.Name))
                + " consume. Split them into two types.");
        }

        if (publishing.Length > 0 || (properties.Length == 0 && events.Length == 0))
        {
            throw new InvalidOperationException(
                "'" + type.FullName + "' has nothing to bind. A type that publishes goes to NoireRemote.Publish.");
        }

        var client = new NoireRemoteClient(name);
        var binding = new NoireRemoteBinding(client);

        foreach (var property in properties)
            BindProperty(binding, client, target, property);

        foreach (var member in events)
            BindEvent(binding, client, target, name, member);

        return binding;
    }

    internal static string RemoteNameOf(Type type, NoireRemoteClassAttribute? attribute, string? explicitName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName!;

        if (!string.IsNullOrWhiteSpace(attribute?.Name))
            return attribute!.Name!;

        return type.Name;
    }

    private static void BindProperty(NoireRemoteBinding binding, NoireRemoteClient client, object? target, PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<NoireRemoteAttribute>()!;
        var member = string.IsNullOrWhiteSpace(attribute.Name) ? property.Name : attribute.Name!;
        var setter = property.GetSetMethod(nonPublic: true) ?? property.GetSetMethod(nonPublic: false)
            ?? throw new InvalidOperationException("Consumer property '" + property.DeclaringType?.FullName + "." + property.Name + "' must have a setter.");

        object? value;

        if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(NoireRemoteConsumer<>))
        {
            var consumerType = typeof(NoireRemoteConsumer<>).MakeGenericType(property.PropertyType.GetGenericArguments());
            value = Activator.CreateInstance(
                consumerType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [client, member],
                null);
        }
        else if (typeof(Delegate).IsAssignableFrom(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
        {
            var consumerType = typeof(NoireRemoteConsumer<>).MakeGenericType(property.PropertyType);
            var consumer = Activator.CreateInstance(
                consumerType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [client, member],
                null)!;

            value = consumerType.GetProperty(nameof(NoireRemoteConsumer<Action>.Invoke))!.GetValue(consumer);
        }
        else
        {
            throw new InvalidOperationException(
                "Consumer property '" + property.DeclaringType?.FullName + "." + property.Name
                + "' has to be a delegate or a NoireRemoteConsumer. A property of any other type publishes its value.");
        }

        setter.Invoke(setter.IsStatic ? null : target, [value]);
        binding.Add(() => setter.Invoke(setter.IsStatic ? null : target, [null]));
    }

    private static void BindEvent(NoireRemoteBinding binding, NoireRemoteClient client, object? target, string api, EventInfo member)
    {
        var attribute = member.GetCustomAttribute<NoireRemoteAttribute>()!;
        var topic = string.IsNullOrWhiteSpace(attribute.Name)
            ? (api + "." + member.Name).ToLowerInvariant()
            : attribute.Name!;

        var handlerType = member.EventHandlerType
            ?? throw new InvalidOperationException("Event '" + member.DeclaringType?.FullName + "." + member.Name + "' declares no handler type.");

        var raise = member.DeclaringType?.GetField(member.Name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        var subscription = client.Subscribe(topic, (_, payload) =>
        {
            var handler = raise?.GetValue(raise.IsStatic ? null : target) as Delegate;

            if (handler == null)
                return;

            var arguments = RemoteEventArguments.Unpack(handlerType, payload);

            Sender = client.LastSender;

            try
            {
                handler.DynamicInvoke(arguments);
            }
            finally
            {
                Sender = null;
            }
        });

        binding.Add(subscription.Dispose);
    }
}
