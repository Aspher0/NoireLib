using System;

namespace NoireLib.IPC;

/// <summary>
/// Provides strongly typed invocation helpers for <see cref="NoireIpcConsumer{TDelegate}"/> wrappers.
/// </summary>
public static class NoireIpcConsumerInvokeExtensions
{
    public static bool TryInvoke(this NoireIpcConsumer<Action> consumer)
        => consumer.TryInvokeRaw(Array.Empty<object?>());

    public static void Invoke(this NoireIpcConsumer<Action> consumer)
        => consumer.InvokeRaw(Array.Empty<object?>());

    public static bool TryInvoke<TResult>(this NoireIpcConsumer<Func<TResult>> consumer, out TResult? result)
        => consumer.TryInvokeRaw(out result, Array.Empty<object?>());

    public static TResult Invoke<TResult>(this NoireIpcConsumer<Func<TResult>> consumer)
        => consumer.InvokeRaw<TResult>(Array.Empty<object?>());

    public static TResult InvokeOrDefault<TResult>(this NoireIpcConsumer<Func<TResult>> consumer, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, Array.Empty<object?>());

    public static void Invoke<T1>(this NoireIpcConsumer<Action<T1>> consumer, T1 arg1)
        => consumer.InvokeRaw(arg1);

    public static bool TryInvoke<T1>(this NoireIpcConsumer<Action<T1>> consumer, T1 arg1)
        => consumer.TryInvokeRaw(arg1);

    public static TResult Invoke<T1, TResult>(this NoireIpcConsumer<Func<T1, TResult>> consumer, T1 arg1)
        => consumer.InvokeRaw<TResult>(arg1);

    public static bool TryInvoke<T1, TResult>(this NoireIpcConsumer<Func<T1, TResult>> consumer, T1 arg1, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1);

    public static TResult InvokeOrDefault<T1, TResult>(this NoireIpcConsumer<Func<T1, TResult>> consumer, T1 arg1, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1);

    public static void Invoke<T1, T2>(this NoireIpcConsumer<Action<T1, T2>> consumer, T1 arg1, T2 arg2)
        => consumer.InvokeRaw(arg1, arg2);

    public static bool TryInvoke<T1, T2>(this NoireIpcConsumer<Action<T1, T2>> consumer, T1 arg1, T2 arg2)
        => consumer.TryInvokeRaw(arg1, arg2);

    public static TResult Invoke<T1, T2, TResult>(this NoireIpcConsumer<Func<T1, T2, TResult>> consumer, T1 arg1, T2 arg2)
        => consumer.InvokeRaw<TResult>(arg1, arg2);

    public static bool TryInvoke<T1, T2, TResult>(this NoireIpcConsumer<Func<T1, T2, TResult>> consumer, T1 arg1, T2 arg2, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2);

    public static TResult InvokeOrDefault<T1, T2, TResult>(this NoireIpcConsumer<Func<T1, T2, TResult>> consumer, T1 arg1, T2 arg2, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2);

    public static void Invoke<T1, T2, T3>(this NoireIpcConsumer<Action<T1, T2, T3>> consumer, T1 arg1, T2 arg2, T3 arg3)
        => consumer.InvokeRaw(arg1, arg2, arg3);

    public static bool TryInvoke<T1, T2, T3>(this NoireIpcConsumer<Action<T1, T2, T3>> consumer, T1 arg1, T2 arg2, T3 arg3)
        => consumer.TryInvokeRaw(arg1, arg2, arg3);

    public static TResult Invoke<T1, T2, T3, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3);

    public static bool TryInvoke<T1, T2, T3, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3);

    public static TResult InvokeOrDefault<T1, T2, T3, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3);

    public static void Invoke<T1, T2, T3, T4>(this NoireIpcConsumer<Action<T1, T2, T3, T4>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => consumer.InvokeRaw(arg1, arg2, arg3, arg4);

    public static bool TryInvoke<T1, T2, T3, T4>(this NoireIpcConsumer<Action<T1, T2, T3, T4>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => consumer.TryInvokeRaw(arg1, arg2, arg3, arg4);

    public static TResult Invoke<T1, T2, T3, T4, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3, arg4);

    public static bool TryInvoke<T1, T2, T3, T4, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3, arg4);

    public static TResult InvokeOrDefault<T1, T2, T3, T4, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3, arg4);

    public static void Invoke<T1, T2, T3, T4, T5>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        => consumer.InvokeRaw(arg1, arg2, arg3, arg4, arg5);

    public static bool TryInvoke<T1, T2, T3, T4, T5>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        => consumer.TryInvokeRaw(arg1, arg2, arg3, arg4, arg5);

    public static TResult Invoke<T1, T2, T3, T4, T5, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3, arg4, arg5);

    public static bool TryInvoke<T1, T2, T3, T4, T5, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3, arg4, arg5);

    public static TResult InvokeOrDefault<T1, T2, T3, T4, T5, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3, arg4, arg5);

    public static void Invoke<T1, T2, T3, T4, T5, T6>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        => consumer.InvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        => consumer.TryInvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6);

    public static TResult Invoke<T1, T2, T3, T4, T5, T6, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3, arg4, arg5, arg6);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3, arg4, arg5, arg6);

    public static TResult InvokeOrDefault<T1, T2, T3, T4, T5, T6, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3, arg4, arg5, arg6);

    public static void Invoke<T1, T2, T3, T4, T5, T6, T7>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6, T7>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        => consumer.InvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6, arg7);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6, T7>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6, T7>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        => consumer.TryInvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6, arg7);

    public static TResult Invoke<T1, T2, T3, T4, T5, T6, T7, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3, arg4, arg5, arg6, arg7);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6, T7, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3, arg4, arg5, arg6, arg7);

    public static TResult InvokeOrDefault<T1, T2, T3, T4, T5, T6, T7, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3, arg4, arg5, arg6, arg7);

    public static void Invoke<T1, T2, T3, T4, T5, T6, T7, T8>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6, T7, T8>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        => consumer.InvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6, T7, T8>(this NoireIpcConsumer<Action<T1, T2, T3, T4, T5, T6, T7, T8>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        => consumer.TryInvokeRaw(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

    public static TResult Invoke<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        => consumer.InvokeRaw<TResult>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

    public static bool TryInvoke<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, out TResult? result)
        => consumer.TryInvokeRaw(out result, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

    public static TResult InvokeOrDefault<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this NoireIpcConsumer<Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult>> consumer, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, TResult defaultValue = default!)
        => consumer.InvokeRawOrDefault(defaultValue, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
}
