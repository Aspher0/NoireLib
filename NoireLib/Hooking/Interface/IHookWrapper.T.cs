using System;

namespace NoireLib.Hooking;

/// <summary>
/// Defines the common contract for high-level hook wrappers with strongly typed detours.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used by the underlying hook.</typeparam>
public interface IHookWrapper<TDelegate> : IHookWrapper
    where TDelegate : Delegate
{
    /// <summary>
    /// Gets the name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    string? HookName { get; }

    /// <summary>
    /// Gets the full name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    string? HookFullName { get; }

    /// <summary>
    /// Gets the original, unhooked delegate.
    /// </summary>
    TDelegate Original { get; }

    /// <summary>
    /// Gets the original, unhooked delegate that remains available after disposal.
    /// </summary>
    TDelegate OriginalDisposeSafe { get; }

    /// <summary>
    /// Gets the detour delegate assigned to the hook.
    /// </summary>
    TDelegate Detour { get; }
}
