using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Marks a tweak class as globally disabled.<br/>
/// When applied, the tweak cannot be enabled by users.<br/>
/// If <see cref="ShowInList"/> is <see langword="true"/>, the tweak is still visible in the
/// tweak list (shown in red with a tooltip) but cannot be toggled.
/// When <see langword="false"/> (default), the tweak is completely hidden.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TweakDisabledAttribute : Attribute
{
    /// <summary>
    /// An optional reason explaining why the tweak is globally disabled.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Whether the tweak should still be shown in the tweak list while disabled.<br/>
    /// When <see langword="true"/>, the tweak appears in the list with a red name and a tooltip
    /// showing the reason, but cannot be enabled or interacted with.<br/>
    /// When <see langword="false"/> (default), the tweak is completely hidden from users.
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
