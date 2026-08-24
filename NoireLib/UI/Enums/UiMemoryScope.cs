namespace NoireLib.UI;

/// <summary>
/// How long a widget remembers a piece of its own state.
/// </summary>
public enum UiMemoryScope
{
    /// <summary>Not remembered. The state resets whenever the widget would otherwise restore it.</summary>
    None,

    /// <summary>
    /// Remembered for the rest of the session, in <see cref="NoireUiSession"/>, and gone on reload.
    /// </summary>
    Session,

    /// <summary>
    /// Remembered across reloads, in <see cref="NoireUiState"/>.
    /// </summary>
    /// <remarks>Requires a stable widget id.</remarks>
    Persisted,
}
