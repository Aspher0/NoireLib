using System;
using System.Diagnostics;

namespace NoireLib.Hooking;

/// <summary>
/// Describes where the function a hook installs on lives, with one factory per resolution source and
/// <see cref="Deferred"/> for an address that does not exist yet.
/// </summary>
public sealed class HookTarget
{
    private HookTarget(HookTargetKind kind) => Kind = kind;

    /// <summary>
    /// Gets how this target locates the function.
    /// </summary>
    public HookTargetKind Kind { get; }

    /// <summary>
    /// Gets the raw pointer the target was built from: the function address, the virtual table, or the function pointer variable.
    /// </summary>
    public nint Pointer { get; private init; }

    /// <summary>
    /// Gets the virtual table slot index, or -1 when the target is not a virtual table entry.
    /// </summary>
    public int VtableSlot { get; private init; } = -1;

    /// <summary>
    /// Gets the module name for a symbol or import target.
    /// </summary>
    public string? ModuleName { get; private init; }

    /// <summary>
    /// Gets the exported or imported function name for a symbol or import target.
    /// </summary>
    public string? ExportName { get; private init; }

    /// <summary>
    /// Gets the byte signature for a signature target.
    /// </summary>
    public string? SignatureText { get; private init; }

    /// <summary>
    /// Gets the module whose import table is rewritten for an import target.
    /// </summary>
    public ProcessModule? ImportModule { get; private init; }

    /// <summary>
    /// Gets the hint or ordinal for an import target.
    /// </summary>
    public uint ImportHintOrOrdinal { get; private init; }

    /// <summary>
    /// Gets the delegate type a XIVClientStructs target resolves its address from.
    /// </summary>
    public Type? DelegateType { get; private init; }

    /// <summary>
    /// Gets the callback a deferred target resolves through.
    /// </summary>
    public Func<nint>? Resolver { get; private init; }

    /// <summary>
    /// Creates a target that reads the address from the XIVClientStructs type declaring <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <typeparam name="TDelegate">A delegate nested in a XIVClientStructs <c>Delegates</c> container.</typeparam>
    /// <returns>The target.</returns>
    public static HookTarget ClientStructs<TDelegate>()
        where TDelegate : Delegate
        => ClientStructs(typeof(TDelegate));

    /// <summary>
    /// Creates a target that reads the address from the XIVClientStructs type declaring the delegate.
    /// </summary>
    /// <param name="delegateType">A delegate type nested in a XIVClientStructs <c>Delegates</c> container.</param>
    /// <returns>The target.</returns>
    public static HookTarget ClientStructs(Type delegateType)
    {
        ArgumentNullException.ThrowIfNull(delegateType);
        return new HookTarget(HookTargetKind.ClientStructs) { DelegateType = delegateType };
    }

    /// <summary>
    /// Creates a target at an explicit function address.
    /// </summary>
    /// <param name="address">The function address.</param>
    /// <returns>The target.</returns>
    public static HookTarget Address(nint address)
    {
        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address), "A hook target address cannot be zero.");

        return new HookTarget(HookTargetKind.Address) { Pointer = address };
    }

    /// <summary>
    /// Creates a target at a slot in a virtual function table.
    /// </summary>
    /// <param name="vtable">The virtual table pointer.</param>
    /// <param name="slot">The zero-based slot index.</param>
    /// <returns>The target.</returns>
    public static HookTarget Vtable(nint vtable, int slot)
    {
        if (vtable == 0)
            throw new ArgumentOutOfRangeException(nameof(vtable), "A virtual table pointer cannot be zero.");

        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        return new HookTarget(HookTargetKind.Vtable) { Pointer = vtable, VtableSlot = slot };
    }

    /// <summary>
    /// Creates a target at an exported symbol in a loaded module.
    /// </summary>
    /// <param name="moduleName">The module name.</param>
    /// <param name="exportName">The exported function name.</param>
    /// <returns>The target.</returns>
    public static HookTarget Symbol(string moduleName, string exportName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        return new HookTarget(HookTargetKind.Symbol) { ModuleName = moduleName, ExportName = exportName };
    }

    /// <summary>
    /// Creates a target at a function pointer variable, hooked by rewriting the variable rather than the function.
    /// </summary>
    /// <param name="variableAddress">The address of the function pointer variable.</param>
    /// <returns>The target.</returns>
    public static HookTarget FunctionPointerVariable(nint variableAddress)
    {
        if (variableAddress == 0)
            throw new ArgumentOutOfRangeException(nameof(variableAddress), "A function pointer variable address cannot be zero.");

        return new HookTarget(HookTargetKind.FunctionPointerVariable) { Pointer = variableAddress };
    }

    /// <summary>
    /// Creates a target at an entry in a module import table.
    /// </summary>
    /// <param name="module">The module whose import table is rewritten, or null for the current process main module.</param>
    /// <param name="moduleName">The imported module name.</param>
    /// <param name="functionName">The imported function name.</param>
    /// <param name="hintOrOrdinal">The hint or ordinal of the imported function.</param>
    /// <returns>The target.</returns>
    public static HookTarget Import(ProcessModule? module, string moduleName, string functionName, uint hintOrOrdinal = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        return new HookTarget(HookTargetKind.Import)
        {
            ImportModule = module,
            ModuleName = moduleName,
            ExportName = functionName,
            ImportHintOrOrdinal = hintOrOrdinal,
        };
    }

    /// <summary>
    /// Creates a target resolved by scanning the game module for a byte signature, where a signature starting at an
    /// E8 or E9 resolves to the function the call targets rather than the call site.
    /// </summary>
    /// <param name="signature">The byte signature.</param>
    /// <returns>The target.</returns>
    public static HookTarget Signature(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        return new HookTarget(HookTargetKind.Signature) { SignatureText = signature };
    }

    /// <summary>
    /// Creates a target whose address is produced by a callback, retried until it returns a non-zero value.
    /// </summary>
    /// <param name="resolver">The callback producing the address, returning zero while it is not available yet.</param>
    /// <returns>The target.</returns>
    public static HookTarget Deferred(Func<nint> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return new HookTarget(HookTargetKind.Deferred) { Resolver = resolver };
    }

    /// <summary>
    /// Returns a short human-readable description of the target.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe() => Kind switch
    {
        HookTargetKind.ClientStructs => $"ClientStructs {DelegateType?.FullName}",
        HookTargetKind.Address => $"address 0x{Pointer:X}",
        HookTargetKind.Vtable => $"vtable 0x{Pointer:X} slot {VtableSlot}",
        HookTargetKind.Symbol => $"symbol {ModuleName}!{ExportName}",
        HookTargetKind.FunctionPointerVariable => $"function pointer variable 0x{Pointer:X}",
        HookTargetKind.Import => $"import {ModuleName}!{ExportName}",
        HookTargetKind.Signature => $"signature {SignatureText}",
        HookTargetKind.Deferred => "deferred resolver",
        _ => Kind.ToString(),
    };

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
