namespace NoireLib.UI;

// The root of a skinned window's tree. Its id and path are the window's id.
internal sealed class WindowRoot : Component
{
    internal WindowRoot(NoireSkinnedWindowBase window) => SetRoot(window.Id, window);
}
