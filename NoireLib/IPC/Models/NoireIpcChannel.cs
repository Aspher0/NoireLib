using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a strongly configured wrapper around a single IPC channel name.
/// </summary>
public sealed class NoireIpcChannel
{
    internal NoireIpcChannel(string fullName, Type messageResultType)
    {
        FullName = fullName;
        MessageResultType = messageResultType;
    }

    /// <summary>
    /// Gets the fully qualified IPC channel name.
    /// </summary>
    /// <returns>The fully qualified IPC channel name.</returns>
    public string FullName { get; }

    /// <summary>
    /// Gets the trailing generic type used when treating the channel as a message channel.
    /// </summary>
    /// <returns>The message result type associated with the channel.</returns>
    public Type MessageResultType { get; }

    /// <summary>
    /// Registers a provider delegate for the channel.
    /// </summary>
    /// <param name="handler">The delegate to expose through IPC.</param>
    /// <param name="kind">The registration kind to use. When set to <see cref="NoireIpcRegistrationKind.Auto"/>, the delegate return type is used to infer the correct registration type.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration Register(Delegate handler, NoireIpcRegistrationKind kind = NoireIpcRegistrationKind.Auto)
        => NoireIPC.RegisterCore(FullName, handler, kind, MessageResultType);

    /// <summary>
    /// Registers an action provider for the channel.
    /// </summary>
    /// <param name="handler">The action delegate to expose through IPC.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration RegisterAction(Delegate handler)
        => Register(handler, NoireIpcRegistrationKind.Action);

    /// <summary>
    /// Registers a function provider for the channel.
    /// </summary>
    /// <param name="handler">The function delegate to expose through IPC.</param>
    /// <returns>A handle that can be disposed early to unregister the provider before plugin shutdown.</returns>
    public NoireIpcRegistration RegisterFunc(Delegate handler)
        => Register(handler, NoireIpcRegistrationKind.Function);

    /// <summary>
    /// Subscribes a message handler to the channel.
    /// </summary>
    /// <param name="handler">The delegate to invoke when another plugin publishes a message to the channel.</param>
    /// <returns>A handle that can be disposed early to unsubscribe before plugin shutdown.</returns>
    public NoireIpcSubscription Subscribe(Delegate handler)
        => NoireIPC.SubscribeCore(FullName, handler, MessageResultType);

    /// <summary>
    /// Sends a message through the channel using inferred argument types.
    /// </summary>
    /// <param name="arguments">The message payload arguments.</param>
    public void Send(params object?[] arguments)
        => Send(NoireIPC.InferParameterTypes(arguments), arguments);

    /// <summary>
    /// Sends a message through the channel using explicit argument types.
    /// </summary>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The message payload arguments.</param>
    public void Send(Type[] parameterTypes, params object?[] arguments)
        => NoireIPC.SendCore(FullName, parameterTypes, MessageResultType, arguments);

    /// <summary>
    /// Invokes an action IPC on another plugin using inferred argument types.
    /// </summary>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public void InvokeAction(params object?[] arguments)
        => InvokeAction(NoireIPC.InferParameterTypes(arguments), arguments);

    /// <summary>
    /// Invokes an action IPC on another plugin using explicit argument types.
    /// </summary>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The IPC arguments to pass to the action.</param>
    public void InvokeAction(Type[] parameterTypes, params object?[] arguments)
        => NoireIPC.InvokeActionCore(FullName, parameterTypes, MessageResultType, arguments);

    /// <summary>
    /// Invokes a function IPC on another plugin using inferred argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public TResult InvokeFunc<TResult>(params object?[] arguments)
        => InvokeFunc<TResult>(NoireIPC.InferParameterTypes(arguments), arguments);

    /// <summary>
    /// Invokes a function IPC on another plugin using explicit argument types.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="parameterTypes">The explicit IPC parameter types for <paramref name="arguments"/>.</param>
    /// <param name="arguments">The IPC arguments to pass to the function.</param>
    /// <returns>The value returned by the remote IPC function.</returns>
    public TResult InvokeFunc<TResult>(Type[] parameterTypes, params object?[] arguments)
        => NoireIPC.InvokeFuncCore<TResult>(FullName, parameterTypes, arguments);

    /// <summary>
    /// Resolves the raw Dalamud provider for the channel.
    /// </summary>
    /// <param name="callGateTypes">The exact generic type arguments required by the underlying Dalamud call gate.</param>
    /// <returns>The raw Dalamud provider instance.</returns>
    public object GetRawProvider(params Type[] callGateTypes)
        => NoireIPC.GetRawProvider(FullName, callGateTypes);

    /// <summary>
    /// Resolves the raw Dalamud subscriber for the channel.
    /// </summary>
    /// <param name="callGateTypes">The exact generic type arguments required by the underlying Dalamud call gate.</param>
    /// <returns>The raw Dalamud subscriber instance.</returns>
    public object GetRawSubscriber(params Type[] callGateTypes)
        => NoireIPC.GetRawSubscriber(FullName, callGateTypes);

    /// <summary>
    /// Checks whether an IPC provider is currently available for the channel with the specified signature.
    /// </summary>
    /// <param name="parameterTypes">The parameter types expected by the IPC signature.</param>
    /// <param name="returnType">The return type expected by the IPC signature. Use <see langword="null"/> or <see cref="System.Object"/> for action channels.</param>
    /// <returns><see langword="true"/> if the IPC provider is available; otherwise, <see langword="false"/>.</returns>
    public bool IsAvailable(Type[] parameterTypes, Type? returnType = null)
        => NoireIPC.IsAvailable(FullName, parameterTypes, returnType, prefix: null, useDefaultPrefix: false);

    /// <summary>
    /// Checks whether an action IPC provider is currently available for the channel.
    /// </summary>
    /// <param name="parameterTypes">The parameter types expected by the action signature.</param>
    /// <returns><see langword="true"/> if the action provider is available; otherwise, <see langword="false"/>.</returns>
    public bool IsActionAvailable(params Type[] parameterTypes)
        => IsAvailable(parameterTypes, typeof(object));

    /// <summary>
    /// Checks whether a function IPC provider is currently available for the channel.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="parameterTypes">The parameter types expected by the function signature.</param>
    /// <returns><see langword="true"/> if the function provider is available; otherwise, <see langword="false"/>.</returns>
    public bool IsFuncAvailable<TResult>(params Type[] parameterTypes)
        => IsAvailable(parameterTypes, typeof(TResult));
}
