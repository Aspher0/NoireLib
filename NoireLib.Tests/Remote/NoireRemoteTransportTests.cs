using FluentAssertions;
using NoireLib.Remote;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Which wires a member is reachable on. Serving a socket costs no second listener. A class serves both unless
/// it says otherwise. What <c>Transports</c> buys is a smaller surface, never a cheaper one.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteTransportTests : NoireRemoteTestBase
{
    [NoireRemoteClass("Mixed")]
    public static class MixedProbe
    {
        [NoireRemote]
        public static int Value() => 3;

        [NoireRemote(Transports = NoireRemoteTransport.Websocket)]
        public static int Tick() => 4;
    }

    [NoireRemoteClass("HttpOnly", Transports = NoireRemoteTransport.Http)]
    public static class HttpOnlyProbe
    {
        [NoireRemote]
        public static int Value() => 3;
    }

    [NoireRemoteClass("SocketOnly", Transports = NoireRemoteTransport.Websocket)]
    public static class SocketOnlyProbe
    {
        [NoireRemote]
        public static int Value() => 3;
    }

    [Fact]
    public void AClassServesBothWiresUnlessItSaysOtherwise()
    {
        using var publication = NoireRemote.PublishType(typeof(MixedProbe));

        publication.Members.Single(member => member.Name == "Value").Transports
            .Should().Be(NoireRemoteTransport.All);
    }

    [Fact]
    public void AMemberNarrowsItsClass()
    {
        using var publication = NoireRemote.PublishType(typeof(MixedProbe));

        publication.Members.Single(member => member.Name == "Tick").Transports
            .Should().Be(NoireRemoteTransport.Websocket);
    }

    [Fact]
    public void AClassOnHttpKeepsItsMembersOnHttp()
    {
        using var publication = NoireRemote.PublishType(typeof(HttpOnlyProbe));

        publication.Members.Single().Transports.Should().Be(NoireRemoteTransport.Http);
    }

    [Fact]
    public void AMemberOnTheSocketAloneIsNotServedOverHttp()
    {
        using var publication = NoireRemote.PublishType(typeof(SocketOnlyProbe));

        publication.Members.Single().Transports.Should().Be(NoireRemoteTransport.Websocket,
            "the router answers it like a member that does not exist, which from outside is the same fact");
    }

    [Fact]
    public void TheManifestNamesEachMembersWires()
    {
        using var publication = NoireRemote.PublishType(typeof(MixedProbe));

        var manifest = NoireRemote.Manifest();
        var members = manifest.Endpoints.Single(endpoint => endpoint.Name == "Mixed").Members;

        members.Single(member => member.Name == "Value").Transports.Should().BeEquivalentTo(["http", "ws"]);
        members.Single(member => member.Name == "Tick").Transports.Should().BeEquivalentTo(["ws"]);
    }
}
