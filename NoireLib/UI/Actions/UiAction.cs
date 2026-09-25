using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

/// <summary>Something the user can do to a <typeparamref name="T"/>, shown the same way in menus and buttons.</summary>
/// <typeparam name="T">What the action runs on.</typeparam>
public sealed class UiAction<T>
{
    private readonly Action<T> run;

    /// <summary>Declares an action.</summary>
    /// <param name="label">The label.</param>
    /// <param name="icon">The icon, or <see langword="null"/> for none.</param>
    /// <param name="run">What the action does.</param>
    public UiAction(NoireString label, NoireIcon? icon, Action<T> run)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(run);

        Label = label;
        Icon = icon;
        this.run = run;
    }

    /// <summary>The label.</summary>
    public NoireString Label { get; }

    /// <summary>The icon, or <see langword="null"/> for none.</summary>
    public NoireIcon? Icon { get; }

    /// <summary>Whether the action can run on a target, always when <see langword="null"/>.</summary>
    public Func<T, bool>? Available { get; init; }

    /// <summary>Whether a menu hides the action while it is unavailable instead of showing it disabled. Buttons always show it disabled.</summary>
    public bool HideWhenUnavailable { get; init; } = true;

    /// <summary>Whether the action only runs while Ctrl is held.</summary>
    public bool RequiresCtrl { get; init; }

    /// <summary>A tooltip, also shown as the reason while the action is unavailable or waits for Ctrl.</summary>
    public NoireString? Hint { get; init; }

    /// <summary>Whether the action can run on a target.</summary>
    /// <param name="target">The target.</param>
    /// <returns>True when it can.</returns>
    public bool IsAvailable(T target) => Available?.Invoke(target) ?? true;

    /// <summary>Runs the action when it is available.</summary>
    /// <param name="target">The target.</param>
    /// <returns>True when it ran.</returns>
    public bool Run(T target)
    {
        if (!IsAvailable(target))
            return false;

        run(target);
        return true;
    }
}
