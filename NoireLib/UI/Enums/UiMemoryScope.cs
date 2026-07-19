namespace NoireLib.UI;

/// <summary>
/// How long a widget remembers a piece of its own state.
/// </summary>
/// <remarks>
/// One enum rather than a pair of booleans, because "remember it" and "remember it across reloads" are three
/// positions on one axis and not two independent switches: a widget cannot meaningfully persist something it is also
/// told to forget.
/// </remarks>
public enum UiMemoryScope
{
    /// <summary>Not remembered. The state resets whenever the widget would otherwise restore it.</summary>
    None,

    /// <summary>
    /// Remembered for the rest of the session, in <see cref="NoireUiSession"/>, and gone on reload.
    /// </summary>
    /// <remarks>
    /// The right choice for the state that is worth keeping while someone works and worth forgetting afterwards. It
    /// also needs no stable widget id, because a generated id lasts exactly as long as the memory keyed on it.
    /// </remarks>
    Session,

    /// <summary>
    /// Remembered across reloads, in <see cref="NoireUiState"/>.
    /// </summary>
    /// <remarks>
    /// Requires a stable widget id. One that was generated is a new GUID every session, so nothing keyed on it could
    /// be read back, and a widget asked to persist against one refuses with a single log line instead.
    /// </remarks>
    Persisted,
}
