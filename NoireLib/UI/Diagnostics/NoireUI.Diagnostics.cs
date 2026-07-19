namespace NoireLib.UI;

/// <summary>
/// The hub's diagnostics facade.
/// </summary>
public static partial class NoireUI
{
    /// <summary>
    /// What NoireUI knows about itself: live counts, recent faults, the fault ladder and the stack-leak net.
    /// See <see cref="UiDiagnostics"/>.
    /// </summary>
    public static UiDiagnostics Diagnostics { get; } = new();

    /// <summary>
    /// How many <see cref="RunOnDraw"/> actions have been dropped because the queue was full, since startup.
    /// </summary>
    public static int DroppedDrawActions => DrawPump.DroppedCount;
}
