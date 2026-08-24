using FluentAssertions;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the on-disk format against a literal fixture: an existing file loads unchanged, and saving the same values
/// reproduces it byte for byte. Drift here breaks every configuration file already on a user's disk.
/// </summary>
[Collection(ConfigStateCollection.Name)]
public sealed class NoireConfigFileFormatTests : IDisposable
{
    private readonly string tempDirectory;

    public NoireConfigFileFormatTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigFileFormatTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ResetState();
    }

    public void Dispose()
    {
        ResetState();

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }

        GC.SuppressFinalize(this);
    }

    private static void ResetState()
    {
        NoireConfigManager.UnloadConfig<FormatConfig>();
        FormatConfig.PathOverride = null;
    }

    #region Configuration shape

    public enum FormatMode
    {
        Alpha,
        Beta,
        Gamma,
    }

    public sealed class FormatLeaf
    {
        public bool Locked { get; set; }

        public float Scale { get; set; } = 1f;
    }

    public sealed class FormatNested
    {
        public string Label { get; set; } = "leaf";

        public FormatLeaf Leaf { get; set; } = new();
    }

    public sealed class FormatConfig : NoireConfigBase
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => "format-config.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public bool Flag { get; set; } = true;

        public int Count { get; set; } = 3;

        public string Name { get; set; } = "name";

        public FormatMode Mode { get; set; } = FormatMode.Beta;

        public int? MaybeCount { get; set; }

        public List<string> Tags { get; set; } = ["alpha", "beta"];

        public Dictionary<uint, FormatNested> Zones { get; set; } = new();

        public FormatNested Ui { get; set; } = new();
    }

    #endregion

    /// <summary>
    /// The text the system writes for the graph <see cref="Populate"/> builds, with platform line endings.
    /// </summary>
    private static readonly string PinnedFileText = PinnedFileBody.ReplaceLineEndings();

    private const string PinnedFileBody = """
        {
          "Version": 1,
          "Flag": false,
          "Count": 42,
          "Name": "written by a plugin",
          "Mode": 2,
          "MaybeCount": 7,
          "Tags": [
            "one",
            "two",
            "three"
          ],
          "Zones": {
            "129": {
              "Label": "limsa",
              "Leaf": {
                "Locked": true,
                "Scale": 2.5
              }
            },
            "130": {
              "Label": "uldah",
              "Leaf": {
                "Locked": false,
                "Scale": 1.0
              }
            }
          },
          "Ui": {
            "Label": "root",
            "Leaf": {
              "Locked": true,
              "Scale": 0.75
            }
          }
        }
        """;

    private string FileFor(string name) => Path.Combine(tempDirectory, name);

    private static void Populate(FormatConfig config)
    {
        config.Flag = false;
        config.Count = 42;
        config.Name = "written by a plugin";
        config.Mode = FormatMode.Gamma;
        config.MaybeCount = 7;
        config.Tags = ["one", "two", "three"];
        config.Zones = new Dictionary<uint, FormatNested>
        {
            [129] = new FormatNested { Label = "limsa", Leaf = new FormatLeaf { Locked = true, Scale = 2.5f } },
            [130] = new FormatNested { Label = "uldah" },
        };
        config.Ui = new FormatNested { Label = "root", Leaf = new FormatLeaf { Locked = true, Scale = 0.75f } };
    }

    private static void AssertValues(FormatConfig loaded)
    {
        loaded.Flag.Should().BeFalse();
        loaded.Count.Should().Be(42);
        loaded.Name.Should().Be("written by a plugin");
        loaded.Mode.Should().Be(FormatMode.Gamma);
        loaded.MaybeCount.Should().Be(7);
        loaded.Tags.Should().Equal("one", "two", "three");
        loaded.Zones.Should().ContainKeys(129u, 130u);
        loaded.Zones[129].Label.Should().Be("limsa");
        loaded.Zones[129].Leaf.Locked.Should().BeTrue();
        loaded.Zones[129].Leaf.Scale.Should().Be(2.5f);
        loaded.Zones[130].Label.Should().Be("uldah");
        loaded.Ui.Label.Should().Be("root");
        loaded.Ui.Leaf.Locked.Should().BeTrue();
        loaded.Ui.Leaf.Scale.Should().Be(0.75f);
    }

    [Fact]
    public void AnExistingPluginFile_LoadsUnchanged()
    {
        var file = FileFor("existing.json");
        File.WriteAllText(file, PinnedFileText);
        FormatConfig.PathOverride = file;

        var config = new FormatConfig();
        config.Load().Should().BeTrue("the pinned text is a file a real plugin already has on disk");

        AssertValues(config);
    }

    [Fact]
    public void SavingTheSameValues_ReproducesThePinnedText_ByteForByte()
    {
        var file = FileFor("written.json");
        FormatConfig.PathOverride = file;

        var config = new FormatConfig();
        Populate(config);
        config.Save().Should().BeTrue();

        File.ReadAllText(file).Should().Be(PinnedFileText,
            "the serializer contract is pinned, and any drift here is a compatibility break");
    }

    [Fact]
    public void RoundTrip_LoadEditSaveReload_PreservesEveryValue()
    {
        var file = FileFor("round-trip.json");
        File.WriteAllText(file, PinnedFileText);
        FormatConfig.PathOverride = file;

        var first = new FormatConfig();
        first.Load().Should().BeTrue();
        first.Name = "edited";
        first.Tags.Add("four");
        first.Save().Should().BeTrue();

        NoireConfigManager.UnloadConfig<FormatConfig>();

        var second = new FormatConfig();
        second.Load().Should().BeTrue();

        second.Name.Should().Be("edited");
        second.Tags.Should().Equal("one", "two", "three", "four");
        second.Zones[129].Leaf.Scale.Should().Be(2.5f, "values the edit did not touch survive the round trip");
    }
}
