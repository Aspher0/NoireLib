namespace NoireLib.Hooking;

/// <summary>
/// Identifies how a <see cref="HookTarget"/> locates the function to hook.
/// </summary>
public enum HookTargetKind
{
    /// <summary>
    /// The address is read from the XIVClientStructs type that declares the delegate.
    /// </summary>
    ClientStructs,

    /// <summary>
    /// The address is supplied directly.
    /// </summary>
    Address,

    /// <summary>
    /// The address is read from a slot in a virtual function table.
    /// </summary>
    Vtable,

    /// <summary>
    /// The address is resolved from an exported symbol in a loaded module.
    /// </summary>
    Symbol,

    /// <summary>
    /// The target is a function pointer variable whose value is rewritten.
    /// </summary>
    FunctionPointerVariable,

    /// <summary>
    /// The target is an entry in a module import table.
    /// </summary>
    Import,

    /// <summary>
    /// The address is resolved by scanning the game module for a byte signature.
    /// </summary>
    Signature,

    /// <summary>
    /// The address comes from a callback that is retried until it returns a non-zero value.
    /// </summary>
    Deferred,
}
