using System.Collections.Generic;

namespace NoireLib.Localizer;

/// <summary>
/// Fluent locale writer for fast translation registration.
/// </summary>
public sealed class LocaleWriter
{
    private readonly NoireLocalizer owner;
    private readonly string locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocaleWriter"/> class.
    /// </summary>
    /// <param name="owner">The owner localizer instance.</param>
    /// <param name="locale">The locale bound to this writer.</param>
    internal LocaleWriter(NoireLocalizer owner, string locale)
    {
        this.owner = owner;
        this.locale = locale;
        this.owner.EnsureLocale(locale);
    }

    /// <summary>
    /// Adds or updates a translation in the writer locale.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The translation value.</param>
    /// <param name="overwrite">Whether an existing key should be overwritten.</param>
    /// <returns>The same writer instance for fluent chaining.</returns>
    public LocaleWriter Add(string key, string value, bool overwrite = true)
    {
        owner.AddTranslation(locale, key, value, overwrite);
        return this;
    }

    /// <summary>
    /// Adds or updates multiple translations in the writer locale.
    /// </summary>
    /// <param name="values">The key/value pairs to add.</param>
    /// <param name="overwrite">Whether existing keys should be overwritten.</param>
    /// <returns>The same writer instance for fluent chaining.</returns>
    public LocaleWriter AddRange(IReadOnlyDictionary<string, string> values, bool overwrite = true)
    {
        owner.AddTranslations(locale, values, overwrite);
        return this;
    }

    /// <summary>
    /// Ends fluent registration and returns the localization module instance.
    /// </summary>
    /// <returns>The owning <see cref="NoireLocalizer"/> instance.</returns>
    public NoireLocalizer Done()
        => owner;
}
