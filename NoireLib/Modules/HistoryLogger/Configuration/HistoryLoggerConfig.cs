using NoireLib.Configuration;

namespace NoireLib.HistoryLogger;

/// <summary>Persisted settings for the History Logger window.</summary>
[NoireConfig("HistoryLoggerConfig")]
public class HistoryLoggerConfigInstance : NoireConfigBase
{
    /// <inheritdoc />
    public override int Version { get; set; } = 1;

    /// <inheritdoc />
    public override string GetConfigFileName() => "HistoryLoggerConfig";

    /// <summary>Whether the log entries table tints each row by its level.</summary>
    [AutoSave]
    public bool ShowLevelBackgroundColors { get; set; } = true;

    /// <summary>Whether individual lines of a multi-line entry can be selected separately.</summary>
    [AutoSave]
    public bool SelectLinesSeparately { get; set; } = true;

    /// <summary>Whether the category column is hidden in the log entries table.</summary>
    [AutoSave]
    public bool HideCategoryColumn { get; set; } = false;

    /// <summary>Whether the source column is hidden in the log entries table. Hidden by default.</summary>
    [AutoSave]
    public bool HideSourceColumn { get; set; } = true;

    /// <summary>The number of entries shown per page.</summary>
    [AutoSave]
    public int ItemsPerPage { get; set; } = 100;

    /// <summary>Whether the header panel is expanded.</summary>
    [AutoSave]
    public bool IsHeaderPanelExpanded { get; set; } = true;
}
