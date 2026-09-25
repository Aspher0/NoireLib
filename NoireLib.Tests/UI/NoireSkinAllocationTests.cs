using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FluentAssertions;
using NoireLib.Changelog;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Holds every skin draw path at zero allocation, a skinned window's whole frame included.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSkinAllocationTests : IClassFixture<UiHarness>, IDisposable
{
    private const int Warm = 3;

    private static readonly NoireString WindowTitle = new("test.skins.alloc.title", "Skinned");
    private static readonly NoireString Play = new("test.skins.alloc.play", "Play");
    private static readonly NoireString Hint = new("test.skins.alloc.hint", "Not now.");
    private static readonly NoireString Confirm = new("test.skins.alloc.confirm", "Delete");

    private static readonly UiAction<int> PlayAction = new(Play, NoireIcon.Play, static _ => { });
    private static readonly UiAction<int> BlockedAction = new(Play, NoireIcon.Block, static _ => { }) { Available = static _ => false, Hint = Hint };

    private static readonly string[] Options = ["One", "Two", "Three"];
    private static readonly TabItem[] TabItems = [new("Items", NoireIcon.Star, 12), new("Blocked", NoireIcon.Block), new("All")];
    private static readonly string[] RowIds = CreateRowIds(40);

    private static readonly IControlSkin Controls = StockSkin.Instance.Controls;
    private static readonly IOverlaySkin Overlays = StockSkin.Instance.Overlays;
    private static readonly NoireHold Hold = new();
    private static readonly NoireListState List = new();
    private static readonly NoirePill Pill = new();
    private static readonly NoireConfirm Asked = new(WindowTitle, Confirm, new ConfirmParagraph(Hint)) { CountdownSeconds = 3, Danger = true };

    private static bool check = true;
    private static bool toggle;
    private static int segment;
    private static int combo;
    private static int stepper = 3;
    private static TimeSpan duration = TimeSpan.FromSeconds(30);
    private static float slider = 0.5f;
    private static string search = string.Empty;
    private static int tab;

    private static readonly TestWindow NativeWindow = new("alloc.native");
    private static readonly TestWindow DrawnWindow = new("alloc.drawn");
    private static readonly TestWindow OptionsWindow = new("alloc.options");
    private static readonly string OptionsMenuId = OptionsWindow.Id + ".menu";
    private static readonly TestWindow CollapsedWindow = new("alloc.collapsed");
    private static readonly TestWindow OwnerWindow = new("alloc.owner");
    private static readonly ModalWindow Modal = new();
    private static readonly BareWindow Bare = new();
    private static readonly DrawnSkin Drawn = new();
    private static NoireChangelogWindow? changelog;

    private readonly UiHarness harness;

    public NoireSkinAllocationTests(UiHarness harness)
    {
        this.harness = harness;
        NoireSkins.Reset();
    }

    public void Dispose() => NoireSkins.Reset();

    [Fact]
    public void Draw_AndDrawChildren_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var skin = StockSkin.Instance;
                skin.Draw(NativeWindow.Leaf);
                skin.DrawChildren(NativeWindow, new UiRect(ImGui.GetCursorScreenPos(), new Vector2(400f, 300f)));
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
        result.HasScope("alloc.native/group/leaf").Should().BeTrue("a component's profiler scope is its path");
    }

    [Fact]
    public void StockControls_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                Controls.Button("b", "Save", NoireIcon.Check, ButtonTone.Accent);
                Controls.Button("d", "Off", null, ButtonTone.Danger, enabled: false, width: 120f);
                Controls.IconButton("i", NoireIcon.Star, "Favourite", on: true);
                Controls.HoldButton("h", "Hold", Hold);
                Controls.Checkbox("c", "Check", ref check);
                Controls.Switch("s", ref toggle, danger: true);
                Controls.Segmented("seg", Options, ref segment, 0f);
                Controls.Combo("combo", Options, ref combo, 160f);
                Controls.Stepper("step", ref stepper, 0, 10, 120f);
                Controls.Duration("dur", ref duration, TimeSpan.Zero, TimeSpan.FromMinutes(5), 120f);
                Controls.Slider("sl", ref slider, 0f, 1f, 200f);
                Controls.Search("se", ref search, "Search", 200f);
                Controls.Tabs("tabs", TabItems, ref tab);
                Controls.HelpMark("help", "Help text.");
                Controls.Section("Section");
                Controls.Notice(NoticeTone.Warning, "Careful.", "Detail.");
                Controls.Chip("chip", "Chip", true);
                Controls.Badge("NEW", BadgeTone.Accent);
                Controls.GameIcon(0, 32f);
                Controls.Empty("Nothing here.");
                Controls.Separator();

                if (Controls.BeginList("list", new Vector2(300f, 200f), List, RowIds.Length))
                {
                    for (var i = List.First; i <= List.Last && i < RowIds.Length; i++)
                        Controls.Row(RowIds[i], new RowInfo("Row", "detail", Selected: i == 2, Marked: i == 3, Note: "Added"));

                    Controls.EndList(List);
                }
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void StockOverlays_AllocateNothing()
    {
        Pill.Show("Saved!");

        var result = harness.Draw(
            static () =>
            {
                Overlays.Tooltip("A tooltip.", new Vector2(50f, 50f), new Vector2(90f, 70f));

                if (Overlays.CardBegin("card", new Vector2(50f, 50f), new Vector2(90f, 70f), 200f))
                {
                    ImGui.TextUnformatted("Card");
                    Overlays.CardEnd();
                }

                Overlays.Pill(Pill, new Vector2(0f, 0f), new Vector2(400f, 300f));
                Overlays.Confirm(new ConfirmState(Asked, 2, 0.5f), new Vector2(0f, 0f), new Vector2(400f, 300f));
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedFacade_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                NoireUI.Button(PlayAction, 1, ButtonTone.Accent);
                NoireUI.Button(BlockedAction, 1);
                NoireUI.Tip("Tip");
                NoireUI.Tip("Tip", new Vector2(0f, 0f), new Vector2(10f, 10f));
                NoireUI.Card("card", new Vector2(-50f, -50f), new Vector2(-40f, -40f), 200f, 1, static _ => { });
                NoireUI.Line(TextRole.Heading, "Heading");
                NoireUI.Paragraph(TextRole.Body, "A paragraph.", ThemeColor.TextMuted);
                NoireUI.Icon(NoireIcon.Info, 16f);
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedWindow_WithTheNativeChrome_AllocatesNothing()
    {
        NativeWindow.EditingLayout = true;

        var result = harness.Draw(static () => DrawFrame(NativeWindow), warmUpFrames: Warm);

        NativeWindow.EditingLayout = false;
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedWindow_WithADrawnChrome_AllocatesNothing()
    {
        NoireSkins.Use(Drawn);
        DrawnWindow.Ask.Ask(Asked, static _ => { });

        var result = harness.Draw(static () => DrawFrame(DrawnWindow), warmUpFrames: Warm);

        DrawnWindow.Ask.Answer(ConfirmAnswer.Cancelled);
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
        Drawn.HeaderDrawn.Should().BeTrue();
    }

    [Fact]
    public void SkinnedWindow_WithABareChrome_AllocatesNothing()
    {
        NoireSkins.Use(Drawn);

        var result = harness.Draw(static () => DrawFrame(Bare), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedWindow_WithEveryOptionAwayFromItsDefault_AllocatesNothing()
    {
        NoireSkins.Use(Drawn);
        var options = OptionsWindow.Options;
        options.Opacity = 0.5f;
        options.TextStep = 0;
        options.ClickThrough = true;
        options.AlwaysOnTop = true;
        options.ReducedMotion = true;
        options.LockWidth = true;

        var result = harness.Draw(
            static () =>
            {
                // Opened inside a frame, the way the chrome's menu button does.
                if (!NoireWindowMenu.IsOpen(OptionsMenuId))
                    NoireWindowMenu.Toggle(OptionsMenuId);

                DrawFrame(OptionsWindow);
            },
            warmUpFrames: Warm);

        NoireWindowMenu.Close(OptionsMenuId);
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedWindow_Collapsed_AllocatesNothing()
    {
        NoireSkins.Use(Drawn);
        CollapsedWindow.SetCollapsed(true);

        var result = harness.Draw(static () => DrawFrame(CollapsedWindow), warmUpFrames: Warm);

        CollapsedWindow.SetCollapsed(false);
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void SkinnedWindow_BlockedByAModal_AllocatesNothing()
    {
        NoireSkins.Use(Drawn);
        OwnerWindow.Pin(Modal);
        Modal.IsOpen = true;

        var result = harness.Draw(
            static () =>
            {
                DrawFrame(OwnerWindow);
                DrawFrame(Modal);
            },
            warmUpFrames: Warm);

        Modal.IsOpen = false;
        OwnerWindow.Blocked.Should().BeFalse();
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
        Modal.Presentation.Should().Be(Presentation.Modal);
    }

    [Fact]
    public void SkinnedChangelogWindow_AllocatesNothing()
    {
        WindowedModuleCollection.EnsureWindowSystem();
        var versions = new List<ChangelogVersion>
        {
            new()
            {
                Version = new Version(1, 1, 0, 0),
                Date = "2026-09-23",
                Title = "Second",
                Description = "What changed.",
                Entries =
                [
                    new() { Text = "New", IsHeader = true, Icon = FontAwesomeIcon.Star },
                    new() { Text = "A bullet.", HasBullet = true },
                    new() { Text = "An icon.", Icon = FontAwesomeIcon.Check },
                    new() { IsSeparator = true },
                    new() { Text = "A button.", ButtonText = "Open", ButtonAction = static _ => { } },
                ],
            },
            new() { Version = new Version(1, 0, 0, 0), Date = "2026-09-01", Title = "First", Entries = [new() { Text = "One." }] },
        };

        var manager = new NoireChangelogManager(active: false, enableLogging: false, versions: versions);
        changelog = new NoireChangelogWindow(manager);

        var result = harness.Draw(static () => DrawFrame(changelog!), warmUpFrames: Warm);

        changelog.Dispose();
        manager.Dispose();
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    private static void DrawFrame(NoireSkinnedWindowBase window)
    {
        window.PreDraw();
        window.Draw();
        window.PostDraw();
    }

    private static string[] CreateRowIds(int count)
    {
        var ids = new string[count];

        for (var i = 0; i < count; i++)
            ids[i] = "row" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return ids;
    }

    private sealed class TestWindow : NoireSkinnedWindow
    {
        public TestWindow(string id)
            : base(id, WindowTitle)
        {
            var group = Add("group", new Group { CanHide = true });
            Leaf = group.Put("leaf", new Leaf());
            Add("list", new Leaf { Grows = true });
            HeaderButton("logs", NoireIcon.Logs, WindowTitle, static () => { });
        }

        public Leaf Leaf { get; }

        public void Pin(NoireSkinnedWindowBase window)
        {
            if (!Attached.Contains(window))
                Attach(window);
        }
    }

    private sealed class ModalWindow : NoireSkinnedWindow
    {
        public ModalWindow()
            : base("alloc.modal", WindowTitle)
            => Add("leaf", new Leaf());
    }

    private sealed class BareWindow : NoireSkinnedWindow
    {
        public BareWindow()
            : base("alloc.bare", WindowTitle)
            => Add("leaf", new Leaf());
    }

    private sealed class Group : Component
    {
        public T Put<T>(string id, T child) where T : Component => Add(id, child);
    }

    private sealed class Leaf : Component
    {
        protected internal override NoireView? DefaultView() => new LeafView();
    }

    private sealed class LeafView : NoireView<Leaf>
    {
        protected internal override void Draw(Leaf target)
        {
            Controls.Button("go", "Go");
            Controls.Section("Leaf");
        }
    }

    private sealed class DrawnSkin : NoireSkin
    {
        public DrawnSkin()
            : base("alloc.drawn", WindowTitle)
            => Present<ModalWindow>(Presentation.Modal);

        public bool HeaderDrawn => ((DrawnChrome)Chrome).HeaderDrawn;

        public override IChromeSkin Chrome { get; } = new DrawnChrome();

        public override IChromeSkin ChromeFor(NoireSkinnedWindowBase window) => window is BareWindow ? BareChrome.Instance : Chrome;
    }

    private sealed class DrawnChrome : IChromeSkin
    {
        public bool HeaderDrawn { get; private set; }

        public bool Native => false;

        public ChromeMetrics Metrics => new(32f, 8f, 32f, 14f, 5f);

        public WindowMenuStyle? MenuStyle => null;

        public void PushWindowStyle(float scale) => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        public void Background(in ChromeFrame frame)
        {
            using var draw = UiDraw.Begin();
            draw.List.AddRectFilled(frame.Min, frame.Max, ImGui.GetColorU32(ImGuiCol.WindowBg), Metrics.Radius * frame.Scale);
        }

        public ChromeControl Header(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
        {
            HeaderDrawn = true;
            var height = Metrics.HeaderHeight * frame.Scale;
            var centre = new Vector2(frame.Max.X - (height * 0.5f), frame.Min.Y + (height * 0.5f));
            menuMin = centre - new Vector2(height * 1.5f, height * 0.5f);
            menuMax = menuMin + new Vector2(height, height);

            ImGui.SetCursorScreenPos(frame.Min + new Vector2(8f, 8f));
            ImGui.TextUnformatted(title.Title);

            foreach (var button in buttons)
                _ = button.Id;

            if (NoireWindowChrome.ChromeButton("menu", menuMin + new Vector2(height * 0.5f, height * 0.5f), height, ChromeGlyph.Menu))
                return ChromeControl.Menu;

            return NoireWindowChrome.CloseButton(centre, height) ? ChromeControl.Close : ChromeControl.None;
        }

        public ChromeControl Collapsed(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
            => Header(frame, title, buttons, out menuMin, out menuMax);

        public void ResizeGrip(in ChromeFrame frame)
        {
        }

        public void ClickThroughOutline(in ChromeFrame frame)
        {
        }

        public void BlockedVeil(Vector2 bodyMin, Vector2 bodyMax, float scale)
        {
            using var draw = UiDraw.Begin();
            draw.List.AddRectFilled(bodyMin, bodyMax, 0x80000000u);
        }
    }
}
