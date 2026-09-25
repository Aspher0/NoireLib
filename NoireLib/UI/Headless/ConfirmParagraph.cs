using NoireLib.Localizer;

namespace NoireLib.UI;

/// <summary>One paragraph of a <see cref="NoireConfirm"/>.</summary>
/// <param name="Text">The text.</param>
/// <param name="Tone">How it reads.</param>
public readonly record struct ConfirmParagraph(NoireString Text, ParagraphTone Tone = ParagraphTone.Normal);
