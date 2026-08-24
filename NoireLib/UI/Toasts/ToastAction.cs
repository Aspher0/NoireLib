using System;

namespace NoireLib.UI;

/// <summary>
/// A button on a toast: the label the user reads, what it does, and how it is colored.
/// </summary>
public sealed class ToastAction
{
    /// <summary>
    /// Creates an action.
    /// </summary>
    /// <param name="label">The button label.</param>
    /// <param name="onInvoke">What the button does.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="onInvoke"/> is <see langword="null"/>.</exception>
    public ToastAction(string label, Action<NoireToast> onInvoke)
    {
        ArgumentNullException.ThrowIfNull(onInvoke);

        Label = label ?? string.Empty;
        OnInvoke = onInvoke;
    }

    /// <summary>
    /// The button label.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// What the button does, invoked on the draw thread with the toast it belongs to.
    /// </summary>
    public Action<NoireToast> OnInvoke { get; set; }

    /// <summary>
    /// How the button is colored.
    /// </summary>
    public ButtonTone Tone { get; set; } = ButtonTone.Ghost;

    /// <summary>
    /// Whether invoking the action also dismisses the toast.
    /// </summary>
    public bool DismissesToast { get; set; } = true;
}
