using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.Database;
using NoireLib.ObservedStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireObservedStore module, driving the real SQLite path.
/// <br/><br/>
/// The store persists to a database under the plugin's configuration directory, which does not exist outside the
/// game, so every test points its own database at a temporary directory through
/// <see cref="NoireDatabase.SetDatabaseDirectoryOverride"/>.
/// <br/><br/>
/// There is no logged-in character in a test process, so <see cref="ObservationScope.Character"/> has nothing to key
/// on. The tests therefore work in <see cref="ObservationScope.Shared"/> or against an explicitly named character
/// through <see cref="NoireObservedStore.Of"/>, which is also how an import writes down another character's data.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireObservedStoreTests : IDisposable
{
    #region Helpers

    private readonly string tempDirectory;
    private readonly List<string> databasesToClean = new();
    private readonly List<NoireObservedStore> storesToClean = new();

    public NoireObservedStoreTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        foreach (var store in storesToClean)
        {
            try
            {
                store.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        ObservationRecord.ResetIndexCache();

        // The SQLite connections are cached per database name and hold the files open, so they are released before
        // the directory holding them is removed. Only this test's own databases are disposed: NoireDatabase.DisposeAll
        // clears the process-wide instance cache, which would tear a concurrently running test class's database out
        // from under it. Every name here is a fresh GUID, so nothing asks for one of these again.
        foreach (var databaseName in databasesToClean)
        {
            try
            {
                NoireDatabase.GetInstance(databaseName).Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }

            NoireDatabase.RemoveDatabaseDirectoryOverride(databaseName);
        }

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp directory left behind is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a store backed by a database of its own inside this test's temporary directory.
    /// </summary>
    /// <param name="configure">An optional hook to adjust the options before activation.</param>
    /// <returns>The store, already active and registered for cleanup.</returns>
    private NoireObservedStore CreateStore(Action<ObservedStoreOptions>? configure = null)
    {
        var databaseName = $"NoireLibTests_{Guid.NewGuid():N}";
        NoireDatabase.SetDatabaseDirectoryOverride(databaseName, tempDirectory);
        databasesToClean.Add(databaseName);

        var options = new ObservedStoreOptions
        {
            DatabaseName = databaseName,
            DefaultScope = ObservationScope.Shared,
            DefaultSource = "test",
        };

        configure?.Invoke(options);

        var store = new NoireObservedStore(options, active: true, enableLogging: false);
        storesToClean.Add(store);
        return store;
    }

    private sealed class Payload
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    #endregion

    #region Recording and reading

    [Fact]
    public void Record_then_read_returns_the_value()
    {
        var store = CreateStore();

        store.Shared.Record("retainer.1", new Payload { Name = "Ephemeral Bag", Count = 3 }).Should().BeTrue();

        var observation = store.Shared.Read<Payload>("retainer.1");

        observation.Should().NotBeNull();
        observation!.Value.Name.Should().Be("Ephemeral Bag");
        observation.Value.Count.Should().Be(3);
    }

    [Fact]
    public void Read_of_an_unknown_key_is_null_rather_than_empty()
    {
        var store = CreateStore();

        store.Shared.Read<Payload>("never.seen").Should().BeNull();
        store.Shared.Knows("never.seen").Should().BeFalse();
    }

    [Fact]
    public void Recording_the_same_key_replaces_the_previous_sighting()
    {
        var store = CreateStore();

        store.Shared.Record("count", 1);
        store.Shared.Record("count", 2);

        store.Shared.Read<int>("count")!.Value.Should().Be(2);
        store.Shared.Count().Should().Be(1);
    }

    [Fact]
    public void The_store_survives_being_reopened()
    {
        var databaseName = $"NoireLibTests_{Guid.NewGuid():N}";
        NoireDatabase.SetDatabaseDirectoryOverride(databaseName, tempDirectory);
        databasesToClean.Add(databaseName);

        var options = new ObservedStoreOptions { DatabaseName = databaseName, DefaultScope = ObservationScope.Shared };

        var first = new NoireObservedStore(options, active: true, enableLogging: false);
        storesToClean.Add(first);
        first.Shared.Record("persisted", "kept");
        first.Dispose();

        var second = new NoireObservedStore(options, active: true, enableLogging: false);
        storesToClean.Add(second);

        second.Shared.Read<string>("persisted")!.Value.Should().Be("kept");
    }

    [Fact]
    public void TryRead_reports_presence_and_hands_back_the_observation()
    {
        var store = CreateStore();
        store.Shared.Record("here", 42);

        store.Shared.TryRead<int>("here", out var found).Should().BeTrue();
        found.Value.Should().Be(42);

        store.Shared.TryRead<int>("absent", out _).Should().BeFalse();
    }

    [Fact]
    public void ReadValue_falls_back_when_the_key_is_unknown()
    {
        var store = CreateStore();

        store.Shared.ReadValue("missing", -1).Should().Be(-1);
    }

    [Fact]
    public void RecordMany_writes_every_pair()
    {
        var store = CreateStore();

        var written = store.Shared.RecordMany(new Dictionary<string, int>
        {
            ["item.1"] = 1,
            ["item.2"] = 2,
            ["item.3"] = 3,
        });

        written.Should().Be(3);
        store.Shared.ReadAll<int>("item.").Should().HaveCount(3);
    }

    #endregion

    #region Metadata

    [Fact]
    public void An_observation_carries_the_source_it_was_recorded_with()
    {
        var store = CreateStore();

        store.Shared.Record("k", 1, new RecordOptions { Source = "retainer-window" });

        store.Shared.Describe("k")!.Value.Source.Should().Be("retainer-window");
    }

    [Fact]
    public void An_observation_falls_back_to_the_stores_default_source()
    {
        var store = CreateStore();

        store.Shared.Record("k", 1);

        store.Shared.Describe("k")!.Value.Source.Should().Be("test");
    }

    [Fact]
    public void A_backdated_sighting_reports_the_age_of_the_sighting_not_of_the_write()
    {
        var store = CreateStore();
        var seenAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(3);

        store.Shared.Record("old", 1, new RecordOptions { ObservedAt = seenAt });

        var observation = store.Shared.Read<int>("old");

        observation!.ObservedAt.Should().BeCloseTo(seenAt, TimeSpan.FromSeconds(1));
        observation.Age.Should().BeGreaterThan(TimeSpan.FromDays(2));
    }

    [Fact]
    public void Describe_does_not_need_the_value_to_be_readable_as_anything()
    {
        var store = CreateStore();
        store.Shared.Record("k", new Payload { Name = "x" });

        var info = store.Shared.Describe("k");

        info.Should().NotBeNull();
        info!.Value.Key.Should().Be("k");
        info.Value.Scope.Should().Be(ObservationScope.Shared);
    }

    [Fact]
    public void Reading_a_value_as_the_wrong_type_answers_nothing_rather_than_throwing()
    {
        var store = CreateStore();
        store.Shared.Record("payload", new Payload { Name = "x", Count = 1 });

        var read = () => store.Shared.Read<int>("payload");

        read.Should().NotThrow();
        read().Should().BeNull();
    }

    #endregion

    #region Expiry

    [Fact]
    public void An_expired_observation_is_not_returned_by_default()
    {
        var store = CreateStore();

        store.Shared.Record("short", 1, new RecordOptions
        {
            ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
            ExpiresAfter = TimeSpan.FromHours(1),
        });

        store.Shared.Read<int>("short").Should().BeNull();
        store.Shared.Knows("short").Should().BeFalse();
    }

    [Fact]
    public void An_expired_observation_is_still_there_when_asked_for()
    {
        var store = CreateStore();

        store.Shared.Record("short", 1, new RecordOptions
        {
            ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
            ExpiresAfter = TimeSpan.FromHours(1),
        });

        var observation = store.Shared.Read<int>("short", includeExpired: true);

        observation.Should().NotBeNull();
        observation!.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void A_zero_expiry_opts_out_of_the_stores_default()
    {
        var store = CreateStore(options => options.DefaultExpiresAfter = TimeSpan.FromMinutes(1));

        store.Shared.Record("forever", 1, new RecordOptions
        {
            ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            ExpiresAfter = TimeSpan.Zero,
        });

        store.Shared.Read<int>("forever").Should().NotBeNull();
    }

    [Fact]
    public void PruneExpired_removes_expired_rows()
    {
        var store = CreateStore();

        store.Shared.Record("gone", 1, new RecordOptions
        {
            ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
            ExpiresAfter = TimeSpan.FromHours(1),
        });
        store.Shared.Record("kept", 2);

        store.PruneExpired().Should().Be(1);
        store.Shared.Count(includeExpired: true).Should().Be(1);
    }

    [Fact]
    public void ReadFresh_refuses_a_sighting_older_than_the_limit()
    {
        var store = CreateStore();

        store.Shared.Record("aged", 1, new RecordOptions { ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(5) });

        store.Shared.ReadFresh<int>("aged", TimeSpan.FromHours(1)).Should().BeNull();
        store.Shared.ReadFresh<int>("aged", TimeSpan.FromHours(10)).Should().NotBeNull();
    }

    #endregion

    #region Scopes

    [Fact]
    public void Two_characters_hold_separate_observations_under_one_key()
    {
        var store = CreateStore();

        store.Of(111).Record("gil", 100);
        store.Of(222).Record("gil", 250);

        store.Of(111).Read<int>("gil")!.Value.Should().Be(100);
        store.Of(222).Read<int>("gil")!.Value.Should().Be(250);
    }

    [Fact]
    public void A_shared_observation_is_not_a_character_one()
    {
        var store = CreateStore();

        store.Shared.Record("interior.1375", "Dark Minimalist");

        store.Of(111).Read<string>("interior.1375").Should().BeNull();
        store.Shared.Read<string>("interior.1375").Should().NotBeNull();
    }

    [Fact]
    public void A_character_scoped_call_with_nobody_logged_in_answers_nothing()
    {
        var store = CreateStore();

        store.CurrentCharacterId.Should().BeNull();
        store.Character.Record("k", 1).Should().BeFalse();
        store.Character.Read<int>("k").Should().BeNull();
    }

    [Fact]
    public void Record_options_override_the_views_own_binding()
    {
        var store = CreateStore();

        store.Shared.Record("k", 7, new RecordOptions
        {
            Scope = ObservationScope.Character,
            CharacterId = 999,
        });

        store.Shared.Read<int>("k").Should().BeNull();
        store.Of(999).Read<int>("k")!.Value.Should().Be(7);
    }

    [Fact]
    public void A_view_reports_what_it_is_bound_to()
    {
        var store = CreateStore();

        store.Shared.Scope.Should().Be(ObservationScope.Shared);
        store.Of(42).Scope.Should().Be(ObservationScope.Character);
        store.Of(42).CharacterId.Should().Be(42);
        store.Character.CharacterId.Should().BeNull();
    }

    #endregion

    #region Enumeration and removal

    [Fact]
    public void Keys_are_filtered_by_prefix_and_ordered()
    {
        var store = CreateStore();

        store.Shared.Record("a.2", 1);
        store.Shared.Record("a.1", 1);
        store.Shared.Record("b.1", 1);

        store.Shared.Keys("a.").Should().Equal("a.1", "a.2");
        store.Shared.Keys().Should().HaveCount(3);
    }

    [Fact]
    public void A_prefix_underscore_is_matched_literally_rather_than_as_a_wildcard()
    {
        var store = CreateStore();

        store.Shared.Record("a_1", 1);
        store.Shared.Record("axb", 2);

        store.Shared.Keys("a_").Should().Equal("a_1");
    }

    [Fact]
    public void Forget_removes_one_observation()
    {
        var store = CreateStore();
        store.Shared.Record("k", 1);

        store.Shared.Forget("k").Should().BeTrue();
        store.Shared.Knows("k").Should().BeFalse();
        store.Shared.Forget("k").Should().BeFalse();
    }

    [Fact]
    public void ForgetPrefix_removes_only_the_matching_keys()
    {
        var store = CreateStore();

        store.Shared.Record("bag.1", 1);
        store.Shared.Record("bag.2", 1);
        store.Shared.Record("other", 1);

        store.Shared.ForgetPrefix("bag.").Should().Be(2);
        store.Shared.Keys().Should().Equal("other");
    }

    [Fact]
    public void Prune_removes_only_what_is_older_than_the_cutoff()
    {
        var store = CreateStore();

        store.Shared.Record("old", 1, new RecordOptions { ObservedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(10) });
        store.Shared.Record("new", 1);

        store.Shared.Prune(TimeSpan.FromDays(1)).Should().Be(1);
        store.Shared.Keys().Should().Equal("new");
    }

    [Fact]
    public void Clear_empties_only_its_own_scope()
    {
        var store = CreateStore();

        store.Shared.Record("k", 1);
        store.Of(111).Record("k", 1);

        store.Shared.Clear().Should().Be(1);
        store.Shared.Count().Should().Be(0);
        store.Of(111).Count().Should().Be(1);
    }

    #endregion

    #region Events

    [Fact]
    public void Recording_raises_a_recorded_event_carrying_what_it_replaced()
    {
        var store = CreateStore();
        var seen = new List<ObservationRecordedEvent>();

        using var token = store.OnRecorded(seen.Add);

        store.Shared.Record("k", 1);
        store.Shared.Record("k", 2);

        seen.Should().HaveCount(2);
        seen[0].Replaced.Should().BeNull();
        seen[1].Replaced.Should().NotBeNull();
        seen[1].Info.Key.Should().Be("k");
    }

    [Fact]
    public void Forgetting_raises_a_forgotten_event()
    {
        var store = CreateStore();
        var seen = new List<ObservationForgottenEvent>();

        using var token = store.OnForgotten(seen.Add);

        store.Shared.Record("k", 1);
        store.Shared.Forget("k");

        seen.Should().ContainSingle();
        seen[0].Info.Key.Should().Be("k");
    }

    [Fact]
    public void Bulk_removal_raises_a_pruned_event_with_the_count()
    {
        var store = CreateStore();
        var seen = new List<ObservationsPrunedEvent>();

        using var token = store.OnPruned(seen.Add);

        store.Shared.Record("bag.1", 1);
        store.Shared.Record("bag.2", 1);
        store.Shared.ForgetPrefix("bag.");

        seen.Should().ContainSingle();
        seen[0].Count.Should().Be(2);
    }

    [Fact]
    public void Disposing_a_token_stops_delivery()
    {
        var store = CreateStore();
        var count = 0;

        var token = store.OnRecorded(_ => count++);
        store.Shared.Record("a", 1);
        token.Dispose();
        store.Shared.Record("b", 1);

        count.Should().Be(1);
    }

    [Fact]
    public void UnsubscribeOwner_removes_everything_registered_with_it()
    {
        var store = CreateStore();
        var owner = new object();
        var count = 0;

        store.OnRecorded(_ => count++, new() { Owner = owner });
        store.OnForgotten(_ => count++, new() { Owner = owner });

        store.UnsubscribeOwner(owner).Should().Be(2);
        store.SubscriptionCount.Should().Be(0);
    }

    #endregion

    #region Rules that need no database

    [Fact]
    public void Age_is_measured_from_the_sighting_and_never_goes_negative()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var info = new ObservationInfo("k", ObservationScope.Shared, 0, "s", now, null);

        info.AgeAt(now + TimeSpan.FromHours(3)).Should().Be(TimeSpan.FromHours(3));
        info.AgeAt(now - TimeSpan.FromHours(3)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void An_observation_with_no_expiry_never_expires()
    {
        var seen = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var info = new ObservationInfo("k", ObservationScope.Shared, 0, "s", seen, null);

        info.IsExpiredAt(seen + TimeSpan.FromDays(3650)).Should().BeFalse();
    }

    [Fact]
    public void Expiry_is_measured_against_the_sighting()
    {
        var seen = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var info = new ObservationInfo("k", ObservationScope.Shared, 0, "s", seen, TimeSpan.FromHours(1));

        info.IsExpiredAt(seen + TimeSpan.FromMinutes(59)).Should().BeFalse();
        info.IsExpiredAt(seen + TimeSpan.FromMinutes(61)).Should().BeTrue();
    }

    [Fact]
    public void Serializer_settings_can_never_turn_type_name_handling_on()
    {
        var requested = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            Formatting = Formatting.Indented,
        };

        var resolved = NoireObservedStore.BuildSerializerSettings(requested);

        resolved.Should().NotBeNull();
        resolved!.TypeNameHandling.Should().Be(TypeNameHandling.None);
        resolved.Formatting.Should().Be(Formatting.Indented);
    }

    [Theory]
    [InlineData(ObservationScope.Shared)]
    [InlineData(ObservationScope.Character)]
    public void Scope_round_trips_through_its_stored_text(ObservationScope scope)
        => NoireObservedStore.ParseScope(NoireObservedStore.ScopeText(scope)).Should().Be(scope);

    [Fact]
    public void A_content_id_past_the_signed_range_round_trips_as_text()
    {
        const ulong id = ulong.MaxValue - 3;

        var text = NoireObservedStore.CharacterText(id);

        ulong.Parse(text, System.Globalization.CultureInfo.InvariantCulture).Should().Be(id);
    }

    [Fact]
    public void Like_wildcards_in_a_prefix_are_escaped()
    {
        NoireObservedStore.EscapeLike("a_b%c").Should().Be("a\\_b\\%c");
        NoireObservedStore.EscapeLike("plain").Should().Be("plain");
    }

    #endregion

    #region Lifecycle

    [Fact]
    public void An_inactive_store_answers_nothing_rather_than_throwing()
    {
        var store = CreateStore();
        store.Shared.Record("k", 1);

        store.SetActive(false);

        store.Shared.Record("k2", 1).Should().BeFalse();
        store.Shared.Read<int>("k").Should().BeNull();
        store.Shared.Count().Should().Be(0);
    }

    [Fact]
    public void SetOptions_restarts_the_store_and_keeps_subscriptions()
    {
        var store = CreateStore();
        var count = 0;

        using var token = store.OnRecorded(_ => count++);

        var options = store.Options.Clone();
        options.DefaultSource = "changed";
        store.SetOptions(options);

        store.IsActive.Should().BeTrue();
        store.Shared.Record("k", 1);

        count.Should().Be(1);
        store.Shared.Describe("k")!.Value.Source.Should().Be("changed");
    }

    [Fact]
    public void Two_stores_with_different_database_names_do_not_see_each_other()
    {
        var first = CreateStore();
        var second = CreateStore();

        first.Shared.Record("k", 1);

        second.Shared.Knows("k").Should().BeFalse();
    }

    #endregion
}
