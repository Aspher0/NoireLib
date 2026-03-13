using System;

namespace NoireLib.Localizer;

/// <summary>
/// Declares a translation key on a field or property in an attributed localization provider class.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NoireLocalizationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireLocalizationAttribute"/> class.
    /// </summary>
    /// <param name="key">The translation key to register.</param>
    public NoireLocalizationAttribute(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the translation key to register.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets or sets an optional locale override for this translation.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Gets or sets an optional explicit translation value.
    /// When not provided, the member value is used.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets or sets whether an existing translation should be overwritten.
    /// </summary>
    public bool Overwrite { get; init; } = true;
}
