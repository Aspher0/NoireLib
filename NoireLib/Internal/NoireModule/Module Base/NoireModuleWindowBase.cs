using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using NoireLib.Helpers;
using System;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for module windows within the NoireLib library.<br/>
/// Provides automatic window registration and management within the NoireLib window system.
/// </summary>
/// <typeparam name="TModule">The type of the parent module associated with this window.</typeparam>
public abstract class NoireModuleWindowBase<TModule> : Window, IDisposable, INoireModuleWindow where TModule : class, INoireModuleWithWindow, new()
{
    /// <summary>
    /// The parent module associated with this window.
    /// </summary>
    protected TModule ParentModule { get; init; }

    /// <summary>
    /// The name displayed in the title bar of the window.
    /// </summary>
    public abstract string DisplayWindowName { get; set; }

    /// <summary>
    /// Initializes a new instance of the module window.
    /// </summary>
    /// <param name="parentModule">The parent module associated with this window.</param>
    /// <param name="flags">Optional window flags for ImGui configuration.</param>
    /// <param name="forceMainWindow">Optional flag to force the window to be a main window.</param>
    protected NoireModuleWindowBase(TModule parentModule, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
        : base($"{(parentModule.DisplayWindowName.IsNullOrWhitespace() ? RandomGenerator.GenerateGuidString() : parentModule.DisplayWindowName)}###{parentModule.GetUniqueIdentifier()}", flags, forceMainWindow)
    {
        ParentModule = parentModule;
        UpdateWindowName();
    }

    /// <summary>
    /// Updates the title bar buttons of the window.
    /// </summary>
    public virtual void UpdateTitleBarButtons()
    {
        TitleBarButtons.Clear();
        foreach (var titleBarButton in ParentModule.TitleBarButtons)
            TitleBarButtons.Add(titleBarButton);
    }

    /// <summary>
    /// Shows or hides the window.
    /// </summary>
    /// <param name="show">Whether to show the window. Set to null to toggle the window.</param>
    public virtual void ShowWindow(bool? show = null)
    {
        if (show.HasValue)
            IsOpen = show.Value;
        else
            IsOpen = !IsOpen;
    }

    /// <summary>
    /// Force closes the window.
    /// </summary>
    public virtual void CloseWindow() => ShowWindow(false);

    /// <summary>
    /// Force opens the window.
    /// </summary>
    public virtual void OpenWindow() => ShowWindow(true);

    /// <summary>
    /// Toggles the window's show/hide state.
    /// </summary>
    public virtual void ToggleWindow() => ShowWindow();

    /// <summary>
    /// Updates the full window name, including the unique ID.
    /// </summary>
    public virtual void UpdateWindowName() => WindowName = GetWindowName();

    /// <summary>
    /// Gets the unique window name used by ImGui, including the ID part.
    /// </summary>
    /// <returns></returns>
    public virtual string GetWindowName() => $"{DisplayWindowName}###{ParentModule.GetUniqueIdentifier()}";

    // TODO: Add an option to change the window size

    /// <summary>
    /// Disposes the window.
    /// </summary>
    public abstract void Dispose();
}
