namespace NoireLib.IPC;

/// <summary>
/// Defines the target IPC behavior for an attributed member.
/// </summary>
public enum NoireIpcTargetKind
{
    /// <summary>
    /// Infers the target behavior from the annotated member shape.
    /// </summary>
    Auto,

    /// <summary>
    /// Treats the member as a call-style action or function IPC.
    /// </summary>
    Call,

    /// <summary>
    /// Treats the member as an event-style message IPC.
    /// </summary>
    Event,
}
