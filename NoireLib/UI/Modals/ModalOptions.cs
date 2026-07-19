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
    /// The label of the button that declines. When <see langword="null"/>, a sensible default is used.<br/>
    /// Set it to an empty string to remove the button entirely, for a dialog that only acknowledges something.
    /// </summary>
    public string? CancelLabel { get; set; }

    /// <summary>
    /// Whether the dialog is about something destructive, which colors the confirming button in the theme's danger
    /// color.
    /// </summary>
    public bool Danger { get; set; }

    /// <summary>
    /// How long the confirming button must be held, in seconds. Zero makes it an ordinary button.<br/>
    /// Pair it with <see cref="Danger"/> for an irreversible action: the pause is what stops a reflex click.
    /// </summary>
    public float HoldSeconds { get; set; }

    /// <summary>
    /// A key under which the user's answer is remembered, so the dialog offers "don't ask again" and skips itself next
    /// time.<br/>
    /// When <see langword="null"/> the dialog always appears, which is the default.
    /// </summary>
    /// <remarks>
    /// Only for confirmations whose answer is genuinely stable, such as "close to tray". Never offer it for a
    /// destructive action or for anything that applies content from outside the plugin: a remembered yes turns the
    /// confirmation into no confirmation at all, which is precisely what those dialogs exist to prevent.<br/>
    /// The key has to be stable across sessions, since it is what the answer is stored against in
    /// <see cref="NoireUiState"/>. Clear a remembered answer with <see cref="NoireModal.Forget(string)"/>.
    /// </remarks>
    public string? RememberKey { get; set; }

    /// <summary>
    /// The label of the "don't ask again" checkbox. When <see langword="null"/>, a sensible default is used.
    /// </summary>
    public string? RememberLabel { get; set; }

    /// <summary>
    /// The dialog width in pixels.
    /// </summary>
    public float Width { get; set; } = 420f;
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
    /// Whether an empty value may be confirmed. Off by default, so a prompt cannot return an empty string by accident.
    /// </summary>
    public bool AllowEmpty { get; set; }
}
