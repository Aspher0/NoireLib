using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Core.Modules;
using NoireLib.TweakManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireTweakManager module, locking the invariant that only a user's own decision to
/// enable or disable a tweak is ever written to the configuration.<br/>
/// Tearing the module down disables every tweak that is running, and that transient state must never be
/// persisted, because the next activation reads the same entries back and would restore an all-off set.<br/>
/// They also lock the two halves of the module's persistence rule: a write the module makes on its own follows
/// <see cref="NoireTweakManager.AutomaticPersistence"/>, a write the consumer asks for by name is carried out
/// whatever that setting says, and either way an operation covering several tweaks costs exactly one write.<br/>
/// The configuration these tests drive is a real <see cref="TweakManagerConfigInstance"/> pointed at a file in a
/// temporary directory, seeded into the configuration cache the module reads from, so the assertions are made
/// against the bytes on disk rather than an approximation of them.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WindowedModuleCollection.Name)]
public class NoireTweakManagerTests : IDisposable
{
    #region Fixtures

    private readonly string tempDirectory;
    private readonly string configPath;
    private readonly TempFileTweakManagerConfig configStore;
    private readonly List<NoireTweakManager> managersToClean = new();

    public NoireTweakManagerTests()
    {
        WindowedModuleCollection.EnsureWindowSystem();

        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibTweakManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        configPath = Path.Combine(tempDirectory, "TweakManagerConfig.json");

        // The module reaches its configuration through NoireConfigManager, which caches one instance per type for
        // the whole process. Replacing that entry with an instance pointing at a temporary file is what lets the
        // real save path run without a plugin configuration directory.
        NoireConfigManager.UnloadConfig<TweakManagerConfigInstance>();
        configStore = new TempFileTweakManagerConfig { FilePathOverride = configPath };
        NoireConfigManager.AddConfigToCache(typeof(TweakManagerConfigInstance), configStore);
    }

    public void Dispose()
    {
        foreach (var manager in managersToClean)
        {
            try
            {
                manager.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        NoireConfigManager.UnloadConfig<TweakManagerConfigInstance>();

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
    }

    /// <summary>
    /// A configuration that resolves to a caller-provided path, so the real load and save paths run without a
    /// running game to supply a plugin configuration directory.<br/>
    /// It also counts the writes it performs, which is what lets an operation covering several tweaks be held to
    /// the single write it promises rather than merely to the right end state.
    /// </summary>
    private sealed class TempFileTweakManagerConfig : TweakManagerConfigInstance
    {
        internal string? FilePathOverride { get; set; }

        /// <summary>
        /// How many times this configuration has been written since the last <see cref="ResetSaveCount"/>.
        /// </summary>
        internal int SaveCount { get; private set; }

        /// <summary>
        /// Forgets the writes counted so far, so that a test counts only the ones its own operation produces
        /// rather than the ones setting the test up produced.
        /// </summary>
        internal void ResetSaveCount() => SaveCount = 0;

        protected override string? GetConfigFilePath() => FilePathOverride;

        /// <inheritdoc/>
        public override bool Save()
        {
            SaveCount++;
            return base.Save();
        }
    }

    /// <summary>
    /// A tweak that records how often it was hooked and unhooked, standing in for one that touches the game.
    /// </summary>
    private sealed class SpyTweak : TweakBase
    {
        private readonly string key;

        public SpyTweak(string key = "spy") => this.key = key;

        public override string InternalKey => key;

        public override string Name => $"Spy {key}";

        public override string Description => "A tweak used to observe lifecycle calls.";

        public int EnableCount { get; private set; }

        public int DisableCount { get; private set; }

        protected override void OnEnable() => EnableCount++;

        protected override void OnDisable() => DisableCount++;
    }

    /// <summary>
    /// A tweak that has been renamed, standing in for one whose <see cref="TweakBase.InternalKey"/> changed between
    /// releases and declared the previous key so the data the user built up under it follows.
    /// </summary>
    [TweakKeyMigration(RenamedTweak.OldKey)]
    private sealed class RenamedTweak : TweakBase
    {
        internal const string OldKey = "NoireLibTests_Tweak_OldName";
        internal const string NewKey = "NoireLibTests_Tweak_NewName";

        public override string InternalKey => NewKey;

        public override string Name => "Renamed tweak";

        public override string Description => "A tweak used to observe what a key migration moves.";

        protected override void OnEnable() { }

        protected override void OnDisable() { }
    }

    /// <summary>
    /// The settings a <see cref="ConfiguredTweak"/> reports as changed through <see cref="TweakBase.MarkConfigDirty"/>.
    /// </summary>
    private sealed class ConfiguredTweakSettings : TweakConfigBase
    {
        public override int Version { get; set; } = 1;

        public int Threshold { get; set; }
    }

    /// <summary>
    /// A tweak with a typed configuration, standing in for one whose settings the user changes from its own UI.
    /// </summary>
    private sealed class ConfiguredTweak : TweakBase<ConfiguredTweakSettings>
    {
        internal const string Key = "NoireLibTests_Tweak_Configured";

        public override string InternalKey => Key;

        public override string Name => "Configured tweak";

        public override string Description => "A tweak used to observe what recording a config change writes.";

        protected override void OnEnable() { }

        protected override void OnDisable() { }
    }

    private NoireTweakManager MakeManager(params TweakBase[] tweaks)
        => MakeManager(automaticPersistence: true, tweaks);

    /// <summary>
    /// Builds a manager for a consumer who took persistence over, which is the setting that decides whether the
    /// module writes anything on its own.
    /// </summary>
    private NoireTweakManager MakeManagerWithoutPersistence(params TweakBase[] tweaks)
        => MakeManager(automaticPersistence: false, tweaks);

    private NoireTweakManager MakeManager(bool automaticPersistence, params TweakBase[] tweaks)
    {
        var manager = new NoireTweakManager(
            active: false,
            enableLogging: false,
            automaticPersistence: automaticPersistence,
            additionalTweaks: tweaks.ToList());

        managersToClean.Add(manager);
        return manager;
    }

    /// <summary>
    /// Reads the enabled flag a tweak key carries in the configuration file on disk.
    /// </summary>
    private bool? EnabledOnDisk(string internalKey)
    {
        if (!File.Exists(configPath))
            return null;

        var entry = JObject.Parse(File.ReadAllText(configPath))["TweakConfigs"]?[internalKey];
        return entry?["enabled"]?.Value<bool>();
    }

    /// <summary>
    /// Reads whether a tweak key is favorited in the configuration file on disk.
    /// </summary>
    private bool FavoritedOnDisk(string internalKey)
    {
        if (!File.Exists(configPath))
            return false;

        var favorites = JObject.Parse(File.ReadAllText(configPath))["FavoriteTweaks"];
        return favorites?.Any(favorite => favorite.Value<string>() == internalKey) == true;
    }

    /// <summary>
    /// Reads the serialized tweak configuration a tweak key carries in the configuration file on disk.
    /// </summary>
    private string? ConfigJsonOnDisk(string internalKey)
    {
        if (!File.Exists(configPath))
            return null;

        var entry = JObject.Parse(File.ReadAllText(configPath))["TweakConfigs"]?[internalKey];
        return entry?["configJson"]?.Value<string>();
    }

    #endregion

    #region Teardown must not erase the user's selection

    [Fact]
    public void DeactivateThenActivate_PreservesTheEnabledTweakOnDisk()
    {
        // Teardown disables every running tweak; persisting that transient state would replace the user's
        // enabled set with an all-off one, which the next activation would then faithfully restore.
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey).Should().BeTrue();
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue("a user enabling a tweak is recorded");

        manager.SetActive(true);
        manager.SetActive(false);

        tweak.Enabled.Should().BeFalse("teardown still unhooks the tweak");
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue("module teardown is not the user turning the tweak off");
        configStore.GetTweakConfig(tweak.InternalKey)!.Enabled.Should().BeTrue();

        manager.SetActive(true);

        tweak.Enabled.Should().BeTrue("activating again restores the set the user chose");
        tweak.EnableCount.Should().Be(2, "the tweak was hooked once by the user and once by the restore");
    }

    [Fact]
    public void Deactivate_WithSeveralTweaks_PreservesEachOne()
    {
        var enabledA = new SpyTweak("a");
        var enabledB = new SpyTweak("b");
        var untouched = new SpyTweak("c");
        var manager = MakeManager(enabledA, enabledB, untouched);

        manager.EnableTweaks("a", "b");
        manager.SetActive(true);
        manager.SetActive(false);

        EnabledOnDisk("a").Should().BeTrue();
        EnabledOnDisk("b").Should().BeTrue();
        EnabledOnDisk("c").Should().NotBe(true, "a tweak the user never enabled stays off");

        manager.SetActive(true);

        enabledA.Enabled.Should().BeTrue();
        enabledB.Enabled.Should().BeTrue();
        untouched.Enabled.Should().BeFalse();
    }

    [Fact]
    public void UnregisterTweak_DoesNotRecordTheTweakAsDisabled()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey);

        manager.UnregisterTweak(tweak.InternalKey).Should().BeTrue();

        tweak.DisableCount.Should().Be(1, "the tweak is still unhooked");
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue("removing a tweak from the manager is not a decision to turn it off");
    }

    [Fact]
    public void ClearTweaks_DoesNotRecordTheTweaksAsDisabled()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey);

        manager.ClearTweaks();

        tweak.DisableCount.Should().Be(1);
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue("clearing the manager is not a decision to turn anything off");
    }

    [Fact]
    public void Dispose_DoesNotRecordTheTweakAsDisabled()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey);
        manager.Dispose();

        tweak.DisableCount.Should().Be(1, "disposal unhooks the tweak through the tweak's own disposal");
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue();
    }

    #endregion

    #region A user's own decision is still recorded

    [Fact]
    public void DisableTweak_ByTheUser_IsRecorded()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey);
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue();

        manager.DisableTweak(tweak.InternalKey).Should().BeTrue();

        EnabledOnDisk(tweak.InternalKey).Should().BeFalse("an explicit disable is exactly what must persist");
        configStore.GetTweakConfig(tweak.InternalKey)!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void ToggleTweak_RecordsBothDirections()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.ToggleTweak(tweak.InternalKey).Should().BeTrue();
        EnabledOnDisk(tweak.InternalKey).Should().BeTrue();

        manager.ToggleTweak(tweak.InternalKey).Should().BeTrue();
        EnabledOnDisk(tweak.InternalKey).Should().BeFalse();
    }

    [Fact]
    public void DisableAllTweaks_ByTheUser_IsRecorded()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManager(a, b);

        manager.EnableAllTweaks();
        EnabledOnDisk("a").Should().BeTrue();
        EnabledOnDisk("b").Should().BeTrue();

        manager.DisableAllTweaks();

        EnabledOnDisk("a").Should().BeFalse();
        EnabledOnDisk("b").Should().BeFalse();
    }

    [Fact]
    public void EnabledSet_SurvivesAFreshManagerReadingTheSameConfiguration()
    {
        // What the user actually experiences across a restart: the selection is reloaded from the file.
        var first = new SpyTweak();
        var manager = MakeManager(first);
        manager.EnableTweak(first.InternalKey);
        manager.SetActive(true);
        manager.SetActive(false);
        manager.Dispose();

        var second = new SpyTweak();
        var reloaded = MakeManager(second);
        reloaded.SetActive(true);

        second.Enabled.Should().BeTrue("a new manager restores the enabled set the previous one left behind");
    }

    #endregion

    #region Automatic persistence

    [Fact]
    public void ParameterlessConstructor_LeavesAutomaticPersistenceOn()
    {
        // The backing field has no initializer of its own, so this constructor must default automatic persistence
        // on explicitly, or a manager built through it would silently never save anything.
        var manager = new NoireTweakManager();
        managersToClean.Add(manager);

        manager.AutomaticPersistence.Should().BeTrue();
    }

    [Fact]
    public void ConstructorUsedByAddModule_LeavesAutomaticPersistenceOn()
    {
        // This is the overload NoireLibMain.AddModule reaches for. It passes no arguments through to
        // InitializeModule, so it must default automatic persistence on itself.
        var manager = new NoireTweakManager((ModuleId?)null, active: false, enableLogging: false);
        managersToClean.Add(manager);

        manager.AutomaticPersistence.Should().BeTrue();
    }

    [Fact]
    public void AutomaticPersistenceOff_KeepsTheConfigurationInMemoryOnly()
    {
        var tweak = new SpyTweak();
        var manager = new NoireTweakManager(
            active: false,
            enableLogging: false,
            automaticPersistence: false,
            additionalTweaks: new List<TweakBase> { tweak });
        managersToClean.Add(manager);

        manager.EnableTweak(tweak.InternalKey);

        File.Exists(configPath).Should().BeFalse("nothing is written while automatic persistence is off");
        manager.GetAllTweakConfigs()[tweak.InternalKey].Enabled.Should().BeTrue("the state is still available for manual persistence");
    }

    [Fact]
    public void SetAutomaticPersistence_TogglesTheFlag()
    {
        var manager = MakeManager();

        manager.SetAutomaticPersistence(false).AutomaticPersistence.Should().BeFalse();
        manager.SetAutomaticPersistence(true).AutomaticPersistence.Should().BeTrue();
    }

    #endregion

    #region The module's own writes are gated, the ones the consumer asks for are not

    // The module writes for two different reasons, and AutomaticPersistence separates them: a write it makes on its
    // own, recording something that happened, is gated, and a write the consumer asked for by name through a save
    // method is carried out whatever the setting says. Turning the setting off is how a consumer decides when writes
    // happen, so a save they then ask for cannot be silently discarded.

    [Fact]
    public void SetFavorite_ByTheUser_IsRecorded()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.SetFavorite(tweak.InternalKey, true).Should().BeTrue();

        FavoritedOnDisk(tweak.InternalKey).Should().BeTrue("starring a tweak is a decision the user made");

        manager.SetFavorite(tweak.InternalKey, false).Should().BeTrue();

        FavoritedOnDisk(tweak.InternalKey).Should().BeFalse("unstarring it is a decision too");
    }

    [Fact]
    public void SetFavorite_WritesNothing_WhenAutomaticPersistenceIsOff()
    {
        // Favoriting is a user action exactly like enabling, so it is recorded on the same terms as one rather than
        // reaching the file through a path of its own.
        var tweak = new SpyTweak();
        var manager = MakeManagerWithoutPersistence(tweak);

        manager.SetFavorite(tweak.InternalKey, true).Should().BeTrue();

        File.Exists(configPath).Should().BeFalse("a consumer that opted out of the module's own writes gets none");
        manager.IsFavorite(tweak.InternalKey).Should().BeTrue("the star is still applied in memory");
    }

    [Fact]
    public void MarkConfigDirty_WritesNothing_WhenAutomaticPersistenceIsOff()
    {
        // A tweak reporting that its own configuration changed is the module being told to record something, not a
        // consumer asking for a write, so the setting governs it.
        var tweak = new ConfiguredTweak();
        var manager = MakeManagerWithoutPersistence(tweak);

        tweak.Config.Threshold = 42;
        tweak.MarkConfigDirty();

        File.Exists(configPath).Should().BeFalse("recording a reported change is one of the module's own writes");
        manager.GetAllTweakConfigs()[ConfiguredTweak.Key].ConfigJson.Should().Contain("42",
            "the change is still available for manual persistence");
    }

    [Fact]
    public void MarkConfigDirty_IsRecorded_WhenAutomaticPersistenceIsOn()
    {
        var tweak = new ConfiguredTweak();
        var manager = MakeManager(tweak);

        tweak.Config.Threshold = 42;
        tweak.MarkConfigDirty();

        ConfigJsonOnDisk(ConfiguredTweak.Key).Should().Contain("42", "the tweak reported a change to record");
    }

    [Fact]
    public void Config_OnAFreshTweak_IsLinkedToItSoItCanPersistWithoutBeingDeserialized()
    {
        // A tweak with no stored entry never goes through the Config setter or deserialization, so the configuration
        // built by the field initializer is the one it keeps. That default has to be linked back to the tweak anyway,
        // or the first attempt to persist a change through it throws for lack of a parent.
        var tweak = new ConfiguredTweak();

        tweak.Config.Parent.Should().BeSameAs(tweak, "the default configuration is linked to its tweak at construction");

        var persist = () => tweak.Config.Save();
        persist.Should().NotThrow<InvalidOperationException>(
            "a default configuration must be able to persist through its parent rather than throw for lack of one");
    }

    [Fact]
    public void SaveTweakConfig_WritesEvenWhenAutomaticPersistenceIsOff()
    {
        // The decision this pins: a save the consumer asked for by name is not one of the module's own writes, so
        // opting out of those does not make it a no-op. A consumer who turns the setting off to control when writes
        // happen has to have a way to say now.
        var tweak = new SpyTweak();
        var manager = MakeManagerWithoutPersistence(tweak);

        manager.EnableTweak(tweak.InternalKey);
        File.Exists(configPath).Should().BeFalse("enabling is one of the module's own writes and stays in memory");

        manager.SaveTweakConfig(tweak.InternalKey);

        EnabledOnDisk(tweak.InternalKey).Should().BeTrue("a save requested by name is carried out whatever the setting says");
    }

    [Fact]
    public void SaveAllTweakConfigs_WritesEvenWhenAutomaticPersistenceIsOff()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManagerWithoutPersistence(a, b);

        manager.EnableTweak("a");
        File.Exists(configPath).Should().BeFalse();

        manager.SaveAllTweakConfigs();

        EnabledOnDisk("a").Should().BeTrue("a save requested by name is carried out whatever the setting says");
        EnabledOnDisk("b").Should().BeFalse("every registered tweak is written, including the ones left off");
    }

    [Fact]
    public void ImportTweakConfigs_WritesNothing_WhenAutomaticPersistenceIsOff()
    {
        // The other half of the decision: an import puts back state the consumer is already holding rather than
        // asking for a write, so it follows the setting. Writing it would push their copy into a file they do not
        // read from, which is the arrangement they opted into by taking persistence over.
        var tweak = new SpyTweak();
        var manager = MakeManagerWithoutPersistence(tweak);

        manager.ImportTweakConfigs(new Dictionary<string, TweakConfigEntry>
        {
            [tweak.InternalKey] = new(true, null, 0),
        });

        File.Exists(configPath).Should().BeFalse("an import is state coming back, not a write being requested");
        configStore.GetTweakConfig(tweak.InternalKey)!.Enabled.Should().BeTrue("the import is still applied in memory");
    }

    [Fact]
    public void ImportTweakConfigs_IsWritten_WhenAutomaticPersistenceIsOn()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.ImportTweakConfigs(new Dictionary<string, TweakConfigEntry>
        {
            [tweak.InternalKey] = new(true, null, 0),
        });

        EnabledOnDisk(tweak.InternalKey).Should().BeTrue();
    }

    #endregion

    #region One operation costs one write

    [Fact]
    public void SetFavorite_InsideABatch_CostsASingleWrite()
    {
        // Favoriting must go through the same write path as everything else; a Save() of its own would bypass
        // the batch and reintroduce the per-tweak write that batching exists to remove.
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var c = new SpyTweak("c");
        var manager = MakeManager(a, b, c);
        configStore.ResetSaveCount();

        manager.RunAsBatch(() =>
        {
            manager.SetFavorite("a", true);
            manager.SetFavorite("b", true);
            manager.SetFavorite("c", true);
        });

        configStore.SaveCount.Should().Be(1, "a batch collapses the writes it covers into one");
        FavoritedOnDisk("a").Should().BeTrue("the single write still carries every star");
        FavoritedOnDisk("b").Should().BeTrue();
        FavoritedOnDisk("c").Should().BeTrue();
    }

    [Fact]
    public void EnableAllTweaks_CostsASingleWrite()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var c = new SpyTweak("c");
        var manager = MakeManager(a, b, c);
        configStore.ResetSaveCount();

        manager.EnableAllTweaks();

        configStore.SaveCount.Should().Be(1, "a bulk operation writes once, not once per tweak");
        EnabledOnDisk("a").Should().BeTrue();
        EnabledOnDisk("b").Should().BeTrue();
        EnabledOnDisk("c").Should().BeTrue();
    }

    [Fact]
    public void SaveAllTweakConfigs_CostsASingleWrite()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var c = new SpyTweak("c");
        var manager = MakeManager(a, b, c);
        configStore.ResetSaveCount();

        manager.SaveAllTweakConfigs();

        configStore.SaveCount.Should().Be(1, "writing every tweak is one operation and costs one write");
        EnabledOnDisk("a").Should().BeFalse();
        EnabledOnDisk("b").Should().BeFalse();
        EnabledOnDisk("c").Should().BeFalse();
    }

    [Fact]
    public void ImportTweakConfigs_CostsASingleWrite()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManager(a, b);
        configStore.ResetSaveCount();

        manager.ImportTweakConfigs(new Dictionary<string, TweakConfigEntry>
        {
            ["a"] = new(true, null, 0),
            ["b"] = new(true, null, 0),
        });

        configStore.SaveCount.Should().Be(1, "an import is one operation however many entries it carries");
    }

    [Fact]
    public void SaveAllTweakConfigs_InsideABatch_DefersToTheBatch()
    {
        // A batch has to be a guarantee for every write, including the ones the consumer asks for. A save that wrote
        // immediately because it was explicit would make the promise conditional on what the operation contains.
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManager(a, b);
        configStore.ResetSaveCount();

        manager.RunAsBatch(() =>
        {
            manager.SaveAllTweakConfigs();
            manager.SaveTweakConfig("a");
            manager.SaveAllTweakConfigs();
        });

        configStore.SaveCount.Should().Be(1, "a save requested by name still defers to the batch it runs inside");
        EnabledOnDisk("a").Should().BeFalse("the deferred write still lands");
    }

    [Fact]
    public void SaveAllTweakConfigs_InsideABatch_CarriesTheWrite_WhenAutomaticPersistenceIsOff()
    {
        // The mixed case. The gate suppresses the module's own writes, so nothing in the batch would be written, and
        // the explicit save is what makes the single write happen. Once it does, everything the store holds lands
        // with it, because the configuration is written as one document rather than key by key.
        var a = new SpyTweak("a");
        var manager = MakeManagerWithoutPersistence(a);
        configStore.ResetSaveCount();

        manager.RunAsBatch(() =>
        {
            manager.SetFavorite("a", true);
            manager.SaveAllTweakConfigs();
        });

        configStore.SaveCount.Should().Be(1, "the save requested by name carries the batch's single write");
        EnabledOnDisk("a").Should().BeFalse("the entry the explicit save asked for is written");
        FavoritedOnDisk("a").Should().BeTrue("the star was already applied in memory and the file records the store as a whole");
    }

    [Fact]
    public void RunAsBatch_WritesNothing_WhenTheOperationRecordsNothing()
    {
        var manager = MakeManager(new SpyTweak("a"));
        configStore.ResetSaveCount();

        manager.RunAsBatch(() => { });

        configStore.SaveCount.Should().Be(0, "an operation that records nothing has nothing to write");
        File.Exists(configPath).Should().BeFalse();
    }

    #endregion

    #region Configuration queries

    [Fact]
    public void GetAllTweakConfigs_DoesNotMutateTheStore()
    {
        // Reading has to stay a read: a getter that wrote live state back into the store would make those
        // changes ride along on the next unrelated save.
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        configStore.GetTweakConfig(tweak.InternalKey).Should().BeNull("nothing has recorded this tweak yet");

        var configs = manager.GetAllTweakConfigs();

        configs.Should().ContainKey(tweak.InternalKey, "the snapshot still reports the live tweak");
        configs[tweak.InternalKey].Enabled.Should().BeFalse();
        configStore.GetTweakConfig(tweak.InternalKey).Should().BeNull("reading the configuration must not write to it");
    }

    [Fact]
    public void GetAllTweakConfigs_ReportsLiveStateOverTheStoredEntry()
    {
        var tweak = new SpyTweak();
        var manager = MakeManager(tweak);

        manager.EnableTweak(tweak.InternalKey);

        manager.GetAllTweakConfigs()[tweak.InternalKey].Enabled.Should().BeTrue();
    }

    [Fact]
    public void SaveAllTweakConfigs_WritesEveryRegisteredTweak()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManager(a, b);

        manager.SaveAllTweakConfigs();

        EnabledOnDisk("a").Should().BeFalse();
        EnabledOnDisk("b").Should().BeFalse();
    }

    #endregion

    #region Registration

    [Fact]
    public void RegisterTweak_RejectsADuplicateKey()
    {
        var manager = MakeManager(new SpyTweak("dup"));

        manager.RegisterTweak(new SpyTweak("dup"));

        manager.GetAllTweaks().Should().ContainSingle();
    }

    [Fact]
    public void UnregisterTweak_ReturnsFalseForAnUnknownKey()
    {
        var manager = MakeManager();

        manager.UnregisterTweak("nothing-here").Should().BeFalse();
    }

    [Fact]
    public void GetTweak_FindsByKeyAndByType()
    {
        var tweak = new SpyTweak("findable");
        var manager = MakeManager(tweak);

        manager.GetTweak("findable").Should().BeSameAs(tweak);
        manager.GetTweak<SpyTweak>().Should().BeSameAs(tweak);
        manager.GetTweak("missing").Should().BeNull();
    }

    [Fact]
    public void GetEnabledTweaks_ReflectsTheRunningSet()
    {
        var a = new SpyTweak("a");
        var b = new SpyTweak("b");
        var manager = MakeManager(a, b);

        manager.EnableTweak("a");

        manager.GetEnabledTweaks().Should().ContainSingle().Which.Should().BeSameAs(a);
    }

    #endregion

    #region Key migration moves everything the old key holds

    [Fact]
    public void KeyMigration_ByAttribute_MovesTheFavoriteAlongWithTheConfig()
    {
        // The favorite is keyed by the same internal key as the config entry, so a migration that moves only the
        // entry would leave the star behind on a key no tweak answers to any more, silently unfavoriting the tweak.
        configStore.SetTweakConfig(RenamedTweak.OldKey, new TweakConfigEntry(true, null, 0));
        configStore.SetFavorite(RenamedTweak.OldKey, true);

        var manager = MakeManager(new RenamedTweak());

        manager.IsFavorite(RenamedTweak.NewKey).Should().BeTrue("a rename must not cost the user their favorite");
        configStore.IsFavorite(RenamedTweak.OldKey).Should().BeFalse("the old key keeps nothing once it is migrated");
        configStore.GetTweakConfig(RenamedTweak.NewKey)!.Enabled.Should().BeTrue("the enabled state still migrates");
        configStore.GetTweakConfig(RenamedTweak.OldKey).Should().BeNull();

        FavoritedOnDisk(RenamedTweak.NewKey).Should().BeTrue("the migration is written, not just applied in memory");
        FavoritedOnDisk(RenamedTweak.OldKey).Should().BeFalse();
    }

    [Fact]
    public void KeyMigration_ByAttribute_MovesAFavoriteThatHasNoConfigEntry()
    {
        // Favoriting a tweak never records an entry for it, so the old key can hold a favorite and nothing else;
        // the migration must move it too, not only entries that carry a config.
        configStore.SetFavorite(RenamedTweak.OldKey, true);

        var manager = MakeManager(new RenamedTweak());

        manager.IsFavorite(RenamedTweak.NewKey).Should().BeTrue();
        configStore.IsFavorite(RenamedTweak.OldKey).Should().BeFalse();
    }

    [Fact]
    public void KeyMigration_ByAttribute_LeavesTheNewKeyAlone_WhenItAlreadyHasData()
    {
        // Data under the new key belongs to the tweak that is already using it, and a leftover under a key it used to
        // have must never overwrite it.
        configStore.SetTweakConfig(RenamedTweak.OldKey, new TweakConfigEntry(true, null, 0));
        configStore.SetTweakConfig(RenamedTweak.NewKey, new TweakConfigEntry(false, null, 0));

        var manager = MakeManager(new RenamedTweak());

        manager.GetTweak(RenamedTweak.NewKey).Should().NotBeNull();
        configStore.GetTweakConfig(RenamedTweak.NewKey)!.Enabled.Should().BeFalse("the live entry is kept as it is");
        configStore.GetTweakConfig(RenamedTweak.OldKey).Should().NotBeNull("nothing is destroyed by a skipped migration");
    }

    [Fact]
    public void ExecuteKeyMigrations_MovesTheFavoriteAlongWithTheConfig()
    {
        // The mapping registered at runtime reaches the same move as the attribute does.
        var manager = MakeManager(new SpyTweak("renamed"));

        configStore.SetTweakConfig("gone", new TweakConfigEntry(true, null, 0));
        configStore.SetFavorite("gone", true);
        manager.AddKeyMigration("gone", "renamed");

        manager.ExecuteKeyMigrations().Should().Be(1);

        manager.IsFavorite("renamed").Should().BeTrue();
        configStore.IsFavorite("gone").Should().BeFalse();
        configStore.GetTweakConfig("renamed")!.Enabled.Should().BeTrue();
        configStore.GetTweakConfig("gone").Should().BeNull();
        configStore.KeyMigrations.Should().NotContainKey("gone", "a mapping that has been applied is spent");
    }

    [Fact]
    public void ExecuteKeyMigrations_KeepsAMappingThatHadNothingToMove()
    {
        var manager = MakeManager();

        manager.AddKeyMigration("never-existed", "somewhere");

        manager.ExecuteKeyMigrations().Should().Be(0);
        configStore.KeyMigrations.Should().ContainKey("never-existed",
            "a mapping still applies if the data it names turns up later");
    }

    [Fact]
    public void MigrateTweakKey_MovesNothing_WhenTheOldKeyHoldsNothing()
    {
        configStore.SetTweakConfig("live", new TweakConfigEntry(true, null, 0));

        configStore.MigrateTweakKey("empty", "live").Should().BeFalse();

        configStore.GetTweakConfig("live")!.Enabled.Should().BeTrue();
    }

    #endregion

    #region A migration is written, not just applied in memory

    [Fact]
    public void ExecuteKeyMigrations_WritesTheMoveToDisk()
    {
        // The module reaches its configuration through the instance rather than the auto-saving static accessor,
        // so a migration must be written explicitly or it stays in memory: the file keeps the old keys, and the
        // same move is redone on every load.
        var manager = MakeManager(new SpyTweak("renamed"));

        configStore.SetTweakConfig("gone", new TweakConfigEntry(true, null, 0));
        configStore.SetFavorite("gone", true);
        manager.AddKeyMigration("gone", "renamed");

        manager.ExecuteKeyMigrations().Should().Be(1);

        EnabledOnDisk("renamed").Should().BeTrue("the enabled state must land under the new key on disk");
        EnabledOnDisk("gone").Should().BeNull("the old key keeps nothing on disk either");
        FavoritedOnDisk("renamed").Should().BeTrue("the favorite moves with the entry all the way to the file");
        FavoritedOnDisk("gone").Should().BeFalse();
    }

    [Fact]
    public void KeyMigration_ByMapping_IsWrittenWhenTheModuleInitializes()
    {
        // What a user experiences across a restart: the mapping the configuration carries is applied while the module
        // initializes, and the file it read from has to settle rather than be migrated again on the next load.
        configStore.SetTweakConfig("gone", new TweakConfigEntry(true, null, 0));
        configStore.SetFavorite("gone", true);
        configStore.AddKeyMigration("gone", "renamed");

        MakeManager(new SpyTweak("renamed"));

        EnabledOnDisk("renamed").Should().BeTrue();
        FavoritedOnDisk("renamed").Should().BeTrue();
        EnabledOnDisk("gone").Should().BeNull();
        configStore.KeyMigrations.Should().NotContainKey("gone", "a mapping that has been applied is spent");
    }

    [Fact]
    public void ExecuteKeyMigrations_WritesNothing_WhenNothingMoved()
    {
        // Loading a configuration that has no migration to make must leave the file exactly as it was, so that merely
        // starting up never rewrites it.
        var manager = MakeManager(new SpyTweak("renamed"));

        manager.AddKeyMigration("never-existed", "somewhere");

        manager.ExecuteKeyMigrations().Should().Be(0);

        File.Exists(configPath).Should().BeFalse("a run that moves nothing has nothing to record");
    }

    [Fact]
    public void KeyMigration_ByMapping_WritesNothing_WhenAutomaticPersistenceIsOff()
    {
        configStore.SetTweakConfig("gone", new TweakConfigEntry(true, null, 0));
        configStore.AddKeyMigration("gone", "renamed");

        var manager = new NoireTweakManager(
            active: false,
            enableLogging: false,
            automaticPersistence: false,
            additionalTweaks: new List<TweakBase> { new SpyTweak("renamed") });
        managersToClean.Add(manager);

        configStore.GetTweakConfig("renamed")!.Enabled.Should().BeTrue("the move is still applied in memory");
        File.Exists(configPath).Should().BeFalse("a consumer that opted out of writes gets none, migration included");
    }

    #endregion

    #region Window state

    [Fact]
    public void IsWindowOpen_ReflectsTheWindowState()
    {
        var manager = MakeManager();

        manager.HasWindow.Should().BeTrue("the module registers its window while initializing");
        manager.IsWindowOpen.Should().BeFalse("a module's window starts closed");

        manager.SetActive(true);
        manager.ShowWindow(true);
        manager.IsWindowOpen.Should().BeTrue();

        manager.ShowWindow(false);
        manager.IsWindowOpen.Should().BeFalse();
    }

    #endregion
}
