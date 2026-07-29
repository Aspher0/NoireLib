namespace NoireLib.UI;

/// <summary>
/// How long a widget remembers a piece of its own state.
/// </summary>
/// <remarks>
/// Three positions on one axis, not two independent switches: a widget cannot meaningfully persist something it is
/// also told to forget.
/// </remarks>
public enum UiMemoryScope
{
    /// <summary>Not remembered. The state resets whenever the widget would otherwise restore it.</summary>
    None,

    /// <summary>
    /// Remembered for the rest of the session, in <see cref="NoireUiSession"/>, and gone on reload.
    /// </summary>
    /// <remarks>
    /// The right choice for state worth keeping while someone works and forgetting afterward. Needs no stable widget
    /// id: a generated id lasts exactly as long as the memory keyed on it.
    /// </remarks>
    Session,

    /// <summary>
    /// Remembered across reloads, in <see cref="NoireUiState"/>.
    /// </summary>
    /// <remarks>
    /// Requires a stable widget id: a generated one is a new GUID every session, so nothing keyed on it could be read
    /// back. A widget asked to persist against one refuses with a single log line instead.
    /// </remarks>
    Persisted,
}
