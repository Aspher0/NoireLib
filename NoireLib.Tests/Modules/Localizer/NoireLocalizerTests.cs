using FluentAssertions;
using NoireLib.Configuration;
using NoireLib.EventBus;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the lookup-order cache against every fallback change, one missing-translation event per key and locale, and
/// the default-locale precedence: persisted source, then an earlier selection, then the constructor argument.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(LocalizerStateCollection.Name)]
public class NoireLocalizerTests : IDisposable
{
    #region Helpers

    private readonly List<NoireLocalizer> localizersToClean = new();

    public NoireLocalizerTests()
    {
        ResetPersistedConfiguration();
    }

    public void Dispose()
    {
        foreach (var localizer in localizersToClean)
        {
            try
            {
                localizer.Dispose();
            }
            catch
            {
            }
        }

        ResetPersistedConfiguration();
    }

    // A localizer applies the persisted configuration: a previous test's values would decide this one's locales.
    private static void ResetPersistedConfiguration()
    {
        // Without a plugin the manager declines to cache the configuration. Caching it shares one instance, as in game.
        NoireConfigManager.UnloadConfig<LocalizerConfigInstance>();
        var config = new LocalizerConfigInstance();
        NoireConfigManager.AddConfigToCache(typeof(LocalizerConfigInstance), config);

        config.SelectedLocale = null;
        config.DefaultLocaleSource = DefaultLocaleSource.Custom;
        config.CustomDefaultLocale = "en-US";
        config.HasCustomDefaultLocaleSelection = false;
    }

    private NoireLocalizer MakeLocalizer(string defaultLocale = "en-US", string? currentLocale = null)
    {
        var localizer = new NoireLocalizer(
            active: false,
            enableLogging: false,
            defaultLocale: defaultLocale,
            currentLocale: currentLocale ?? defaultLocale);

        localizersToClean.Add(localizer);
        return localizer;
    }

    // Pins the Windows UI culture: the test asserts the rule, not the machine running it.
    private static void WithWindowsUiCulture(string locale, Action action)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);

        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>The lookup orders the module has cached, keyed by requested locale.</summary>
    private static Dictionary<string, IReadOnlyList<string>> LookupOrderCache(NoireLocalizer localizer)
        => (Dictionary<string, IReadOnlyList<string>>)typeof(NoireLocalizer)
            .GetField("lookupOrderCache", InstanceMembers)!
            .GetValue(localizer)!;

    /// <summary>The order the module serves, which is the cached one once a lookup has populated it.</summary>
    private static IReadOnlyList<string> ServedOrder(NoireLocalizer localizer, string locale)
        => (IReadOnlyList<string>)typeof(NoireLocalizer)
            .GetMethod("GetLookupOrderLocked", InstanceMembers)!
            .Invoke(localizer, new object[] { locale })!;

    /// <summary>The order computed from scratch, bypassing the cache entirely.</summary>
    private static IReadOnlyList<string> FreshlyComputedOrder(NoireLocalizer localizer, string locale)
        => (IReadOnlyList<string>)typeof(NoireLocalizer)
            .GetMethod("BuildLookupOrderLocked", InstanceMembers)!
            .Invoke(localizer, new object[] { locale, true })!;

    /// <summary>
    /// Builds a localizer with a chain deep enough that a stale cache is visible: the requested locale has a parent, an
    /// explicit fallback which itself has a parent, and a default locale with a parent behind all of it.
    /// </summary>
    private NoireLocalizer MakeMultiLevelChain()
    {
        var localizer = MakeLocalizer(defaultLocale: "en-US", currentLocale: "fr-CA");
        localizer.SetFallbackLocales("fr-CA", "de-DE");
        return localizer;
    }

    #endregion

    #region Default locale precedence

    /// <summary>With nothing selected, the declared locale wins, though CustomDefaultLocale is always populated.</summary>
    [Fact]
    public void DefaultLocale_WithNoPersistedSelection_IsTheConstructorArgument()
    {
        LocalizerConfig.Instance.CustomDefaultLocale.Should().Be("en-US",
            "the untouched configuration default is what would silently win if it were treated as a selection");

        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.DefaultLocale.Should().Be("fr-FR");
    }

    /// <summary>A selection made while running outranks the declared locale.</summary>
    [Fact]
    public void DefaultLocale_WithAPersistedSelection_OverridesTheConstructorArgument()
    {
        var config = LocalizerConfig.Instance;
        config.CustomDefaultLocale = "de-DE";
        config.HasCustomDefaultLocaleSelection = true;

        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.DefaultLocale.Should().Be("de-DE");
    }

    /// <summary>A locale in the custom slot without the selection flag decides nothing.</summary>
    [Fact]
    public void DefaultLocale_WithACustomLocaleThatWasNeverSelected_IsStillTheConstructorArgument()
    {
        var config = LocalizerConfig.Instance;
        config.CustomDefaultLocale = "de-DE";
        config.HasCustomDefaultLocaleSelection = false;

        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.DefaultLocale.Should().Be("fr-FR",
            "an untouched configuration value must not masquerade as a choice the user made");
    }

    [Fact]
    public void SetDefaultLocale_RecordsTheSelection()
    {
        var localizer = MakeLocalizer(defaultLocale: "en-US");

        localizer.SetDefaultLocale("ja-JP");

        var config = LocalizerConfig.Instance;
        config.CustomDefaultLocale.Should().Be("ja-JP");
        config.HasCustomDefaultLocaleSelection.Should().BeTrue();
        config.DefaultLocaleSource.Should().Be(DefaultLocaleSource.Custom);
    }

    [Fact]
    public void UseCustomDefaultLocale_RecordsTheSelection()
    {
        var localizer = MakeLocalizer(defaultLocale: "en-US");

        localizer.UseCustomDefaultLocale("it-IT");

        LocalizerConfig.Instance.CustomDefaultLocale.Should().Be("it-IT");
        LocalizerConfig.Instance.HasCustomDefaultLocaleSelection.Should().BeTrue();
    }

    /// <summary>Switching the active locale is not selecting a default.</summary>
    [Fact]
    public void SetCurrentLocale_DoesNotRecordADefaultLocaleSelection()
    {
        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.SetCurrentLocale("ja-JP");

        LocalizerConfig.Instance.SelectedLocale.Should().Be("ja-JP", "the active locale is a choice and is persisted");
        LocalizerConfig.Instance.HasCustomDefaultLocaleSelection.Should().BeFalse(
            "no default locale was selected, so the constructor argument must keep deciding it");
    }

    /// <summary>A language switch survives, and a new declared default is still honoured.</summary>
    [Fact]
    public void DefaultLocale_AfterOnlyTheActiveLocaleWasChanged_FollowsTheNewConstructorArgument()
    {
        MakeLocalizer(defaultLocale: "en-US").SetCurrentLocale("ja-JP");

        var next = MakeLocalizer(defaultLocale: "fr-FR");

        next.DefaultLocale.Should().Be("fr-FR", "nobody selected a default locale, so the declared one applies");
        next.CurrentLocale.Should().Be("ja-JP", "the language the user picked is a selection and survives");
    }

    /// <summary>
    /// The counterpart: once a default locale has actually been selected, a later constructor argument does not take
    /// it away.
    /// </summary>
    [Fact]
    public void DefaultLocale_AfterItWasSelected_SurvivesANewConstructorArgument()
    {
        MakeLocalizer(defaultLocale: "en-US").SetDefaultLocale("ja-JP");

        var next = MakeLocalizer(defaultLocale: "fr-FR");

        next.DefaultLocale.Should().Be("ja-JP");
    }

    /// <summary>
    /// A persisted Windows or GameClient source resolves the default locale itself, and so outranks both a selection
    /// and a declaration.
    /// </summary>
    [Fact]
    public void DefaultLocale_WithAPersistedWindowsSource_OutranksBothTheSelectionAndTheArgument()
    {
        var config = LocalizerConfig.Instance;
        config.DefaultLocaleSource = DefaultLocaleSource.Windows;
        config.CustomDefaultLocale = "de-DE";
        config.HasCustomDefaultLocaleSelection = true;

        WithWindowsUiCulture("es-ES", () =>
        {
            var localizer = MakeLocalizer(defaultLocale: "fr-FR");

            localizer.DefaultLocaleSource.Should().Be(DefaultLocaleSource.Windows);
            localizer.DefaultLocale.Should().Be("es-ES");
        });
    }

    /// <summary>Leaving Custom for another source keeps the selection for the return.</summary>
    [Fact]
    public void DefaultLocale_SelectedThenLeftForAnotherSource_IsRestoredOnReturningToCustom()
    {
        WithWindowsUiCulture("es-ES", () =>
        {
            MakeLocalizer(defaultLocale: "en-US")
                .SetDefaultLocale("ja-JP")
                .UseWindowsLocaleAsDefaultLocale()
                .DefaultLocale.Should().Be("es-ES", "the Windows source is the one in effect once selected");
        });

        LocalizerConfig.Instance.CustomDefaultLocale.Should().Be("ja-JP",
            "resolving the default from Windows must not overwrite the custom locale that was selected");

        LocalizerConfig.Instance.DefaultLocaleSource = DefaultLocaleSource.Custom;
        var next = MakeLocalizer(defaultLocale: "fr-FR");

        next.DefaultLocale.Should().Be("ja-JP");
    }

    #endregion

    #region Lookup order cache correctness

    [Fact]
    public void LookupOrder_ForAMultiLevelChain_MatchesTheUncachedComputation()
    {
        var localizer = MakeMultiLevelChain();

        var fresh = FreshlyComputedOrder(localizer, "fr-CA");
        var served = ServedOrder(localizer, "fr-CA");

        served.Should().Equal(fresh, "the cache must not change what a lookup walks, only how often it is computed");
        served.Should().Equal("fr-CA", "fr", "de-DE", "de", "en-US", "en");
    }

    [Fact]
    public void LookupOrder_OnRepeatedLookups_IsServedFromTheCache()
    {
        var localizer = MakeMultiLevelChain();

        var first = ServedOrder(localizer, "fr-CA");
        var second = ServedOrder(localizer, "fr-CA");

        second.Should().BeSameAs(first, "a repeated lookup must reuse the resolved order rather than rebuild it");
        LookupOrderCache(localizer).Should().ContainKey("fr-CA");
    }

    [Fact]
    public void LookupOrder_WithParentFallbackDisabled_SkipsParentCultures()
    {
        var localizer = MakeMultiLevelChain();
        localizer.SetAllowParentCultureFallback(false);

        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "de-DE", "en-US");
    }

    [Fact]
    public void LookupOrder_WithDefaultFallbackDisabled_StopsBeforeTheDefaultLocale()
    {
        var localizer = MakeMultiLevelChain();
        localizer.SetAllowDefaultLocaleFallback(false);

        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "fr", "de-DE", "de");
    }

    #endregion

    #region Lookup order cache invalidation

    private static void PrimeCache(NoireLocalizer localizer)
    {
        ServedOrder(localizer, "fr-CA");
        LookupOrderCache(localizer).Should().NotBeEmpty("the cache must be primed for the assertion to mean anything");
    }

    [Fact]
    public void SetFallbackLocales_InvalidatesTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.SetFallbackLocales("fr-CA", "ja-JP");

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "fr", "ja-JP", "ja", "en-US", "en");
    }

    /// <summary>Any fallback change invalidates every cached order: another locale's chain can reach this one.</summary>
    [Fact]
    public void SetFallbackLocales_ForAnotherLocale_StillInvalidatesTheCachedOrder()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.SetFallbackLocales("de-DE", "ja-JP");

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "fr", "de-DE", "de", "ja-JP", "ja", "en-US", "en");
    }

    [Fact]
    public void SetDefaultLocale_InvalidatesTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.SetDefaultLocale("ja-JP");

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "fr", "de-DE", "de", "ja-JP", "ja");
    }

    [Fact]
    public void SetAllowParentCultureFallback_InvalidatesTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.SetAllowParentCultureFallback(false);

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "de-DE", "en-US");
    }

    [Fact]
    public void SetAllowDefaultLocaleFallback_InvalidatesTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.SetAllowDefaultLocaleFallback(false);

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal("fr-CA", "fr", "de-DE", "de");
    }

    [Fact]
    public void ClearAllTranslations_InvalidatesTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.ClearAllTranslations();

        LookupOrderCache(localizer).Should().BeEmpty();
        ServedOrder(localizer, "fr-CA").Should().Equal(new[] { "fr-CA", "fr", "en-US", "en" },
            "clearing drops the explicit fallback chains with everything else");
    }

    /// <summary>Adding translations keeps the cache: they never change the order of locales to try.</summary>
    [Fact]
    public void AddingTranslations_DoesNotInvalidateTheLookupOrderCache()
    {
        var localizer = MakeMultiLevelChain();
        PrimeCache(localizer);

        localizer.AddTranslation("fr-CA", "Greeting", "Bonjour");
        localizer.EnsureLocale("it-IT");
        localizer.RemoveKey("Greeting");

        LookupOrderCache(localizer).Should().NotBeEmpty(
            "an order is a list of locales to try and does not depend on what any of them contain");
    }

    /// <summary>
    /// The mutation paths must agree with the uncached computation, not merely differ from the previous cached value.
    /// </summary>
    [Fact]
    public void LookupOrder_AfterEveryMutationPath_StillMatchesTheUncachedComputation()
    {
        var localizer = MakeMultiLevelChain();

        var mutations = new List<Action>
        {
            () => localizer.SetFallbackLocales("fr-CA", "ja-JP"),
            () => localizer.SetDefaultLocale("de-DE"),
            () => localizer.SetAllowParentCultureFallback(false),
            () => localizer.SetAllowDefaultLocaleFallback(false),
            () => localizer.SetAllowParentCultureFallback(true),
            () => localizer.SetAllowDefaultLocaleFallback(true),
            () => localizer.ClearAllTranslations(),
        };

        foreach (var mutate in mutations)
        {
            PrimeCache(localizer);
            mutate();

            ServedOrder(localizer, "fr-CA").Should().Equal(FreshlyComputedOrder(localizer, "fr-CA"));
        }
    }

    #endregion

    #region Missing translation reporting

    [Fact]
    public void MissingTranslation_OnRepeatedLookupsOfTheSameKey_IsRaisedOnce()
    {
        var localizer = MakeLocalizer();
        var raised = new List<LocalizationMissingTranslationEvent>();
        localizer.MissingTranslation += raised.Add;

        for (var i = 0; i < 5; i++)
            localizer.Get("AbsentKey");

        raised.Should().ContainSingle(
            "a key missing from per-frame text would otherwise raise this on every frame");
        raised[0].Key.Should().Be("AbsentKey");
        raised[0].RequestedLocale.Should().Be("en-US");
    }

    /// <summary>
    /// Deduplicating the event must not cost the counters their accuracy: they are what a consumer reads when it wants
    /// frequency rather than the edge.
    /// </summary>
    [Fact]
    public void MissingTranslation_CountsEveryFailure_EvenWhileSilent()
    {
        var localizer = MakeLocalizer();
        var raised = 0;
        localizer.MissingTranslation += _ => raised++;

        for (var i = 0; i < 5; i++)
            localizer.Get("AbsentKey");

        raised.Should().Be(1);
        localizer.GetMissingTranslationCounts()["AbsentKey"].Should().Be(5);
        localizer.GetStatistics().MissingTranslationsByKey["AbsentKey"].Should().Be(5);
    }

    [Fact]
    public void MissingTranslation_ForADifferentKey_IsRaisedAgain()
    {
        var localizer = MakeLocalizer();
        var raised = new List<LocalizationMissingTranslationEvent>();
        localizer.MissingTranslation += raised.Add;

        localizer.Get("FirstAbsentKey");
        localizer.Get("SecondAbsentKey");
        localizer.Get("FirstAbsentKey");

        raised.Select(evt => evt.Key).Should().Equal("FirstAbsentKey", "SecondAbsentKey");
    }

    /// <summary>A miss in another locale is its own fact, announced separately.</summary>
    [Fact]
    public void MissingTranslation_ForTheSameKeyInAnotherLocale_IsRaisedAgain()
    {
        var localizer = MakeLocalizer();
        localizer.EnsureLocale("ja-JP");

        var raised = new List<LocalizationMissingTranslationEvent>();
        localizer.MissingTranslation += raised.Add;

        localizer.GetForLocale("en-US", "AbsentKey");
        localizer.GetForLocale("en-US", "AbsentKey");
        localizer.GetForLocale("ja-JP", "AbsentKey");

        raised.Select(evt => evt.RequestedLocale).Should().Equal("en-US", "ja-JP");
    }

    [Fact]
    public void MissingTranslation_IsAlsoPublishedToTheEventBus_Once()
    {
        var eventBus = new NoireEventBus(null, true, enableLogging: false);
        var localizer = MakeLocalizer();
        localizer.EventBus = eventBus;

        var published = 0;
        eventBus.Subscribe<LocalizationMissingTranslationEvent>(_ => published++);

        for (var i = 0; i < 4; i++)
            localizer.Get("AbsentKey");

        published.Should().Be(1, "the event bus carries the same edge, at the same rate, as the CLR event");
    }

    [Fact]
    public void MissingTranslation_AfterClearAllTranslations_IsRaisedAgain()
    {
        var localizer = MakeLocalizer();
        var raised = 0;
        localizer.MissingTranslation += _ => raised++;

        localizer.Get("AbsentKey");
        localizer.Get("AbsentKey");
        raised.Should().Be(1);

        localizer.ClearAllTranslations();
        localizer.Get("AbsentKey");

        raised.Should().Be(2, "clearing the store resets the missing-key ledger with it");
    }

    [Fact]
    public void MissingTranslation_CarriesTheLocalesItAttempted()
    {
        var localizer = MakeMultiLevelChain();
        LocalizationMissingTranslationEvent? received = null;
        localizer.MissingTranslation += evt => received = evt;

        localizer.Get("AbsentKey");

        received.Should().NotBeNull();
        received!.AttemptedLocales.Should().Equal("fr-CA", "fr", "de-DE", "de", "en-US", "en");
    }

    #endregion

    #region Resolution end to end

    [Fact]
    public void Get_WithAnExactHit_ReturnsTheValueOfTheRequestedLocale()
    {
        var localizer = MakeLocalizer(defaultLocale: "en-US", currentLocale: "fr-FR");
        localizer.AddTranslation("fr-FR", "Greeting", "Bonjour");
        localizer.AddTranslation("en-US", "Greeting", "Hello");

        localizer.Get("Greeting").Should().Be("Bonjour");
    }

    [Fact]
    public void Get_WithNoValueInTheRequestedLocale_FallsBackThroughTheChain()
    {
        var localizer = MakeMultiLevelChain();
        localizer.AddTranslation("de-DE", "Greeting", "Guten Tag");
        localizer.AddTranslation("en-US", "Greeting", "Hello");

        localizer.Get("Greeting").Should().Be("Guten Tag",
            "the explicit fallback comes before the default locale in the chain");
    }

    [Fact]
    public void Get_WithNoValueAnywhere_FallsBackToTheDefaultLocale()
    {
        var localizer = MakeMultiLevelChain();
        localizer.AddTranslation("en-US", "Greeting", "Hello");

        localizer.Get("Greeting").Should().Be("Hello");
    }

    [Fact]
    public void Get_WithAParentCultureValue_ResolvesThroughTheParent()
    {
        var localizer = MakeLocalizer(defaultLocale: "en-US", currentLocale: "fr-CA");
        localizer.AddTranslation("fr", "Greeting", "Bonjour");

        localizer.Get("Greeting").Should().Be("Bonjour");
    }

    [Fact]
    public void Get_WithAMiss_ReturnsTheFormattedPlaceholder()
    {
        var localizer = MakeLocalizer();

        localizer.Get("AbsentKey").Should().Be("[Missing: AbsentKey]");
    }

    [Fact]
    public void Get_WithAMissAndReturnKeyWhenMissing_ReturnsTheKey()
    {
        var localizer = MakeLocalizer();
        localizer.SetReturnKeyWhenMissing(true);

        localizer.Get("AbsentKey").Should().Be("AbsentKey");
    }

    [Fact]
    public void Get_WithFormatArguments_SubstitutesThem()
    {
        var localizer = MakeLocalizer();
        localizer.AddTranslation("en-US", "Greeting", "Hello {0}, you have {1} messages");

        localizer.Get("Greeting", "Noire", 3).Should().Be("Hello Noire, you have 3 messages");
    }

    [Fact]
    public void TryGet_WithAMiss_ReturnsFalseWithoutRaisingTheMissingEvent()
    {
        var localizer = MakeLocalizer();
        var raised = 0;
        localizer.MissingTranslation += _ => raised++;

        var found = localizer.TryGet("AbsentKey", out var value);

        found.Should().BeFalse();
        value.Should().BeEmpty();
        raised.Should().Be(0, "TryGet is documented as free of the missing-translation side effects");
    }

    #endregion
}
