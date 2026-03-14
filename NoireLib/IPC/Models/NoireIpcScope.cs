using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.IPC;

/// <summary>
/// Represents a reusable configuration scope for resolving IPC names and channels.
/// </summary>
public sealed class NoireIpcScope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcScope"/> class.
    /// </summary>
    /// <param name="prefix">The prefix to apply to names resolved by the scope.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for message channels created by the scope.</param>
    public NoireIpcScope(string? prefix, bool useDefaultPrefix, Type messageResultType)
    {
        Prefix = prefix;
        UseDefaultPrefix = useDefaultPrefix;
        MessageResultType = messageResultType;
    }

    /// <summary>
    /// Gets the explicit prefix assigned to the scope.
    /// </summary>
    /// <returns>The scope prefix, or <see langword="null"/> to inherit the current scope prefix.</returns>
    public string? Prefix { get; }

    /// <summary>
    /// Gets a value indicating whether default prefix resolution is enabled for the scope.
    /// </summary>
    /// <returns><see langword="true"/> when default prefix resolution is enabled; otherwise, <see langword="false"/>.</returns>
    public bool UseDefaultPrefix { get; }

    /// <summary>
    /// Gets the trailing generic type used for message channels created by the scope.
    /// </summary>
    /// <returns>The message result type used by channels created from the scope.</returns>
    public Type MessageResultType { get; }

    /// <summary>
    /// Resolves a local IPC name into its final channel name using the scope configuration.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name.</param>
    /// <returns>The fully qualified IPC channel name.</returns>
    public string ResolveName(string name)
        => NoireIPC.ResolveScopedName(name, Prefix, UseDefaultPrefix);

    /// <summary>
    /// Creates a copy of the scope with a different prefix configuration.
    /// </summary>
    /// <param name="prefix">The new explicit prefix.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution remains enabled for the returned scope.</param>
    /// <returns>A new scope using the requested prefix settings.</returns>
    public NoireIpcScope WithPrefix(string? prefix, bool useDefaultPrefix = true)
        => new(prefix, useDefaultPrefix, MessageResultType);

    /// <summary>
    /// Creates a copy of the scope with a different message result type.
    /// </summary>
    /// <param name="messageResultType">The trailing generic type to use for message channels created by the returned scope.</param>
    /// <returns>A new scope using the requested message result type.</returns>
    public NoireIpcScope WithMessageResultType(Type messageResultType)
        => new(Prefix, UseDefaultPrefix, NoireIPC.ValidateMessageResultType(messageResultType));

    /// <summary>
    /// Creates a channel wrapper using the scope configuration.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name.</param>
    /// <returns>A configured IPC channel wrapper.</returns>
    public NoireIpcChannel Channel(string name)
        => new(ResolveName(name), MessageResultType);

    /// <summary>
    /// Registers a provider delegate on a channel resolved by the scope.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The delegate to expose through IPC.</param>
    /// <param name="kind">The registration kind to use. When set to <see cref="NoireIpcRegistrationKind.Auto"/>, the delegate return type is used to infer the correct registration type.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration Register(string name, Delegate handler, NoireIpcRegistrationKind kind = NoireIpcRegistrationKind.Auto)
        => Channel(name).Register(handler, kind);

    /// <summary>
    /// Registers an action provider on a channel resolved by the scope.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The action delegate to expose through IPC.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration RegisterAction(string name, Delegate handler)
        => Channel(name).RegisterAction(handler);

    /// <summary>
    /// Registers a function provider on a channel resolved by the scope.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The function delegate to expose through IPC.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration RegisterFunc(string name, Delegate handler)
        => Channel(name).RegisterFunc(handler);

    /// <summary>
    /// Subscribes a message handler to a channel resolved by the scope.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to subscribe to.</param>
    /// <param name="handler">The delegate to invoke when another plugin publishes a message to the channel.</param>
    /// <returns>A handle that can be disposed early to unsubscribe before plugin shutdown.</returns>
    public NoireIpcSubscription Subscribe(string name, Delegate handler)
        => Channel(name).Subscribe(handler);

    /// <summary>
    /// Sends a message through a channel resolved by the scope using inferred argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to publish to.</param>
    /// <param name="arguments">The message payload arguments.</param>
    public void Send(string name, params object?[] arguments)
        => Channel(name).Send(arguments);

    /// <summary>
    /// Sends a message through a channel resolved by the scope using explicit argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to publish to.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The message payload arguments.</param>
    public void Send(string name, Type[] parameterTypes, params object?[] arguments)
        => Channel(name).Send(parameterTypes, arguments);

    /// <summary>
    /// Invokes an action IPC resolved by the scope using inferred argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public void InvokeAction(string name, params object?[] arguments)
        => Channel(name).InvokeAction(arguments);

    /// <summary>
    /// Invokes an action IPC resolved by the scope using explicit argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public void InvokeAction(string name, Type[] parameterTypes, params object?[] arguments)
        => Channel(name).InvokeAction(parameterTypes, arguments);

    /// <summary>
    /// Invokes a function IPC resolved by the scope using inferred argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public TResult InvokeFunc<TResult>(string name, params object?[] arguments)
        => Channel(name).InvokeFunc<TResult>(arguments);

    /// <summary>
    /// Invokes a function IPC resolved by the scope using explicit argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public TResult InvokeFunc<TResult>(string name, Type[] parameterTypes, params object?[] arguments)
        => Channel(name).InvokeFunc<TResult>(parameterTypes, arguments);

    /// <summary>
    /// Registers or binds every annotated member on an object instance decorated with <see cref="NoireIpcAttribute"/>.
    /// Methods are registered as IPC providers. Properties with delegate types are bound as IPC consumers. Events are bridged to IPC messages for publishing and subscription.
    /// </summary>
    /// <param name="instance">The object instance whose annotated members should be processed.</param>
    /// <param name="bindingFlags">The binding flags used to discover members on <paramref name="instance"/>.</param>
    /// <returns>A group containing handles for the annotated registrations and consumer bindings.</returns>
    public NoireIpcGroup Initialize(object instance, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return RegisterAttributedMembers(instance.GetType(), instance, bindingFlags);
    }

    /// <summary>
    /// Registers or binds every annotated static member on a type decorated with <see cref="NoireIpcAttribute"/>.
    /// Methods are registered as IPC providers. Properties with delegate types are bound as IPC consumers. Events are bridged to IPC messages for publishing and subscription.
    /// </summary>
    /// <param name="type">The type containing annotated static members.</param>
    /// <param name="bindingFlags">The binding flags used to discover static members on <paramref name="type"/>.</param>
    /// <returns>A group containing handles for the annotated registrations and consumer bindings.</returns>
    public NoireIpcGroup RegisterType(Type type, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    {
        ArgumentNullException.ThrowIfNull(type);

        return ProcessAttributedMembers(type, null, bindingFlags);
    }

    private NoireIpcGroup RegisterAttributedMembers(Type type, object? target, BindingFlags bindingFlags)
        => ProcessAttributedMembers(type, target, bindingFlags);

    private NoireIpcGroup ProcessAttributedMembers(Type type, object? target, BindingFlags bindingFlags)
    {
        var classAttribute = type.GetCustomAttribute<NoireIpcClassAttribute>();
        var effectivePrefix = classAttribute?.Prefix ?? Prefix;
        var effectiveUseDefaultPrefix = classAttribute?.UseDefaultPrefix
            ?? (string.IsNullOrWhiteSpace(Prefix) ? UseDefaultPrefix : false);
        var effectiveMessageResultType = classAttribute?.MessageResultType ?? MessageResultType;

        var handles = new List<NoireIpcHandle>();

        var annotatedMethods = type.GetMethods(bindingFlags)
            .Select(method => (Member: (MemberInfo)method, Method: method, Property: (PropertyInfo?)null, Attribute: method.GetCustomAttribute<NoireIpcAttribute>()))
            .Where(entry => entry.Attribute != null);

        var annotatedProperties = type.GetProperties(bindingFlags)
            .Select(property => (Member: (MemberInfo)property, Method: (MethodInfo?)null, Property: property, Attribute: property.GetCustomAttribute<NoireIpcAttribute>()))
            .Where(entry => entry.Attribute != null);

        var annotatedEvents = type.GetEvents(bindingFlags)
            .Select(@event => (Member: (MemberInfo)@event, Method: (MethodInfo?)null, Property: (PropertyInfo?)null, Event: @event, Attribute: @event.GetCustomAttribute<NoireIpcAttribute>()))
            .Where(entry => entry.Attribute != null);

        var allMembers = annotatedMethods
            .Select(entry => (entry.Member, entry.Method, entry.Property, Event: (EventInfo?)null, entry.Attribute))
            .Concat(annotatedProperties.Select(entry => (entry.Member, entry.Method, entry.Property, Event: (EventInfo?)null, entry.Attribute)))
            .Concat(annotatedEvents)
            .ToArray();

        foreach (var entry in allMembers)
        {
            var memberName = entry.Attribute!.Name ?? entry.Member.Name;
            var scope = new NoireIpcScope(
                entry.Attribute.Prefix ?? effectivePrefix,
                entry.Attribute.UseDefaultPrefix && effectiveUseDefaultPrefix,
                entry.Attribute.MessageResultType ?? effectiveMessageResultType);
            var targetKind = ResolveTarget(entry.Member, entry.Attribute);

            if (entry.Property != null && NoireIPC.IsNullableDelegateType(entry.Property.PropertyType, out var delegateType))
            {
                if (targetKind == NoireIpcTargetKind.Event)
                    throw new InvalidOperationException($"Consumer property '{type.FullName}.{entry.Property.Name}' targets an event channel and must use an event member or NoireIpcEvent<TDelegate>.");

                var fullName = scope.ResolveName(memberName);
                var getMethod = entry.Property.GetGetMethod(nonPublic: true) ?? entry.Property.GetGetMethod(nonPublic: false);
                if (getMethod == null)
                    throw new InvalidOperationException($"Consumer property '{type.FullName}.{entry.Property.Name}' must have a getter.");

                var consumerDelegate = NoireIPC.CreateConsumerDelegateForProperty(fullName, delegateType!, scope.MessageResultType);
                var binding = NoireIPC.BindConsumerToProperty(target, entry.Property, consumerDelegate, fullName);
                handles.Add(binding);
            }
            else if (entry.Property != null && NoireIPC.IsConsumerWrapperType(entry.Property.PropertyType, out var wrapperDelegateType))
            {
                var fullName = scope.ResolveName(memberName);
                var getMethod = entry.Property.GetGetMethod(nonPublic: true) ?? entry.Property.GetGetMethod(nonPublic: false);
                if (getMethod == null)
                    throw new InvalidOperationException($"Consumer property '{type.FullName}.{entry.Property.Name}' must have a getter.");

                var consumerWrapper = NoireIPC.CreateConsumerWrapperForProperty(fullName, entry.Property.PropertyType, wrapperDelegateType!, scope.MessageResultType);
                var binding = NoireIPC.BindConsumerToProperty(target, entry.Property, consumerWrapper, fullName);
                handles.Add(binding);
            }
            else if (entry.Property != null && NoireIPC.IsEventWrapperType(entry.Property.PropertyType, out var eventWrapperDelegateType))
            {
                var fullName = scope.ResolveName(memberName);
                var getMethod = entry.Property.GetGetMethod(nonPublic: true) ?? entry.Property.GetGetMethod(nonPublic: false);
                if (getMethod == null)
                    throw new InvalidOperationException($"Consumer property '{type.FullName}.{entry.Property.Name}' must have a getter.");

                var eventWrapper = NoireIPC.CreateEventWrapperForProperty(fullName, entry.Property.PropertyType, eventWrapperDelegateType!, scope.MessageResultType);
                var binding = NoireIPC.BindConsumerToProperty(target, entry.Property, eventWrapper, fullName);
                handles.Add(binding);
            }
            else if (entry.Event != null)
            {
                if (targetKind == NoireIpcTargetKind.Call)
                    throw new InvalidOperationException($"Event member '{type.FullName}.{entry.Event.Name}' cannot target a call-style IPC.");

                var fullName = scope.ResolveName(memberName);
                var eventHandlerType = entry.Event.EventHandlerType
                    ?? throw new InvalidOperationException($"Event '{type.FullName}.{entry.Event.Name}' does not declare a handler type.");

                NoireIPC.ValidateEventHandlerType(eventHandlerType);

                if (IsConsumerEvent(entry.Attribute))
                {
                    var consumerSubscription = NoireIPC.BindConsumerToEvent(target, entry.Event, fullName, scope.MessageResultType);
                    handles.Add(consumerSubscription);
                }
                else
                {
                    var publisherDelegate = NoireIPC.CreateEventPublisherDelegate(fullName, eventHandlerType, scope.MessageResultType);
                    var publisherBinding = NoireIPC.BindEventPublisher(target, entry.Event, publisherDelegate, fullName);
                    handles.Add(publisherBinding);
                }
            }
            else if (entry.Method != null)
            {
                var delegateInstance = NoireIPC.CreateDelegate(target, entry.Method);
                if (IsConsumerMethod(entry.Method, entry.Attribute, targetKind))
                {
                    if (entry.Method.ReturnType != typeof(void))
                        throw new InvalidOperationException($"Consumer method '{type.FullName}.{entry.Method.Name}' must return void.");

                    var subscription = scope.Channel(memberName).Subscribe(delegateInstance);
                    handles.Add(subscription);
                }
                else
                {
                    var registration = scope.Channel(memberName).Register(delegateInstance, entry.Attribute.Kind);
                    handles.Add(registration);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Member '{type.FullName}.{entry.Member.Name}' with [NoireIpc] must be either a method, an event, a property with a delegate type, or a property with NoireIpcConsumer<TDelegate>.");
            }
        }

        return new NoireIpcGroup(handles);
    }

    private static bool IsConsumerMethod(MethodInfo method, NoireIpcAttribute attribute, NoireIpcTargetKind targetKind)
    {
        return attribute.Mode switch
        {
            NoireIpcMode.Consumer => true,
            NoireIpcMode.Provider => false,
            _ => targetKind == NoireIpcTargetKind.Event || (!method.IsPublic && method.ReturnType == typeof(void)),
        };
    }

    private static bool IsConsumerEvent(NoireIpcAttribute attribute)
        => attribute.Mode == NoireIpcMode.Consumer;

    private static NoireIpcTargetKind ResolveTarget(MemberInfo member, NoireIpcAttribute attribute)
    {
        if (attribute.Target != NoireIpcTargetKind.Auto)
            return attribute.Target;

        return member switch
        {
            EventInfo => NoireIpcTargetKind.Event,
            PropertyInfo property when NoireIPC.IsEventWrapperType(property.PropertyType, out _) => NoireIpcTargetKind.Event,
            _ => NoireIpcTargetKind.Call,
        };
    }
}
