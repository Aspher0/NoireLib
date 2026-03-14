using System;
using System.Runtime.CompilerServices;

namespace NoireLib.IPC;

/// <summary>
/// Provides extension methods for checking IPC availability.
/// </summary>
public static class NoireIpcExtensions
{
    private static readonly ConditionalWeakTable<Delegate, NoireIpcConsumerProxy> ConsumerProxies = new();

    /// <summary>
    /// Checks if the IPC associated with a consumer delegate is currently available.
    /// </summary>
    /// <param name="consumer">The consumer delegate to check.</param>
    /// <returns><see langword="true"/> if the IPC provider is available; otherwise, <see langword="false"/>.</returns>
    public static bool IsIpcAvailable(this Delegate? consumer)
    {
        if (consumer == null)
            return false;

        if (ConsumerProxies.TryGetValue(consumer, out var trackedProxy))
            return trackedProxy.IsAvailable();

        var target = consumer.Target;
        if (target is NoireIpcConsumerProxy proxy)
            return proxy.IsAvailable();

        return false;
    }

    internal static TDelegate TrackConsumer<TDelegate>(TDelegate consumer, NoireIpcConsumerProxy proxy)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(proxy);

        ConsumerProxies.Add(consumer, proxy);
        return consumer;
    }
}

internal sealed class NoireIpcEventPublisherProxy
{
    private readonly string _fullName;
    private readonly Type[] _parameterTypes;
    private readonly Type _messageResultType;

    public NoireIpcEventPublisherProxy(string fullName, Type[] parameterTypes, Type messageResultType)
    {
        _fullName = fullName;
        _parameterTypes = parameterTypes;
        _messageResultType = messageResultType;
    }

    public void Send(params object?[] args)
        => NoireIPC.SendCore(_fullName, _parameterTypes, _messageResultType, args);
}

internal sealed class NoireIpcEventConsumerProxy
{
    private readonly object? _target;
    private readonly System.Reflection.FieldInfo _backingField;
    private readonly Delegate? _publisherDelegate;

    public NoireIpcEventConsumerProxy(object? target, System.Reflection.FieldInfo backingField, Delegate? publisherDelegate)
    {
        _target = target;
        _backingField = backingField;
        _publisherDelegate = publisherDelegate;
    }

    public void Raise(params object?[] args)
    {
        var targetInstance = _backingField.IsStatic ? null : _target;
        var currentDelegate = _backingField.GetValue(targetInstance) as Delegate;
        if (currentDelegate == null)
            return;

        if (_publisherDelegate != null)
            currentDelegate = Delegate.Remove(currentDelegate, _publisherDelegate);

        currentDelegate?.DynamicInvoke(args);
    }
}

/// <summary>
/// Internal proxy used to wrap consumer delegate invocations with availability tracking.
/// </summary>
internal sealed class NoireIpcConsumerProxy
{
    private readonly string _fullName;
    private readonly Type[] _parameterTypes;
    private readonly Type? _returnType;
    private readonly object _subscriber;
    private readonly bool _isAction;

    public NoireIpcConsumerProxy(string fullName, Type[] parameterTypes, Type? returnType, object subscriber, bool isAction)
    {
        _fullName = fullName;
        _parameterTypes = parameterTypes;
        _returnType = returnType;
        _subscriber = subscriber;
        _isAction = isAction;
    }

    public string FullName => _fullName;

    public bool IsAvailable()
    {
        try
        {
            return NoireIPC.IsAvailable(_fullName, _parameterTypes, _returnType, prefix: null, useDefaultPrefix: false);
        }
        catch
        {
            return false;
        }
    }

    public object? Invoke(params object?[] args)
    {
        var methodName = _isAction ? "InvokeAction" : "InvokeFunc";
        try
        {
            return NoireIPC.InvokeInstanceMethod(_subscriber, methodName, args);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to invoke consumer call '{_fullName}'.", "[NoireIPC] ");
            throw;
        }
    }
}
