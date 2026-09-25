namespace NoireLib.UI;

/// <summary>The answer to a <see cref="NoireConfirm"/>.</summary>
public enum ConfirmAnswer
{
    /// <summary>No answer yet.</summary>
    Pending,

    /// <summary>Confirmed.</summary>
    Confirmed,

    /// <summary>Cancelled, by the button, Escape or closing the window.</summary>
    Cancelled,
}
