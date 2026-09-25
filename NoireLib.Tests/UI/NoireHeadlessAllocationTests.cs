using FluentAssertions;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the per-frame paths of the headless state at zero allocation: the context menu with its actions, glyph icons,
/// list layout, hold, drag, dismissal, placement, pickers, clicks, the pill and the confirmation state.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireHeadlessAllocationTests : IClassFixture<UiHarness>
{
    private const int Repeats = 20;

    private static readonly NoireString Play = new("test.headless.alloc.play", "Play");
    private static readonly NoireString Remove = new("test.headless.alloc.remove", "Remove");
    private static readonly NoireString HoldCtrl = new("test.headless.alloc.ctrl", "Hold Ctrl.");

    private static readonly UiAction<int> PlayAction = new(Play, NoireIcon.Play, static _ => { });

    private static readonly UiAction<int> RemoveAction = new(Remove, NoireIcon.Trash, static _ => { })
    {
        RequiresCtrl = true,
        Hint = HoldCtrl,
    };

    private static readonly UiAction<int> HiddenAction = new(Remove, NoireIcon.Block, static _ => { }) { Available = static _ => false };

    private static readonly Action<NoireMenu, int> MenuBody = static (menu, target) =>
    {
        menu.Title("Row", 0);
        menu.Item(PlayAction, target);
        menu.Separator();
        menu.Item(RemoveAction, target);
        menu.Item(HiddenAction, target);
        menu.Separator();
    };

    private static readonly NoireMenuState OpenMenu = new();
    private static readonly NoireListState List = new();
    private static readonly NoireHold Hold = new();
    private static readonly NoireDrag<string> Drag = new();
    private static readonly NoireClicks<string> Clicks = new();
    private static readonly NoirePill Pill = new();
    private static readonly NoireAsk Ask = new();
    private static readonly List<string> Source = ["Wave", "Bow", "Dance"];
    private static readonly NoirePicker<string> Picker = new(static () => Source, static (item, query) => item.Contains(query, StringComparison.Ordinal));
    private static readonly NoireIcon[] Glyphs = Enum.GetValues<NoireIcon>()[2..];

    private readonly UiHarness harness;

    public NoireHeadlessAllocationTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Menu_WithActions_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                if (!OpenMenu.IsOpen)
                    OpenMenu.Open(new Vector2(100f, 100f));

                NoireUI.Menu(OpenMenu, 7, MenuBody);
            },
            warmUpFrames: 3);

        OpenMenu.IsOpen.Should().BeTrue();
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void GlyphIcons_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                foreach (var icon in Glyphs)
                    NoireIcons.Draw(icon, 16f);
            },
            warmUpFrames: 3);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void PerFrameState_AllocatesNothing()
    {
        Ask.Ask(new NoireConfirm(Play, Remove) { CountdownSeconds = 2 }, static _ => { });
        Pill.Show("Done");
        Drag.Cancel();

        var result = harness.Draw(
            static () =>
            {
                for (var i = 0; i < Repeats; i++)
                {
                    List.Layout(1000, 20f, 300f);
                    List.Reveal(0);
                    Hold.Update(false);
                    Drag.Update();
                    _ = Clicks.TakeSingle();
                    _ = Pill.Progress;
                    _ = Ask.State;
                    _ = Picker.Visible;
                    _ = NoireDismiss.Outside(Vector2.Zero, new Vector2(10f, 10f));
                    _ = NoirePlacement.Above(new Vector2(100f, 100f), new Vector2(200f, 120f), new Vector2(50f, 20f), 8f);
                }
            },
            warmUpFrames: 3);

        result.AllocatedBytes.Should().Be(0L);
    }
}
