using FluentAssertions;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives the accessor the source generator emits for the probe classes below, compiled into this assembly:
/// identity with the manager, immediate capture on an autosaved setter, capture of a collection mutation, and the
/// members that are not forwarded.
/// </summary>
[Collection(ConfigStateCollection.Name)]
public sealed class NoireConfigAccessorTests : IDisposable
{
    private readonly string tempDirectory;

    public NoireConfigAccessorTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigAccessorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ResetState();
        AccessorProbeConfig.PathOverride = Path.Combine(tempDirectory, "accessor-probe.json");
    }

    public void Dispose()
    {
        NoireConfigWatch.RunChecks();
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
        NoireConfigManager.UnloadConfig<AccessorProbeConfig>();
        AccessorProbeConfig.PathOverride = null;
    }

    [Fact]
    public void TheAccessorAndTheManager_ShareOneIdentity()
    {
        var viaAccessor = AccessorProbe.Instance;

        NoireConfigManager.GetConfig<AccessorProbeConfig>().Should().BeSameAs(viaAccessor,
            "the generated accessor keeps no cache of its own");
    }

    [Fact]
    public void SettingAnAutosavedProperty_CapturesImmediately()
    {
        AccessorProbe.Toggle = true;

        AccessorProbe.Instance.HasPendingSave.Should().BeTrue("the setter requested a save inside the assignment");
        AccessorProbe.Instance.FlushPendingSave().Should().BeTrue();
        File.ReadAllText(AccessorProbeConfig.PathOverride!).Should().Contain("\"Toggle\": true");
    }

    [Fact]
    public void MutatingAListThroughTheAccessor_IsCapturedByTheCheck()
    {
        // The read of Items armed the watch; the check on the next tick captures the add.
        AccessorProbe.Items.Add("added-through-accessor");

        NoireConfigWatch.RunChecks();
        AccessorProbe.Instance.FlushPendingSave().Should().BeTrue();

        File.ReadAllText(AccessorProbeConfig.PathOverride!).Should().Contain("added-through-accessor");
    }

    [Fact]
    public void TheAccessor_DoesNotForwardTheBasePlumbing()
    {
        typeof(AccessorProbe).GetProperty("Version").Should().BeNull(
            "the schema version is the system's to stamp, not a setting to expose");
        typeof(AccessorProbe).GetProperty("LoadFromDiskOnInitialization").Should().BeNull();
        typeof(AccessorProbe).GetMethod("GetConfigFileName").Should().BeNull();
    }

    [Fact]
    public void TheAccessor_ExposesTheUtilityMembers()
    {
        typeof(AccessorProbe).GetMethod("Save").Should().NotBeNull();
        typeof(AccessorProbe).GetMethod("RequestSave").Should().NotBeNull();
        typeof(AccessorProbe).GetMethod("Reload").Should().NotBeNull();
        typeof(AccessorProbe).GetMethod("ClearCache").Should().NotBeNull();
    }

    [Fact]
    public void AnUnnamedAttribute_NamesTheAccessorAfterTheClassPlusStatic()
    {
        // Naming the type at all is the assertion: this only compiles if the generator emitted UnnamedProbeStatic.
        typeof(UnnamedProbeStatic).GetProperty("Flag").Should().NotBeNull("the accessor forwards the configuration's members");
    }
}

/// <summary>
/// The probe the source generator compiles an accessor for, enrolled through the class-level attribute. Public and
/// top-level so the generated static class lands beside it in this namespace.
/// </summary>
[NoireConfig("AccessorProbe")]
[AutoSave]
public sealed class AccessorProbeConfig : NoireConfigBase
{
    public static string? PathOverride;

    public override int Version { get; set; } = 1;

    public override string GetConfigFileName() => "accessor-probe.json";

    protected override string? GetConfigFilePath() => PathOverride;

    public bool Toggle { get; set; }

    public List<string> Items { get; set; } = [];
}

/// <summary>
/// Names no accessor, so the generator emits <c>UnnamedProbeStatic</c>.
/// </summary>
[NoireConfig]
public sealed class UnnamedProbe : NoireConfigBase
{
    public override int Version { get; set; } = 1;

    public override string GetConfigFileName() => "unnamed-probe.json";

    protected override string? GetConfigFilePath() => null;

    public bool Flag { get; set; }
}
