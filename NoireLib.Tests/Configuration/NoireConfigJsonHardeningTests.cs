using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Helpers;
using NoireLib.TweakManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the invariant that the JSON written and read by the configuration system, the file helper, the encryption
/// helper and the database models is decided by NoireLib's own settings objects rather than by
/// <see cref="JsonConvert.DefaultSettings"/>. That property is process-global and NoireLib is loaded next to unrelated
/// plugins in one process, so any of them can assign it at any time.<br/>
/// The configuration paths matter most: they read and write the user's own settings file, which means a global
/// assigned elsewhere in the process would otherwise decide how a file on the user's disk is parsed.
/// </summary>
[Collection(JsonDefaultSettingsCollection.Name)]
[SupportedOSPlatform("windows")]
public class NoireConfigJsonHardeningTests : IDisposable
{
    private readonly string tempDirectory;

    public NoireConfigJsonHardeningTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
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
    /// A configuration that resolves to a temp file instead of the plugin configuration directory, which is what lets
    /// these tests exercise the real <see cref="NoireConfigBase.Save"/> and <see cref="NoireConfigBase.Load"/> without
    /// a running game.
    /// </summary>
    private sealed class HardeningTestConfig : NoireConfigBase
    {
        /// <summary>
        /// Held in a field rather than a public property because the member copy performed at the end of a load
        /// reflects over public properties, so a property here would be overwritten with the null carried by the
        /// instance deserialized from the file and the configuration would lose its own path mid-load.
        /// </summary>
        internal string? filePathOverride;

        public override int Version { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        /// <summary>
        /// Carries an initializer so that a load proves <see cref="ObjectCreationHandling.Replace"/> is in force: with
        /// it the dictionary read from the file replaces this one, without it the file's entries are merged into it.
        /// </summary>
        public Dictionary<string, string> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fromInitializer"] = "present",
        };

        /// <summary>
        /// Typed as <see cref="object"/> so that a file naming a type in this position would have somewhere to resolve
        /// it to if type resolution were ever enabled.
        /// </summary>
        public object? Payload { get; set; }

        public override string GetConfigFileName() => "HardeningTestConfig.json";

        protected override string? GetConfigFilePath() => filePathOverride;
    }

    private sealed class HardeningTestTweakConfig : TweakConfigBase
    {
        public override int Version { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Typed as <see cref="object"/> so that a stored value naming a type in this position would have somewhere
        /// to resolve it to if type resolution were ever enabled.
        /// </summary>
        public object? Payload { get; set; }
    }

    private sealed class HardeningTestPayload
    {
        public string Text { get; set; } = string.Empty;

        public int Value { get; set; }
    }

    private string PathFor(string fileName) => Path.Combine(tempDirectory, fileName);

    private HardeningTestConfig CreateConfig(string fileName)
        => new() { filePathOverride = PathFor(fileName) };

    #region Configuration

    [Fact]
    public void Config_Save_Writes_Identical_Bytes_Under_Hostile_DefaultSettings()
    {
        var path = PathFor("roundtrip.json");
        var config = CreateConfig("roundtrip.json");
        config.Name = "value";
        config.Count = 42;
        config.Entries["key"] = "entry";
        config.Payload = new HardeningTestPayload { Text = "payload", Value = 7 };

        config.Save().Should().BeTrue();
        var baseline = File.ReadAllText(path);

        // Removed so that the save below has to write the file rather than take the unchanged-content shortcut.
        File.Delete(path);

        HostileJsonDefaults.While(() => config.Save()).Should().BeTrue();
        var underHostileDefaults = File.ReadAllText(path);

        underHostileDefaults.Should().Be(baseline,
            "the user's configuration file must be written the same way no matter what else is loaded in the process");
    }

    [Fact]
    public void Config_Save_Never_Writes_Type_Or_Reference_Metadata_Under_Hostile_DefaultSettings()
    {
        var config = CreateConfig("metadata.json");
        config.Name = "value";
        config.Payload = new HardeningTestPayload { Text = "payload", Value = 7 };

        HostileJsonDefaults.While(() => config.Save()).Should().BeTrue();
        var written = File.ReadAllText(PathFor("metadata.json"));

        written.Should().NotContain("$type", "a configuration file stores settings, never a type to construct");
        written.Should().NotContain("$id", "reference metadata would only be readable by the settings that emitted it");
        written.Should().NotContain("$ref");
    }

    [Fact]
    public void Config_Load_Never_Resolves_A_Type_Named_By_The_File()
    {
        var config = CreateConfig("hostiletype.json");
        File.WriteAllText(PathFor("hostiletype.json"), """
            {
              "Version": 1,
              "Name": "loaded",
              "Payload": {
                "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
                "Text": "payload"
              }
            }
            """);

        HostileJsonDefaults.While(() => config.Load()).Should().BeTrue();

        config.Name.Should().Be("loaded");
        config.Payload.Should().BeOfType<JObject>(
            "a configuration file must never name the type it is materialized into");
    }

    [Fact]
    public void Config_Load_Ignores_A_Type_Named_At_The_Root_Of_The_File()
    {
        var config = CreateConfig("hostilerootype.json");
        File.WriteAllText(PathFor("hostilerootype.json"), """
            {
              "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
              "Version": 1,
              "Name": "loaded",
              "Count": 5
            }
            """);

        HostileJsonDefaults.While(() => config.Load()).Should().BeTrue();

        config.Name.Should().Be("loaded");
        config.Count.Should().Be(5);
    }

    [Fact]
    public void Config_Load_Replaces_A_Collection_Built_By_A_Property_Initializer()
    {
        var config = CreateConfig("replace.json");
        File.WriteAllText(PathFor("replace.json"), """
            {
              "Version": 1,
              "Entries": { "fromFile": "value" }
            }
            """);

        config.Load().Should().BeTrue();

        config.Entries.Should().ContainKey("fromFile");
        config.Entries.Should().NotContainKey("fromInitializer",
            "ObjectCreationHandling.Replace must keep the file's collection from being merged into the one the " +
            "property initializer built");
        config.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Config_Round_Trips_Through_Save_And_Load()
    {
        var config = CreateConfig("happypath.json");
        config.Name = "value";
        config.Count = 42;
        config.Entries.Clear();
        config.Entries["key"] = "entry";

        config.Save().Should().BeTrue();

        var loaded = CreateConfig("happypath.json");
        loaded.Load().Should().BeTrue();

        loaded.Name.Should().Be("value");
        loaded.Count.Should().Be(42);
        loaded.Entries.Should().ContainKey("key").WhoseValue.Should().Be("entry");
    }

    [Fact]
    public void Config_Load_Returns_False_On_A_Malformed_File()
    {
        var config = CreateConfig("malformed.json");
        File.WriteAllText(PathFor("malformed.json"), "{\"Version\": 1, \"Name\":");

        config.Load().Should().BeFalse("a configuration file that cannot be parsed leaves the defaults in place");
    }

    [Fact]
    public void Config_Load_Returns_False_On_Content_After_The_Configuration_Object()
    {
        var config = CreateConfig("trailing.json");
        File.WriteAllText(PathFor("trailing.json"), "{\"Version\": 1, \"Name\": \"loaded\"} garbage");

        config.Load().Should().BeFalse("a configuration file holds exactly one JSON document");
    }

    [Fact]
    public void Tweak_Config_Serializes_Without_Type_Information_Under_Hostile_DefaultSettings()
    {
        var config = new HardeningTestTweakConfig { Name = "value" };

        var baseline = config.ToJson();
        var underHostileDefaults = HostileJsonDefaults.While(() => config.ToJson());

        underHostileDefaults.Should().Be(baseline,
            "tweak configurations are serialized by settings pinned on NoireConfigBase rather than by the " +
            "process-global defaults");
        underHostileDefaults.Should().NotContain("$type");
        underHostileDefaults.Should().NotContain("$id");
    }

    [Fact]
    public void Tweak_Config_Round_Trips_Byte_Identically_Under_Hostile_DefaultSettings()
    {
        var config = new HardeningTestTweakConfig
        {
            Name = "value",
            Payload = new HardeningTestPayload { Text = "payload", Value = 7 },
        };

        var baseline = config.SerializeToJson();

        var restored = HostileJsonDefaults.While(() =>
        {
            var json = config.SerializeToJson();

            json.Should().Be(baseline,
                "the manager stores this JSON and reads it back later, so its shape must not depend on what else " +
                "is loaded in the process at the moment of the write");

            return TweakConfigBase.DeserializeFromJson<HardeningTestTweakConfig>(json, storedVersion: 1);
        });

        restored.Name.Should().Be("value");
        restored.SerializeToJson().Should().Be(baseline,
            "a value written, read back and written again must produce the same bytes it started as");
    }

    [Fact]
    public void Tweak_Config_Deserialize_Never_Resolves_A_Type_Named_By_The_Stored_Json()
    {
        const string json = """
            {
              "Version": 1,
              "Name": "loaded",
              "Payload": {
                "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
                "Text": "payload"
              }
            }
            """;

        var restored = HostileJsonDefaults.While(
            () => TweakConfigBase.DeserializeFromJson<HardeningTestTweakConfig>(json, storedVersion: 1));

        restored.Name.Should().Be("loaded");
        restored.Payload.Should().BeOfType<JObject>(
            "a stored tweak configuration must never name the type it is materialized into");
    }

    [Fact]
    public void Tweak_Config_Deserialize_Falls_Back_To_Defaults_On_Content_After_The_Object()
    {
        var restored = TweakConfigBase.DeserializeFromJson<HardeningTestTweakConfig>(
            "{\"Version\": 1, \"Name\": \"loaded\"} garbage", storedVersion: 1);

        restored.Name.Should().BeEmpty(
            "a stored tweak configuration holds exactly one JSON document, and one that does not is discarded for " +
            "the defaults rather than half-read");
    }

    #endregion

    #region FileHelper

    [Fact]
    public void FileHelper_Json_Round_Trips_Under_Hostile_DefaultSettings()
    {
        var filePath = Path.Combine(tempDirectory, "filehelper.json");
        var payload = new HardeningTestPayload { Text = "value", Value = 42 };

        var loaded = HostileJsonDefaults.While(() =>
        {
            FileHelper.WriteJsonToFile(filePath, payload).Should().BeTrue();
            File.ReadAllText(filePath).Should().NotContain("$type");

            return FileHelper.ReadJsonFromFile<HardeningTestPayload>(filePath);
        });

        loaded.Should().NotBeNull();
        loaded!.Text.Should().Be("value");
        loaded.Value.Should().Be(42);
    }

    [Fact]
    public void FileHelper_Write_Overrides_Caller_Supplied_TypeNameHandling()
    {
        var filePath = Path.Combine(tempDirectory, "callersettings.json");
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        FileHelper.WriteJsonToFile(filePath, new HardeningTestPayload { Text = "value" }, settings).Should().BeTrue();

        File.ReadAllText(filePath).Should().NotContain("$type",
            "the library forbids type information in its JSON, so a caller asking for it is overridden");
    }

    [Fact]
    public void FileHelper_Write_Preserves_Other_Caller_Supplied_Settings()
    {
        var filePath = Path.Combine(tempDirectory, "formatting.json");
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        FileHelper.WriteJsonToFile(filePath, new HardeningTestPayload { Text = "value", Value = 1 }, settings)
            .Should().BeTrue();

        var written = File.ReadAllText(filePath);
        written.Should().Contain(Environment.NewLine, "the caller asked for indented output and only the type " +
            "handling is overridden");
        written.Should().NotContain("$type");
    }

    [Fact]
    public void FileHelper_Read_Never_Resolves_A_Type_Named_By_The_File()
    {
        var filePath = Path.Combine(tempDirectory, "hostilefile.json");
        File.WriteAllText(filePath, """
            { "$type": "System.Diagnostics.Process, System.Diagnostics.Process", "Text": "value" }
            """);

        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        var loaded = HostileJsonDefaults.While(() => FileHelper.ReadJsonFromFile<HardeningTestPayload>(filePath, settings));

        loaded.Should().BeOfType<HardeningTestPayload>("a file must never name the type it is read into");
        loaded!.Text.Should().Be("value");
    }

    [Fact]
    public void FileHelper_Read_Returns_Default_On_Content_After_The_Document()
    {
        var filePath = Path.Combine(tempDirectory, "trailingfile.json");
        File.WriteAllText(filePath, "{\"Text\": \"value\"} garbage");

        FileHelper.ReadJsonFromFile<HardeningTestPayload>(filePath).Should().BeNull(
            "a file holds exactly one JSON document, and a read that fails reports it by returning the default");
    }

    #endregion

    #region EncryptionHelper

    [Fact]
    public void Encryption_Base64_Round_Trips_Under_Hostile_DefaultSettings()
    {
        var payload = new HardeningTestPayload { Text = "value", Value = 42 };

        var baseline = EncryptionHelper.SerializeToBase64(payload);
        var underHostileDefaults = HostileJsonDefaults.While(() => EncryptionHelper.SerializeToBase64(payload));

        underHostileDefaults.Should().Be(baseline,
            "an encoded payload must not change shape because unrelated code assigned the global defaults");
        underHostileDefaults.FromBase64ToString().Should().NotContain("$type");

        var decoded = HostileJsonDefaults.While(() => underHostileDefaults.DeserializeFromBase64<HardeningTestPayload>());

        decoded.Should().NotBeNull();
        decoded!.Text.Should().Be("value");
        decoded.Value.Should().Be(42);
    }

    [Fact]
    public void Encryption_Serialize_Overrides_Caller_Supplied_TypeNameHandling()
    {
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        var base64 = EncryptionHelper.SerializeToBase64(new HardeningTestPayload { Text = "value" }, jsonSettings: settings);

        base64.FromBase64ToString().Should().NotContain("$type",
            "the library forbids type information in its JSON, so a caller asking for it is overridden");
    }

    [Fact]
    public void Encryption_Deserialize_Never_Resolves_A_Type_Named_By_The_Payload()
    {
        const string json = """
            { "$type": "System.Diagnostics.Process, System.Diagnostics.Process", "Text": "value" }
            """;

        var base64 = json.ToBase64();
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

        var decoded = HostileJsonDefaults.While(() => base64.DeserializeFromBase64<HardeningTestPayload>(settings));

        decoded.Should().BeOfType<HardeningTestPayload>("a payload must never name the type it is read into");
        decoded!.Text.Should().Be("value");
    }

    [Fact]
    public void Encryption_Hash_Is_Stable_Under_Hostile_DefaultSettings()
    {
        var payload = new HardeningTestPayload { Text = "value", Value = 42 };

        var baseline = EncryptionHelper.Sha256(payload);
        var underHostileDefaults = HostileJsonDefaults.While(() => EncryptionHelper.Sha256(payload));

        underHostileDefaults.Should().Be(baseline,
            "a hash that moved when another plugin assigned the global defaults would stop matching the values " +
            "already derived from the same object");
    }

    [Fact]
    public void Encryption_Aes_Round_Trips_An_Object_Under_Hostile_DefaultSettings()
    {
        var payload = new HardeningTestPayload { Text = "value", Value = 42 };

        var decrypted = HostileJsonDefaults.While(() =>
        {
            var encrypted = EncryptionHelper.AesEncrypt(payload, "password", iterations: 1);
            return EncryptionHelper.AesDecryptToObject<HardeningTestPayload>(encrypted, "password");
        });

        decrypted.Should().NotBeNull();
        decrypted!.Text.Should().Be("value");
        decrypted.Value.Should().Be(42);
    }

    [Fact]
    public void Encryption_Aes_Round_Trips_An_Object()
    {
        var encrypted = EncryptionHelper.AesEncrypt(new HardeningTestPayload { Text = "value", Value = 42 }, "password", iterations: 1);
        var decrypted = EncryptionHelper.AesDecryptToObject<HardeningTestPayload>(encrypted, "password");

        decrypted.Should().NotBeNull();
        decrypted!.Text.Should().Be("value");
        decrypted.Value.Should().Be(42);
    }

    #endregion
}
