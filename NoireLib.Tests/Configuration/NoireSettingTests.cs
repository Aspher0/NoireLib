using FluentAssertions;
using Newtonsoft.Json;
using NoireLib.Configuration;
using NoireLib.Helpers;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Drives the generated settings class: descriptors, defaults, annotation rules, enum labels and share codes.</summary>
[Collection(ConfigStateCollection.Name)]
public sealed class NoireSettingTests : IDisposable
{
    private static readonly NoireString SecondModeLabel = new("SettingsProbeMode.Second", "Second mode");

    private readonly string tempDirectory;

    public NoireSettingTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibSettingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        NoireConfigManager.UnloadConfig<SettingsProbeConfig>();
        SettingsProbeConfig.PathOverride = Path.Combine(tempDirectory, "settings-probe.json");
    }

    public void Dispose()
    {
        SettingsProbe.Instance.FlushPendingSave();
        NoireConfigManager.UnloadConfig<SettingsProbeConfig>();
        SettingsProbeConfig.PathOverride = null;

        try
        {
            Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
    }

    [Fact]
    public void All_HoldsOneSettingPerSimpleProperty_InDeclarationOrder()
    {
        SettingsProbeCfg.All.Select(s => s.Name).Should().Equal("Enabled", "Count", "Opacity", "Throttle", "Label", "Mode", "Optional");
    }

    [Fact]
    public void Defaults_AreTheValuesOfAFreshInstance()
    {
        SettingsProbeCfg.Count.Default.Should().Be(3);
        SettingsProbeCfg.Throttle.Default.Should().Be(TimeSpan.FromMinutes(5));
        SettingsProbeCfg.Label.Default.Should().Be("abc");
        SettingsProbeCfg.Optional.Default.Should().BeNull();
    }

    [Fact]
    public void Rules_AreReadFromTheDataAnnotations()
    {
        SettingsProbeCfg.Enabled.Rules.Should().BeSameAs(NoireRules<bool>.None);

        var count = SettingsProbeCfg.Count.Rules;
        (count.HasMin, count.Min, count.HasMax, count.Max).Should().Be((true, 1, true, 50));

        SettingsProbeCfg.Opacity.Rules.Max.Should().Be(1f);
        SettingsProbeCfg.Throttle.Rules.Max.Should().Be(TimeSpan.FromHours(1), "a [Range(typeof(TimeSpan), ...)] is parsed into the property's type");
        SettingsProbeCfg.Label.Rules.MaxLength.Should().Be(5);
        SettingsProbeCfg.Optional.Rules.Should().BeSameAs(NoireRules<int?>.None);
    }

    [Fact]
    public void Value_AppliesTheRules_AndSavesWhenItChanged()
    {
        SettingsProbeCfg.Count.Value = 99;

        SettingsProbe.Count.Should().Be(50);
        SettingsProbeCfg.Count.IsModified.Should().BeTrue();
        SettingsProbe.Instance.HasPendingSave.Should().BeTrue();

        SettingsProbeCfg.Label.Value = "abcdefgh";
        SettingsProbe.Label.Should().Be("abcde");

        SettingsProbeCfg.Count.Reset();
        SettingsProbe.Count.Should().Be(3);
        SettingsProbeCfg.Count.IsModified.Should().BeFalse();
    }

    [Fact]
    public void Value_OfANullableSetting_KeepsNull()
    {
        SettingsProbeCfg.Optional.Value = 4;
        SettingsProbeCfg.Optional.Value = null;

        SettingsProbe.Optional.Should().BeNull();
    }

    [Fact]
    public void EnumChoices_UseTheDeclaredTextForEachValue_ElseItsName_InDeclarationOrder()
    {
        var declared = SecondModeLabel.Source;
        var choice = (INoireChoice)SettingsProbeCfg.Mode;

        choice.Labels.Should().Equal(["First", declared], "the enum declares First before Second although First has the higher value");
        SettingsProbeCfg.Mode.Value = SettingsProbeMode.Second;
        choice.Index.Should().Be(1);
        choice.ValueAt(0).Should().Be(SettingsProbeMode.First);
    }

    [Fact]
    public void Load_BringsAnOutOfRangeFileBackInsideTheRules()
    {
        File.WriteAllText(SettingsProbeConfig.PathOverride!, JsonConvert.SerializeObject(new { Version = 1, Count = 99, Opacity = -2f, Label = "abcdefgh" }));

        var config = new SettingsProbeConfig();
        config.Load().Should().BeTrue();

        config.Count.Should().Be(50);
        config.Opacity.Should().Be(0f);
        config.Label.Should().Be("abcde");
        config.HasPendingSave.Should().BeTrue("the corrected values are written back");
    }

    [Fact]
    public void ShareCode_CarriesOnlyModifiedSettings_AndRestoresThem()
    {
        SettingsProbeCfg.Count.Value = 7;
        SettingsProbeCfg.Mode.Value = SettingsProbeMode.Second;
        SettingsProbeCfg.Throttle.Value = TimeSpan.FromMinutes(12);

        var code = NoireSettingsShare.Export(SettingsProbeCfg.All);

        foreach (var setting in SettingsProbeCfg.All)
            setting.Reset();

        var result = NoireSettingsShare.Import(code, SettingsProbeCfg.All);

        result.Success.Should().BeTrue();
        result.Value.Should().Be(3);
        SettingsProbe.Count.Should().Be(7);
        SettingsProbe.Mode.Should().Be(SettingsProbeMode.Second);
        SettingsProbe.Throttle.Should().Be(TimeSpan.FromMinutes(12));
    }

    [Fact]
    public void ShareCode_Import_AppliesTheRules_AndSkipsUnknownNames()
    {
        var code = ShareCodeHelper.Encode("noire.settings", new Dictionary<string, object?> { ["Count"] = 500, ["Removed"] = true, ["Mode"] = "Nonsense" });

        var result = NoireSettingsShare.Import(code, SettingsProbeCfg.All);

        result.Value.Should().Be(1);
        SettingsProbe.Count.Should().Be(50);
        SettingsProbe.Mode.Should().Be(SettingsProbeMode.First);
    }

    [Fact]
    public void ShareCode_Import_RefusesAnotherKind()
    {
        var code = ShareCodeHelper.Encode("other.kind", new Dictionary<string, object?> { ["Count"] = 5 });

        var result = NoireSettingsShare.Import(code, SettingsProbeCfg.All);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ShareCodeError.WrongKind);
        SettingsProbe.Count.Should().Be(3);
    }
}

public enum SettingsProbeMode
{
    First = 1,
    Second = 0,
}

/// <summary>Names a settings class: the generator emits <c>SettingsProbeCfg</c> beside the <c>SettingsProbe</c> accessor.</summary>
[NoireConfig("SettingsProbe", SettingsClassName = "SettingsProbeCfg")]
public sealed class SettingsProbeConfig : NoireConfigBase
{
    public static string? PathOverride;

    public override int Version { get; set; } = 1;

    public override string GetConfigFileName() => "settings-probe.json";

    protected override string? GetConfigFilePath() => PathOverride;

    public bool Enabled { get; set; } = true;

    [Range(1, 50)]
    public int Count { get; set; } = 3;

    [Range(0.0, 1.0)]
    public float Opacity { get; set; } = 1f;

    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan Throttle { get; set; } = TimeSpan.FromMinutes(5);

    [StringLength(5)]
    public string Label { get; set; } = "abc";

    public SettingsProbeMode Mode { get; set; } = SettingsProbeMode.First;

    public int? Optional { get; set; }

    public List<int> Items { get; set; } = [];

    public int Computed => Count * 2;
}
