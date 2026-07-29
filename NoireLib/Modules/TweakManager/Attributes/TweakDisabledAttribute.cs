using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Marks a tweak class as globally disabled, so it cannot be enabled by users. If <see cref="ShowInList"/> is true,
/// the tweak stays visible in the list (red name, tooltip) but cannot be toggled; when false (default), it is
/// completely hidden.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TweakDisabledAttribute : Attribute
{
    /// <summary>
    /// An optional reason explaining why the tweak is globally disabled.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Whether the tweak should still be shown in the tweak list while disabled: true shows it with a red name
    /// and a tooltip giving the reason, non-interactive; false (default) hides it completely.
    /// </summary>
    public bool ShowInList { get; }

    /// <summary>
    /// Marks a tweak as globally disabled with no reason and hidden from the list.
    /// </summary>
    public TweakDisabledAttribute()
    {
        Reason = null;
        ShowInList = false;
    }

    /// <summary>
    /// Marks a tweak as globally disabled with a reason.
    /// </summary>
    /// <param name="reason">The reason why the tweak is globally disabled.</param>
    /// <param name="showInList">Whether to still show the tweak in the list (red, non-interactive). Defaults to <see langword="false"/>.</param>
    public TweakDisabledAttribute(string reason, bool showInList = false)
    {
        Reason = reason;
        ShowInList = showInList;
    }
}
