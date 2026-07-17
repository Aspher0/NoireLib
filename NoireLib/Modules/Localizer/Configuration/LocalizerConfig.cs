using NoireLib.Configuration;

namespace NoireLib.Localizer;

/// <summary>
/// Configuration storage for the <see cref="NoireLocalizer"/> module settings.
/// </summary>
[NoireConfig("LocalizerConfig")]
public class LocalizerConfigInstance : NoireConfigBase
{
    /// <inheritdoc/>
    public override int Version { get; set; } = 1;

    /// <inheritdoc/>
    public override string GetConfigFileName() => "LocalizerConfig";

    /// <summary>
    /// Gets or sets the currently selected locale persisted to disk.
    /// </summary>
    [AutoSave]
    public string? SelectedLocale { get; set; }

    /// <summary>
    /// Gets or sets the strategy used to determine the default locale.
    /// </summary>
    [AutoSave]
    public DefaultLocaleSource DefaultLocaleSource { get; set; } = DefaultLocaleSource.Custom;

    /// <summary>
    /// Gets or sets the custom default locale used when <see cref="DefaultLocaleSource"/> is <see cref="DefaultLocaleSource.Custom"/>.<br/>
    /// Only meaningful while <see cref="HasCustomDefaultLocaleSelection"/> is true: until a default locale has actually
    /// been selected, this holds the value below rather than anyone's choice.
    /// </summary>
    [AutoSave]
    public string CustomDefaultLocale { get; set; } = "en-US";

    /// <summary>
    /// Gets or sets whether <see cref="CustomDefaultLocale"/> records a default locale that was selected at runtime,
    /// through <see cref="NoireLocalizer.SetDefaultLocale(string)"/> or <see cref="NoireLocalizer.UseCustomDefaultLocale(string)"/>.<br/>
    /// This is what separates a stored selection from the value <see cref="CustomDefaultLocale"/> starts life with.
    /// A localizer restores a stored selection over the default locale it was constructed with, and must not do the
    /// same for a value nobody picked, which would make that constructor argument unreachable.
    /// </summary>
    [AutoSave]
    public bool HasCustomDefaultLocaleSelection { get; set; } = false;
}
