using NoireLib.Configuration;

namespace NoireLib.HistoryLogger;

/// <summary>
/// Configuration storage for History Logger settings.
/// </summary>
[NoireConfig("HistoryLoggerConfig")]
public class HistoryLoggerConfigInstance : NoireConfigBase
{
    /// <inheritdoc />
    public override int Version { get; set; } = 1;

    /// <inheritdoc />
    public override string GetConfigFileName() => "HistoryLoggerConfig";

    /// <summary>
    /// Gets or sets whether level background colors should be shown in the log entries table.
    /// </summary>
    [AutoSave]
    public bool ShowLevelBackgroundColors { get; set; } = true;

    /// <summary>
    /// Gets or sets whether individual lines can be selected separately in multi-line entries.
    /// </summary>
    [AutoSave]
    public bool SelectLinesSeparately { get; set; } = true;

    /// <summary>
    /// Hides the category column in the log entries table when set to true.
    /// </summary>
    [AutoSave]
    public bool HideCategoryColumn { get; set; } = false;

    /// <summary>
    /// Hides the source column in the log entries table when set to true.
    /// </summary>
    [AutoSave]
    public bool HideSourceColumn { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of items to display per page.
    /// </summary>
    [AutoSave]
    public int ItemsPerPage { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether the header panel is expanded (true) or collapsed (false).
    /// </summary>
    [AutoSave]
    public bool IsHeaderPanelExpanded { get; set; } = true;
}
