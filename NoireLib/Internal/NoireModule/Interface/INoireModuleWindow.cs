namespace NoireLib.Core.Modules;

/// <summary>
/// Interface for windows managed by NoireLib modules.
/// </summary>
public interface INoireModuleWindow
{
    /// <summary>
    /// Whether the window is currently open.
    /// </summary>
    bool IsOpen { get; set; }

    /// <summary>
    /// Gets the display name of the window, without the unique ID part of the window.
    /// </summary>
    string DisplayWindowName { get; set; }

    /// <summary>
    /// Force closes the window.
    /// </summary>
    void CloseWindow();

    /// <summary>
    /// Force opens the window.
    /// </summary>
    void OpenWindow();

    /// <summary>
    /// Toggles the window's open/closed state.
    /// </summary>
    void ToggleWindow();

    /// <summary>
    /// Shows or hides the window.
    /// </summary>
    /// <param name="show">Whether to show or hide the window. Set to null to toggle the window.</param>
    void ShowWindow(bool? show);

    /// <summary>
    /// Updates the full window name of the window, including the unique ID of the window.
    /// </summary>
    void UpdateWindowName();

    /// <summary>
    /// Updates the title bar buttons of the window.
    /// </summary>
    void UpdateTitleBarButtons();

    /// <summary>
    /// Disposes of the window, releasing any resources.
    /// </summary>
    void Dispose();
}
