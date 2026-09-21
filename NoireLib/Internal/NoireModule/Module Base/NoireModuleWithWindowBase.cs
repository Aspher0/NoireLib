using Dalamud.Interface.Windowing;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules that integrate a window within the NoireLib library.<br/>
/// Inherits from <see cref="NoireModuleBase{TModule}"/>.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
/// <typeparam name="TWindow">The type of the window associated with the module.</typeparam>
public abstract class NoireModuleWithWindowBase<TModule, TWindow> : NoireModuleBase<TModule>, INoireModuleWithWindow
    where TModule : NoireModuleWithWindowBase<TModule, TWindow>, new()
    where TWindow : Window, INoireModuleWindow
{
    /// <summary>
    /// The window associated with this module, if any.
    /// </summary>
    protected TWindow? ModuleWindow { get; set; }

    /// <summary>
    /// A window supplied by the plugin that replaces <see cref="ModuleWindow"/> for display, or <see langword="null"/> to use the built-in one.
    /// </summary>
    protected Window? CustomDisplayWindow { get; private set; }

    /// <summary>
    /// The window the show, hide and toggle methods act on: <see cref="CustomDisplayWindow"/> when set, otherwise <see cref="ModuleWindow"/>.
    /// </summary>
    protected Window? DisplayedWindow => CustomDisplayWindow ?? ModuleWindow;

    /// <summary>
    /// Gets whether this module has an associated window.
    /// </summary>
    public bool HasWindow => ModuleWindow != null;

    /// <summary>
    /// Whether the module's displayed window is currently open; false when the module holds no window (see <see cref="HasWindow"/>).
    /// Use <see cref="SetShowWindow"/>, <see cref="ShowWindow"/>, <see cref="HideWindow"/> or <see cref="ToggleWindow"/> to change it.
    /// </summary>
    public bool IsWindowOpen => DisplayedWindow?.IsOpen == true;

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
    /// Adds a button to the title bar of the module's window.
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
    /// Removes a button from the title bar of the module's window by its index.
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
    /// Sets the title bar buttons of the module's window, replacing any existing buttons.
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
    /// Clears all title bar buttons from the module's window.
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
    /// Sets the window name of the module's window.
    /// </summary>
    /// <param name="windowName">The name of the window.</param>
    /// <returns>The module instance for chaining.</returns>
    public TModule SetWindowName(string windowName)
    {
        DisplayWindowName = windowName;
        return (TModule)this;
    }

    /// <summary>
    /// Gets the full window name of the module's window, including the unique IDs.
    /// </summary>
    /// <returns>The full window name, or an empty string when the module holds no window.</returns>
    public string GetFullWindowName() => ModuleWindow?.WindowName ?? string.Empty;

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
    /// Unregisters the module's window from the NoireLib window system.<br/>
    /// Does nothing when the module holds no window, which <see cref="HasWindow"/> reports.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the module holds a window and the NoireLib window system is not initialized.</exception>
    protected TModule UnregisterWindow()
    {
        // A windowless module needs no window system; skipping this check would throw on disposal for every module without one.
        if (ModuleWindow == null)
            return (TModule)this;

        if (NoireService.NoireWindowSystem == null)
            throw new InvalidOperationException("NoireLib window system is not initialized. Cannot unregister window. Please initialize NoireLib.");

        if (ModuleWindow is Window dalamudWindow)
        {
            NoireService.NoireWindowSystem.RemoveWindow(dalamudWindow);

            if (EnableLogging)
                NoireLogger.LogInfo((TModule)this, $"Window '{ModuleWindow.DisplayWindowName}' unregistered from NoireLib window system.");
        }

        ModuleWindow?.Dispose();
        ModuleWindow = null;

        return (TModule)this;
    }

    /// <summary>
    /// Replaces the built-in window with <paramref name="window"/> for display, carrying the open state across.<br/>
    /// The module never draws or registers <paramref name="window"/>; <see langword="null"/> restores the built-in window.
    /// </summary>
    /// <param name="window">The replacement window, or <see langword="null"/>.</param>
    protected void SetCustomDisplayWindow(Window? window)
    {
        if (ReferenceEquals(CustomDisplayWindow, window))
            return;

        var previous = DisplayedWindow;
        var wasOpen = previous?.IsOpen == true;

        if (wasOpen)
            previous!.IsOpen = false;

        CustomDisplayWindow = window;

        if (wasOpen && DisplayedWindow != null)
            DisplayedWindow.IsOpen = true;
    }

    /// <summary>
    /// Shows the module's window if it has one.
    /// </summary>
    /// <param name="show">Whether to show the window. Set to null to toggle the window.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetShowWindow(bool? show)
    {
        if (DisplayedWindow != null)
        {
            if (!show.HasValue)
                ToggleWindow();
            else if (show.Value)
                ShowWindow();
            else
                HideWindow();
        }
        else if (EnableLogging)
            NoireLogger.LogWarning((TModule)this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Shows the module's window if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ShowWindow()
    {
        if (DisplayedWindow != null)
            DisplayedWindow.IsOpen = true;
        else if (EnableLogging)
            NoireLogger.LogWarning((TModule)this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Hides the module's window if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule HideWindow()
    {
        if (DisplayedWindow != null)
            DisplayedWindow.IsOpen = false;
        else if (EnableLogging)
            NoireLogger.LogWarning((TModule)this, "This module does not have an associated window.");

        return (TModule)this;
    }

    /// <summary>
    /// Toggles the module's window visibility if it has one.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule ToggleWindow()
    {
        if (DisplayedWindow != null)
            DisplayedWindow.IsOpen = !DisplayedWindow.IsOpen;
        else if (EnableLogging)
            NoireLogger.LogWarning((TModule)this, "This module does not have an associated window.");

        return (TModule)this;
    }

    #endregion

    // Window unregistered before base teardown. It must stop drawing before its state is torn apart.
    private protected override void DisposeCore()
    {
        // The custom window belongs to the plugin; released here, never closed or disposed.
        CustomDisplayWindow = null;
        UnregisterWindow();
        DisposeInternal();
    }
}

/// <summary>
/// Base class for modules that integrate a window within the NoireLib library.<br/>
/// Inherits from <see cref="NoireModuleBase{TModule}"/>. Eagerly loads <typeparamref name="TConfiguration"/> in the
/// static constructor.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
/// <typeparam name="TWindow">The type of the window associated with the module.</typeparam>
/// <typeparam name="TConfiguration">The type of the configuration associated with the module.</typeparam>
public abstract class NoireModuleWithWindowBase<TModule, TWindow, TConfiguration> : NoireModuleWithWindowBase<TModule, TWindow>
    where TModule : NoireModuleWithWindowBase<TModule, TWindow, TConfiguration>, new()
    where TWindow : Window, INoireModuleWindow
    where TConfiguration : NoireConfigBase, new()
{
    static NoireModuleWithWindowBase()
    {
        NoireConfigManager.GetConfig<TConfiguration>();
    }

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
}
