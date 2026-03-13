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
    /// Gets or sets the custom default locale used when <see cref="DefaultLocaleSource"/> is <see cref="DefaultLocaleSource.Custom"/>.
    /// </summary>
    [AutoSave]
    public string CustomDefaultLocale { get; set; } = "en-US";
}
