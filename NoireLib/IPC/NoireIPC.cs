using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace NoireLib.IPC;

/// <summary>
/// Provides a high-level wrapper around Dalamud IPC registration, subscription, publication, and invocation.
/// </summary>
public static class NoireIPC
{
    private const string DisposeKey = "NoireLib.Internal.NoireIPC.Dispose";
    private static bool _disposeHookRegistered;
    private static readonly object SyncRoot = new();
    private static readonly List<NoireIpcHandle> OwnedHandles = [];
    private static readonly IReadOnlyDictionary<int, MethodInfo> ProviderFactoryMethods = typeof(IDalamudPluginInterface)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Where(method => method.Name == nameof(IDalamudPluginInterface.GetIpcProvider) && method.IsGenericMethodDefinition)
        .ToDictionary(method => method.GetGenericArguments().Length);
    private static readonly IReadOnlyDictionary<int, MethodInfo> SubscriberFactoryMethods = typeof(IDalamudPluginInterface)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Where(method => method.Name == nameof(IDalamudPluginInterface.GetIpcSubscriber) && method.IsGenericMethodDefinition)
        .ToDictionary(method => method.GetGenericArguments().Length);

    /// <summary>
    /// Gets the configured default prefix used when no explicit prefix is provided.
    /// </summary>
    public static string? DefaultPrefix { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current plugin internal name should be used as the default prefix when no explicit prefix is configured.
    /// </summary>
    public static bool UsePluginInternalNameAsDefaultPrefix { get; private set; } = true;

    /// <summary>
    /// Gets the separator inserted between an IPC prefix and its channel name.
    /// </summary>
    public static string NameSeparator { get; private set; } = ".";

    /// <summary>
    /// Gets the default trailing generic type used for message channels when no explicit message result type is supplied.
    /// </summary>
    public static Type DefaultMessageResultType { get; private set; } = typeof(object);

    /// <summary>
    /// Configures global IPC naming and message behavior.
    /// </summary>
    /// <param name="defaultPrefix">The explicit default prefix to use for IPC names. Use <see langword="null"/> to clear the explicit default prefix.</param>
    /// <param name="usePluginInternalNameAsDefaultPrefix">If set to <see langword="true"/>, the current plugin internal name is used when no explicit prefix is supplied.</param>
    /// <param name="nameSeparator">The separator inserted between a prefix and a channel name.</param>
    /// <param name="defaultMessageResultType">The default trailing generic type to use for message channels. When <see langword="null"/>, <see cref="System.Object"/> is used.</param>
    public static void Configure(string? defaultPrefix = null, bool usePluginInternalNameAsDefaultPrefix = true, string nameSeparator = ".", Type? defaultMessageResultType = null)
    {
        ValidateSeparator(nameSeparator);

        DefaultPrefix = NormalizePrefix(defaultPrefix);
        UsePluginInternalNameAsDefaultPrefix = usePluginInternalNameAsDefaultPrefix;
        NameSeparator = nameSeparator;
        DefaultMessageResultType = ValidateMessageResultType(defaultMessageResultType ?? typeof(object));
    }

    /// <summary>
    /// Resets the global IPC configuration back to its defaults.
    /// </summary>
    public static void ResetConfiguration()
    {
        DefaultPrefix = null;
        UsePluginInternalNameAsDefaultPrefix = true;
        NameSeparator = ".";
        DefaultMessageResultType = typeof(object);
    }

    /// <summary>
    /// Resolves the effective prefix for a registration or invocation.
    /// </summary>
    /// <param name="prefix">The explicit prefix to use. When provided, it takes precedence over every fallback.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, global defaults are used when <paramref name="prefix"/> is not provided.</param>
    /// <returns>The resolved prefix, or an empty string when no prefix should be applied.</returns>
    public static string ResolvePrefix(string? prefix = null, bool useDefaultPrefix = true)
    {
        if (!string.IsNullOrWhiteSpace(prefix))
            return NormalizePrefix(prefix)!;

        if (useDefaultPrefix)
        {
            if (!string.IsNullOrWhiteSpace(DefaultPrefix))
                return DefaultPrefix!;

            if (UsePluginInternalNameAsDefaultPrefix && NoireService.IsInitialized())
                return NoireService.PluginInterface.InternalName;
        }

        return string.Empty;
    }

    /// <summary>
    /// Builds a fully qualified IPC name from a local name and an optional prefix.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name.</param>
    /// <param name="prefix">The explicit prefix to prepend when <paramref name="name"/> is not already prefixed.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, the default prefix resolution pipeline is used when <paramref name="prefix"/> is not supplied.</param>
    /// <returns>The fully qualified IPC channel name.</returns>
    public static string BuildName(string name, string? prefix = null, bool useDefaultPrefix = true)
    {
        var normalizedName = NormalizeName(name);
        var resolvedPrefix = ResolvePrefix(prefix, useDefaultPrefix);

        if (string.IsNullOrEmpty(resolvedPrefix))
            return normalizedName;

        var prefixedName = resolvedPrefix + NameSeparator;
        return normalizedName.StartsWith(prefixedName, StringComparison.Ordinal)
            ? normalizedName
            : prefixedName + normalizedName;
    }

    /// <summary>
    /// Creates a reusable IPC scope that shares prefix and message configuration.
    /// </summary>
    /// <param name="prefix">The prefix to apply to names resolved by the scope.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for message channels created by the scope.</param>
    /// <returns>A configured IPC scope.</returns>
    public static NoireIpcScope Scope(string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null)
        => new(prefix, useDefaultPrefix, ValidateMessageResultType(messageResultType ?? DefaultMessageResultType));

    /// <summary>
    /// Creates a scope that does not apply any automatic prefix resolution.
    /// </summary>
    /// <param name="messageResultType">The trailing generic type to use for message channels created by the scope.</param>
    /// <returns>An unprefixed IPC scope.</returns>
    public static NoireIpcScope Raw(Type? messageResultType = null)
        => new(null, false, ValidateMessageResultType(messageResultType ?? DefaultMessageResultType));

    /// <summary>
    /// Creates a channel wrapper for a specific IPC name.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name.</param>
    /// <param name="prefix">The explicit prefix to apply when the name is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use when sending or subscribing to messages on the channel.</param>
    /// <returns>A configured IPC channel wrapper.</returns>
    public static NoireIpcChannel Channel(string name, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null)
        => Scope(prefix, useDefaultPrefix, messageResultType).Channel(name);

    /// <summary>
    /// Registers a provider delegate for an IPC channel.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The delegate to expose through IPC.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="kind">The registration kind to use. When set to <see cref="NoireIpcRegistrationKind.Auto"/>, the delegate return type is used to infer the correct registration type.</param>
    /// <param name="messageResultType">The trailing generic type to use when registering a message-oriented action channel.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public static NoireIpcRegistration Register(string name, Delegate handler, string? prefix = null, bool useDefaultPrefix = true, NoireIpcRegistrationKind kind = NoireIpcRegistrationKind.Auto, Type? messageResultType = null)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).Register(handler, kind);

    /// <summary>
    /// Registers an action provider for an IPC channel.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The action delegate to expose through IPC.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for message-oriented action channels.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public static NoireIpcRegistration RegisterAction(string name, Delegate handler, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).RegisterAction(handler);

    /// <summary>
    /// Registers a function provider for an IPC channel.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to register.</param>
    /// <param name="handler">The function delegate to expose through IPC.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public static NoireIpcRegistration RegisterFunc(string name, Delegate handler, string? prefix = null, bool useDefaultPrefix = true)
        => Channel(name, prefix, useDefaultPrefix).RegisterFunc(handler);

    /// <summary>
    /// Subscribes a message handler to an IPC channel.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to subscribe to.</param>
    /// <param name="handler">The delegate to invoke when another plugin publishes a message to the channel.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for the message channel.</param>
    /// <returns>A handle that can be disposed early to unsubscribe before plugin shutdown.</returns>
    public static NoireIpcSubscription Subscribe(string name, Delegate handler, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).Subscribe(handler);

    /// <summary>
    /// Sends a message through an IPC channel using inferred argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to publish to.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for the message channel.</param>
    /// <param name="arguments">The message payload arguments.</param>
    public static void Send(string name, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).Send(arguments);

    /// <summary>
    /// Sends a message through an IPC channel using explicit argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to publish to.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use for the message channel.</param>
    /// <param name="arguments">The message payload arguments.</param>
    public static void Send(string name, Type[] parameterTypes, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).Send(parameterTypes, arguments);

    /// <summary>
    /// Invokes an action IPC on another plugin using inferred argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use when invoking a message-oriented action channel.</param>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public static void InvokeAction(string name, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).InvokeAction(arguments);

    /// <summary>
    /// Invokes an action IPC on another plugin using explicit argument types.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The trailing generic type to use when invoking a message-oriented action channel.</param>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public static void InvokeAction(string name, Type[] parameterTypes, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix, messageResultType).InvokeAction(parameterTypes, arguments);

    /// <summary>
    /// Invokes a function IPC on another plugin using inferred argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public static TResult InvokeFunc<TResult>(string name, string? prefix = null, bool useDefaultPrefix = true, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix).InvokeFunc<TResult>(arguments);

    /// <summary>
    /// Invokes a function IPC on another plugin using explicit argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="name">The local or fully qualified IPC name to invoke.</param>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public static TResult InvokeFunc<TResult>(string name, Type[] parameterTypes, string? prefix = null, bool useDefaultPrefix = true, params object?[] arguments)
        => Channel(name, prefix, useDefaultPrefix).InvokeFunc<TResult>(parameterTypes, arguments);

    /// <summary>
    /// Registers or binds every annotated member on an object instance decorated with <see cref="NoireIpcAttribute"/>.
    /// Methods are registered as IPC providers. Properties with delegate types are bound as IPC consumers.
    /// </summary>
    /// <param name="instance">The object instance whose annotated members should be processed.</param>
    /// <param name="prefix">The explicit prefix to apply when annotated members do not specify their own prefix.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The default trailing generic type to use for message-oriented annotated members.</param>
    /// <param name="bindingFlags">The binding flags used to discover members on <paramref name="instance"/>.</param>
    /// <returns>A group containing handles for the annotated registrations and consumer bindings.</returns>
    public static NoireIpcGroup Initialize(object instance, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        => Scope(prefix, useDefaultPrefix, messageResultType).Initialize(instance, bindingFlags);

    /// <summary>
    /// Registers or binds every annotated static member on <typeparamref name="T"/> decorated with <see cref="NoireIpcAttribute"/>.
    /// Methods are registered as IPC providers. Properties with delegate types are bound as IPC consumers.
    /// </summary>
    /// <typeparam name="T">The type containing annotated static members.</typeparam>
    /// <param name="prefix">The explicit prefix to apply when annotated members do not specify their own prefix.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The default trailing generic type to use for message-oriented annotated members.</param>
    /// <param name="bindingFlags">The binding flags used to discover static members on <typeparamref name="T"/>.</param>
    /// <returns>A group containing handles for the annotated registrations and consumer bindings.</returns>
    public static NoireIpcGroup RegisterType<T>(string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        => RegisterType(typeof(T), prefix, useDefaultPrefix, messageResultType, bindingFlags);

    /// <summary>
    /// Registers or binds every annotated static member on a type decorated with <see cref="NoireIpcAttribute"/>.
    /// Methods are registered as IPC providers. Properties with delegate types are bound as IPC consumers.
    /// </summary>
    /// <param name="type">The type containing annotated static members.</param>
    /// <param name="prefix">The explicit prefix to apply when annotated members do not specify their own prefix.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="messageResultType">The default trailing generic type to use for message-oriented annotated members.</param>
    /// <param name="bindingFlags">The binding flags used to discover static members on <paramref name="type"/>.</param>
    /// <returns>A group containing handles for the annotated registrations and consumer bindings.</returns>
    public static NoireIpcGroup RegisterType(Type type, string? prefix = null, bool useDefaultPrefix = true, Type? messageResultType = null, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        => Scope(prefix, useDefaultPrefix, messageResultType).RegisterType(type, bindingFlags);

    /// <summary>
    /// Automatically registers every static type in an assembly decorated with <see cref="NoireIpcClassAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="bindingFlags">The binding flags used to discover static members on discovered types.</param>
    /// <returns>A group containing handles for every discovered registration and consumer binding.</returns>
    public static NoireIpcGroup RegisterAttributedTypes(Assembly assembly, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var handles = new List<NoireIpcHandle>();
        var attributedTypes = GetLoadableTypes(assembly)
            .Where(type => type.GetCustomAttribute<NoireIpcClassAttribute>() != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var type in attributedTypes)
        {
            if (!type.IsAbstract || !type.IsSealed)
            {
                //NoireLogger.LogWarning($"Skipping automatic IPC registration for '{type.FullName}' because automatic registration only supports static types. Use NoireIPC.Initialize for instance types.", $"[{typeof(NoireIPC).Name}] ");
                continue;
            }

            NoireLogger.LogDebug($"Automatically registering IPC type '{type.FullName}'.", $"[{typeof(NoireIPC).Name}] ");
            var group = RegisterType(type, bindingFlags: bindingFlags);
            foreach (var handle in group)
                handles.Add(handle);
        }

        return new NoireIpcGroup(handles);
    }

    /// <summary>
    /// Resolves the raw Dalamud IPC provider for advanced scenarios.
    /// </summary>
    /// <param name="fullName">The fully qualified IPC channel name.</param>
    /// <param name="callGateTypes">The exact generic type arguments required by the underlying Dalamud call gate.</param>
    /// <returns>The raw Dalamud provider instance.</returns>
    public static object GetRawProvider(string fullName, params Type[] callGateTypes)
        => GetCallGateFactoryResult(ProviderFactoryMethods, NormalizeName(fullName), callGateTypes);

    /// <summary>
    /// Resolves the raw Dalamud IPC subscriber for advanced scenarios.
    /// </summary>
    /// <param name="fullName">The fully qualified IPC channel name.</param>
    /// <param name="callGateTypes">The exact generic type arguments required by the underlying Dalamud call gate.</param>
    /// <returns>The raw Dalamud subscriber instance.</returns>
    public static object GetRawSubscriber(string fullName, params Type[] callGateTypes)
        => GetCallGateFactoryResult(SubscriberFactoryMethods, NormalizeName(fullName), callGateTypes);

    /// <summary>
    /// Checks whether an IPC provider is currently available for the specified channel and signature.
    /// Availability probing avoids invoking delegates that require parameters or represent actions, because synthetic test calls can have side effects or fail for valid providers.
    /// Only parameterless function IPCs are probe-invoked.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to check.</param>
    /// <param name="parameterTypes">The parameter types expected by the IPC signature.</param>
    /// <param name="returnType">The return type expected by the IPC signature. Use <see langword="null"/> or <see cref="System.Object"/> for action channels.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <returns><see langword="true"/> if the IPC provider appears available for the requested signature; otherwise, <see langword="false"/>.</returns>
    public static bool IsAvailable(string name, Type[] parameterTypes, Type? returnType = null, string? prefix = null, bool useDefaultPrefix = true)
    {
        var fullName = BuildName(name, prefix, useDefaultPrefix);
        var finalReturnType = returnType ?? typeof(object);
        var callGateTypes = BuildCallGateTypes(parameterTypes, finalReturnType);

        try
        {
            var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
            var methodName = finalReturnType == typeof(object) ? "InvokeAction" : "InvokeFunc";
            var method = subscriber.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

            if (method == null)
                return false;

            if (finalReturnType == typeof(object) || parameterTypes.Length > 0)
                return true;

            InvokeInstanceMethod(subscriber, methodName, Array.Empty<object>());
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether an action IPC provider is currently available.
    /// </summary>
    /// <param name="name">The local or fully qualified IPC name to check.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="parameterTypes">The parameter types expected by the action signature.</param>
    /// <returns><see langword="true"/> if the action provider is available; otherwise, <see langword="false"/>.</returns>
    public static bool IsActionAvailable(string name, string? prefix = null, bool useDefaultPrefix = true, params Type[] parameterTypes)
        => IsAvailable(name, parameterTypes, typeof(object), prefix, useDefaultPrefix);

    /// <summary>
    /// Checks whether a function IPC provider is currently available.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="name">The local or fully qualified IPC name to check.</param>
    /// <param name="prefix">The explicit prefix to apply when <paramref name="name"/> is not already fully qualified.</param>
    /// <param name="useDefaultPrefix">If set to <see langword="true"/>, default prefix resolution is used when <paramref name="prefix"/> is not supplied.</param>
    /// <param name="parameterTypes">The parameter types expected by the function signature.</param>
    /// <returns><see langword="true"/> if the function provider is available; otherwise, <see langword="false"/>.</returns>
    public static bool IsFuncAvailable<TResult>(string name, string? prefix = null, bool useDefaultPrefix = true, params Type[] parameterTypes)
        => IsAvailable(name, parameterTypes, typeof(TResult), prefix, useDefaultPrefix);

    #region Private & Internal Methods

    internal static NoireIpcRegistration RegisterCore(string fullName, Delegate handler, NoireIpcRegistrationKind kind, Type messageResultType)
    {
        EnsureInitialized();
        ValidateDelegate(handler, allowVoidReturn: kind != NoireIpcRegistrationKind.Function);

        var normalizedKind = NormalizeRegistrationKind(handler, kind);
        var callGateTypes = BuildCallGateTypes(handler, normalizedKind, messageResultType);
        var provider = GetCallGateFactoryResult(ProviderFactoryMethods, fullName, callGateTypes);
        var registrationMethodName = normalizedKind == NoireIpcRegistrationKind.Function ? "RegisterFunc" : "RegisterAction";

        InvokeInstanceMethod(provider, registrationMethodName, handler);
        NoireLogger.LogDebug($"Registered {normalizedKind} provider '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");

        var registration = new NoireIpcRegistration(
            fullName,
            normalizedKind,
            () =>
            {
                NoireLogger.LogDebug($"Unregistering {normalizedKind} provider '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
                InvokeInstanceMethod(provider, normalizedKind == NoireIpcRegistrationKind.Function ? "UnregisterFunc" : "UnregisterAction");
            },
            HandleDisposed);

        TrackHandle(registration);
        return registration;
    }

    internal static NoireIpcSubscription SubscribeCore(string fullName, Delegate handler, Type messageResultType)
    {
        EnsureInitialized();
        ValidateDelegate(handler, allowVoidReturn: true);

        var callGateTypes = BuildMessageCallGateTypes(handler, messageResultType);
        var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
        InvokeInstanceMethod(subscriber, "Subscribe", handler);
        NoireLogger.LogDebug($"Subscribed handler to '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");

        var subscription = new NoireIpcSubscription(
            fullName,
            () =>
            {
                NoireLogger.LogDebug($"Unsubscribing handler from '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
                InvokeInstanceMethod(subscriber, "Unsubscribe", handler);
            },
            HandleDisposed);

        TrackHandle(subscription);
        return subscription;
    }

    internal static void SendCore(string fullName, Type[] parameterTypes, Type messageResultType, object?[] arguments)
    {
        EnsureInitialized();
        ValidateInvocationSignature(parameterTypes, arguments);

        var callGateTypes = BuildCallGateTypes(parameterTypes, messageResultType);
        var provider = GetCallGateFactoryResult(ProviderFactoryMethods, fullName, callGateTypes);
        try
        {
            InvokeInstanceMethod(provider, "SendMessage", arguments);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to send message '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
            throw;
        }
    }

    internal static void InvokeActionCore(string fullName, Type[] parameterTypes, Type messageResultType, object?[] arguments)
    {
        EnsureInitialized();
        ValidateInvocationSignature(parameterTypes, arguments);

        var callGateTypes = BuildCallGateTypes(parameterTypes, messageResultType);
        var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
        try
        {
            InvokeInstanceMethod(subscriber, "InvokeAction", arguments);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to invoke action '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
            throw;
        }
    }

    internal static TResult InvokeFuncCore<TResult>(string fullName, Type[] parameterTypes, object?[] arguments)
    {
        EnsureInitialized();
        ValidateInvocationSignature(parameterTypes, arguments);

        var callGateTypes = BuildCallGateTypes(parameterTypes, typeof(TResult));
        var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
        object? result;
        try
        {
            result = InvokeInstanceMethod(subscriber, "InvokeFunc", arguments);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to invoke function '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
            throw;
        }

        return result is TResult typedResult
            ? typedResult
            : throw new InvalidCastException($"IPC '{fullName}' returned {result?.GetType().FullName ?? "null"} instead of {typeof(TResult).FullName}.");
    }

    internal static string ResolveScopedName(string name, string? prefix, bool useDefaultPrefix)
        => BuildName(name, prefix, useDefaultPrefix);

    internal static Type[] InferParameterTypes(object?[] arguments)
    {
        if (arguments.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports up to 8 parameters.");

        var parameterTypes = new Type[arguments.Length];

        for (var i = 0; i < arguments.Length; i++)
        {
            parameterTypes[i] = arguments[i]?.GetType()
                ?? throw new InvalidOperationException("IPC parameter types cannot be inferred when one or more arguments are null. Use the overload that accepts explicit parameter types.");
        }

        return parameterTypes;
    }

    internal static Type ValidateMessageResultType(Type messageResultType)
    {
        ArgumentNullException.ThrowIfNull(messageResultType);

        if (messageResultType == typeof(void))
            throw new ArgumentException("The IPC message result type cannot be void.", nameof(messageResultType));

        return messageResultType;
    }

    internal static bool IsDelegateType(Type type)
    {
        return type.IsSubclassOf(typeof(Delegate)) || type == typeof(Delegate) || type == typeof(MulticastDelegate);
    }

    internal static bool IsNullableDelegateType(Type type, out Type? delegateType)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null && IsDelegateType(underlyingType))
        {
            delegateType = underlyingType;
            return true;
        }

        if (IsDelegateType(type))
        {
            delegateType = type;
            return true;
        }

        delegateType = null;
        return false;
    }

    internal static bool IsEventWrapperType(Type type, out Type? delegateType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NoireIpcEventConsumer<>))
        {
            var candidate = type.GetGenericArguments()[0];
            if (IsDelegateType(candidate))
            {
                delegateType = candidate;
                return true;
            }
        }

        delegateType = null;
        return false;
    }

    internal static void ValidateEventHandlerType(Type delegateType)
    {
        ArgumentNullException.ThrowIfNull(delegateType);

        if (!IsDelegateType(delegateType))
            throw new ArgumentException($"Type '{delegateType.FullName}' is not a delegate type.", nameof(delegateType));

        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType.FullName}' does not have an Invoke method.");

        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        if (parameterTypes.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports delegates with up to 8 parameters.");

        if (parameterTypes.Any(type => type.IsByRef))
            throw new NotSupportedException("Dalamud IPC delegates cannot use ref or out parameters.");

        if (invokeMethod.ReturnType != typeof(void))
            throw new NotSupportedException("Attributed IPC events must use delegates that return void.");
    }

    internal static bool IsConsumerWrapperType(Type type, out Type? delegateType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NoireIpcConsumer<>))
        {
            var candidate = type.GetGenericArguments()[0];
            if (IsDelegateType(candidate))
            {
                delegateType = candidate;
                return true;
            }
        }

        delegateType = null;
        return false;
    }

    internal static Delegate CreateDelegate(object? target, MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainsGenericParameters)
            throw new NotSupportedException($"Method '{method.DeclaringType?.FullName}.{method.Name}' cannot be registered because it contains open generic parameters.");

        var parameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        if (parameterTypes.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports delegates with up to 8 parameters.");

        if (parameterTypes.Any(type => type.IsByRef))
            throw new NotSupportedException("Dalamud IPC delegates cannot use ref or out parameters.");

        var delegateType = method.ReturnType == typeof(void)
            ? parameterTypes.Length == 0
                ? typeof(Action)
                : Expression.GetActionType(parameterTypes)
            : Expression.GetFuncType([.. parameterTypes, method.ReturnType]);

        return method.IsStatic
            ? method.CreateDelegate(delegateType)
            : target != null
                ? method.CreateDelegate(delegateType, target)
                : throw new ArgumentNullException(nameof(target), $"An instance is required to register method '{method.DeclaringType?.FullName}.{method.Name}'.");
    }

    internal static Delegate CreateConsumerDelegate(string fullName, MethodInfo method, Type messageResultType)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainsGenericParameters)
            throw new NotSupportedException($"Method '{method.DeclaringType?.FullName}.{method.Name}' cannot be bound as a consumer because it contains open generic parameters.");

        var parameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        if (parameterTypes.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports delegates with up to 8 parameters.");

        if (parameterTypes.Any(type => type.IsByRef))
            throw new NotSupportedException("Dalamud IPC delegates cannot use ref or out parameters.");

        if (method.ReturnType == typeof(void))
        {
            var callGateTypes = BuildCallGateTypes(parameterTypes, messageResultType);
            var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
            return BuildActionInvoker(subscriber, parameterTypes);
        }
        else
        {
            var callGateTypes = BuildCallGateTypes(parameterTypes, method.ReturnType);
            var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
            return BuildFuncInvoker(subscriber, parameterTypes, method.ReturnType);
        }
    }

    internal static object CreateConsumerWrapperForProperty(string fullName, Type wrapperType, Type delegateType, Type messageResultType)
    {
        ArgumentNullException.ThrowIfNull(wrapperType);
        ArgumentNullException.ThrowIfNull(delegateType);

        var consumerDelegate = CreateConsumerDelegateForProperty(fullName, delegateType, messageResultType);
        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType.FullName}' does not have an Invoke method.");
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var returnType = invokeMethod.ReturnType == typeof(void) ? (Type?)null : invokeMethod.ReturnType;

        var wrapperFactoryMethod = typeof(NoireIPC).GetMethod(nameof(CreateConsumerWrapperCore), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(delegateType);

        return wrapperFactoryMethod.Invoke(null, [fullName, parameterTypes, returnType, consumerDelegate])
            ?? throw new InvalidOperationException($"Failed to create IPC consumer wrapper for '{fullName}'.");
    }

    internal static object CreateEventWrapperForProperty(string fullName, Type wrapperType, Type delegateType, Type messageResultType)
    {
        ArgumentNullException.ThrowIfNull(wrapperType);
        ArgumentNullException.ThrowIfNull(delegateType);

        ValidateEventHandlerType(delegateType);

        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType.FullName}' does not have an Invoke method.");
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        var wrapperFactoryMethod = typeof(NoireIPC).GetMethod(nameof(CreateEventWrapperCore), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(delegateType);

        return wrapperFactoryMethod.Invoke(null, [fullName, messageResultType])
            ?? throw new InvalidOperationException($"Failed to create IPC event wrapper for '{fullName}'.");
    }

    internal static object CreateUnavailableConsumerWrapperForProperty(string fullName, Type wrapperType)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(wrapperType);

        var unavailableMethod = wrapperType.GetMethod(nameof(NoireIpcConsumer<Action>.Unavailable), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)])
            ?? throw new InvalidOperationException($"Wrapper type '{wrapperType.FullName}' does not expose a compatible Unavailable factory.");

        return unavailableMethod.Invoke(null, [fullName])
            ?? throw new InvalidOperationException($"Failed to create unavailable IPC consumer wrapper for '{fullName}'.");
    }

    internal static NoireIpcConsumerBinding BindEventPublisher(object? target, EventInfo eventInfo, Delegate publisherDelegate, string fullName)
    {
        ArgumentNullException.ThrowIfNull(eventInfo);
        ArgumentNullException.ThrowIfNull(publisherDelegate);

        var addMethod = eventInfo.GetAddMethod(nonPublic: true) ?? eventInfo.GetAddMethod(nonPublic: false)
            ?? throw new InvalidOperationException($"IPC event '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}' must have an add accessor.");
        var removeMethod = eventInfo.GetRemoveMethod(nonPublic: true) ?? eventInfo.GetRemoveMethod(nonPublic: false)
            ?? throw new InvalidOperationException($"IPC event '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}' must have a remove accessor.");

        if (addMethod.IsStatic)
            addMethod.Invoke(null, [publisherDelegate]);
        else
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), $"An instance is required to bind IPC event '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}'.");

            addMethod.Invoke(target, [publisherDelegate]);
        }

        NoireLogger.LogDebug($"Bound event publisher '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}' to '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");

        var binding = new NoireIpcConsumerBinding(
            fullName,
            () =>
            {
                NoireLogger.LogDebug($"Unbinding event publisher '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}' from '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
                if (removeMethod.IsStatic)
                    removeMethod.Invoke(null, [publisherDelegate]);
                else if (target != null)
                    removeMethod.Invoke(target, [publisherDelegate]);
            },
            HandleDisposed);

        TrackHandle(binding);
        return binding;
    }

    internal static Delegate CreateEventPublisherDelegate(string fullName, Type delegateType, Type messageResultType)
    {
        ValidateEventHandlerType(delegateType);

        var invokeMethod = delegateType.GetMethod("Invoke")!;
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var proxy = new NoireIpcEventPublisherProxy(fullName, parameterTypes, messageResultType);

        return BuildEventPublisherDelegate(proxy, delegateType, parameterTypes);
    }

    internal static Delegate CreateEventConsumerDelegate(object? target, EventInfo eventInfo, Delegate? publisherDelegate)
    {
        ArgumentNullException.ThrowIfNull(eventInfo);

        var eventHandlerType = eventInfo.EventHandlerType
            ?? throw new InvalidOperationException($"Event '{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}' does not declare a handler type.");
        ValidateEventHandlerType(eventHandlerType);

        var invokeMethod = eventHandlerType.GetMethod("Invoke")!;
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var backingField = ResolveEventBackingField(eventInfo);
        var proxy = new NoireIpcEventConsumerProxy(target, backingField, publisherDelegate);

        return BuildEventConsumerDelegate(proxy, eventHandlerType, parameterTypes);
    }

    internal static NoireIpcSubscription BindConsumerToEvent(object? target, EventInfo eventInfo, string fullName, Type messageResultType, Delegate? publisherDelegate = null)
    {
        ArgumentNullException.ThrowIfNull(eventInfo);

        var consumerDelegate = CreateEventConsumerDelegate(target, eventInfo, publisherDelegate);
        return SubscribeCore(fullName, consumerDelegate, messageResultType);
    }

    internal static Delegate CreateConsumerDelegateForProperty(string fullName, Type delegateType, Type messageResultType)
    {
        ArgumentNullException.ThrowIfNull(delegateType);

        if (!IsDelegateType(delegateType))
            throw new ArgumentException($"Type '{delegateType.FullName}' is not a delegate type.", nameof(delegateType));

        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{delegateType.FullName}' does not have an Invoke method.");

        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        if (parameterTypes.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports delegates with up to 8 parameters.");

        if (parameterTypes.Any(type => type.IsByRef))
            throw new NotSupportedException("Dalamud IPC delegates cannot use ref or out parameters.");

        if (invokeMethod.ReturnType == typeof(void))
        {
            var callGateTypes = BuildCallGateTypes(parameterTypes, messageResultType);
            var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
            var proxy = new NoireIpcConsumerProxy(fullName, parameterTypes, null, subscriber, isAction: true);
            return BuildActionInvokerWithProxy(proxy, parameterTypes);
        }
        else
        {
            var callGateTypes = BuildCallGateTypes(parameterTypes, invokeMethod.ReturnType);
            var subscriber = GetCallGateFactoryResult(SubscriberFactoryMethods, fullName, callGateTypes);
            var proxy = new NoireIpcConsumerProxy(fullName, parameterTypes, invokeMethod.ReturnType, subscriber, isAction: false);
            return BuildFuncInvokerWithProxy(proxy, parameterTypes, invokeMethod.ReturnType);
        }
    }

    internal static NoireIpcConsumerBinding BindConsumerToProperty(object? target, PropertyInfo property, object consumerValue, string fullName)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(consumerValue);

        var unboundValue = (IsConsumerWrapperType(property.PropertyType, out _))
            ? CreateUnavailableConsumerWrapperForProperty(fullName, property.PropertyType)
            : null;

        var setMethod = property.GetSetMethod(nonPublic: true) ?? property.GetSetMethod(nonPublic: false);
        if (setMethod == null)
            throw new InvalidOperationException($"Consumer property '{property.DeclaringType?.FullName}.{property.Name}' must have a setter.");

        if (!property.PropertyType.IsAssignableFrom(consumerValue.GetType()))
            throw new InvalidOperationException($"Consumer property '{property.DeclaringType?.FullName}.{property.Name}' type mismatch.");

        if (property.GetSetMethod()?.IsStatic == true || property.GetGetMethod()?.IsStatic == true)
        {
            setMethod.Invoke(null, [consumerValue]);
        }
        else
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), $"An instance is required to bind consumer property '{property.DeclaringType?.FullName}.{property.Name}'.");

            setMethod.Invoke(target, [consumerValue]);
        }

        NoireLogger.LogDebug($"Bound consumer property '{property.DeclaringType?.FullName}.{property.Name}' to '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");

        var binding = new NoireIpcConsumerBinding(
            fullName,
            () =>
            {
                NoireLogger.LogDebug($"Unbinding consumer property '{property.DeclaringType?.FullName}.{property.Name}' from '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
                if (property.GetSetMethod()?.IsStatic == true || property.GetGetMethod()?.IsStatic == true)
                    setMethod.Invoke(null, [unboundValue]);
                else if (target != null)
                    setMethod.Invoke(target, [unboundValue]);
            },
            HandleDisposed);

        TrackHandle(binding);
        return binding;
    }

    private static Delegate BuildActionInvoker(object subscriber, Type[] parameterTypes)
    {
        var delegateType = parameterTypes.Length == 0 ? typeof(Action) : Expression.GetActionType(parameterTypes);
        var parameters = parameterTypes.Select((type, i) => Expression.Parameter(type, $"arg{i}")).ToArray();

        var subscriberConst = Expression.Constant(subscriber);
        var invokeActionMethod = subscriber.GetType().GetMethod("InvokeAction", BindingFlags.Instance | BindingFlags.Public)!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            callExpression = Expression.Call(subscriberConst, invokeActionMethod);
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))));
            callExpression = Expression.Call(subscriberConst, invokeActionMethod, argsArray);
        }

        var lambda = Expression.Lambda(delegateType, callExpression, parameters);
        return lambda.Compile();
    }

    private static Delegate BuildFuncInvoker(object subscriber, Type[] parameterTypes, Type returnType)
    {
        var delegateType = Expression.GetFuncType([.. parameterTypes, returnType]);
        var parameters = parameterTypes.Select((type, i) => Expression.Parameter(type, $"arg{i}")).ToArray();

        var subscriberConst = Expression.Constant(subscriber);
        var invokeFuncMethod = subscriber.GetType().GetMethod("InvokeFunc", BindingFlags.Instance | BindingFlags.Public)!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            callExpression = Expression.Call(subscriberConst, invokeFuncMethod);
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))));
            callExpression = Expression.Call(subscriberConst, invokeFuncMethod, argsArray);
        }

        var castExpression = Expression.Convert(callExpression, returnType);
        var lambda = Expression.Lambda(delegateType, castExpression, parameters);
        return lambda.Compile();
    }

    private static Delegate BuildActionInvokerWithProxy(NoireIpcConsumerProxy proxy, Type[] parameterTypes)
    {
        var delegateType = parameterTypes.Length == 0 ? typeof(Action) : Expression.GetActionType(parameterTypes);
        var parameters = parameterTypes.Select((type, i) => Expression.Parameter(type, $"arg{i}")).ToArray();

        var proxyConst = Expression.Constant(proxy);
        var invokeMethod = typeof(NoireIpcConsumerProxy).GetMethod(nameof(NoireIpcConsumerProxy.Invoke))!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            var emptyArray = Expression.Constant(Array.Empty<object>());
            callExpression = Expression.Call(proxyConst, invokeMethod, emptyArray);
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))));
            callExpression = Expression.Call(proxyConst, invokeMethod, argsArray);
        }

        var lambda = Expression.Lambda(delegateType, callExpression, parameters);
        return NoireIpcExtensions.TrackConsumer(lambda.Compile(), proxy);
    }

    private static Delegate BuildEventPublisherDelegate(NoireIpcEventPublisherProxy proxy, Type delegateType, Type[] parameterTypes)
    {
        var parameters = parameterTypes.Select((type, index) => Expression.Parameter(type, $"arg{index}")).ToArray();
        var proxyConst = Expression.Constant(proxy);
        var sendMethod = typeof(NoireIpcEventPublisherProxy).GetMethod(nameof(NoireIpcEventPublisherProxy.Send))!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            callExpression = Expression.Call(proxyConst, sendMethod, Expression.Constant(Array.Empty<object>()));
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));
            callExpression = Expression.Call(proxyConst, sendMethod, argsArray);
        }

        return Expression.Lambda(delegateType, callExpression, parameters).Compile();
    }

    private static Delegate BuildEventConsumerDelegate(NoireIpcEventConsumerProxy proxy, Type delegateType, Type[] parameterTypes)
    {
        var parameters = parameterTypes.Select((type, index) => Expression.Parameter(type, $"arg{index}")).ToArray();
        var proxyConst = Expression.Constant(proxy);
        var raiseMethod = typeof(NoireIpcEventConsumerProxy).GetMethod(nameof(NoireIpcEventConsumerProxy.Raise))!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            callExpression = Expression.Call(proxyConst, raiseMethod, Expression.Constant(Array.Empty<object>()));
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));
            callExpression = Expression.Call(proxyConst, raiseMethod, argsArray);
        }

        return Expression.Lambda(delegateType, callExpression, parameters).Compile();
    }

    private static Delegate BuildFuncInvokerWithProxy(NoireIpcConsumerProxy proxy, Type[] parameterTypes, Type returnType)
    {
        var delegateType = Expression.GetFuncType([.. parameterTypes, returnType]);
        var parameters = parameterTypes.Select((type, i) => Expression.Parameter(type, $"arg{i}")).ToArray();

        var proxyConst = Expression.Constant(proxy);
        var invokeMethod = typeof(NoireIpcConsumerProxy).GetMethod(nameof(NoireIpcConsumerProxy.Invoke))!;

        Expression callExpression;
        if (parameters.Length == 0)
        {
            var emptyArray = Expression.Constant(Array.Empty<object>());
            callExpression = Expression.Call(proxyConst, invokeMethod, emptyArray);
        }
        else
        {
            var argsArray = Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))));
            callExpression = Expression.Call(proxyConst, invokeMethod, argsArray);
        }

        var castExpression = Expression.Convert(callExpression, returnType);
        var lambda = Expression.Lambda(delegateType, castExpression, parameters);
        return NoireIpcExtensions.TrackConsumer(lambda.Compile(), proxy);
    }

    internal static NoireIpcConsumerBinding BindConsumerDelegate(object? target, MethodInfo method, Delegate consumerDelegate, string fullName)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(consumerDelegate);

        FieldInfo? targetField;
        if (method.IsStatic)
        {
            var declaringType = method.DeclaringType
                ?? throw new InvalidOperationException($"Cannot determine declaring type for static method '{method.Name}'.");

            var backingFieldName = $"<{method.Name}>k__BackingField";
            targetField = declaringType.GetField(backingFieldName, BindingFlags.Static | BindingFlags.NonPublic);

            if (targetField == null || !targetField.FieldType.IsAssignableFrom(consumerDelegate.GetType()))
            {
                throw new InvalidOperationException(
                    $"Consumer method '{declaringType.FullName}.{method.Name}' must be a static auto-property with a compatible delegate type. " +
                    $"Example: public static Action? {method.Name} {{ get; set; }}");
            }

            targetField.SetValue(null, consumerDelegate);
        }
        else
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), $"An instance is required to bind consumer method '{method.DeclaringType?.FullName}.{method.Name}'.");

            var declaringType = method.DeclaringType!;
            var backingFieldName = $"<{method.Name}>k__BackingField";
            targetField = declaringType.GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (targetField == null || !targetField.FieldType.IsAssignableFrom(consumerDelegate.GetType()))
            {
                throw new InvalidOperationException(
                    $"Consumer method '{declaringType.FullName}.{method.Name}' must be an auto-property with a compatible delegate type. " +
                    $"Example: public Action? {method.Name} {{ get; set; }}");
            }

            targetField.SetValue(target, consumerDelegate);
        }

        NoireLogger.LogDebug($"Bound consumer delegate '{method.DeclaringType?.FullName}.{method.Name}' to '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");

        var binding = new NoireIpcConsumerBinding(
            fullName,
            () =>
            {
                NoireLogger.LogDebug($"Unbinding consumer delegate '{method.DeclaringType?.FullName}.{method.Name}' from '{fullName}'.", $"[{typeof(NoireIPC).Name}] ");
                if (method.IsStatic)
                    targetField!.SetValue(null, null);
                else
                    targetField!.SetValue(target, null);
            },
            HandleDisposed);

        TrackHandle(binding);
        return binding;
    }

    private static void EnsureInitialized()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib must be initialized before using NoireIPC.");

        EnsureDisposeHook();
    }

    private static void EnsureDisposeHook()
    {
        if (_disposeHookRegistered)
            return;

        lock (SyncRoot)
        {
            if (_disposeHookRegistered)
                return;

            if (!NoireLibMain.RegisterOnDispose(DisposeKey, DisposeOwnedHandles, int.MaxValue))
                throw new InvalidOperationException("Failed to register NoireIPC automatic disposal callback.");

            _disposeHookRegistered = true;
        }
    }

    private static NoireIpcRegistrationKind NormalizeRegistrationKind(Delegate handler, NoireIpcRegistrationKind kind)
    {
        var invokeMethod = GetDelegateInvokeMethod(handler);
        return kind switch
        {
            NoireIpcRegistrationKind.Auto when invokeMethod.ReturnType == typeof(void) => NoireIpcRegistrationKind.Action,
            NoireIpcRegistrationKind.Auto => NoireIpcRegistrationKind.Function,
            NoireIpcRegistrationKind.Action when invokeMethod.ReturnType != typeof(void) => throw new ArgumentException("Action IPC registrations must target delegates that return void.", nameof(handler)),
            NoireIpcRegistrationKind.Function when invokeMethod.ReturnType == typeof(void) => throw new ArgumentException("Function IPC registrations must target delegates with a return value.", nameof(handler)),
            _ => kind,
        };
    }

    private static object GetCallGateFactoryResult(IReadOnlyDictionary<int, MethodInfo> factories, string fullName, Type[] callGateTypes)
    {
        EnsureInitialized();
        ValidateCallGateTypes(callGateTypes);

        if (!factories.TryGetValue(callGateTypes.Length, out var factoryMethod))
            throw new NotSupportedException($"Dalamud IPC only supports signatures with up to {ProviderFactoryMethods.Keys.Max() - 1} parameters.");

        var genericMethod = factoryMethod.MakeGenericMethod(callGateTypes);
        return genericMethod.Invoke(NoireService.PluginInterface, [fullName])
            ?? throw new InvalidOperationException($"Failed to resolve IPC call gate '{fullName}'.");
    }

    internal static object? InvokeInstanceMethod(object target, string methodName, params object?[] arguments)
    {
        var method = FindBestInstanceMethod(target.GetType(), methodName, arguments)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);

        return method.Invoke(target, arguments);
    }

    private static MethodInfo? FindBestInstanceMethod(Type targetType, string methodName, object?[] arguments)
    {
        var candidates = targetType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(candidate => candidate.Name == methodName && !candidate.ContainsGenericParameters && candidate.GetParameters().Length == arguments.Length)
            .Select(candidate => new { Method = candidate, Score = ScoreMethod(candidate, arguments) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => GetInheritanceDepth(candidate.Method.DeclaringType))
            .Select(candidate => candidate.Method)
            .ToArray();

        return candidates.FirstOrDefault();
    }

    private static int ScoreMethod(MethodInfo method, object?[] arguments)
    {
        var parameters = method.GetParameters();
        var score = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            var argument = arguments[i];

            if (argument == null)
            {
                if (!CanAcceptNull(parameterType))
                    return -1;

                score += 1;
                continue;
            }

            var argumentType = argument.GetType();
            if (parameterType == argumentType)
            {
                score += 8;
                continue;
            }

            if (parameterType.IsAssignableFrom(argumentType))
            {
                score += 4;
                continue;
            }

            return -1;
        }

        return score;
    }

    private static bool CanAcceptNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    private static int GetInheritanceDepth(Type? type)
    {
        var depth = 0;
        while (type != null)
        {
            depth++;
            type = type.BaseType;
        }

        return depth;
    }

    private static void TrackHandle(NoireIpcHandle handle)
    {
        lock (SyncRoot)
        {
            if (!OwnedHandles.Contains(handle))
                OwnedHandles.Add(handle);
        }
    }

    private static void HandleDisposed(NoireIpcHandle handle)
    {
        lock (SyncRoot)
            OwnedHandles.Remove(handle);
    }

    private static Type[] BuildCallGateTypes(Delegate handler, NoireIpcRegistrationKind kind, Type messageResultType)
    {
        var invokeMethod = GetDelegateInvokeMethod(handler);
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        return kind == NoireIpcRegistrationKind.Function
            ? BuildCallGateTypes(parameterTypes, invokeMethod.ReturnType)
            : BuildCallGateTypes(parameterTypes, messageResultType);
    }

    private static Type[] BuildMessageCallGateTypes(Delegate handler, Type messageResultType)
    {
        var invokeMethod = GetDelegateInvokeMethod(handler);
        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        return BuildMessageCallGateTypes(parameterTypes, messageResultType);
    }

    private static Type[] BuildMessageCallGateTypes(Type[] parameterTypes, Type messageResultType)
    {
        return BuildCallGateTypes(parameterTypes, messageResultType);
    }

    private static Type[] BuildCallGateTypes(Type[] parameterTypes, Type finalType)
    {
        ValidateParameterTypes(parameterTypes);
        var validatedFinalType = ValidateMessageResultType(finalType);
        return [.. parameterTypes, validatedFinalType];
    }

    private static void ValidateCallGateTypes(Type[] callGateTypes)
    {
        if (callGateTypes == null)
            throw new ArgumentNullException(nameof(callGateTypes));

        if (callGateTypes.Length is < 1 or > 9)
            throw new NotSupportedException("Dalamud IPC only supports signatures containing between 0 and 8 parameters.");

        if (callGateTypes.Any(type => type == null))
            throw new ArgumentException("IPC call gate types cannot contain null entries.", nameof(callGateTypes));
    }

    private static void ValidateParameterTypes(Type[] parameterTypes)
    {
        if (parameterTypes == null)
            throw new ArgumentNullException(nameof(parameterTypes));

        if (parameterTypes.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports up to 8 parameters.");

        if (parameterTypes.Any(type => type == null))
            throw new ArgumentException("IPC parameter types cannot contain null entries.", nameof(parameterTypes));
    }

    private static void ValidateInvocationSignature(Type[] parameterTypes, object?[] arguments)
    {
        ValidateParameterTypes(parameterTypes);

        if (arguments == null)
            throw new ArgumentNullException(nameof(arguments));

        if (parameterTypes.Length != arguments.Length)
            throw new ArgumentException("The number of supplied IPC arguments must match the number of supplied parameter types.");
    }

    private static void ValidateDelegate(Delegate handler, bool allowVoidReturn)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var method = GetDelegateInvokeMethod(handler);
        var parameters = method.GetParameters();

        if (parameters.Length > 8)
            throw new NotSupportedException("Dalamud IPC only supports delegates with up to 8 parameters.");

        if (parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut))
            throw new NotSupportedException("Dalamud IPC delegates cannot use ref or out parameters.");

        if (!allowVoidReturn && method.ReturnType == typeof(void))
            throw new ArgumentException("The supplied delegate must return a value.", nameof(handler));
    }

    private static MethodInfo GetDelegateInvokeMethod(Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return handler.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Delegate type '{handler.GetType().FullName}' does not expose an Invoke method.");
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name), "IPC name cannot be null, empty or whitespace.");

        return name.Trim().TrimStart(NameSeparator.ToCharArray());
    }

    private static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        return prefix.Trim().TrimEnd(NameSeparator.ToCharArray());
    }

    private static void ValidateSeparator(string separator)
    {
        if (string.IsNullOrWhiteSpace(separator))
            throw new ArgumentNullException(nameof(separator), "The IPC name separator cannot be null, empty or whitespace.");
    }

    private static FieldInfo ResolveEventBackingField(EventInfo eventInfo)
    {
        var declaringType = eventInfo.DeclaringType
            ?? throw new InvalidOperationException($"Cannot determine declaring type for event '{eventInfo.Name}'.");

        var eventHandlerType = eventInfo.EventHandlerType
            ?? throw new InvalidOperationException($"Event '{declaringType.FullName}.{eventInfo.Name}' does not declare a handler type.");

        var bindingFlags = BindingFlags.NonPublic | (eventInfo.AddMethod?.IsStatic == true ? BindingFlags.Static : BindingFlags.Instance);
        var backingField = declaringType.GetField(eventInfo.Name, bindingFlags)
            ?? declaringType.GetField($"<{eventInfo.Name}>k__BackingField", bindingFlags);

        if (backingField == null || !eventHandlerType.IsAssignableFrom(backingField.FieldType))
            throw new InvalidOperationException($"Attributed IPC event '{declaringType.FullName}.{eventInfo.Name}' must be an auto-event with a compatible backing field.");

        return backingField;
    }

    private static NoireIpcConsumer<TDelegate> CreateConsumerWrapperCore<TDelegate>(string fullName, Type[] parameterTypes, Type? returnType, Delegate consumerDelegate)
        where TDelegate : Delegate
        => new(fullName, parameterTypes, returnType, () => (TDelegate)consumerDelegate);

    private static NoireIpcEventConsumer<TDelegate> CreateEventWrapperCore<TDelegate>(string fullName, Type messageResultType)
        where TDelegate : Delegate
        => new(fullName, messageResultType);

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null).Cast<Type>().ToArray();
        }
    }

    #endregion

    private static void DisposeOwnedHandles()
    {
        NoireIpcHandle[] handles;
        lock (SyncRoot)
            handles = [.. OwnedHandles];

        foreach (var handle in handles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to dispose IPC handle for '{handle.FullName}'.");
            }
        }

        lock (SyncRoot)
            OwnedHandles.Clear();

        ResetConfiguration();
    }
}
