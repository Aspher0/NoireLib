using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using static Dalamud.Interface.Windowing.Window;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules that integrate a window within the NoireLib library.<br/>
/// Inherits from <see cref="NoireModuleBase{TModule}"/>.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
/// <typeparam name="TWindow">The type of the window associated with the module.</typeparam>
public abstract class NoireModuleWithWindowBase<TModule, TWindow> : NoireModuleBase<TModule>, INoireModuleWithWindow where TModule : NoireModuleWithWindowBase<TModule, TWindow>, new() where TWindow : Window, INoireModuleWindow
{
    /// <summary>
    /// The window associated with this module, if any.
    /// </summary>
    protected TWindow? ModuleWindow { get; set; }

    /// <summary>
    /// Gets whether this module has an associated window.
    /// </summary>
    public bool HasWindow => ModuleWindow != null;

    /// <summary>
    /// Gets or sets the display name of the module's window.
    /// </summary>
    public virtual string DisplayWindowName
    {
        get => ModuleWindow?.DisplayWindowName ?? string.Empty;
        set
        {
            if (HasWindow)
            {
                ModuleWindow!.DisplayWindowName = value;
                ModuleWindow!.UpdateWindowName();
            }
        }
    }

    /// <summary>
    /// Do not add buttons directly to this list, use the provided methods instead.<br/>
    /// <see cref="AddTitleBarButton"/>, <see cref="RemoveTitleBarButton"/>, <see cref="SetTitleBarButtons"/>, <see cref="ClearTitleBarButtons"/>
    /// </summary>
    public List<TitleBarButton> TitleBarButtons { get; private set; } = new();

    /// <summary>
    /// Constructor for the module base class.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="args">Arguments for module initialization.</param>
    public NoireModuleWithWindowBase(string? moduleId = null, bool active = true, bool enableLogging = true, params object?[] args)
        : base(moduleId, active, enableLogging, args) { }

    /// <summary>
    /// Every derived class (module class) shall implement a constructor like this, calling base(moduleId, active, enableLogging)<br/>
    /// Used in <see cref="NoireLibMain.AddModule{T}(string?)"/> to create modules with specific IDs.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireModuleWithWindowBase(ModuleId? moduleId = null, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #region Title bar button management

    /// <summary>
    /// Adds a button to the title bar of the changelog window.
    /// </summary>
    /// <param name="titleBarButton">The title bar button to add.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule AddTitleBarButton(TitleBarButton titleBarButton)
    {
        if (titleBarButton == null)
            return (TModule)this;

        TitleBarButtons.Add(titleBarButton);

        if (ModuleWindow != null)
            ModuleWindow.UpdateTitleBarButtons();

        return (TModule)this;
    }

    /// <summary>
    /// Removes a button from the title bar of the changelog window by its index.
    /// </summary>
    /// <param name="index">The index of the title bar button to remove.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule RemoveTitleBarButton(int index)
    {
        if (index < 0 || index >= TitleBarButtons.Count)
            return (TModule)this;

        TitleBarButtons.RemoveAt(index);

        if (ModuleWindow != null)
            ModuleWindow.UpdateTitleBarButtons();

        return (TModule)this;
    }

    /// <summary>
    /// Sets the title bar buttons of the changelog window, replacing any existing buttons.
    /// </summary>
    /// <param name="titleBarButtons">The list of title bar buttons to set.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetTitleBarButtons(List<TitleBarButton> titleBarButtons)
    {
        TitleBarButtons = titleBarButtons ?? new();

        if (ModuleWindow != null)
            ModuleWindow.UpdateTitleBarButtons();

        return (TModule)this;
    }

    /// <summary>
    /// Clears all title bar buttons from the changelog window.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ClearTitleBarButtons()
    {
        TitleBarButtons.Clear();

        if (ModuleWindow != null)
            ModuleWindow.UpdateTitleBarButtons();

        return (TModule)this;
    }

    #endregion

    #region Window management

    /// <summary>
    /// Sets the window name of the changelog window.
    /// </summary>
    /// <param name="windowName">The name of the window.</param>
    /// <returns>The module instance for chaining.</returns>
    public TModule SetWindowName(string windowName)
    {
        DisplayWindowName = windowName;
        return (TModule)this;
    }

    /// <summary>
    /// Gets the full window name of the changelog window, including the unique IDs.
    /// </summary>
    /// <returns>The full window name.</returns>
    public string GetFullWindowName() => ModuleWindow!.WindowName ?? string.Empty;

    /// <summary>
    /// Registers a window with the NoireLib window system.<br/>
    /// Call this from your derived module's InitializeModule method after creating your window.
    /// </summary>
    /// <param name="window">The window to register.</param>
    /// <returns>The module instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if NoireLib window system is not initialized or if the window is null or invalid.</exception>
    protected TModule RegisterWindow(TWindow window)
    {
        if (NoireService.NoireWindowSystem == null)
            throw new InvalidOperationException("NoireLib window system is not initialized. Cannot register window. Please initialize NoireLib.");

        if (window == null)
            throw new InvalidOperationException("Attempted to register a null window.");

        if (ModuleWindow != null)
        {
            if (EnableLogging)
                NoireLogger.LogWarning((TModule)this, "A window is already registered for this module. Unregistering the previous window first.");
            UnregisterWindow();
        }

        ModuleWindow = window;

        if (window is not Window dalamudWindow)
            throw new InvalidOperationException($"The provided window is not a valid Dalamud Window. Cannot register it.");

        NoireService.NoireWindowSystem.AddWindow(dalamudWindow);

        if (EnableLogging)
            NoireLogger.LogInfo((TModule)this, $"Window '{window.DisplayWindowName}' registered to NoireLib window system.");

        return (TModule)this;
    }

    /// <summary>
    /// Unregisters the module's window from the NoireLib window system.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if NoireLib window system is not initialized.</exception>
    protected TModule UnregisterWindow()
    {
        if (NoireService.NoireWindowSystem == null)
            throw new InvalidOperationException("NoireLib window system is not initialized. Cannot unregister window. Please initialize NoireLib.");

        if (ModuleWindow == null)
            return (TModule)this;

        if (ModuleWindow is Window dalamudWindow)
        {
            NoireService.NoireWindowSystem.RemoveWindow(dalamudWindow);

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Window '{ModuleWindow.DisplayWindowName}' unregistered from NoireLib window system.");
        }

        ModuleWindow?.Dispose();
        ModuleWindow = null;

        return (TModule)this;
    }

    /// <summary>
    /// Shows the module's window if it has one.
    /// </summary>
    /// <param name="show">Whether to show the window. Set to null to toggle the window.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ShowWindow(bool? show)
    {
        if (ModuleWindow != null)
        {
            if (!show.HasValue)
                ToggleWindow();
            else if (show.Value)
                ShowWindow();
            else
                HideWindow();
        }
        else if (EnableLogging)
            NoireLogger.LogWarning(this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Shows the module's window if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ShowWindow()
    {
        if (ModuleWindow != null)
            ModuleWindow.IsOpen = true;
        else if (EnableLogging)
            NoireLogger.LogWarning(this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Hides the module's window if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule HideWindow()
    {
        if (ModuleWindow != null)
            ModuleWindow.IsOpen = false;
        else if (EnableLogging)
            NoireLogger.LogWarning(this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Toggles the module's window visibility if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ToggleWindow()
    {
        if (ModuleWindow != null)
            ModuleWindow.IsOpen = !ModuleWindow.IsOpen;
        else if (EnableLogging)
            NoireLogger.LogWarning(this, "This module does not have an associated window.");

        return (TModule)this;
    }

    #endregion

    /// <summary>
    /// Disposes the module completely, unregistering any module window.<br/>
    /// Do not call manually unless you are managing module lifecycles yourself (i.e. Without using <see cref="NoireLibMain.AddModule{T}(T)"/>).
    /// </summary>
    public override void Dispose()
    {
        UnregisterWindow();
        DisposeInternal();
    }
}
