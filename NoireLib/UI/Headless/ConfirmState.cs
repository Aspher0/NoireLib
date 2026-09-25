namespace NoireLib.UI;

/// <summary>A confirmation being asked, as it is drawn this frame.</summary>
/// <param name="Confirm">What is asked.</param>
/// <param name="SecondsLeft">Seconds before confirming is allowed.</param>
/// <param name="Age">Seconds since it opened.</param>
public readonly record struct ConfirmState(NoireConfirm Confirm, int SecondsLeft, float Age);
