using System;

namespace NoireLib.Localizer;

/// <summary>
/// Declares the default locale used by an attributed localization provider class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NoireLocalizationLocaleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireLocalizationLocaleAttribute"/> class.
    /// </summary>
    /// <param name="locale">The locale applied to member-level translations when they do not specify one.</param>
    /// <param name="registerAutomatically">Whether this provider should be discovered and registered automatically.</param>
    public NoireLocalizationLocaleAttribute(string locale, bool registerAutomatically = true)
    {
        Locale = locale;
        RegisterAutomatically = registerAutomatically;
    }

    /// <summary>
    /// Gets the locale declared for the provider class.
    /// </summary>
    public string Locale { get; }

    /// <summary>
    /// Gets a value indicating whether this provider should be discovered and registered automatically.
    /// </summary>
    public bool RegisterAutomatically { get; }
}
