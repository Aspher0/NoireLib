using Dalamud.Game.Text;
using FluentAssertions;
using NoireLib.Helpers;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the parts of <see cref="ContextMenuHelper"/> that decide what a player sees.</summary>
public sealed class ContextMenuHelperTests
{
    private const string Label = "Add to shopping list";

    #region Glyphs

    [Fact]
    public void BuildLabel_WithoutGlyphs_IsTheLabelAlone()
    {
        ContextMenuHelper.BuildLabel(Label, []).TextValue.Should().Be(Label);
    }

    [Fact]
    public void BuildLabel_WithOneGlyph_LeavesItToThePrefixSlot()
    {
        var label = ContextMenuHelper.BuildLabel(Label, [SeIconChar.BoxedLetterM]);

        label.TextValue.Should().Be(Label, "the game draws a single glyph itself from the item's prefix");
    }

    [Fact]
    public void BuildLabel_WithSeveralGlyphs_CarriesEveryGlyphAfterTheFirst()
    {
        var glyphs = new[] { SeIconChar.BoxedLetterM, SeIconChar.BoxedLetterB, SeIconChar.BoxedLetterX };

        var label = ContextMenuHelper.BuildLabel(Label, glyphs);

        label.TextValue.Should().Be(
            $"{SeIconChar.BoxedLetterB.ToIconString()} {SeIconChar.BoxedLetterX.ToIconString()} {Label}");
    }

    [Fact]
    public void GlyphPrefix_TakesTheFirstGlyphOrNothing()
    {
        ContextMenuHelper.GlyphPrefix([]).Should().BeNull();
        ContextMenuHelper.GlyphPrefix([SeIconChar.BoxedLetterM, SeIconChar.BoxedLetterB])
            .Should().Be(SeIconChar.BoxedLetterM);
    }

    #endregion

    #region Filtering

    [Fact]
    public void Matches_KeepsAnEntryOnItsOwnMenuOnly()
    {
        var entry = new ContextMenuEntry { Label = Label, Scope = ContextMenuScope.Inventory };

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory)).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Default)).Should().BeFalse();
    }

    [Fact]
    public void Matches_EverywhereKeepsBothMenus()
    {
        var entry = new ContextMenuEntry { Label = Label, Scope = ContextMenuScope.Everywhere };

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory)).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Default)).Should().BeTrue();
    }

    [Fact]
    public void Matches_FiltersByAddonName()
    {
        var entry = new ContextMenuEntry
        {
            Label = Label,
            Scope = ContextMenuScope.Inventory,
            Addons = ["Inventory", "InventoryLarge"],
        };

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory, "InventoryLarge")).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory, "ArmouryBoard")).Should().BeFalse();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory)).Should().BeFalse();
    }

    [Fact]
    public void Matches_AsksThePredicateLast()
    {
        var asked = 0;
        var entry = new ContextMenuEntry
        {
            Label = Label,
            Scope = ContextMenuScope.Inventory,
            ShowWhen = context =>
            {
                asked++;
                return context.AddonName == "Inventory";
            },
        };

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Default)).Should().BeFalse();
        asked.Should().Be(0, "a scope that does not match answers before the predicate runs");

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory, "Inventory")).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory, "ArmouryBoard")).Should().BeFalse();
        asked.Should().Be(2);
    }

    #endregion

    #region Ordering

    [Fact]
    public void Order_SortsByPriorityLowestFirst()
    {
        var first = new ContextMenuEntry { Label = "first", Priority = -10 };
        var second = new ContextMenuEntry { Label = "second", Priority = 0 };
        var third = new ContextMenuEntry { Label = "third", Priority = 50 };

        var ordered = ContextMenuHelper.Order([third, first, second]);

        ordered.Should().ContainInOrder(first, second, third);
    }

    [Fact]
    public void Order_LeavesTiesInTheOrderTheyCameIn()
    {
        var first = new ContextMenuEntry { Label = "first" };
        var second = new ContextMenuEntry { Label = "second" };

        ContextMenuHelper.Order([first, second]).Should().ContainInOrder(first, second);
        ContextMenuHelper.Order([second, first]).Should().ContainInOrder(second, first);
    }

    #endregion

    #region Registrations

    [Fact]
    public void Register_CountsTheEntryAndDisposeTakesItAway()
    {
        var before = ContextMenuHelper.RegistrationCount;

        var registration = ContextMenuHelper.Register(Label, _ => { }, ContextMenuScope.Inventory);

        registration.IsDisposed.Should().BeFalse();
        registration.Entry.Label.Should().Be(Label);
        ContextMenuHelper.RegistrationCount.Should().Be(before + 1);

        registration.Dispose();

        registration.IsDisposed.Should().BeTrue();
        ContextMenuHelper.RegistrationCount.Should().Be(before);
    }

    [Fact]
    public void Dispose_CalledTwice_RemovesTheEntryOnce()
    {
        var before = ContextMenuHelper.RegistrationCount;
        var kept = ContextMenuHelper.Register(new ContextMenuEntry { Label = "kept" });
        var removed = ContextMenuHelper.Register(new ContextMenuEntry { Label = "removed" });

        removed.Dispose();
        removed.Dispose();

        ContextMenuHelper.RegistrationCount.Should().Be(before + 1, "the second dispose must not drop the other entry");

        kept.Dispose();
        ContextMenuHelper.RegistrationCount.Should().Be(before);
    }

    [Fact]
    public void Register_WithoutAGame_NeverSubscribes()
    {
        var registration = ContextMenuHelper.Register(new ContextMenuEntry { Label = Label });

        ContextMenuHelper.IsAttached.Should().BeFalse();

        registration.Dispose();

        ContextMenuHelper.IsAttached.Should().BeFalse();
    }

    [Fact]
    public void Register_WithoutAnEntry_Throws()
    {
        var register = () => ContextMenuHelper.Register((ContextMenuEntry)null!);

        register.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Building an item

    [Fact]
    public void BuildItem_PutsTheFirstGlyphInThePrefixAndTheRestInTheLabel()
    {
        var entry = new ContextMenuEntry
        {
            Label = Label,
            Glyphs = [SeIconChar.BoxedLetterM, SeIconChar.BoxedLetterB],
            GlyphColor = 45,
            Priority = -3,
        };

        var item = ContextMenuHelper.BuildItem(entry, Opening(ContextMenuScope.Default));

        item.Prefix.Should().Be(SeIconChar.BoxedLetterM);
        item.PrefixColor.Should().Be(45);
        item.UseDefaultPrefix.Should().BeFalse();
        item.Priority.Should().Be(-3);
        item.Name.TextValue.Should().Be($"{SeIconChar.BoxedLetterB.ToIconString()} {Label}");
    }

    [Fact]
    public void BuildItem_WithoutGlyphs_AsksForDalamudsOwnPrefix()
    {
        var item = ContextMenuHelper.BuildItem(new ContextMenuEntry { Label = Label }, Opening(ContextMenuScope.Default));

        item.Prefix.Should().BeNull();
        item.UseDefaultPrefix.Should().BeTrue("the game refuses a top level entry with no prefix");
    }

    [Fact]
    public void BuildItem_AsksTheEnabledPredicateAndDrawsTheSubmenuArrow()
    {
        var entry = new ContextMenuEntry
        {
            Label = Label,
            EnabledWhen = context => context.ItemId != 0,
            Submenu = _ => [new ContextMenuEntry { Label = "a list" }],
        };

        var item = ContextMenuHelper.BuildItem(entry, Opening(ContextMenuScope.Inventory));

        item.IsEnabled.Should().BeFalse();
        item.IsSubmenu.Should().BeTrue();
    }

    [Fact]
    public void BuildItem_RunsTheConfigureHookLast()
    {
        var entry = new ContextMenuEntry
        {
            Label = Label,
            IsEnabled = true,
            OnConfigure = item => item.IsEnabled = false,
        };

        ContextMenuHelper.BuildItem(entry, Opening(ContextMenuScope.Default)).IsEnabled.Should().BeFalse();
    }

    #endregion

    #region Items

    [Fact]
    public void Matches_ItemScope_KeepsAnyMenuThatOpenedOnAnItem()
    {
        var entry = new ContextMenuEntry { Label = Label, Scope = ContextMenuScope.Item };

        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Default) with { ItemId = 4850 }).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Inventory) with { ItemId = 4850 }).Should().BeTrue();
        ContextMenuHelper.Matches(entry, Opening(ContextMenuScope.Default)).Should().BeFalse("a menu opened on a player carries no item");
    }

    [Fact]
    public void Decode_SplitsTheQualityOffsetFromTheRowId()
    {
        ContextMenuItemResolver.Decode(4850, out var normal).Should().Be(4850u);
        normal.Should().BeFalse();

        ContextMenuItemResolver.Decode(1_004_850, out var high).Should().Be(4850u);
        high.Should().BeTrue();

        ContextMenuItemResolver.Decode(504_850, out var collectable).Should().Be(4850u);
        collectable.Should().BeFalse();

        ContextMenuItemResolver.Decode(2_000_123, out _).Should().Be(0u, "event items are not rows of the Item sheet");
        ContextMenuItemResolver.Decode(0, out _).Should().Be(0u);
    }

    [Fact]
    public void Resolve_ChatLog_ReadsTheLinkedItem()
    {
        var resolved = ContextMenuItemResolver.Resolve(Reading("ChatLog", chatLink: 1_004_850, hovered: 12));

        resolved.Should().Be((4850u, true, ContextMenuItemSource.ChatLink));
    }

    [Fact]
    public void Resolve_Recipes_ReadTheirOwnAgents()
    {
        ContextMenuItemResolver.Resolve(Reading("RecipeNote", recipeNote: 5057))
            .Should().Be((5057u, false, ContextMenuItemSource.Recipe));

        ContextMenuItemResolver.Resolve(Reading("RecipeMaterialList", recipeList: 5058))
            .Should().Be((5058u, false, ContextMenuItemSource.Recipe));
    }

    [Fact]
    public void Resolve_AnyOtherAddon_ReadsTheHoveredItem()
    {
        ContextMenuItemResolver.Resolve(Reading("Shop", chatLink: 99, hovered: 1_051_249))
            .Should().Be((51249u, true, ContextMenuItemSource.Hovered), "a stale chat link belongs to the chat log only");
    }

    [Fact]
    public void Resolve_AChatLogMenuWithoutALink_FallsBackToTheHoveredItem()
    {
        ContextMenuItemResolver.Resolve(Reading("ChatLog", hovered: 4850))
            .Should().Be((4850u, false, ContextMenuItemSource.Hovered));
    }

    [Fact]
    public void Resolve_ACharacterTarget_CarriesNoItem()
    {
        ContextMenuItemResolver.Resolve(Reading("ChatLog", character: true, chatLink: 4850, hovered: 4850))
            .Should().Be((0u, false, ContextMenuItemSource.None), "a player name right-clicked after an item link is still a player");
    }

    [Fact]
    public void Resolve_NothingUnderTheMenu_CarriesNoItem()
    {
        ContextMenuItemResolver.Resolve(Reading("PartyList")).Should().Be((0u, false, ContextMenuItemSource.None));
    }

    private static ContextMenuItemReading Reading(
        string addonName,
        bool character = false,
        uint chatLink = 0,
        uint recipeNote = 0,
        uint recipeList = 0,
        ulong hovered = 0)
    {
        return new ContextMenuItemReading(addonName, character, chatLink, recipeNote, recipeList, hovered);
    }

    #endregion

    private static ContextMenuContext Opening(ContextMenuScope menu, string addonName = "")
    {
        return new ContextMenuContext { Menu = menu, AddonName = addonName };
    }
}
