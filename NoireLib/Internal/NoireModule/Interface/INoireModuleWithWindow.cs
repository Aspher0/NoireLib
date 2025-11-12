using System.Collections.Generic;
using static Dalamud.Interface.Windowing.Window;

namespace NoireLib.Core.Modules;

/// <summary>
/// Interface for modules that have an associated window within the NoireLib library.
/// </summary>
public interface INoireModuleWithWindow : INoireModule
{
    /// <summary>
    /// Gets or sets the name displayed in the title bar of the window.
    /// </summary>
    string DisplayWindowName { get; set; }

    /// <summary>
    /// Gets or sets the title bar buttons for the module's window.
    /// </summary>
    List<TitleBarButton> TitleBarButtons { get; }
}
