using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NoireLib.Hooking;

/// <summary>
/// Builds the delegate a hook installs: the consumer detour wrapped in a try/catch and optionally timed, emitted
/// as IL because detour signatures routinely carry pointer parameters that expression trees cannot express.
/// </summary>
internal static class DetourGuardFactory
{
    private static readonly MethodInfo GetTimestampMethod = typeof(Stopwatch).GetMethod(nameof(Stopwatch.GetTimestamp))!;

    /// <summary>
    /// Wraps a detour according to the guard mode, or returns it unchanged when no wrapper is needed.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="context">The state the wrapper reads at runtime.</param>
    /// <param name="mode">What the wrapper does when the detour throws.</param>
    /// <param name="guarded">Whether a wrapper was actually installed.</param>
    /// <returns>The delegate to install.</returns>
    public static TDelegate Wrap<TDelegate>(HookGuardContext<TDelegate> context, HookGuardMode mode, out bool guarded)
        where TDelegate : Delegate
    {
        guarded = false;

        if (mode == HookGuardMode.None && !context.CollectStats && context.FaultLimit <= 0)
            return context.Detour;

        try
        {
            var wrapper = Emit(context, mode);
            guarded = mode != HookGuardMode.None;
            return wrapper;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"Could not generate a fault guard for hook '{context.Name}', so it runs unguarded: {ex.Message}", HookLog.Prefix);
            return context.Detour;
        }
    }

    /// <summary>
    /// Builds a detour that only calls the original function, counting the call on the way through.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="context">The state the detour reads at runtime.</param>
    /// <returns>The passthrough detour.</returns>
    public static TDelegate CreatePassthrough<TDelegate>(HookGuardContext<TDelegate> context)
        where TDelegate : Delegate
    {
        var contextType = typeof(HookGuardContext<TDelegate>);
        var invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"'{typeof(TDelegate).FullName}' declares no Invoke method.");

        var parameters = invoke.GetParameters();
        var returnType = invoke.ReturnType;

        var signature = new Type[parameters.Length + 1];
        signature[0] = contextType;
        for (var i = 0; i < parameters.Length; i++)
            signature[i + 1] = parameters[i].ParameterType;

        var method = new DynamicMethod(
            $"NoireHookObserver_{typeof(TDelegate).Name}",
            returnType,
            signature,
            contextType.Module,
            skipVisibility: true);

        var il = method.GetILGenerator();
        var originalField = contextType.GetField(nameof(HookGuardContext<Delegate>.Original))!;
        var afterCall = contextType.GetMethod(nameof(HookGuardContext<Delegate>.AfterCall))!;

        var result = returnType == typeof(void) ? null : il.DeclareLocal(returnType);
        var startTimestamp = il.DeclareLocal(typeof(long));
        var done = il.DefineLabel();
        var hasOriginal = il.DefineLabel();

        il.Emit(OpCodes.Call, GetTimestampMethod);
        il.Emit(OpCodes.Stloc, startTimestamp);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, originalField);
        il.Emit(OpCodes.Brtrue, hasOriginal);

        if (result != null)
        {
            EmitDefault(il, returnType, result);
            il.Emit(OpCodes.Stloc, result);
        }

        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, originalField);
        LoadArguments(il, parameters.Length);
        il.Emit(OpCodes.Callvirt, invoke);

        if (result != null)
            il.Emit(OpCodes.Stloc, result);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startTimestamp);
        il.Emit(OpCodes.Call, afterCall);

        il.MarkLabel(done);

        if (result != null)
            il.Emit(OpCodes.Ldloc, result);

        il.Emit(OpCodes.Ret);

        return (TDelegate)method.CreateDelegate(typeof(TDelegate), context);
    }

    private static TDelegate Emit<TDelegate>(HookGuardContext<TDelegate> context, HookGuardMode mode)
        where TDelegate : Delegate
    {
        var contextType = typeof(HookGuardContext<TDelegate>);
        var invoke = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"'{typeof(TDelegate).FullName}' declares no Invoke method.");

        var parameters = invoke.GetParameters();
        var returnType = invoke.ReturnType;

        var signature = new Type[parameters.Length + 1];
        signature[0] = contextType;
        for (var i = 0; i < parameters.Length; i++)
            signature[i + 1] = parameters[i].ParameterType;

        var method = new DynamicMethod(
            $"NoireHookGuard_{typeof(TDelegate).Name}",
            returnType,
            signature,
            contextType.Module,
            skipVisibility: true);

        var il = method.GetILGenerator();

        var detourField = contextType.GetField(nameof(HookGuardContext<Delegate>.Detour))!;
        var originalField = contextType.GetField(nameof(HookGuardContext<Delegate>.Original))!;
        var afterCall = contextType.GetMethod(nameof(HookGuardContext<Delegate>.AfterCall))!;
        var onFault = contextType.GetMethod(nameof(HookGuardContext<Delegate>.OnFault))!;

        var result = returnType == typeof(void) ? null : il.DeclareLocal(returnType);
        var startTimestamp = context.CollectStats ? il.DeclareLocal(typeof(long)) : null;

        if (startTimestamp != null)
        {
            il.Emit(OpCodes.Call, GetTimestampMethod);
            il.Emit(OpCodes.Stloc, startTimestamp);
        }

        var leaveTarget = il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, detourField);
        LoadArguments(il, parameters.Length);
        il.Emit(OpCodes.Callvirt, invoke);

        if (result != null)
            il.Emit(OpCodes.Stloc, result);

        // Emitted even when stats are off: a call that succeeds has to clear the consecutive-fault count.
        il.Emit(OpCodes.Ldarg_0);

        if (startTimestamp != null)
            il.Emit(OpCodes.Ldloc, startTimestamp);
        else
            il.Emit(OpCodes.Ldc_I8, 0L);

        il.Emit(OpCodes.Call, afterCall);

        il.Emit(OpCodes.Leave, leaveTarget);

        il.BeginCatchBlock(typeof(Exception));

        var exception = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exception);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Call, onFault);

        EmitRecovery(il, mode, parameters.Length, originalField, invoke, returnType, result, leaveTarget);

        il.EndExceptionBlock();

        if (result != null)
            il.Emit(OpCodes.Ldloc, result);

        il.Emit(OpCodes.Ret);

        return (TDelegate)method.CreateDelegate(typeof(TDelegate), context);
    }

    private static void EmitRecovery(
        ILGenerator il,
        HookGuardMode mode,
        int parameterCount,
        FieldInfo originalField,
        MethodInfo invoke,
        Type returnType,
        LocalBuilder? result,
        Label leaveTarget)
    {
        if (mode == HookGuardMode.Rethrow)
        {
            il.Emit(OpCodes.Rethrow);
            return;
        }

        if (mode == HookGuardMode.CallOriginal)
        {
            var callOriginal = il.DefineLabel();
            var noOriginal = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, originalField);
            il.Emit(OpCodes.Brtrue, callOriginal);
            il.Emit(OpCodes.Br, noOriginal);

            il.MarkLabel(callOriginal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, originalField);
            LoadArguments(il, parameterCount);
            il.Emit(OpCodes.Callvirt, invoke);

            if (result != null)
                il.Emit(OpCodes.Stloc, result);

            il.Emit(OpCodes.Leave, leaveTarget);

            il.MarkLabel(noOriginal);
        }

        if (result != null)
        {
            EmitDefault(il, returnType, result);
            il.Emit(OpCodes.Stloc, result);
        }

        il.Emit(OpCodes.Leave, leaveTarget);
    }

    private static void LoadArguments(ILGenerator il, int parameterCount)
    {
        for (var i = 0; i < parameterCount; i++)
        {
            var index = i + 1;
            if (index <= byte.MaxValue)
                il.Emit(OpCodes.Ldarg_S, (byte)index);
            else
                il.Emit(OpCodes.Ldarg, (short)index);
        }
    }

    private static void EmitDefault(ILGenerator il, Type returnType, LocalBuilder result)
    {
        if (returnType.IsPointer || returnType == typeof(nint) || returnType == typeof(nuint))
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_U);
            return;
        }

        if (!returnType.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
            return;
        }

        il.Emit(OpCodes.Ldloca, result);
        il.Emit(OpCodes.Initobj, returnType);
        il.Emit(OpCodes.Ldloc, result);
    }
}
