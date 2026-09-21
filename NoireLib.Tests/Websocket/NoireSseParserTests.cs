using FluentAssertions;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the server-sent-events wire format: which line dispatches an event, how a field is read, what a comment is,
/// and what survives a read landing in the middle of one.
/// </summary>
public sealed class NoireSseParserTests
{
    public static TheoryData<string, string[]> Streams() => new()
    {
        { "data: hello\n\n", ["event|message|hello|"] },
        { "data:hello\n\n", ["event|message|hello|"] },
        { "event: ping\ndata: 1\n\n", ["event|ping|1|"] },
        { "data: a\ndata: b\n\n", ["event|message|a\nb|"] },
        { "data: a\ndata:\ndata: b\n\n", ["event|message|a\n\nb|"] },
        { "data:\n\n", ["event|message||"] },
        { "data\n\n", ["event|message||"] },
        { ":ping\n", ["comment|ping"] },
        { ": keep-alive\n", ["comment|keep-alive"] },
        { ":\n", ["comment|"] },
        { "data: hello\r\n\r\n", ["event|message|hello|"] },
        { "data: hello\r\r", ["event|message|hello|"] },
        { "id: 7\ndata: x\n\n", ["event|message|x|7"] },
        { "id: 7\ndata: a\n\ndata: b\n\n", ["event|message|a|7", "event|message|b|7"] },
        { "unknown: value\ndata: x\n\n", ["event|message|x|"] },
        { "event: ping\n\ndata: x\n\n", ["event|message|x|"] },
        { "data: x\n", [] },
        { "﻿data: x\n\n", ["event|message|x|"] },
        { "﻿data: ﻿x\n\n", ["event|message|﻿x|"] },
    };

    [Theory]
    [MemberData(nameof(Streams))]
    public void Feed_ReadsTheStream_AsTheFormatDefinesIt(string stream, string[] expected)
    {
        Describe(stream).Should().Equal(expected);
    }

    [Fact]
    public void Feed_DispatchesAnEvent_SplitAcrossTwoReads()
    {
        Describe("event: pi", "ng\ndata: hel", "lo\n", "\n").Should().Equal(["event|ping|hello|"]);
    }

    [Fact]
    public void Feed_TreatsACarriageReturnEndingAReadAsOneLineBreak()
    {
        Describe("data: x\r", "\n\n").Should().Equal(["event|message|x|"],
            "a line ending split across two reads counts as one break only");
    }

    [Fact]
    public void Feed_HoldsACharacterSplitAcrossTwoReads()
    {
        var bytes = Encoding.UTF8.GetBytes("data: café\n\n");
        var parser = new SseParser(0);

        parser.Feed(bytes.AsSpan(0, 10)).Should().BeEmpty();

        var dispatched = new List<string>();

        foreach (var item in parser.Feed(bytes.AsSpan(10)))
            dispatched.Add(item.Event!.Data);

        dispatched.Should().Equal(["café"]);
    }

    [Fact]
    public void Feed_KeepsTheLastEventId_AfterAnEventWithoutOne()
    {
        var parser = new SseParser(0) { LastEventId = "0" };

        Drain(parser, "id: 9\ndata: a\n\n");
        Drain(parser, "data: b\n\n");

        parser.LastEventId.Should().Be("9", "the id is what a reconnect resumes from; it outlives its own event");
    }

    [Fact]
    public void Feed_RefusesAnIdCarryingANullCharacter()
    {
        var parser = new SseParser(0);

        Drain(parser, "id: 5\n\nid: a\0b\ndata: x\n\n");

        parser.LastEventId.Should().Be("5");
    }

    [Fact]
    public void Feed_ReadsTheServerRetry_OnlyFromDigits()
    {
        var parser = new SseParser(0);

        Drain(parser, "retry: 4500\n");
        parser.ServerRetry.Should().Be(TimeSpan.FromMilliseconds(4500));

        Drain(parser, "retry: soon\n");
        parser.ServerRetry.Should().Be(TimeSpan.FromMilliseconds(4500), "a value that is not digits leaves the last one standing");
    }

    [Fact]
    public void Feed_ThrowsWhenOneEventPassesTheSizeCap()
    {
        var parser = new SseParser(8);

        var act = () => Drain(parser, "data: 123456789\n\n");

        act.Should().Throw<NoireSocketMessageTooLargeException>().Which.Limit.Should().Be(8);
    }

    private static List<string> Describe(params string[] chunks)
    {
        var parser = new SseParser(0);
        var described = new List<string>();

        foreach (var chunk in chunks)
        {
            foreach (var item in parser.Feed(Encoding.UTF8.GetBytes(chunk)))
            {
                described.Add(item.Event != null
                    ? "event|" + item.Event.Type + "|" + item.Event.Data + "|" + (item.Event.Id ?? string.Empty)
                    : "comment|" + item.Comment);
            }
        }

        return described;
    }

    private static void Drain(SseParser parser, string chunk)
        => _ = parser.Feed(Encoding.UTF8.GetBytes(chunk));
}
