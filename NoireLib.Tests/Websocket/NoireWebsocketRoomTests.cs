using FluentAssertions;
using NoireLib.Websocket;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Rooms are membership and nothing else. One exists while it has a member and goes when the last one leaves or
/// closes. The three bounds refuse the connection that would pass them, never the one already at them, and a
/// broadcast narrowed by room, by predicate or by an excluded sender reaches exactly the peers it names.
/// </summary>
public sealed class NoireWebsocketRoomTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void Join_TheSameNameTwice_GivesTheSameRoom()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var first = harness.Connect(chat);
        using var second = harness.Connect(chat);

        var lobby = first.Connection.Join("lobby");
        var again = second.Connection.Join("LOBBY");

        again.Should().BeSameAs(lobby, "a room name matches ignoring case");
        lobby.Count.Should().Be(2);
        chat.Rooms.Should().ContainSingle();
    }

    [Fact]
    public void Join_Twice_CountsTheConnectionOnce()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        peer.Connection.Join("lobby");
        peer.Connection.Join("lobby");

        chat.GetRoom("lobby")!.Count.Should().Be(1);
        peer.Connection.Rooms.Should().ContainSingle();
    }

    [Fact]
    public void Leave_ByTheLastMember_TakesTheRoomWithIt()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var first = harness.Connect(chat);
        using var second = harness.Connect(chat);

        first.Connection.Join("lobby");
        second.Connection.Join("lobby");

        first.Connection.Leave("lobby").Should().BeTrue();
        chat.GetRoom("lobby").Should().NotBeNull();

        second.Connection.Leave("lobby").Should().BeTrue();
        chat.GetRoom("lobby").Should().BeNull("an empty room is not a room");
        chat.Rooms.Should().BeEmpty();
    }

    [Fact]
    public void Leave_ARoomTheConnectionIsNotIn_IsFalse()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        peer.Connection.Leave("lobby").Should().BeFalse();
    }

    [Fact]
    public async Task AConnectionThatCloses_IsDroppedFromEveryRoom()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var staying = harness.Connect(chat);
        using var leaving = harness.Connect(chat);

        staying.Connection.Join("lobby");
        leaving.Connection.Join("lobby");
        leaving.Connection.Join("duty");

        await leaving.SendAsync(WebsocketTestFrames.Close(1000, null));

        (await NoireWebsocketServerTests.WaitUntilAsync(() => leaving.Connection.State == NoireSocketState.Disconnected)).Should().BeTrue();
        (await NoireWebsocketServerTests.WaitUntilAsync(() => chat.Rooms.Count == 1)).Should().BeTrue();

        chat.GetRoom("duty").Should().BeNull("its only member closed");
        chat.GetRoom("lobby")!.Count.Should().Be(1);
        chat.GetRoom("lobby")!.Contains(staying.Connection).Should().BeTrue();
    }

    [Fact]
    public void MaxRoomsPerConnection_RefusesTheRoomThatWouldPassIt()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxRoomsPerConnection = 3;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        peer.Connection.Join("a");
        peer.Connection.Join("b");
        peer.Connection.Join("c");

        var fourth = () => peer.Connection.Join("d");

        fourth.Should().Throw<NoireSocketException>().WithMessage("*as many rooms as it may join (3)*");
        peer.Connection.Rooms.Should().HaveCount(3);
    }

    [Fact]
    public void MaxRoomsPerConnection_AlsoBoundsJoiningARoomThatAlreadyExists()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxRoomsPerConnection = 1;

        var chat = harness.Sockets.Publish("chat");
        using var opener = harness.Connect(chat);
        using var peer = harness.Connect(chat);

        opener.Connection.Join("lobby");
        peer.Connection.Join("duty");

        var second = () => peer.Connection.Join("lobby");

        second.Should().Throw<NoireSocketException>();
        chat.GetRoom("lobby")!.Count.Should().Be(1);
    }

    [Fact]
    public void MaxRooms_RefusesTheRoomThatWouldPassIt()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxRooms = 2;
        harness.Sockets.Options.MaxRoomsPerConnection = 16;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        peer.Connection.Join("a");
        peer.Connection.Join("b");

        var third = () => peer.Connection.Join("c");

        third.Should().Throw<NoireSocketException>().WithMessage("*as many rooms as it accepts (2)*");
        chat.Rooms.Should().HaveCount(2);
    }

    [Fact]
    public void MaxRoomNameLength_AcceptsANameAtTheCapAndRefusesOneCharacterMore()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxRoomNameLength = 8;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        peer.Connection.Join(new string('r', 8)).Name.Should().HaveLength(8);

        var longer = () => peer.Connection.Join(new string('r', 9));

        longer.Should().Throw<ArgumentException>().WithMessage("*at most 8 characters*");
    }

    [Fact]
    public void Join_ABlankName_Throws()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        var blank = () => peer.Connection.Join("   ");

        blank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Broadcast_ToARoom_ReachesItsMembersOnly()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var inside = harness.Connect(chat);
        using var outside = harness.Connect(chat);

        inside.Connection.Join("lobby");

        chat.Broadcast("hello", room: "lobby");

        (await ReadTextAsync(inside)).Should().Be("hello");
        (await outside.TryReadFrameAsync(Quiet)).Should().BeNull("it is not in the room");
    }

    [Fact]
    public async Task Broadcast_ExceptTheSender_ReachesEveryOtherPeer()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var sender = harness.Connect(chat);
        using var listener = harness.Connect(chat);

        chat.Broadcast("said", except: sender.Connection);

        (await ReadTextAsync(listener)).Should().Be("said");
        (await sender.TryReadFrameAsync(Quiet)).Should().BeNull("a sender does not receive its own message");
    }

    [Fact]
    public async Task Broadcast_WithAPredicate_ReachesOnlyThePeersThatPassIt()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var tagged = harness.Connect(chat);
        using var untagged = harness.Connect(chat);

        tagged.Connection.Items["role"] = "healer";

        chat.Broadcast("triage", where: client => client.Items.TryGetValue("role", out var role) && Equals(role, "healer"));

        (await ReadTextAsync(tagged)).Should().Be("triage");
        (await untagged.TryReadFrameAsync(Quiet)).Should().BeNull("the predicate is the whole filter on its own");
    }

    [Fact]
    public async Task RoomBroadcast_ExceptTheSender_LeavesThatOneOut()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var sender = harness.Connect(chat);
        using var listener = harness.Connect(chat);

        var lobby = sender.Connection.Join("lobby");
        listener.Connection.Join("lobby");

        lobby.Broadcast("in the room", except: sender.Connection);

        (await ReadTextAsync(listener)).Should().Be("in the room");
        (await sender.TryReadFrameAsync(Quiet)).Should().BeNull();
    }

    [Fact]
    public async Task BroadcastAsync_ToARoomNobodyIsIn_Completes()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        await chat.BroadcastAsync("nobody", room: "empty", cancellationToken: TestContext.Current.CancellationToken);

        (await peer.TryReadFrameAsync(Quiet)).Should().BeNull();
    }

    [Fact]
    public void ARoom_ExposesNoBulkRemoval()
    {
        typeof(NoireWebsocketRoom).GetMethods()
            .Select(method => method.Name)
            .Should().NotContain("Clear", "emptying a room without raising anything would leave the application's view of it wrong");
    }

    private static async Task<string> ReadTextAsync(WebsocketTestPeer peer)
    {
        var frame = await peer.ReadFrameAsync();

        (frame[0] & 0x0F).Should().Be(0x1);

        return Encoding.UTF8.GetString(frame.AsSpan(2));
    }
}
