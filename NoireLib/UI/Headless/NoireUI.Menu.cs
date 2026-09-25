using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

public static partial class NoireUI
{
    private const ImGuiWindowFlags MenuFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    private static readonly NoireMenu MenuBody = new();

    /// <summary>Draws an open context menu with the active skin, filled by a body, closing it on a click, Escape or a press outside it.</summary>
    /// <typeparam name="T">The menu's target type.</typeparam>
    /// <param name="state">The menu's state.</param>
    /// <param name="target">What the menu acts on, handed to the body.</param>
    /// <param name="body">Fills the menu; a static lambda keeps it free of allocation.</param>
    public static void Menu<T>(NoireMenuState state, T target, Action<NoireMenu, T> body)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(body);

        if (!state.IsOpen || !UiDraw.Available)
            return;

        using var profile = Profiler.Measure(nameof(Menu));

        var overlays = NoireSkins.Active.Overlays;
        var depth = UiStyleDepth.Capture();
        overlays.MenuStyle();
        var pushed = depth.Since();

        ImGui.SetNextWindowPos(NoirePlacement.OnScreen(state.Position, state.Max - state.Min));
        ImGui.Begin(state.WindowName, MenuFlags);
        UiStyleDepth.Pop(pushed);

        Vector2 min;
        Vector2 max;

        try
        {
            NoireWindowChrome.KeepInFront();
            MenuBody.Begin(overlays);
            overlays.MenuBegin();
            body(MenuBody, target);
        }
        finally
        {
            overlays.MenuEnd();
            min = ImGui.GetWindowPos();
            max = min + ImGui.GetWindowSize();
            ImGui.End();
        }

        state.Min = min;
        state.Max = max;
        state.AfterDraw(MenuBody.Clicked, NoireDismiss.Outside(min, max));
    }
}
