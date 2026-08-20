using System;

namespace NoireLib.Hooking;

/// <summary>
/// The contract a <see cref="NoireHook{TDelegate}"/> satisfies for its own delegate type.
/// </summary>
/// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
public interface INoireHook<TDelegate> : INoireHook
    where TDelegate : Delegate
{
    /// <summary>
    /// Gets the original, unhooked function.
    /// </summary>
    TDelegate Original { get; }

    /// <summary>
    /// Gets the original, unhooked function, still callable after disposal.
    /// </summary>
    TDelegate OriginalDisposeSafe { get; }

    /// <summary>
    /// Gets the detour the consumer supplied.
    /// </summary>
    TDelegate Detour { get; }
}
