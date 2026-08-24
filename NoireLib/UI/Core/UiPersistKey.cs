namespace NoireLib.UI;

// Refuses to build a NoireUiState key when the widget's id was generated rather than given.
internal static class UiPersistKey
{
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
