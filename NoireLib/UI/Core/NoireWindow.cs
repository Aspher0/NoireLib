using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NoireLib.UI;

/// <summary>
/// A Dalamud window that decides for itself which game states it stays visible in.<br/>
/// Overriding <see cref="DrawConditions"/> requires keeping the base call.
/// </summary>
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
    /// <see cref="UiVisibility.Default"/>, which is ordinary plugin behaviour.<br/>
    /// Asking for anything here switches Dalamud's own hiding off for that state across the whole plugin, so every
    /// window of the plugin must then be a <see cref="NoireWindow"/>.
    /// </summary>
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
    /// <returns>True when the window should draw.</returns>
    public override bool DrawConditions() => !NoireUI.ShouldHide(visibility);

}
