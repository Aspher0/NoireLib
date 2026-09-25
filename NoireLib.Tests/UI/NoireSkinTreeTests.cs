using FluentAssertions;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the component tree, view resolution along the fallback chain, user layouts and the skin store round trip.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSkinTreeTests : IDisposable
{
    private static readonly NoireString SkinName = new("test.skins.tree.name", "Test");

    private readonly string path = Path.Combine(Path.GetTempPath(), $"NoireSkinTreeTests_{Guid.NewGuid():N}.json");

    public NoireSkinTreeTests()
    {
        NoireUiState.FilePath = path;
        NoireUiState.Clear();
        NoireSkins.Reset();
    }

    public void Dispose()
    {
        NoireSkins.Reset();
        NoireUiState.FilePath = null;
        NoireUiState.Reload();

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    #region Tree

    [Fact]
    public void Add_DuplicateIdAmongSiblings_Throws()
    {
        var parent = new Node();
        parent.Put("a", new Node());

        var act = () => parent.Put("a", new Node());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_ChildThatAlreadyHasAParent_Throws()
    {
        var child = new Node();
        new Node().Put("a", child);

        var act = () => new Node().Put("b", child);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Add_IdHoldingASlash_Throws()
    {
        var act = () => new Node().Put("a/b", new Node());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Path_IsBuiltAtAttach_AndRebuiltWhenADetachedSubtreeJoinsAWindow()
    {
        var list = new Node();
        var star = list.Put("row", new Node()).Put("star", new Node());

        star.Path.Should().Be("row/star", "the subtree is not attached to anything yet");

        var window = new TreeWindow("main");
        window.Put("list", list);

        star.Path.Should().Be("main/list/row/star");
        ReferenceEquals(star.Path, star.Path).Should().BeTrue();
        window.Find("list/row/star").Should().BeSameAs(star);
        window.Dispose();
    }

    [Fact]
    public void Find_WalksARelativePath_AndReturnsNullForAMissingId()
    {
        var root = new Node();
        var leaf = root.Put("a", new Node()).Put("b", new Node());

        root.Find("a/b").Should().BeSameAs(leaf);
        root.Find("a/c").Should().BeNull();
        root.Find(string.Empty).Should().BeSameAs(root);
    }

    [Fact]
    public void Dispose_CascadesToEveryDescendant_AndDetachesFromTheParent()
    {
        var root = new Node();
        var middle = root.Put("m", new Node());
        var leaf = middle.Put("l", new Node());

        middle.Dispose();

        middle.IsDisposed.Should().BeTrue();
        leaf.IsDisposed.Should().BeTrue();
        root.Children.Should().BeEmpty();
        root.Find("m").Should().BeNull();
        leaf.DisposedCount.Should().Be(1);
    }

    [Fact]
    public void Remove_DetachesAndDisposesTheChild()
    {
        var root = new Node();
        var child = root.Put("a", new Node());

        root.Take(child);

        child.IsDisposed.Should().BeTrue();
        child.Parent.Should().BeNull();
        root.Children.Should().BeEmpty();
    }

    [Fact]
    public void ComponentList_KeysRowsByItem_KeepingTheirPathWhateverTheirPosition()
    {
        var window = new TreeWindow("w");
        var list = window.Put("list", new ComponentList<int, Node>(static item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), static _ => new Node()));

        list.Sync([1, 2, 3]);
        var two = list.Rows[1];
        two.Path.Should().Be("w/list/row[2]");

        list.Sync([3, 2]);

        list.Rows[1].Should().BeSameAs(two);
        two.Path.Should().Be("w/list/row[2]");
        list.Rows.Select(static row => row.Id).Should().Equal("row[3]", "row[2]");
        list.Children.Should().Equal(list.Rows);
        window.Find("list/row[1]").Should().BeNull();
        window.Find("list/row[3]").Should().BeSameAs(list.Rows[0]);
        window.Dispose();
    }

    [Fact]
    public void ComponentList_DisposesTheRowOfAnItemThatLeft()
    {
        var list = new ComponentList<string, Node>(static item => item, static _ => new Node());
        list.Sync(["a", "b"]);
        var a = list.Rows[0];

        list.Sync(["b"]);

        a.IsDisposed.Should().BeTrue();
        list.Children.Should().ContainSingle();
    }

    [Fact]
    public void ComponentList_AnItemTwice_Throws()
    {
        var list = new ComponentList<string, Node>(static item => item, static _ => new Node());

        var act = () => list.Sync(["a", "a"]);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Views

    [Fact]
    public void CreateView_FallsBackSkinBySkin_ThenToNothing()
    {
        var bottom = new ViewSkin("bottom", null);
        bottom.Give<Node, NodeView>();
        var top = new ViewSkin("top", bottom);

        top.CreateView(typeof(Node)).Should().BeOfType<NodeView>("the fallback gives a view the top skin does not");
        top.CreateView(typeof(Other)).Should().BeNull();
        StockSkin.Instance.CreateView(typeof(Node)).Should().BeNull();
    }

    [Fact]
    public void CreateView_OwnViewWinsOverTheFallbacks()
    {
        var bottom = new ViewSkin("bottom", null);
        bottom.Give<Node, NodeView>();
        var top = new ViewSkin("top", bottom);
        top.Give<Node, OtherNodeView>();

        top.CreateView(typeof(Node)).Should().BeOfType<OtherNodeView>();
    }

    [Fact]
    public void ViewFor_UsesTheComponentsDefaultView_WhenNoSkinGivesOne()
    {
        var skin = new ViewSkin("s", null);

        new WithDefault().ViewFor(skin).Should().BeOfType<DefaultView>();
        new Node().ViewFor(skin).Should().BeNull();
    }

    [Fact]
    public void ViewFor_RebuildsAndDisposesTheViewForAnotherSkin()
    {
        var first = new ViewSkin("a", null);
        first.Give<Node, NodeView>();
        var second = new ViewSkin("b", null);
        second.Give<Node, NodeView>();
        var node = new Node();

        var built = (NodeView)node.ViewFor(first)!;
        node.ViewFor(first).Should().BeSameAs(built);

        node.ViewFor(second).Should().NotBeSameAs(built);
        built.Disposed.Should().BeTrue();
    }

    [Fact]
    public void PresentationOf_FallsBackSkinBySkin()
    {
        var bottom = new ViewSkin("bottom", null);
        bottom.Presents<TreeWindow>(Presentation.Modal);
        var top = new ViewSkin("top", bottom);

        top.PresentationOf(typeof(TreeWindow)).Should().Be(Presentation.Modal);
        top.PresentationOf(typeof(OtherWindow)).Should().Be(Presentation.Window);
    }

    #endregion

    #region Layout

    [Fact]
    public void Arrange_AppliesTheUsersOrderAndHidden_KeepingFixedChildrenInPlace()
    {
        var parent = new Node();
        var a = parent.Put("a", new Node());
        var tabs = parent.Put("tabs", new Node { CanMove = false });
        var b = parent.Put("b", new Node { CanHide = true });
        var c = parent.Put("c", new Node());

        var layout = new ComponentLayout();
        layout.SetOrder(parent.Path, ["c", "a"]);

        NoireSkin.Arrange(parent, layout).Should().Equal(c, tabs, a, b);

        layout.SetHidden(parent.Path, "b", true);
        NoireSkin.Arrange(parent, layout).Should().Equal(c, tabs, a);

        layout.SetHidden(parent.Path, "c", true);
        NoireSkin.Arrange(parent, layout).Should().Equal(new[] { c, tabs, a }, "a child the user may not hide stays");

        NoireSkin.Arrange(parent, null).Should().Equal(a, tabs, b, c);
    }

    [Fact]
    public void Arrange_IsCachedUntilTheLayoutOrTheChildrenChange()
    {
        var parent = new Node();
        parent.Put("a", new Node());
        var layout = new ComponentLayout();

        var first = NoireSkin.Arrange(parent, layout);
        NoireSkin.Arrange(parent, layout).Should().BeSameAs(first);

        parent.Put("b", new Node());
        NoireSkin.Arrange(parent, layout).Should().NotBeSameAs(first).And.HaveCount(2);
    }

    #endregion

    #region Store

    [Fact]
    public void Layout_RoundTripsThroughTheStateFile()
    {
        var layout = new ComponentLayout();
        layout.SetOrder("main", ["b", "a"]);
        layout.SetHidden("main/list", "star", true);
        layout.SetButtonHidden("logs", true);

        NoireSkinsStore.SaveLayout("glass", "main", layout);
        NoireUiState.Save();
        NoireUiState.Reload();

        var loaded = NoireSkinsStore.LoadLayout("glass", "main");

        loaded.Order["main"].Should().Equal("b", "a");
        loaded.IsHidden("main/list", "star").Should().BeTrue();
        loaded.HiddenButtons.Should().Contain("logs");
        NoireSkinsStore.LoadLayout("stock", "main").IsDefault.Should().BeTrue("layouts are kept per skin");
    }

    [Fact]
    public void Layout_BackToDefault_RemovesItsEntry()
    {
        var layout = new ComponentLayout();
        layout.SetButtonHidden("logs", true);
        NoireSkinsStore.SaveLayout("glass", "main", layout);

        layout.Clear();
        NoireSkinsStore.SaveLayout("glass", "main", layout);

        NoireUiState.GetKeys().Should().NotContain(static key => key.StartsWith(NoireSkinsStore.LayoutPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void ChromeOptions_RoundTripThroughTheStateFile_AndAreSharedPerKey()
    {
        var options = NoireSkinsStore.LoadChrome("shared");
        NoireSkinsStore.LoadChrome("shared").Should().BeSameAs(options);

        options.Opacity = 0.5f;
        options.LockWidth = true;
        NoireSkinsStore.SaveChrome("shared", options);
        NoireUiState.Save();
        NoireUiState.Reload();
        NoireSkins.Reset();

        var loaded = NoireSkinsStore.LoadChrome("shared");

        loaded.Should().NotBeSameAs(options);
        loaded.Opacity.Should().Be(0.5f);
        loaded.LockWidth.Should().BeTrue();
    }

    [Fact]
    public void ActiveSkin_IsRememberedAndRestoredOnRegister()
    {
        var one = new ViewSkin("one", null);
        var two = new ViewSkin("two", null);
        NoireSkins.Register(one, two);
        NoireSkins.Active.Should().BeSameAs(one, "the first registered is the default");

        NoireSkins.Use(two);
        NoireSkins.Reset();
        NoireSkins.Register(one, two);

        NoireSkins.Active.Should().BeSameAs(two);
    }

    [Fact]
    public void EditColor_ChangesTheActiveTheme_AndResetRestoresIt()
    {
        var skin = new ViewSkin("colors", null);
        NoireSkins.Register(skin);
        var accent = new ThemeRole(nameof(ThemeColor.Accent), SkinName);
        var custom = new ThemeRole("glow", SkinName);
        var red = new Vector4(1f, 0f, 0f, 1f);

        NoireSkins.EditColor(skin, accent, red);
        NoireSkins.EditColor(skin, custom, red);

        NoireSkins.Theme.Resolve(ThemeColor.Accent).Should().Be(red);
        NoireSkins.Theme.Resolve("glow", Vector4.Zero).Should().Be(red);
        NoireSkins.EditedColor(skin, accent).Should().Be(red);

        NoireUiState.Save();
        NoireUiState.Reload();
        NoireSkins.EditedColor(skin, accent).Should().Be(red, "edits survive a reload");

        var revision = NoireSkins.ThemeRevision;
        NoireSkins.ResetColors(skin);

        NoireSkins.Theme.Colors.ContainsKey(ThemeColor.Accent).Should().BeFalse();
        NoireSkins.ThemeRevision.Should().BeGreaterThan(revision);
        NoireSkins.EditedColor(skin, accent).Should().BeNull();
    }

    [Fact]
    public void StockTheme_IsThePluginsOwnTheme_WhileNothingIsEdited()
    {
        NoireSkins.Theme.Should().BeSameAs(NoireTheme.Current);

        NoireSkins.EnterTheme();

        try
        {
            NoireTheme.Current.Should().BeSameAs(NoireSkins.Theme);
            NoireSkins.HostTheme.Should().BeSameAs(NoireSkins.Theme);
        }
        finally
        {
            NoireSkins.LeaveTheme();
        }
    }

    #endregion

    #region Windows

    [Fact]
    public void WindowNameFor_OverridesTheNameTheWindowIsSavedUnder()
    {
        var one = new ViewSkin("one", null);
        NoireSkins.Register(one);
        using var window = new LegacyIdWindow();

        window.PreDraw();
        window.WindowName.Should().Be("Old title##OldIdone");
    }

    [Fact]
    public void PlacementPerSkin_GivesTheWindowAnImGuiIdUnderEachSkin()
    {
        var one = new ViewSkin("one", null);
        var two = new ViewSkin("two", null);
        NoireSkins.Register(one, two);
        using var placed = new PlacedWindow("placed", perSkin: true);
        using var shared = new PlacedWindow("shared", perSkin: false);

        placed.PreDraw();
        shared.PreDraw();
        placed.WindowName.Should().EndWith("###placed.one");
        shared.WindowName.Should().EndWith("###shared");

        NoireSkins.Use(two);
        placed.PreDraw();
        shared.PreDraw();
        placed.WindowName.Should().EndWith("###placed.two", "each skin keeps its own position and size");
        shared.WindowName.Should().EndWith("###shared");
    }

    [Fact]
    public void SetUpNativeWindow_Overridden_LeavesTheDalamudWindowToThePlugin()
    {
        using var stock = new PlacedWindow("stock-native", perSkin: false);
        using var own = new OwnNativeWindow();

        stock.PreDraw();
        own.PreDraw();

        stock.TitleBarButtons.Should().ContainSingle("the default set-up adds the window menu's button");
        stock.BgAlpha.Should().NotBeNull();
        own.TitleBarButtons.Should().BeEmpty();
        own.BgAlpha.Should().BeNull();
        own.Flags.Should().Be(Dalamud.Bindings.ImGui.ImGuiWindowFlags.NoScrollbar);
        own.DisableFadeInFadeOut.Should().BeFalse();
    }

    [Fact]
    public void UnlessHidden_PassesOverASkinnedWindowTheActiveSkinHides()
    {
        var hiding = new ViewSkin("hiding", null);
        hiding.Presents<TreeWindow>(Presentation.Hidden);
        var showing = new ViewSkin("showing", null);
        NoireSkins.Register(showing, hiding);
        using var window = new TreeWindow("module");

        NoireSkinnedWindowBase.UnlessHidden(window).Should().BeSameAs(window);

        NoireSkins.Use(hiding);
        NoireSkinnedWindowBase.UnlessHidden(window).Should().BeNull("the module then shows its next window");
        NoireSkinnedWindowBase.UnlessHidden(null).Should().BeNull();
    }

    [Fact]
    public void SaveOptions_StoresOptionsChangedInCode()
    {
        using var window = new PlacedWindow("saved", perSkin: false);
        window.Options.StayInGpose = true;
        window.SaveOptions();
        NoireUiState.Save();
        NoireUiState.Reload();
        NoireSkins.Reset();

        NoireSkinsStore.LoadChrome("saved").StayInGpose.Should().BeTrue();
    }

    #endregion

    private sealed class Node : Component
    {
        public int DisposedCount { get; private set; }

        public T Put<T>(string id, T child) where T : Component => Add(id, child);

        public void Take(Component child) => Remove(child);

        protected override void OnDisposed() => DisposedCount++;
    }

    private sealed class Other : Component
    {
    }

    private sealed class WithDefault : Component
    {
        protected internal override NoireView? DefaultView() => new DefaultView();
    }

    private sealed class NodeView : NoireView<Node>
    {
        public bool Disposed { get; private set; }

        public override void Dispose() => Disposed = true;

        protected internal override void Draw(Node target)
        {
        }
    }

    private sealed class OtherNodeView : NoireView<Node>
    {
        protected internal override void Draw(Node target)
        {
        }
    }

    private sealed class DefaultView : NoireView<WithDefault>
    {
        protected internal override void Draw(WithDefault target)
        {
        }
    }

    private sealed class ViewSkin : NoireSkin
    {
        public ViewSkin(string id, NoireSkin? fallback)
            : base(id, SkinName, fallback)
        {
        }

        public void Give<TTarget, TView>()
            where TTarget : class
            where TView : NoireView<TTarget>, new()
            => View<TTarget, TView>();

        public void Presents<TWindow>(Presentation presentation) where TWindow : NoireSkinnedWindowBase
            => Present<TWindow>(presentation);
    }

    private class TreeWindow : NoireSkinnedWindow
    {
        public TreeWindow(string id)
            : base(id, SkinName)
        {
        }

        public T Put<T>(string id, T child) where T : Component => Add(id, child);
    }

    private sealed class PlacedWindow : NoireSkinnedWindow
    {
        public PlacedWindow(string id, bool perSkin)
            : base(id, SkinName)
        {
            PlacementPerSkin = perSkin;
        }
    }

    private sealed class LegacyIdWindow : NoireSkinnedWindow
    {
        public LegacyIdWindow()
            : base("legacy", SkinName)
        {
            PlacementPerSkin = true;
        }

        protected override string WindowNameFor(NoireSkin skin) => "Old title##OldId" + skin.Id;
    }

    private sealed class OwnNativeWindow : NoireSkinnedWindow
    {
        public OwnNativeWindow()
            : base("own-native", SkinName)
        {
        }

        protected override void SetUpNativeWindow()
        {
            Flags = Dalamud.Bindings.ImGui.ImGuiWindowFlags.NoScrollbar;
            BgAlpha = null;
            DisableFadeInFadeOut = false;
        }
    }

    private sealed class OtherWindow : TreeWindow
    {
        public OtherWindow()
            : base("other")
        {
        }
    }
}
