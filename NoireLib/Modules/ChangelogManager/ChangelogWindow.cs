using Dalamud.Bindings.ImGui;
using NoireLib.Core.Modules;
using System;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>Changelog window that displays changelog entries using ImGui.</summary>
public class ChangelogWindow : NoireModuleWindowBase<NoireChangelogManager>
{
    /// <summary>Gets or sets the name of the display window.</summary>
    public override string DisplayWindowName { get; set; } = "Changelog";

    /// <summary>Constructor for ChangelogWindow.</summary>
    /// <param name="noireChangelogManager">The <see cref="NoireChangelogManager"/> instance associated with this window.</param>
    public ChangelogWindow(NoireChangelogManager noireChangelogManager)
        : base(noireChangelogManager, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(750, 500);

        UpdateTitleBarButtons();
    }

    /// <summary>Reloads the versions from the ChangelogManager and selects the latest one.</summary>
    public void UpdateVersions() => ParentModule.RebuildVersions();

    /// <summary>
    /// Shows this window on a specific version. If no version is provided, it shows the latest version.
    /// </summary>
    /// <param name="version">The Version object to show.</param>
    public void ShowChangelogForVersion(Version? version = null)
    {
        if (!ParentModule.PrepareDisplay(version))
            return;

        IsOpen = true;

        ParentModule.OnWindowOpened(ParentModule.SelectedVersion!.Version);
    }

    /// <summary>Closes the changelog window.</summary>
    public new void CloseWindow()
    {
        if (IsOpen)
        {
            IsOpen = false;
            ParentModule.OnWindowClosed();
        }
    }

    #region Drawing

    /// <summary>Draws the changelog window content.</summary>
    public override void Draw()
    {
        ChangelogDraw.VersionSelector(ParentModule);
        ImGui.Dummy(new Vector2(0, 3));
        ChangelogDraw.Content(ParentModule, ImGui.GetContentRegionAvail().Y - 40f);
        ImGui.Dummy(new Vector2(0, 3));
        ChangelogDraw.Footer(ParentModule);
    }

    #endregion

    /// <summary>Disposes resources used by the ChangelogWindow.</summary>
    public override void Dispose() { /* no-op */ }
}
