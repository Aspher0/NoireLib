using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using NoireDraw3DDemoPlugin.Windows.Pages;
using NoireLib.Draw3D;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>The demo window: a status strip, an icon rail, and the open page.</summary>
public sealed class DemoWindow : Window, IDisposable
{
    private const float RailWidth = 158f;
    private const float RailRowHeight = 22f;

    private readonly DemoShell shell = new();

    private readonly RendererPage rendererPage;
    private readonly ShowcasePage showcasePage = new();
    private readonly ScenesPage scenesPage = new();
    private readonly GameAssetsPage gameAssetsPage = new();
    private readonly DecalsPage decalsPage = new();
    private readonly NativeUiPage nativeUiPage = new();
    private readonly LightingPage lightingPage = new();
    private readonly InteractionPage interactionPage = new();
    private readonly DiagnosticsPage diagnosticsPage = new();
#if DEBUG
    private readonly DebugPage debugPage = new();
#endif

    public DemoWindow() : base("NoireLib Draw3D Demo###noire3ddemo")
    {
        rendererPage = new RendererPage(shell);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820f, 520f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(1000f, 640f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Hides the window while the game UI is hidden, unless the Renderer page keeps it open.</summary>
    public override bool DrawConditions()
        => shell.KeepWindowWhenUiHidden || !NoireDraw3D.IsGameUiHidden;

    public override void Draw()
    {
        using var style = Ui.Style();

        DrawStatusStrip();
        ImGui.Separator();
        DrawRail();
        ImGui.SameLine();
        DrawPage();
    }

    private static void DrawStatusStrip()
    {
        var enabled = NoireDraw3D.Enabled;
        var live = enabled && NoireDraw3D.HasValidFrame;

        // The font may lack a status glyph.
        var (color, label) = (enabled, live) switch
        {
            (false, _) => (ImGuiColors.DalamudRed, "OFF"),
            (true, false) => (ImGuiColors.DalamudYellow, "NO FRAME"),
            _ => (ImGuiColors.HealerGreen, "LIVE"),
        };

        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);

        // Measured before the tooltip overwrites the last-item state.
        var labelEnd = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;

        if (ImGui.IsItemHovered())
            Ui.Tooltip(live
                ? "The layer has a readable game camera and is compositing."
                : enabled
                    ? "Enabled, but no frame with a readable camera yet - a loading or title screen."
                    : "The master switch is off. Renderer page.");

        if (!live)
            return;

        var stats = NoireDraw3D.Stats;

        var (flag, flagTip) = !stats.DepthAvailable
            ? ("no depth", "The game's depth buffer was unreadable this frame: nothing hides behind world geometry, and decals have no surface to land on.")
            : stats.UsedFallbackCamera
                ? ("fallback cam", "This frame guessed a view-projection instead of reading the real camera. Placement is approximate; the ImGuizmo backend drops to Native.")
                : NoireDraw3D.IsGameUiHidden
                    ? ("ui hidden", "The game UI is hidden and the layer is still drawing, because 'Keep 3D layer' is on.")
                    : (string.Empty, string.Empty);

        var ms = $"scene {stats.SceneGpuMs:F2}  comp {stats.CompositeGpuMs:F2} ms";
        float msWidth;
        using (ImRaii.PushFont(UiBuilder.MonoFont))
            msWidth = ImGui.CalcTextSize(ms).X;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var flagWidth = flag.Length > 0 ? ImGui.CalcTextSize(flag).X + spacing : 0f;

        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var x = right - msWidth - flagWidth;

        // Skipped when too narrow.
        if (x < labelEnd + spacing)
            return;

        ImGui.SameLine(x);

        if (flag.Length > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                ImGui.TextUnformatted(flag);

            if (ImGui.IsItemHovered())
                Ui.Tooltip(flagTip);

            ImGui.SameLine(0f, spacing);
        }

        Ui.Mono(ms, ImGuiColors.DalamudGrey3);
        if (ImGui.IsItemHovered())
            Ui.Tooltip("GPU time last frame: scene draws your geometry, composite blits the layer into the game's frame.");
    }

    private void DrawRail()
    {
        using var child = ImRaii.Child("##rail", new Vector2(RailWidth * Ui.Scale, 0f), true);
        if (!child)
            return;

        var group = string.Empty;
        foreach (var info in DemoPageInfo.All)
        {
            if (info.Group != group)
            {
                group = info.Group;
                if (info.Page != DemoPageInfo.All[0].Page)
                    ImGui.Spacing();

                using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
                ImGui.TextUnformatted(group.ToUpperInvariant());
            }

            DrawRailItem(info);
        }
    }

    // A label cannot mix the icon and text fonts.
    private void DrawRailItem(DemoPageInfo info)
    {
        var active = shell.Current == info.Page;
        var start = ImGui.GetCursorPos();
        var startScreen = ImGui.GetCursorScreenPos();
        var height = RailRowHeight * Ui.Scale;

        using (ImRaii.PushColor(ImGuiCol.Text, Ui.Accent, active))
        {
            if (ImGui.Selectable($"##nav{info.Page}", active, ImGuiSelectableFlags.None, new Vector2(0f, height)))
                shell.Navigate(info.Page);

            var end = ImGui.GetCursorPos();

            if (active)
            {
                ImGui.GetWindowDrawList().AddRectFilled(
                    startScreen,
                    startScreen + new Vector2(2f * Ui.Scale, height),
                    ImGui.GetColorU32(Ui.Accent));
            }

            var textY = start.Y + (height - ImGui.GetTextLineHeight()) * 0.5f;
            ImGui.SetCursorPos(new Vector2(start.X + 8f * Ui.Scale, textY));
            Ui.Icon(info.Icon);
            ImGui.SameLine(0f, 8f * Ui.Scale);
            ImGui.TextUnformatted(info.Label);
            ImGui.SetCursorPos(end);
        }
    }

    private void DrawPage()
    {
        using var id = ImRaii.PushId((int)shell.Current);

        // Without its own child, the rail's height wraps later rows underneath it.
        var pinnedTabs = shell.Current == DemoPage.Scenes;
#if DEBUG
        pinnedTabs = pinnedTabs || shell.Current == DemoPage.Debug;
#endif
        if (pinnedTabs)
        {
            using var frame = ImRaii.Child("##pageframe", Vector2.Zero, false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (frame)
            {
                if (shell.Current == DemoPage.Scenes)
                    scenesPage.Draw();
#if DEBUG
                else
                    debugPage.Draw();
#endif
            }

            return;
        }

        using var body = Ui.Scroll("##body");
        if (!body)
            return;

        switch (shell.Current)
        {
            case DemoPage.Showcase: showcasePage.Draw(); break;
            case DemoPage.GameAssets: gameAssetsPage.Draw(); break;
            case DemoPage.Renderer: rendererPage.Draw(); break;
            case DemoPage.Decals: decalsPage.Draw(); break;
            case DemoPage.NativeUi: nativeUiPage.Draw(); break;
            case DemoPage.Lighting: lightingPage.Draw(); break;
            case DemoPage.Interaction: interactionPage.Draw(); break;
            case DemoPage.Diagnostics: diagnosticsPage.Draw(); break;
#if DEBUG
            case DemoPage.Debug: debugPage.Draw(); break;
#endif
        }
    }

    public void Dispose()
    {
        showcasePage.Dispose();
        scenesPage.Dispose();
        gameAssetsPage.Dispose();
        diagnosticsPage.Dispose();
    }
}
