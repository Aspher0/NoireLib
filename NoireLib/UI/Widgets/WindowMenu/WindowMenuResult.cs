namespace NoireLib.UI;

/// <summary>
/// What happened in a <see cref="NoireWindowMenu"/> on the frame it was drawn.
/// </summary>
/// <param name="Changes">The settings that changed.</param>
/// <param name="Hovered">The switch under the pointer, if any.</param>
/// <param name="RightClicked">The switch right clicked this frame, if any.</param>
public readonly record struct WindowMenuResult(WindowMenuChange Changes, WindowMenuToggle? Hovered, WindowMenuToggle? RightClicked)
{
    /// <summary>Whether anything changed.</summary>
    public bool Changed => Changes != WindowMenuChange.None;
}
