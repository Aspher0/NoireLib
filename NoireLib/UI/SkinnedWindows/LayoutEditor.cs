using Dalamud.Bindings.ImGui;
using NoireLib.Localizer;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

// Beside a skinned window: move and hide the children a skin stacks, hide title buttons, restore the default. Plain
// ImGui: it reads the same over every skin.
internal static class LayoutEditor
{
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;

    private static readonly List<Component> Parents = [];
    private static readonly List<Component> Ordered = [];

    internal static void Draw(NoireSkinnedWindowBase window)
    {
        if (!UiDraw.Available)
            return;

        var scale = NoireUI.Scale;
        var width = 280f * scale;
        var layout = window.LayoutFor(NoireSkins.Active);

        ImGui.SetNextWindowPos(
            NoirePlacement.Beside(window.WindowMin, window.WindowMax, new Vector2(width, 320f * scale), 8f * scale, (window.WindowMin.Y + window.WindowMax.Y) * 0.5f),
            ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

        var open = true;
        var name = UiIds.Labelled(NoireStrings.LayoutTitle.Text, "###noire.layout.", window.Id);

        if (ImGui.Begin(name, ref open, Flags))
        {
            NoireWindowChrome.KeepInFront();

            Parents.Clear();
            CollectParents(window.Root);

            foreach (var parent in Parents)
                DrawParent(window, parent, layout);

            DrawButtons(window, layout);

            ImGui.Spacing();

            if (ImGui.Button(NoireStrings.LayoutReset.Text))
            {
                layout.Clear();
                window.SaveLayout();
            }

            ImGui.SameLine();

            if (ImGui.Button(NoireStrings.Done.Text))
                open = false;
        }

        ImGui.End();

        if (!open)
            window.EditingLayout = false;
    }

    private static void CollectParents(Component component)
    {
        if (component.LaidOut && component.Children.Count > 0)
            Parents.Add(component);

        for (var i = 0; i < component.Children.Count; i++)
            CollectParents(component.Children[i]);
    }

    private static void DrawParent(NoireSkinnedWindowBase window, Component parent, ComponentLayout layout)
    {
        ImGui.PushID(parent.Path);
        ImGui.TextDisabled(parent is WindowRoot ? window.Title.Text : parent.Name?.Text ?? parent.Id);

        NoireSkin.OrderOf(parent, layout, Ordered);

        var side = ImGui.GetFrameHeight();
        var movableSeen = 0;
        var movableCount = 0;

        foreach (var child in Ordered)
        {
            if (child.CanMove)
                movableCount++;
        }

        for (var i = 0; i < Ordered.Count; i++)
        {
            var child = Ordered[i];
            ImGui.PushID(child.Id);

            if (child.CanHide)
            {
                var shown = !layout.IsHidden(parent.Path, child.Id);

                if (ImGui.Checkbox("##shown", ref shown))
                {
                    layout.SetHidden(parent.Path, child.Id, !shown);
                    window.SaveLayout();
                }
            }
            else
            {
                ImGui.Dummy(new Vector2(side, side));
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();

            if (child.CanMove)
                ImGui.TextUnformatted(child.Name?.Text ?? child.Id);
            else
                ImGui.TextDisabled(child.Name?.Text ?? child.Id);

            if (child.CanMove)
            {
                ImGui.SameLine(ImGui.GetContentRegionMax().X - (side * 2f) - ImGui.GetStyle().ItemSpacing.X);
                ImGui.BeginDisabled(movableSeen == 0);

                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                    Move(window, parent, layout, movableSeen, -1);

                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(movableSeen == movableCount - 1);

                if (ImGui.ArrowButton("##down", ImGuiDir.Down))
                    Move(window, parent, layout, movableSeen, 1);

                ImGui.EndDisabled();
                movableSeen++;
            }

            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.PopID();
    }

    private static void DrawButtons(NoireSkinnedWindowBase window, ComponentLayout layout)
    {
        var buttons = window.DeclaredButtons;

        if (buttons.Count == 0)
            return;

        ImGui.TextDisabled(NoireStrings.LayoutButtons.Text);

        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var shown = !layout.HiddenButtons.Contains(button.Id);

            ImGui.PushID(button.Id);

            if (ImGui.Checkbox(button.Tooltip.Text, ref shown))
            {
                layout.SetButtonHidden(button.Id, !shown);
                window.SaveLayout();
            }

            ImGui.PopID();
        }
    }

    // Swaps the movable child at an index of the movable order with its neighbour; the order is stored only on a click.
    private static void Move(NoireSkinnedWindowBase window, Component parent, ComponentLayout layout, int movableIndex, int direction)
    {
        var ids = new List<string>(Ordered.Count);

        foreach (var child in Ordered)
        {
            if (child.CanMove)
                ids.Add(child.Id);
        }

        var target = movableIndex + direction;

        if (target < 0 || target >= ids.Count)
            return;

        (ids[movableIndex], ids[target]) = (ids[target], ids[movableIndex]);
        layout.SetOrder(parent.Path, ids);
        window.SaveLayout();
    }
}
