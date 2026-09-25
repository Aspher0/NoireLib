using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Holds the settings surfaces at zero allocation once warm: a settings window on each page and while searching, every
/// row the stock settings skin draws with the skin picker, a form, the colour editor and the translation editor.
/// </summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireSettingsAllocationTests : IClassFixture<UiHarness>, IDisposable
{
    private const int Warm = 3;

    private static readonly NoireString Title = new("test.settings.alloc.title", "Settings");
    private static readonly NoireString General = new("test.settings.alloc.general", "General");
    private static readonly NoireString Limits = new("test.settings.alloc.limits", "Limits");
    private static readonly NoireString Section = new("test.settings.alloc.section", "Section");
    private static readonly NoireString Name = new("test.settings.alloc.name", "Enabled");
    private static readonly NoireString Help = new("test.settings.alloc.help", "Help text.");
    private static readonly NoireString Reason = new("test.settings.alloc.reason", "Disabled for now.");
    private static readonly NoireString Alarm = new("test.settings.alloc.alarm", "Nothing kept.");
    private static readonly NoireString Notice = new("test.settings.alloc.notice", "A notice.");
    private static readonly NoireString Hold = new("test.settings.alloc.hold", "Hold");
    private static readonly NoireString Confirm = new("test.settings.alloc.confirm", "Sure?");

    private static readonly NoireConfirm AskFirst = new(Confirm, Confirm);
    private static readonly NoireHold ResetHold = new();

    private static bool enabled;
    private static bool other = true;
    private static AllocMode mode = AllocMode.Second;
    private static int count = 3;
    private static TimeSpan delay = TimeSpan.FromSeconds(30);

    private static readonly NoireSetting<bool> Enabled = new("Enabled", false, NoireRules<bool>.None, static () => enabled, static v => enabled = v);
    private static readonly NoireSetting<bool> Other = new("Other", false, NoireRules<bool>.None, static () => other, static v => other = v);
    private static readonly NoireSetting<AllocMode> Mode = new("Mode", AllocMode.First, NoireRules<AllocMode>.None, static () => mode, static v => mode = v);
    private static readonly NoireSetting<int> Count = new("Count", 3, NoireRules<int>.None, static () => count, static v => count = v);
    private static readonly NoireSetting<TimeSpan> Delay = new("Delay", TimeSpan.FromSeconds(30), NoireRules<TimeSpan>.None, static () => delay, static v => delay = v);
    private static readonly INoireSetting[] Shared = [Enabled, Other, Mode, Count, Delay];

    private static readonly SettingRowInfo[] Rows =
    [
        new("toggle", "Toggle", "Help."),
        new("choice", "Choice", null, Modified: true),
        new("number", "Number", null, Disabled: true, DisabledReason: "Off."),
        new("duration", "Duration", null, Alarm: "Careful."),
        new("custom", "Custom"),
    ];

    private static readonly string[] Options = ["One", "Two", "Three"];
    private static readonly TabItem[] Pages = [new("General", NoireIcon.Settings), new("Limits")];

    private static readonly SettingsWindow Window = new();
    private static readonly NoireThemeEditor ThemeEditor = new();
    private static readonly NoireTranslationEditor TranslationEditor = new();

    private static bool toggle;
    private static int choice;
    private static int number = 4;
    private static TimeSpan duration = TimeSpan.FromMinutes(1);
    private static bool formToggle;

    private readonly UiHarness harness;
    private NoireLocalizer? localizer;

    public NoireSettingsAllocationTests(UiHarness harness)
    {
        this.harness = harness;
        NoireSkins.Reset();
        NoireSkins.Register(StockSkin.Instance, new SecondSkin());
    }

    private enum AllocMode
    {
        First,
        Second,
    }

    public void Dispose()
    {
        localizer?.Dispose();
        ResetLocalizerConfiguration();
        NoireSkins.Reset();
    }

    [Fact]
    public void SettingsWindow_OnEveryPage_AllocatesNothing()
    {
        UseLocalizer();

        foreach (var page in new[] { "general", "limits" })
        {
            Window.OpenPage(page);
            var result = harness.Draw(static () => DrawFrame(Window), warmUpFrames: Warm);

            result.TotalVtxCount.Should().BeGreaterThan(0);
            result.AllocatedBytes.Should().Be(0L, $"page {page} is drawn every frame");
        }
    }

    [Fact]
    public void SettingsWindow_WhileSearching_AllocatesNothing()
    {
        Window.Search = "en";

        var result = harness.Draw(static () => DrawFrame(Window), warmUpFrames: Warm);

        Window.Search = string.Empty;
        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void StockSettingsRows_AndTheSkinPicker_AllocateNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var skin = StockSkin.Instance.Settings;
                skin.Tabs(Pages, 0);
                skin.Section("Section");
                skin.BeginRows(Rows);

                var area = skin.Row(Rows[0], SettingControl.Toggle, out _);
                skin.Toggle(area, "toggle", ref toggle);
                area = skin.Row(Rows[1], SettingControl.Choice, out _);
                skin.Choice(area, "choice", Options, ref choice);
                area = skin.Row(Rows[2], SettingControl.Number, out _);
                skin.Number(area, "number", ref number, 0, 10);
                area = skin.Row(Rows[3], SettingControl.Duration, out _);
                skin.Duration(area, "duration", ref duration, TimeSpan.Zero, TimeSpan.FromHours(1));
                skin.Row(Rows[4], SettingControl.Custom, out _);

                skin.EndRows();
                skin.SkinPicker(NoireSkins.All, NoireSkins.Active, out _);
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void Form_AllocatesNothing()
    {
        var result = harness.Draw(
            static () =>
            {
                var form = NoireForm.Begin(Rows.AsSpan(0, 2));
                form.Toggle("form-toggle", ref formToggle);
                form.Row(SettingControl.Custom);
                form.End();
            },
            warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void ThemeEditor_AllocatesNothing()
    {
        var accent = StockSkin.Instance.EditableColors[0];
        NoireSkins.EditColor(StockSkin.Instance, accent, new Vector4(1f, 0.5f, 0f, 1f));

        var result = harness.Draw(static () => ThemeEditor.Draw(), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
    }

    [Fact]
    public void TranslationEditor_AllocatesNothing()
    {
        var active = UseLocalizer();
        active.AddTranslation("fr", Name.Key, "Activé");
        TranslationEditor.Select("fr");

        var result = harness.Draw(static () => TranslationEditor.Draw(), warmUpFrames: Warm);

        result.TotalVtxCount.Should().BeGreaterThan(0);
        result.AllocatedBytes.Should().Be(0L);
        TranslationEditor.RowCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TranslationEditor_WithASearch_AllocatesNothing()
    {
        var active = UseLocalizer();
        active.AddTranslation("fr", Name.Key, "Activé");
        TranslationEditor.Select("fr");
        SetEditorField("search", "e");

        try
        {
            var result = harness.Draw(static () => TranslationEditor.Draw(), warmUpFrames: Warm);

            result.AllocatedBytes.Should().Be(0L);
            TranslationEditor.RowCount.Should().BeGreaterThan(0);
        }
        finally
        {
            SetEditorField("search", string.Empty);
        }
    }

    [Fact]
    public void TranslationEditor_PushedPastTheLeftOfTheScreen_StillDrawsItsRows()
    {
        var active = UseLocalizer();
        active.AddTranslation("fr", Name.Key, "Activé");
        TranslationEditor.Select("fr");

        var onScreen = harness.Draw(static () => DrawEditorAt(40f), warmUpFrames: Warm);
        var pushedLeft = harness.Draw(static () => DrawEditorAt(-120f), warmUpFrames: Warm);

        // The key column is off screen and its text is cut, but the other columns of every row in view still draw.
        TranslationEditor.RowCount.Should().BeGreaterThan(0);
        pushedLeft.TotalVtxCount.Should().BeGreaterThan(onScreen.TotalVtxCount / 2);
    }

    private static void DrawEditorAt(float left)
    {
        ImGui.SetNextWindowPos(new Vector2(left, 20f));
        ImGui.SetNextWindowSize(new Vector2(760f, 520f));

        if (ImGui.Begin("##translator_offscreen", ImGuiWindowFlags.NoSavedSettings))
            TranslationEditor.Draw();

        ImGui.End();
    }

    private static void SetEditorField(string name, object value)
    {
        typeof(NoireTranslationEditor).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(TranslationEditor, value);
        typeof(NoireTranslationEditor).GetField("builtRevision", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(TranslationEditor, -1);
    }

    private NoireLocalizer UseLocalizer()
    {
        ResetLocalizerConfiguration();
        localizer ??= new NoireLocalizer(active: true, enableLogging: false, defaultLocale: "en-US");
        return localizer;
    }

    private static void ResetLocalizerConfiguration()
    {
        NoireConfigManager.UnloadConfig<LocalizerConfigInstance>();
        var config = new LocalizerConfigInstance();
        NoireConfigManager.AddConfigToCache(typeof(LocalizerConfigInstance), config);
        config.SelectedLocale = null;
        config.DefaultLocaleSource = DefaultLocaleSource.Custom;
        config.CustomDefaultLocale = "en-US";
        config.HasCustomDefaultLocaleSelection = false;
    }

    private static void DrawFrame(NoireSkinnedWindowBase window)
    {
        window.PreDraw();
        window.Draw();
        window.PostDraw();
    }

    private static void GeneralPage(NoireSettingsPage page)
    {
        page.Section(Section);
        page.Appearance();
        page.Language();
        page.Section(Section);
        page.Toggle(Enabled, Name, Help).OnChange(static () => { }).Confirm(true, AskFirst);
        page.Toggle(Other, Name).HiddenWhen(static () => false).DisabledWhen(static () => true, Reason);
        page.Notice(NoticeTone.Info, Notice, Help);
        page.Custom(static p => p.Controls.Badge("Badge", BadgeTone.Neutral));
    }

    private static void LimitsPage(NoireSettingsPage page)
    {
        page.Section(Section);
        page.Choice(Mode, Name, Help).Confirm(AllocMode.First, AskFirst);
        page.Number(Count, Name).Alarm(static () => true, Alarm);
        page.Duration(Delay, Name, Help);
        page.CustomRow(Hold, Help, static (in SettingRowArea area) => StockSkin.Instance.Controls.HoldButton("hold", "Hold", ResetHold, 0f, area.Width));
    }

    private sealed class SettingsWindow : NoireSettingsWindow
    {
        public SettingsWindow()
            : base("test.settings.alloc", NoireSettingsAllocationTests.Title)
        {
        }

        protected override System.Collections.Generic.IReadOnlyList<INoireSetting> Shared => NoireSettingsAllocationTests.Shared;

        protected override void Pages(NoireSettingsPages pages)
        {
            pages.Add("general", General, GeneralPage, NoireIcon.Settings);
            pages.Add("limits", Limits, LimitsPage);
        }
    }

    private sealed class SecondSkin : NoireSkin
    {
        public SecondSkin()
            : base("test.settings.alloc.second", Limits)
        {
        }
    }
}
