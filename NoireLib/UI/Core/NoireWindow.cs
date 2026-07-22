using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NoireLib.UI;

/// <summary>
/// A Dalamud window that decides for itself which game states it stays visible in.
/// </summary>
/// <remarks>
/// Which is not something a window decides when reached through Dalamud directly: hiding is settled once per plugin, so
/// keeping one window up in gpose keeps every window of that plugin up in gpose. This carries the bookkeeping that
/// makes it a single window's answer, so a consumer sets a property and stops thinking about it.<br/>
/// Derive from this instead of <see cref="Window"/> and everything else about the window is unchanged. Overriding
/// <see cref="DrawConditions"/> is fine as long as the base call is kept.
/// </remarks>
/// <example>
/// <code>
/// internal sealed class MyWindow : NoireWindow
/// {
///     public MyWindow() : base("My window###myWindow")
///     {
///         Visibility = UiVisibility.InGpose;   // stays up while posing, hides like anything else otherwise
///     }
///
///     public override void Draw() { }
/// }
/// </code>
/// </example>
public abstract class NoireWindow : Window
{
    private UiVisibility visibility = UiVisibility.Default;

    /// <summary>Creates the window.</summary>
    /// <param name="name">The window name, including its <c>###id</c> where it has one.</param>
    /// <param name="flags">The ImGui window flags.</param>
    /// <param name="forceMainWindow">Whether Dalamud treats this as the plugin's main window.</param>
    protected NoireWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
        : base(name, flags, forceMainWindow)
    {
    }

    /// <summary>
    /// Which normally-hidden game states this window keeps drawing in. Defaults to
    /// <see cref="UiVisibility.Default"/>, which is ordinary plugin behaviour.
    /// </summary>
    /// <remarks>
    /// Asking for anything here switches Dalamud's own hiding off for that state, across the whole plugin, and hands
    /// the decision to each window instead. That is what makes it per window, and it is why every window of a plugin
    /// using this should be a <see cref="NoireWindow"/>: one that is not would no longer be hidden by anyone.
    /// </remarks>
    public UiVisibility Visibility
    {
        get => visibility;
        set
        {
            visibility = value;
            NoireUI.RequireVisibility(value);
        }
    }

    /// <summary>
    /// Whether the window draws this frame, which is where the per-window hiding happens.
    /// </summary>
    /// <remarks>
    /// Dalamud asks this to decide whether to draw the window at all, so returning false is the same thing its own
    /// hiding would have done, decided one window at a time rather than once for the plugin.
    /// </remarks>
    /// <returns>True when the window should draw.</returns>
    public override bool DrawConditions() => !NoireUI.ShouldHide(visibility);

}
