using FluentAssertions;
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
/// Game-free tests for the NoireLocalizer module.<br/>
/// They lock three invariants. First, the cached lookup order is the order the uncached computation would produce, and
/// every mutation that changes the fallback graph discards it, so a cached order can never outlive the configuration it
/// was built from. Second, a failed lookup announces itself once per key and requested locale rather than on every
/// call, while still counting every failure. Third, the default locale is resolved from the persisted source, then any
/// default locale selected in an earlier session, then the constructor argument, in that order: a selection outranks a
/// declaration, and a value nobody selected outranks nothing.<br/>
/// End-to-end resolution (exact hit, fallback hit, miss) is covered alongside them.<br/><br/>
/// The module is config-backed, and its configuration is reachable without the game: an uninitialized NoireLib makes
/// the config file path null, so loading and saving log and return false instead of throwing. The instance behind
/// <see cref="LocalizerConfig"/> is a process-wide singleton, so each test resets it before building a localizer.
/// </summary>
[SupportedOSPlatform("windows")]
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
                // Best effort cleanup.
            }
        }

        ResetPersistedConfiguration();
    }

    /// <summary>
    /// Returns the cached configuration singleton to the state of a fresh installation. A localizer applies the
    /// persisted configuration while initializing, so values a previous test persisted would otherwise decide the next
    /// test's locales.
    /// </summary>
    private static void ResetPersistedConfiguration()
    {
        var config = LocalizerConfig.Instance;

        config.SelectedLocale = null;
        config.DefaultLocaleSource = DefaultLocaleSource.Custom;
        config.CustomDefaultLocale = "en-US";
        config.HasCustomDefaultLocaleSelection = false;
    }

    /// <summary>
    /// Builds an inactive, silent localizer with the locales it was asked for. Nothing is applied after construction:
    /// with no persisted selection, the constructor arguments are what decide the locales.
    /// </summary>
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

    /// <summary>
    /// Runs an action against a known Windows UI culture, so that a test covering
    /// <see cref="DefaultLocaleSource.Windows"/> asserts the module's rule rather than the culture of whichever machine
    /// happens to run it.
    /// </summary>
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

    /// <summary>
    /// The lookup orders the module has cached, keyed by requested locale.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> LookupOrderCache(NoireLocalizer localizer)
        => (Dictionary<string, IReadOnlyList<string>>)typeof(NoireLocalizer)
            .GetField("lookupOrderCache", InstanceMembers)!
            .GetValue(localizer)!;

    /// <summary>
    /// The order the module serves, which is the cached one once a lookup has populated it.
    /// </summary>
    private static IReadOnlyList<string> ServedOrder(NoireLocalizer localizer, string locale)
        => (IReadOnlyList<string>)typeof(NoireLocalizer)
            .GetMethod("GetLookupOrderLocked", InstanceMembers)!
            .Invoke(localizer, new object[] { locale })!;

    /// <summary>
    /// The order computed from scratch, bypassing the cache entirely.
    /// </summary>
    private static IReadOnlyList<string> FreshlyComputedOrder(NoireLocalizer localizer, string locale)
        => (IReadOnlyList<string>)typeof(NoireLocalizer)
            .GetMethod("BuildLookupOrderLocked", InstanceMembers)!
            .Invoke(localizer, new object[] { locale })!;

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

    /// <summary>
    /// The rule the whole precedence rests on: with nothing selected, the locale the caller declared is the one it
    /// gets. CustomDefaultLocale is populated from the moment a configuration exists, so a module that restored it
    /// whenever it was populated could never honour this argument at all.
    /// </summary>
    [Fact]
    public void DefaultLocale_WithNoPersistedSelection_IsTheConstructorArgument()
    {
        LocalizerConfig.Instance.CustomDefaultLocale.Should().Be("en-US",
            "the untouched configuration default is what would silently win if it were treated as a selection");

        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.DefaultLocale.Should().Be("fr-FR");
    }

    /// <summary>
    /// A selection is a choice made while running and is what persistence is for, so it outranks a value declared at
    /// construction and re-read on every start.
    /// </summary>
    [Fact]
    public void DefaultLocale_WithAPersistedSelection_OverridesTheConstructorArgument()
    {
        var config = LocalizerConfig.Instance;
        config.CustomDefaultLocale = "de-DE";
        config.HasCustomDefaultLocaleSelection = true;

        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.DefaultLocale.Should().Be("de-DE");
    }

    /// <summary>
    /// The flag is the whole difference between a stored selection and a stored default, so a locale sitting in the
    /// custom slot without it must not decide anything.
    /// </summary>
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

    /// <summary>
    /// Switching the active locale is not selecting a default one. Recording it as such would store the declared
    /// default under the guise of a choice, and the next session would restore that instead of the argument.
    /// </summary>
    [Fact]
    public void SetCurrentLocale_DoesNotRecordADefaultLocaleSelection()
    {
        var localizer = MakeLocalizer(defaultLocale: "fr-FR");

        localizer.SetCurrentLocale("ja-JP");

        LocalizerConfig.Instance.SelectedLocale.Should().Be("ja-JP", "the active locale is a choice and is persisted");
        LocalizerConfig.Instance.HasCustomDefaultLocaleSelection.Should().BeFalse(
            "no default locale was selected, so the constructor argument must keep deciding it");
    }

    /// <summary>
    /// The end-to-end shape of the fix: a user switching language, then the plugin shipping a different declared
    /// default. The switch survives, the declaration is honoured, and neither is mistaken for the other.
    /// </summary>
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

    /// <summary>
    /// A detour through another source must not spend the stored selection: the custom slot still holds what was
    /// chosen, so going back to Custom restores it rather than falling through to the declaration.
    /// </summary>
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

    /// <summary>
    /// Populates the cache so that a following mutation has something stale to discard.
    /// </summary>
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

    /// <summary>
    /// A chain configured for another locale can still be reached from this one, so a fallback change anywhere
    /// invalidates every cached order rather than just the one that was edited.
    /// </summary>
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

    /// <summary>
    /// Adding or removing translations cannot change the order of locales to try, so those paths deliberately keep the
    /// cache. This pins that they stay off the invalidation list rather than being forgotten additions to it.
    /// </summary>
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

    /// <summary>
    /// Failing to resolve a key in another locale is a separate fact with its own attempted chain, so it is announced
    /// separately.
    /// </summary>
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
