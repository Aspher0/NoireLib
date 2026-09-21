using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Adds top-level menus to Dalamud's dev bar, the bar <c>/xldev</c> opens.<br/>
/// Dalamud's own menus cannot be extended. Whether the bar is open is read from Dalamud internals.
/// </summary>
public static class DalamudDevBarHelper
{
    private const string DisposeKey = "NoireLib.DalamudDevBarHelper";

    private static readonly List<DevBarMenu> Menus = [];
    private static readonly object Gate = new();

    private static bool attached;

    /// <summary>Whether Dalamud's dev bar is open right now.</summary>
    public static bool IsOpen
        => DalamudInternals.Read(DalamudInternals.DalamudInterface(), "IsDevMenuOpen") is true;

    /// <summary>The menus this helper currently holds.</summary>
    public static IReadOnlyList<DevBarMenu> Registered
    {
        get
        {
            lock (Gate)
                return Menus.ToArray();
        }
    }

    /// <summary>Adds a menu to the dev bar.</summary>
    /// <param name="options">What the menu holds.</param>
    /// <returns>The menu, disposed to remove it.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    public static DevBarMenu Register(DevBarMenuOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);

        var menu = new DevBarMenu(options);

        lock (Gate)
        {
            Menus.Add(menu);
            Menus.Sort(static (left, right) => left.Options.Priority.CompareTo(right.Options.Priority));
            Attach();
        }

        return menu;
    }

    /// <summary>Adds a menu holding a list of clickable lines.</summary>
    /// <param name="name">The menu's name in the bar.</param>
    /// <param name="items">The lines it holds.</param>
    /// <returns>The menu, disposed to remove it.</returns>
    public static DevBarMenu Register(string name, params DevBarItem[] items)
        => Register(new DevBarMenuOptions { Name = name, Items = items });

    /// <summary>Opens or closes Dalamud's dev bar, like <c>/xldev</c>.</summary>
    /// <param name="open">True to open it, false to close it.</param>
    /// <returns>True when the bar could be reached.</returns>
    public static bool SetOpen(bool open)
    {
        var dalamud = DalamudInternals.DalamudInterface();
        return dalamud != null && new ReflectedObject(dalamud).Set("IsDevMenuOpen", open);
    }

    /// <summary>Removes every menu this helper holds.</summary>
    public static void RemoveAll()
    {
        DevBarMenu[] snapshot;
        lock (Gate)
            snapshot = [.. Menus];

        foreach (var menu in snapshot)
            menu.Dispose();
    }

    internal static void Forget(DevBarMenu menu)
    {
        lock (Gate)
        {
            Menus.Remove(menu);
            if (Menus.Count == 0)
                Detach();
        }
    }

    private static void Attach()
    {
        if (attached)
            return;

        NoireService.PluginInterface.UiBuilder.Draw += Draw;
        NoireLibMain.RegisterOnDispose(DisposeKey, RemoveAll);
        attached = true;
    }

    private static void Detach()
    {
        if (!attached)
            return;

        NoireService.PluginInterface.UiBuilder.Draw -= Draw;
        NoireLibMain.UnregisterOnDispose(DisposeKey);
        attached = false;
    }

    private static void Draw()
    {
        if (!IsOpen)
            return;

        DevBarMenu[] snapshot;
        lock (Gate)
        {
            if (Menus.Count == 0)
                return;

            snapshot = [.. Menus];
        }

        SafeExecutor.ExecuteSafely(() =>
        {
            // A second BeginMainMenuBar in a frame appends to the bar Dalamud opened.
            if (!ImGui.BeginMainMenuBar())
                return;

            foreach (var menu in snapshot)
            {
                if (menu.Visible && !menu.IsDisposed)
                    DrawMenu(menu);
            }

            ImGui.EndMainMenuBar();
        });
    }

    private static void DrawMenu(DevBarMenu menu)
    {
        if (!ImGui.BeginMenu(menu.Name))
            return;

        if (menu.Options.Items is { Count: > 0 } items)
        {
            foreach (var item in items)
                DrawItem(item);
        }

        menu.Options.OnDraw?.Invoke();
        ImGui.EndMenu();
    }

    private static void DrawItem(DevBarItem item)
    {
        if (item.SeparatorBefore)
            ImGui.Separator();

        var enabled = item.Enabled?.Invoke() ?? true;
        var selected = item.Selected?.Invoke() ?? false;

        if (ImGui.MenuItem(item.Label, item.Shortcut ?? string.Empty, selected, enabled))
            item.OnClick();
    }
}
