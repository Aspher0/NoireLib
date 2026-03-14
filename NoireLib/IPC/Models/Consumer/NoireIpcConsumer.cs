using System;
using System.Linq;
using System.Reflection;

namespace NoireLib.IPC;

/// <summary>
/// Provides a safe wrapper around IPC consumer delegates with built-in availability checking.
/// </summary>
/// <typeparam name="TDelegate">The delegate type for the IPC consumer.</typeparam>
public sealed class NoireIpcConsumer<TDelegate> where TDelegate : Delegate
{
    private sealed class BindingState
    {
        public BindingState(TDelegate? @delegate, Exception? error)
        {
            Delegate = @delegate;
            Error = error;
        }

        public TDelegate? Delegate { get; }

        public Exception? Error { get; }
    }

    private readonly string _fullName;
    private readonly Type[] _parameterTypes;
    private readonly Type? _returnType;
    private readonly Lazy<BindingState> _binding;

    internal NoireIpcConsumer(string fullName, Type[] parameterTypes, Type? returnType, Func<TDelegate> delegateFactory)
    {
        _fullName = fullName;
        _parameterTypes = parameterTypes;
        _returnType = returnType;
        _binding = new Lazy<BindingState>(() =>
        {
            try
            {
                return new BindingState(delegateFactory(), null);
            }
            catch (Exception ex)
            {
                return new BindingState(null, ex);
            }
        });
    }

    /// <summary>
    /// Creates a consumer wrapper that represents an unavailable IPC binding.
    /// </summary>
    /// <param name="fullName">The logical IPC name to associate with the wrapper.</param>
    /// <returns>An unavailable consumer wrapper.</returns>
    internal static NoireIpcConsumer<TDelegate> Unavailable(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        var invokeMethod = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type '{typeof(TDelegate).FullName}' does not have an Invoke method.");

        var parameterTypes = invokeMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var returnType = invokeMethod.ReturnType == typeof(void) ? (Type?)null : invokeMethod.ReturnType;

        return new NoireIpcConsumer<TDelegate>(fullName, parameterTypes, returnType, () => throw new InvalidOperationException($"IPC '{fullName}' is not currently bound."));
    }

    /// <summary>
    /// Gets the fully qualified IPC channel name.
    /// </summary>
    public string FullName => _fullName;

    /// <summary>
    /// Gets the exception captured while resolving the consumer delegate, if any.
    /// </summary>
    public Exception? BindingError => _binding.Value.Error;

    /// <summary>
    /// Gets a value indicating whether the remote IPC provider is currently available.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                var binding = _binding.Value;
                return binding.Delegate != null
                    && binding.Error == null
                    && NoireIPC.IsAvailable(_fullName, _parameterTypes, _returnType, prefix: null, useDefaultPrefix: false);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the underlying delegate for direct invocation. Returns <see langword="null"/> if the provider is not available.
    /// </summary>
    public TDelegate? Delegate => _binding.Value.Delegate;

    /// <summary>
    /// Implicitly converts the consumer to its underlying delegate.
    /// </summary>
    public static implicit operator TDelegate?(NoireIpcConsumer<TDelegate> consumer) => consumer?.Delegate;

    /// <summary>
    /// Throws if the IPC consumer is not available.
    /// </summary>
    public void EnsureAvailable()
        => GetRequiredDelegate();

    /// <summary>
    /// Invokes the consumer delegate and returns the raw result, if any.
    /// </summary>
    /// <param name="args">The arguments to pass to the IPC.</param>
    /// <returns>The raw invocation result.</returns>
    public object? InvokeRaw(params object?[] args)
    {
        var @delegate = GetRequiredDelegate();

        try
        {
            return @delegate.DynamicInvoke(args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException($"IPC '{_fullName}' invocation failed.", ex.InnerException);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"IPC '{_fullName}' invocation failed.", ex);
        }
    }

    /// <summary>
    /// Invokes the consumer delegate and converts the result to the requested type.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="args">The arguments to pass to the IPC.</param>
    /// <returns>The typed invocation result.</returns>
    public TResult InvokeRaw<TResult>(params object?[] args)
    {
        var result = InvokeRaw(args);

        if (result is TResult typedResult)
            return typedResult;

        if (result == null && default(TResult) == null)
            return default!;

        throw new InvalidCastException($"IPC '{_fullName}' returned {result?.GetType().FullName ?? "null"} instead of {typeof(TResult).FullName}.");
    }

    /// <summary>
    /// Attempts to invoke the IPC consumer safely.
    /// </summary>
    /// <param name="result">The result of the invocation, or default if unavailable or invocation fails.</param>
    /// <param name="args">The arguments to pass to the IPC.</param>
    /// <returns><see langword="true"/> if the invocation succeeded; otherwise, <see langword="false"/>.</returns>
    public bool TryInvokeRaw<TResult>(out TResult? result, params object?[] args)
    {
        try
        {
            result = InvokeRaw<TResult>(args);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to invoke the IPC consumer safely.
    /// </summary>
    /// <param name="args">The arguments to pass to the IPC action.</param>
    /// <returns><see langword="true"/> if the invocation succeeded; otherwise, <see langword="false"/>.</returns>
    public bool TryInvokeRaw(params object?[] args)
    {
        try
        {
            InvokeRaw(args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Invokes the consumer delegate with the specified arguments, or returns a default value if unavailable.
    /// </summary>
    /// <typeparam name="TResult">The expected return type.</typeparam>
    /// <param name="defaultValue">The value to return if the IPC is unavailable or invocation fails.</param>
    /// <param name="args">The arguments to pass to the IPC.</param>
    /// <returns>The IPC result or <paramref name="defaultValue"/>.</returns>
    public TResult InvokeRawOrDefault<TResult>(TResult defaultValue = default!, params object?[] args)
    {
        try
        {
            return InvokeRaw<TResult>(args);
        }
        catch
        {
            return defaultValue;
        }
    }

    private TDelegate GetRequiredDelegate()
    {
        var binding = _binding.Value;

        if (binding.Error != null)
            throw new InvalidOperationException($"IPC '{_fullName}' failed to bind to delegate type '{typeof(TDelegate).FullName}'.", binding.Error);

        if (binding.Delegate == null)
            throw CreateUnavailableException();

        if (!NoireIPC.IsAvailable(_fullName, _parameterTypes, _returnType, prefix: null, useDefaultPrefix: false))
            throw CreateUnavailableException();

        return binding.Delegate;
    }

    private InvalidOperationException CreateUnavailableException()
    {
        var parameterList = _parameterTypes.Length == 0
            ? string.Empty
            : string.Join(", ", _parameterTypes.Select(type => type.Name));
        var returnTypeName = _returnType?.Name ?? "void";
        var signature = $"({parameterList}) -> {returnTypeName}";

        return new InvalidOperationException($"IPC '{_fullName}' is not currently available for delegate type '{typeof(TDelegate).FullName}' with signature {signature}.");
    }
}
