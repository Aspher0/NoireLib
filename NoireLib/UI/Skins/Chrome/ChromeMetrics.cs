namespace NoireLib.UI;

/// <summary>An <see cref="IChromeSkin"/>'s measurements at 100%, used by the window to lay out its body and hit test.</summary>
/// <param name="HeaderHeight">The title bar's height.</param>
/// <param name="Radius">The corner radius.</param>
/// <param name="CollapsedHeight">The height of the collapsed strip.</param>
/// <param name="GripSize">The side of the corner resize handles.</param>
/// <param name="EdgeGrab">The thickness of the edge resize handles.</param>
/// <param name="BodyPadding">The room a window's own layout, such as a settings window's, keeps inside the body.</param>
public readonly record struct ChromeMetrics(float HeaderHeight, float Radius, float CollapsedHeight, float GripSize, float EdgeGrab, float BodyPadding = 0f);
