namespace NoireLib.Localizer;

/// <summary>One translatable text, declared once with its text in the plugin's source language.</summary>
public sealed class NoireString
{
    private string? cached;
    private int cachedRevision = -1;

    /// <summary>Declares a text.</summary>
    /// <param name="key">The stable key, for example <c>settings.plugin.enabled</c>.</param>
    /// <param name="source">The text in <see cref="NoireLocalizer.SourceLanguage"/>.</param>
    public NoireString(string key, string source)
    {
        Key = key;
        Source = source;
        NoireLanguages.Register(this);
    }

    /// <summary>The stable key.</summary>
    public string Key { get; }

    /// <summary>The text in the source language, shown when the active language has no translation.</summary>
    public string Source { get; }

    /// <summary>The text in the active language, cached until the language or a translation changes.</summary>
    public string Text
    {
        get
        {
            var revision = NoireLanguages.Revision;

            if (cachedRevision != revision)
            {
                cached = NoireLanguages.Resolve(this);
                cachedRevision = revision;
            }

            return cached!;
        }
    }

    // The text in the log language, for records that outlive the session.
    internal string Record => NoireLanguages.Record(this);

    /// <summary>Fills one <c>{name}</c> placeholder, cached until the value or the language changes.</summary>
    /// <param name="name">The placeholder name, without braces.</param>
    /// <param name="value">The value written in its place.</param>
    /// <returns>The filled text.</returns>
    public string With(string name, string value) => FormatCache.Format(this, name, value);

    /// <summary>Fills two placeholders, cached until a value or the language changes.</summary>
    /// <param name="name1">The first placeholder name.</param>
    /// <param name="value1">The first value.</param>
    /// <param name="name2">The second placeholder name.</param>
    /// <param name="value2">The second value.</param>
    /// <returns>The filled text.</returns>
    public string With(string name1, string value1, string name2, string value2)
        => FormatCache.Format(this, name1, value1, name2, value2);

    /// <summary>The text in the active language.</summary>
    /// <param name="text">The declared text.</param>
    public static implicit operator string(NoireString text) => text.Text;

    /// <inheritdoc/>
    public override string ToString() => Text;
}
