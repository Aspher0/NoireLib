namespace NoireLib.UI;

/// <summary>
/// Builds the <see cref="NoireUiState"/> key a widget stores a piece of remembered state under, and refuses to build
/// one when the widget's id was generated rather than given.
/// </summary>
/// <remarks>
/// A generated id is a fresh GUID every session. Persisting against one would write an entry that can never be read
/// back, so the state file grows forever and restores nothing, and the symptom (a setting that silently never sticks)
/// points nowhere near the cause. Refusing once, with a message naming the fix, is the whole point.<br/>
/// Shared rather than written per widget, because every <c>Persist*</c> switch in the library owes the same guarantee
/// and a widget that quietly skipped it would be the one that fails this way.
/// </remarks>
internal static class UiPersistKey
{
    /// <summary>
    /// Builds a state key for a widget.
    /// </summary>
    /// <param name="kind">The widget kind, for example "ComboBox".</param>
    /// <param name="id">The widget's id.</param>
    /// <param name="hasGeneratedId">Whether that id was generated rather than given by the consumer.</param>
    /// <param name="subKey">What is being remembered, for example "filter".</param>
    /// <param name="refusalLogged">Whether the refusal has already been logged for this widget. Set when it is.</param>
    /// <param name="key">The state key, or an empty string when persisting is refused.</param>
    /// <returns>True when the state may be persisted.</returns>
    internal static bool TryBuild(string kind, string id, bool hasGeneratedId, string subKey, ref bool refusalLogged, out string key)
    {
        if (!hasGeneratedId)
        {
            key = $"{kind}.{id}.{subKey}";
            return true;
        }

        key = string.Empty;

        if (!refusalLogged)
        {
            refusalLogged = true;
            NoireLogger.LogWarning(
                $"This {kind} was created without an id, so its id is a new GUID every session and nothing keyed on it can be restored. " +
                "Its persisted state is being skipped. Give it a stable id in the constructor to persist it.",
                nameof(UiPersistKey));
        }

        return false;
    }
}
