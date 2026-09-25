namespace NoireLib.Localizer;

/// <summary>
/// A message kept as its declared text and values, shown in the active language and recorded in the log language.
/// </summary>
public readonly struct NoireMessage
{
    private readonly NoireString text;
    private readonly string? name1;
    private readonly string? value1;
    private readonly string? name2;
    private readonly string? value2;

    /// <summary>A message with no values.</summary>
    /// <param name="text">The declared text.</param>
    public NoireMessage(NoireString text) => this.text = text;

    /// <summary>A message with one value.</summary>
    /// <param name="text">The declared text.</param>
    /// <param name="name">The placeholder name, without braces.</param>
    /// <param name="value">The value.</param>
    public NoireMessage(NoireString text, string name, string value)
    {
        this.text = text;
        name1 = name;
        value1 = value;
    }

    /// <summary>A message with two values.</summary>
    /// <param name="text">The declared text.</param>
    /// <param name="name1">The first placeholder name.</param>
    /// <param name="value1">The first value.</param>
    /// <param name="name2">The second placeholder name.</param>
    /// <param name="value2">The second value.</param>
    public NoireMessage(NoireString text, string name1, string value1, string name2, string value2)
    {
        this.text = text;
        this.name1 = name1;
        this.value1 = value1;
        this.name2 = name2;
        this.value2 = value2;
    }

    /// <summary>The message in the active language, for the chat and the screen.</summary>
    public string Display => name2 != null
        ? text.With(name1!, value1!, name2, value2!)
        : name1 != null ? text.With(name1, value1!) : text.Text;

    /// <summary>The message in the log language, for history and exports.</summary>
    public string Record
    {
        get
        {
            var filled = text.Record;

            if (name1 != null)
                filled = FormatCache.Fill(filled, name1, value1!);

            if (name2 != null)
                filled = FormatCache.Fill(filled, name2, value2!);

            return filled;
        }
    }
}
