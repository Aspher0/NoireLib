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

/// <summary>
/// The demo window: a status strip, an icon rail, and the open page. A rail rather than a tab bar because eight tabs
/// read as a wall, and the rail keeps every destination one click from any other.
/// <para>Owns the pages and the <see cref="DemoShell"/> they share; forwards disposal to the three that hold live scenes.</para>
/// </summary>
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

    /// <summary>Creates the window (hidden until <c>/noire3ddemo</c> or the plugin-list button).</summary>
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

    /// <summary>
    /// Hides the window while the game UI is hidden, unless asked otherwise on the Renderer page. Decided here rather
    /// than left to Dalamud: <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/> keeps the layer alive precisely by telling
    /// Dalamud not to hide this plugin, so the window can only step aside by checking the game's state itself.
    /// </summary>
    public override bool DrawConditions()
        => shell.KeepWindowWhenUiHidden || !NoireDraw3D.IsGameUiHidden;

    /// <inheritdoc/>
    public override void Draw()
    {
        using var style = Ui.Style();

        DrawStatusStrip();
        ImGui.Separator();
        DrawRail();
        ImGui.SameLine();
        DrawPage();
    }

    // ---------------------------------------------------------------- status

    /// <summary>
    /// Is it running, and what does it cost. Two facts and no more: this strip is on screen on every page and has to fit
    /// the narrowest window, so draw calls, triangles, depth source and the skip counters live on Diagnostics instead of
    /// fighting for room here. An abnormal state earns the only other slot, because when it is showing it matters more
    /// than the timing does.
    /// </summary>
    private static void DrawStatusStrip()
    {
        var enabled = NoireDraw3D.Enabled;
        var live = enabled && NoireDraw3D.HasValidFrame;

        // Colour carries the state rather than a glyph: the demo cannot assume any symbol exists in the loaded font.
        var (color, label) = (enabled, live) switch
        {
            (false, _) => (ImGuiColors.DalamudRed, "OFF"),
            (true, false) => (ImGuiColors.DalamudYellow, "NO FRAME"),
            _ => (ImGuiColors.HealerGreen, "LIVE"),
        };

        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(label);

        // Measure before the tooltip: last-item state is per-context, not per-window, so the tooltip's own text would
        // overwrite it and the fit check below would be reading the wrong rect.
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

        // One slot, worst-first: no depth is broken rendering, a fallback camera is degraded placement, a hidden UI is
        // merely worth knowing. Anything not abnormal says nothing at all.
        var (flag, flagTip) = !stats.DepthAvailable
            ? ("no depth", "The game's depth buffer was unreadable this frame: nothing hides behind world geometry, and decals have no surface to land on.")
            : stats.UsedFallbackCamera
                ? ("fallback cam", "This frame guessed a view-projection instead of reading the real camera. Placement is approximate; the ImGuizmo backend drops to Native.")
                : NoireDraw3D.IsGameUiHidden
                    ? ("ui hidden", "The game UI is hidden and the layer is still drawing, because 'Keep 3D layer' is on.")
                    : (string.Empty, string.Empty);

        // Split rather than totalled: the two halves fail for different reasons, so which one moved is the useful part.
        var ms = $"scene {stats.SceneGpuMs:F2}  comp {stats.CompositeGpuMs:F2} ms";
        float msWidth;
        using (ImRaii.PushFont(UiBuilder.MonoFont))
            msWidth = ImGui.CalcTextSize(ms).X;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var flagWidth = flag.Length > 0 ? ImGui.CalcTextSize(flag).X + spacing : 0f;

        // Right-aligned, so the reading sits in a fixed column instead of sliding as the number changes width.
        // Window-local throughout: SameLine's offset and GetCursorPosX share an origin.
        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var x = right - msWidth - flagWidth;

        // Too narrow to place it without running into the label: drop it rather than clip. It is on Diagnostics anyway.
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

    // ---------------------------------------------------------------- rail

    /// <summary>The page rail: grouped, glyph per entry, an accent bar down the selected one.</summary>
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

    /// <summary>
    /// One rail entry. The caption needs the icon font and the text font on one line, which a Selectable label cannot
    /// carry, so the row is an empty Selectable with its content painted over it.
    /// </summary>
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

    /// <summary>The open page: a scroll body for plain pages, a plain frame for the one that pins its own tab bar.</summary>
    private void DrawPage()
    {
        using var id = ImRaii.PushId((int)shell.Current);

        // Scenes still needs a child, just not a scrolling one. The child is what gives this column its own layout
        // context: without it the rail beside us is part of the same line, and its full height becomes the line height,
        // so everything after the first row wraps underneath the rail. NoScroll keeps the page's tab bar pinned; each
        // of its tabs scrolls its own body instead.
        if (shell.Current == DemoPage.Scenes)
        {
            using var frame = ImRaii.Child("##pageframe", Vector2.Zero, false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (frame)
                scenesPage.Draw();

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

    /// <inheritdoc/>
    public void Dispose()
    {
        showcasePage.Dispose();
        scenesPage.Dispose();
        gameAssetsPage.Dispose();
        diagnosticsPage.Dispose();
    }
}
