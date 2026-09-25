using Dalamud.Bindings.ImGui;
using NoireLib.Configuration;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A settings window: pages as tabs, a search across every page, a reset per modified row, and the modified settings
/// as a share code. Its frame and layout come from the active skin; its pages come from the plugin.
/// </summary>
public abstract class NoireSettingsWindow : NoireSkinnedWindowBase
{
    private readonly NoireSettingsPages pages = new();
    private readonly NoireSettingsPage page = new();
    private readonly string pageKey;
    private TabItem[] tabs = [];
    private int tabsRevision = -1;
    private int current;
    private bool declared;
    private string search = string.Empty;
    private string status = string.Empty;

    /// <summary>Creates the window.</summary>
    /// <param name="id">A stable id: the ImGui id, and the key of its layout, options and last page.</param>
    /// <param name="title">The title.</param>
    protected NoireSettingsWindow(string id, NoireString title)
        : base(id, title)
    {
        pageKey = "noireui.settings." + id + ".page";
    }

    internal string Search
    {
        get => search;
        set => search = value ?? string.Empty;
    }

    internal int CurrentPage
    {
        get
        {
            EnsurePages();
            return current;
        }
    }

    /// <summary>The settings the export and import buttons carry; empty hides them.</summary>
    protected virtual IReadOnlyList<INoireSetting> Shared => Array.Empty<INoireSetting>();

    /// <summary>Declares the pages, in order. Called once, before the first draw.</summary>
    /// <param name="pages">Where the pages are added.</param>
    protected abstract void Pages(NoireSettingsPages pages);

    /// <summary>Drawn above the search and the tabs on every page, for a banner that must never be missed.</summary>
    protected virtual void AboveTabs()
    {
    }

    /// <summary>Opens the window on a page.</summary>
    /// <param name="id">The page's id.</param>
    public void OpenPage(string id)
    {
        EnsurePages();

        var index = pages.IndexOf(id);

        if (index >= 0)
        {
            search = string.Empty;
            SetPage(index);
        }

        Open();
    }

    /// <summary>Draws the banner, the search, the tabs, the page and the export and import buttons.</summary>
    protected sealed override void DrawBody()
    {
        EnsurePages();

        var skin = NoireSkins.Active;
        var settings = skin.Settings;
        var controls = skin.Controls;
        var chrome = skin.ChromeFor(this);
        var padding = chrome.Native ? Vector2.Zero : ImGui.GetStyle().WindowPadding + new Vector2(chrome.Metrics.BodyPadding * NoireUI.Scale);
        var min = BodyMin + padding;
        var max = BodyMax - padding;
        var width = MathF.Max(1f, max.X - min.X);

        ImGui.SetCursorScreenPos(min);
        AboveTabs();

        ImGui.SetCursorScreenPos(new Vector2(min.X, ImGui.GetCursorScreenPos().Y));
        controls.Search("noire-settings-search", ref search, NoireStrings.SearchSettings.Text, width);

        if (search.Length == 0 && pages.Count > 1)
        {
            var picked = settings.Tabs(Tabs(), current);

            if (picked != current && (uint)picked < (uint)pages.Count)
                SetPage(picked);
        }

        var shared = Shared;
        var footer = shared.Count > 0 ? ImGui.GetFrameHeightWithSpacing() : 0f;
        var height = MathF.Max(1f, max.Y - ImGui.GetCursorScreenPos().Y - footer);

        if (ImGui.BeginChild("##noire-settings-page", new Vector2(width, height), false))
        {
            if (search.Length == 0)
            {
                if (pages.Count > 0)
                    DrawPage(current, heading: false);
            }
            else
            {
                var any = false;

                for (var i = 0; i < pages.Count; i++)
                    any |= DrawPage(i, heading: true);

                if (!any)
                    controls.Empty(NoireStrings.NoSettingMatches.Text);
            }
        }

        ImGui.EndChild();

        if (footer > 0f)
            DrawFooter(controls, shared);
    }

    private bool DrawPage(int index, bool heading)
    {
        var entry = pages[index];

        ImGui.PushID(index);
        page.Begin(this, search);
        entry.Draw(page);

        if (heading && page.HasAnyMatch())
            NoireSkins.Active.Settings.Section(entry.Label.Text);

        page.Flush();
        ImGui.PopID();

        return page.HasMatches;
    }

    private void DrawFooter(IControlSkin controls, IReadOnlyList<INoireSetting> shared)
    {
        if (controls.Button("noire-export", NoireStrings.ExportSettings.Text, NoireIcon.Export, ButtonTone.Ghost))
        {
            ImGui.SetClipboardText(NoireSettingsShare.Export(shared));
            status = NoireStrings.ExportedSettings.Text;
        }

        ImGui.SameLine();

        if (controls.Button("noire-import", NoireStrings.ImportSettings.Text, NoireIcon.Copy, ButtonTone.Ghost))
        {
            var result = NoireSettingsShare.Import(ImGui.GetClipboardText(), shared);
            status = result.Success
                ? NoireStrings.ImportedSettings.With("count", NoireLanguages.Number(result.Value))
                : NoireStrings.ImportFailed.Text;
        }

        if (status.Length == 0)
            return;

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, NoireTheme.Current.Resolve(ThemeColor.TextMuted));
        ImGui.TextUnformatted(status);
        ImGui.PopStyleColor();
    }

    private void EnsurePages()
    {
        if (declared)
            return;

        declared = true;
        Pages(pages);
        current = Math.Max(0, pages.IndexOf(NoireUiState.Get<string?>(pageKey, null)));
    }

    private void SetPage(int index)
    {
        current = index;
        NoireUiState.Set(pageKey, pages[index].Id);
    }

    // Tab labels follow the language, rebuilt only when it changes.
    private ReadOnlySpan<TabItem> Tabs()
    {
        if (tabsRevision == NoireLanguages.Revision && tabs.Length == pages.Count)
            return tabs;

        tabs = new TabItem[pages.Count];

        for (var i = 0; i < pages.Count; i++)
            tabs[i] = new TabItem(pages[i].Label.Text, pages[i].Icon);

        tabsRevision = NoireLanguages.Revision;
        return tabs;
    }
}
