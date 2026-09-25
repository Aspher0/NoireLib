using NoireLib.Localizer;

namespace NoireLib.UI;

/// <summary>A confirmation, described once and drawn over the window that asks it.</summary>
/// <param name="Title">The title.</param>
/// <param name="ConfirmLabel">The confirm button's label.</param>
/// <param name="Paragraphs">The body.</param>
public sealed record NoireConfirm(NoireString Title, NoireString ConfirmLabel, params ConfirmParagraph[] Paragraphs)
{
    /// <summary>The cancel button's label, or <see langword="null"/> for the drawing's own.</summary>
    public NoireString? CancelLabel { get; init; }

    /// <summary>Whether the confirmation is drawn as dangerous.</summary>
    public bool Danger { get; init; }

    /// <summary>Seconds before the confirm button can be pressed.</summary>
    public int CountdownSeconds { get; init; }
}
