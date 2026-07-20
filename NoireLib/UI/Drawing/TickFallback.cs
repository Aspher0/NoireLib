namespace NoireLib.UI;

/// <summary>
/// What a frame draws in place of its corner ticks when there is not room for them.
/// </summary>
public enum TickFallback
{
    /// <summary>Nothing. The frame keeps its rules and loses its corner marks.</summary>
    None,

    /// <summary>A square bracket at each end, spanning the short axis.</summary>
    Brackets,
}
