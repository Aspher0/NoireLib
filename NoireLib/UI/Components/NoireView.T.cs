namespace NoireLib.UI;

/// <summary>How one skin draws a component or a window of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The component or window type.</typeparam>
public abstract class NoireView<T> : NoireView where T : class
{
    /// <summary>Draws the target in <see cref="NoireView.Area"/>, advancing the ImGui cursor by the height it used.</summary>
    /// <param name="target">The component or window.</param>
    protected internal abstract void Draw(T target);

    /// <summary>What a window's view draws above its body and chrome: menus, cards, pills. Never called for a component.</summary>
    /// <param name="target">The window.</param>
    protected internal virtual void Overlay(T target)
    {
    }

    internal sealed override void DrawTarget(object target) => Draw((T)target);

    internal sealed override void OverlayTarget(object target) => Overlay((T)target);
}
