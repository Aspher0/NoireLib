using FluentAssertions;
using NoireLib.Remote;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// What an instance tags itself with, and how far those tags travel. A tag is the only handle a caller has on an
/// instance whose character is not logged in yet.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteSelfTests : NoireRemoteTestBase
{
    [Fact]
    public void ATagReachesTheDiscoveryRecord()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        NoireRemote.Self.Set("role", "tank");

        ReadRecord().Metadata["role"].Should().Be("tank");
    }

    [Fact]
    public void ATagSurvivesTheNextRecordWrite()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        NoireRemote.Self.Set("role", "tank");
        NoireRemote.Self.Set("slot", "2");

        var record = ReadRecord();

        record.Metadata["role"].Should().Be("tank");
        record.Metadata["slot"].Should().Be("2");
    }

    [Fact]
    public void RemovingATagRemovesItFromTheRecord()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        NoireRemote.Self.Set("role", "tank");
        NoireRemote.Self.Remove("role");

        ReadRecord().Metadata.Should().NotContainKey("role");
    }

    [Fact]
    public void SettingTheValueItAlreadyHoldsNotifiesNobody()
    {
        var raised = 0;
        NoireRemote.Self.Changed += _ => raised++;

        NoireRemote.Self.Set("role", "tank");
        NoireRemote.Self.Set("role", "tank");

        raised.Should().Be(1, "a Set in a per-frame handler has to cost nothing when nothing changed");
    }

    [Fact]
    public void ATagIsReadableBackByName()
    {
        NoireRemote.Self.Set("role", "tank");

        NoireRemote.Self["role"].Should().Be("tank");
        NoireRemote.Self["ROLE"].Should().Be("tank", "a tag name is matched ignoring case");
        NoireRemote.Self["slot"].Should().BeNull();
    }

    private NoireRemoteInstanceRecord ReadRecord()
        => NoireRemoteDirectory.ReadAll(RegistryDirectory).Single(record => record.Instance == NoireRemote.InstanceId);
}
