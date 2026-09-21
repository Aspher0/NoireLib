namespace NoireLib.UI;

/// <summary>
/// How a dialog raised through <see cref="NoireModal"/> behaves and looks.
/// </summary>
public class ModalOptions
{
    /// <summary>
    /// The label of the button that agrees. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? ConfirmLabel { get; set; }

    /// <summary>
    /// The label of the button that declines, an empty string removing the button entirely and
    /// <see langword="null"/> using a sensible default.
    /// </summary>
    public string? CancelLabel { get; set; }

    /// <summary>Whether the dialog is destructive. The confirming button takes the theme's danger color.</summary>
    public bool Danger { get; set; }

    /// <summary>
    /// How long the confirming button must be held, in seconds, zero making it an ordinary button.
    /// </summary>
    public float HoldSeconds { get; set; }

    /// <summary>
    /// How long the confirming button stays disabled after the dialog appears, in seconds, with the remaining whole
    /// seconds appended to its label.
    /// </summary>
    public float EnableAfterSeconds { get; set; }

    /// <summary>
    /// A key stable across sessions under which the user's answer is remembered in <see cref="NoireUiState"/> and
    /// cleared by <see cref="NoireModal.Forget(string)"/>, <see langword="null"/> always showing the dialog.
    /// </summary>
    public string? RememberKey { get; set; }

    /// <summary>
    /// The label of the "don't ask again" checkbox. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? RememberLabel { get; set; }

    /// <summary>
    /// The dialog width, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; } = 420f;

    /// <summary>
    /// Whether the caller draws the dialog itself through <see cref="NoireModal.Active"/>, the built-in popup taking over when a frame passes without <see cref="NoireModalView.MarkPresented"/>.
    /// </summary>
    public bool CustomDraw { get; set; }

    internal float ScaledWidth => NoireUI.Scaled(Width);
}

/// <summary>
/// How a text prompt raised through <see cref="NoireModal.PromptAsync"/> behaves, on top of everything in
/// <see cref="ModalOptions"/>.
/// </summary>
public sealed class PromptOptions : ModalOptions
{
    /// <summary>
    /// The greyed-out hint shown while the field is empty.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// The longest value the field accepts, in characters.
    /// </summary>
    public int MaxLength { get; set; } = 260;

    /// <summary>
    /// Whether an empty value may be confirmed.
    /// </summary>
    public bool AllowEmpty { get; set; }
}
