using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks what a settings window decides before the skin draws: search, conditions, resets, confirmations and share codes.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSettingsTests : IClassFixture<UiHarness>, IDisposable
{
    private static readonly NoireString Alpha = new("test.settings.alpha", "Alpha row");
    private static readonly NoireString AlphaHelp = new("test.settings.alpha.help", "Turns the first feature on.");
    private static readonly NoireString Beta = new("test.settings.beta", "Beta row");
    private static readonly NoireString Gamma = new("test.settings.gamma", "Gamma row");
    private static readonly NoireString Section = new("test.settings.section", "A section");
    private static readonly NoireString Empty = new("test.settings.empty", "An empty section");
    private static readonly NoireString PageA = new("test.settings.pageA", "Page A");
    private static readonly NoireString PageB = new("test.settings.pageB", "Page B");
    private static readonly NoireString Reason = new("test.settings.reason", "Off for now.");
    private static readonly NoireString Warning = new("test.settings.warning", "Too low.");
    private static readonly NoireString Confirm = new("test.settings.confirm", "Sure?");
    private static readonly NoireString Yes = new("test.settings.yes", "Yes");

    private static readonly NoireConfirm AskFirst = new(Confirm, Yes);

    private static bool alpha;
    private static bool beta = true;
    private static int gamma = 5;

    private static readonly NoireSetting<bool> AlphaSetting = new("AlphaFeature", false, NoireRules<bool>.None, static () => alpha, static v => alpha = v);
    private static readonly NoireSetting<bool> BetaSetting = new("BetaFeature", true, NoireRules<bool>.None, static () => beta, static v => beta = v);
    private static readonly NoireSetting<int> GammaSetting = new("GammaCount", 5, NoireRules<int>.None, static () => gamma, static v => gamma = v);

    private readonly UiHarness harness;
    private readonly SpySkin spy = new();

    public NoireSettingsTests(UiHarness harness)
    {
        this.harness = harness;
        NoireSkins.Reset();
        NoireSkins.Use(spy);
        alpha = false;
        beta = true;
        gamma = 5;
        TestWindow.PageADraw = null;
        TestWindow.PageBDraw = null;
    }

    public void Dispose()
    {
        NoireSkins.Reset();
        TestWindow.PageADraw = null;
        TestWindow.PageBDraw = null;
    }

    [Fact]
    public void Search_KeepsRowsMatchingTheirNameHelpOrSettingName_AcrossEveryPage()
    {
        var customDrawn = 0;
        TestWindow.PageADraw = page =>
        {
            page.Section(Section);
            page.Toggle(AlphaSetting, Alpha, AlphaHelp);
            page.Toggle(BetaSetting, Beta);
            page.Custom(_ => customDrawn++);
        };
        TestWindow.PageBDraw = page => page.Number(GammaSetting, Gamma);

        using var window = new TestWindow("settings.search");

        Draw(window, "feature");
        spy.Rows.Select(r => r.Id).Should().Equal("AlphaFeature", "BetaFeature");
        spy.Sections.Should().Equal("Page A", "A section");
        customDrawn.Should().Be(0, "custom content is left out of search results");

        Draw(window, "first");
        spy.Rows.Select(r => r.Id).Should().Equal(["AlphaFeature"], "the help text matches too");

        Draw(window, "gamma");
        spy.Rows.Select(r => r.Id).Should().Equal("GammaCount");
        spy.Sections.Should().Equal("Page B");

        Draw(window, string.Empty);
        spy.Rows.Select(r => r.Id).Should().Equal("AlphaFeature", "BetaFeature");
        customDrawn.Should().Be(1);
    }

    [Fact]
    public void HiddenWhen_LeavesTheRowOut_AndASectionWithoutRowsIsNotDrawn()
    {
        var hidden = true;
        TestWindow.PageADraw = page =>
        {
            page.Section(Empty);
            page.Toggle(AlphaSetting, Alpha).HiddenWhen(() => hidden);
            page.Section(Section);
            page.Toggle(BetaSetting, Beta);
        };

        using var window = new TestWindow("settings.hidden");

        Draw(window, string.Empty);
        spy.Rows.Select(r => r.Id).Should().Equal("BetaFeature");
        spy.Sections.Should().Equal("A section");

        hidden = false;
        Draw(window, string.Empty);
        spy.Rows.Select(r => r.Id).Should().Equal("AlphaFeature", "BetaFeature");
        spy.Sections.Should().Equal("An empty section", "A section");
    }

    [Fact]
    public void Modified_IsReported_AndResetRestoresTheDefaultThenRunsOnChange()
    {
        var changed = 0;
        gamma = 9;
        TestWindow.PageADraw = page =>
        {
            page.Number(GammaSetting, Gamma).OnChange(() => changed++);
            page.Toggle(AlphaSetting, Alpha);
        };

        using var window = new TestWindow("settings.reset");

        Draw(window, string.Empty);
        spy.Rows.Select(r => (r.Id, r.Modified)).Should().Equal(("GammaCount", true), ("AlphaFeature", false));

        spy.ResetId = "GammaCount";
        Draw(window, string.Empty);

        gamma.Should().Be(5);
        changed.Should().Be(1);
    }

    [Fact]
    public void DisabledAndAlarm_AreReportedWithTheirTexts_OnlyWhileTheirConditionHolds()
    {
        var off = true;
        TestWindow.PageADraw = page =>
        {
            page.Toggle(AlphaSetting, Alpha).DisabledWhen(() => off, Reason);
            page.Number(GammaSetting, Gamma).Alarm(static () => gamma < 3, Warning);
        };

        using var window = new TestWindow("settings.flags");

        Draw(window, string.Empty);
        spy.Rows[0].Disabled.Should().BeTrue();
        spy.Rows[0].DisabledReason.Should().Be("Off for now.");
        spy.Rows[1].Alarm.Should().BeNull();

        off = false;
        gamma = 1;
        Draw(window, string.Empty);
        spy.Rows[0].Disabled.Should().BeFalse();
        spy.Rows[0].DisabledReason.Should().BeNull();
        spy.Rows[1].Alarm.Should().Be("Too low.");
    }

    [Fact]
    public void Confirm_HoldsTheValueUntilAccepted_ThenWritesItAndRunsOnChange()
    {
        var changed = 0;
        TestWindow.PageADraw = page => page.Toggle(AlphaSetting, Alpha).Confirm(true, AskFirst).OnChange(() => changed++);

        using var window = new TestWindow("settings.confirm");

        spy.ToggleTo["AlphaFeature"] = true;
        Draw(window, string.Empty);

        alpha.Should().BeFalse("the value waits for the confirmation");
        window.Ask.Open.Should().BeTrue();
        window.Ask.Current.Should().BeSameAs(AskFirst);

        window.Ask.Answer(ConfirmAnswer.Confirmed).Should().BeTrue();
        alpha.Should().BeTrue();
        changed.Should().Be(1);
    }

    [Fact]
    public void Confirm_Cancelled_KeepsTheValue_AndOtherValuesAreWrittenAtOnce()
    {
        alpha = true;
        TestWindow.PageADraw = page => page.Toggle(AlphaSetting, Alpha).Confirm(true, AskFirst);

        using var window = new TestWindow("settings.cancel");

        spy.ToggleTo["AlphaFeature"] = false;
        Draw(window, string.Empty);
        alpha.Should().BeFalse("only the value the row names asks first");
        window.Ask.Open.Should().BeFalse();

        spy.ToggleTo["AlphaFeature"] = true;
        Draw(window, string.Empty);
        window.Ask.Answer(ConfirmAnswer.Cancelled);
        alpha.Should().BeFalse();
    }

    [Fact]
    public void OpenPage_IsRemembered_ByTheNextWindowWithTheSameId()
    {
        TestWindow.PageADraw = static _ => { };
        TestWindow.PageBDraw = static _ => { };

        using (var first = new TestWindow("settings.remember"))
        {
            first.CurrentPage.Should().Be(0);
            first.OpenPage("b");
            first.CurrentPage.Should().Be(1);
        }

        using var second = new TestWindow("settings.remember");
        second.CurrentPage.Should().Be(1);
    }

    [Fact]
    public void ColorShareCode_RoundTrips_OnlyTheColorsTheSkinLetsTheUserChange()
    {
        var skin = StockSkin.Instance;
        var accent = skin.EditableColors.First(r => r.Key == nameof(ThemeColor.Accent));
        var red = new System.Numerics.Vector4(1f, 0f, 0f, 1f);

        NoireSkins.EditColor(skin, accent, red);
        var code = NoireSkins.ExportColors(skin);
        NoireSkins.ResetColors(skin);
        NoireSkins.EditedColor(skin, accent).Should().BeNull();

        var imported = NoireSkins.ImportColors(skin, code);

        imported.Success.Should().BeTrue();
        imported.Value.Should().BeGreaterThan(0);
        NoireSkins.EditedColor(skin, accent).Should().Be(red);
        NoireSkins.ImportColors(skin, "not a code").Success.Should().BeFalse();
    }

    [Fact]
    public void SettingsShareCode_CarriesTheModifiedSettings_AndImportAppliesThem()
    {
        alpha = true;
        gamma = 8;
        INoireSetting[] shared = [AlphaSetting, BetaSetting, GammaSetting];

        var code = NoireSettingsShare.Export(shared);
        alpha = false;
        gamma = 5;

        var result = NoireSettingsShare.Import(code, shared);

        result.Success.Should().BeTrue();
        result.Value.Should().Be(2, "only the modified settings travel");
        alpha.Should().BeTrue();
        gamma.Should().Be(8);
    }

    private void Draw(TestWindow window, string search)
    {
        window.Search = search;
        spy.Clear();
        harness.Draw(() =>
        {
            window.PreDraw();
            window.Draw();
            window.PostDraw();
        }, warmUpFrames: 0, profile: false);
    }

    private sealed class TestWindow : NoireSettingsWindow
    {
        public static Action<NoireSettingsPage>? PageADraw;
        public static Action<NoireSettingsPage>? PageBDraw;

        public TestWindow(string id)
            : base(id, PageA)
        {
        }

        protected override void Pages(NoireSettingsPages pages)
        {
            if (PageADraw is { } a)
                pages.Add("a", PageA, a);

            if (PageBDraw is { } b)
                pages.Add("b", PageB, b);
        }
    }

    private sealed class SpySkin : NoireSkin
    {
        private readonly SpySettings settings = new();

        public SpySkin()
            : base("test.settings.spy", PageA)
        {
        }

        public List<SettingRowInfo> Rows => settings.Rows;

        public List<string> Sections => settings.Sections;

        public Dictionary<string, bool> ToggleTo => settings.ToggleTo;

        public string? ResetId
        {
            get => settings.ResetId;
            set => settings.ResetId = value;
        }

        public override ISettingsSkin Settings => settings;

        public void Clear()
        {
            settings.Rows.Clear();
            settings.Sections.Clear();
        }
    }

    private sealed class SpySettings : ISettingsSkin
    {
        public List<SettingRowInfo> Rows { get; } = [];

        public List<string> Sections { get; } = [];

        public Dictionary<string, bool> ToggleTo { get; } = [];

        public string? ResetId { get; set; }

        public int Tabs(ReadOnlySpan<TabItem> pages, int current) => current;

        public void Section(string title) => Sections.Add(title);

        public void BeginRows(ReadOnlySpan<SettingRowInfo> rows)
        {
        }

        public SettingRowArea Row(in SettingRowInfo row, SettingControl control, out bool resetClicked)
        {
            Rows.Add(row);
            resetClicked = row.Id == ResetId;

            if (resetClicked)
                ResetId = null;

            return new SettingRowArea(default, 100f, 20f, row.Disabled);
        }

        public void EndRows()
        {
        }

        public bool Toggle(in SettingRowArea area, string id, ref bool value)
        {
            if (!ToggleTo.Remove(id, out var next) || next == value)
                return false;

            value = next;
            return true;
        }

        public bool Choice(in SettingRowArea area, string id, ReadOnlySpan<string> options, ref int index) => false;

        public bool Number(in SettingRowArea area, string id, ref int value, int min, int max) => false;

        public bool Duration(in SettingRowArea area, string id, ref TimeSpan value, TimeSpan min, TimeSpan max) => false;

        public bool SkinPicker(IReadOnlyList<NoireSkin> skins, NoireSkin active, out NoireSkin picked)
        {
            picked = active;
            return false;
        }
    }
}
