using FluentAssertions;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Every RFC 6455 rule the codec enforces, over a buffer.</summary>
public sealed class WebsocketCodecTests
{
    private static readonly byte[] MaskKey = [0x37, 0xFA, 0x21, 0x3D];

    private static WebsocketCodec NewCodec(int maxFrame = 64 * 1024, int maxMessage = 1024 * 1024, int maxFragments = 256)
        => new(new WebsocketCodecLimits(maxFrame, maxMessage, maxFragments));

    // Masked, like RFC 6455 requires.
    private static byte[] ClientFrame(byte opcode, bool final, byte[] payload, bool masked = true, int reserved = 0)
    {
        var header = payload.Length <= 125 ? 2 : payload.Length <= ushort.MaxValue ? 4 : 10;
        var frame = new byte[header + (masked ? 4 : 0) + payload.Length];

        frame[0] = (byte)((final ? 0x80 : 0x00) | (reserved << 4) | opcode);

        if (payload.Length <= 125)
        {
            frame[1] = (byte)payload.Length;
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame[1] = 126;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)payload.Length);
        }
        else
        {
            frame[1] = 127;
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2, 8), (ulong)payload.Length);
        }

        var at = header;

        if (masked)
        {
            frame[1] |= 0x80;
            MaskKey.CopyTo(frame, at);
            at += 4;

            for (var index = 0; index < payload.Length; index++)
                frame[at + index] = (byte)(payload[index] ^ MaskKey[index & 3]);
        }
        else
        {
            payload.CopyTo(frame, at);
        }

        return frame;
    }

    private static byte[] Text(string value)
        => Encoding.UTF8.GetBytes(value);

    private static WebsocketCodecResult ReadOne(WebsocketCodec codec, byte[] frame)
    {
        codec.Deliver(frame);
        codec.TryRead(out var result).Should().BeTrue();

        return result;
    }

    [Fact]
    public void TryRead_ATextFrame_ReassemblesTheMessage()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x1, true, Text("Hello")));

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        result.MessageKind.Should().Be(NoireWebsocketMessageKind.Text);
        Encoding.UTF8.GetString(result.Payload.Span).Should().Be("Hello");
    }

    [Fact]
    public void TryRead_ABinaryFrame_ReassemblesTheMessage()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x2, true, [1, 2, 3, 250]));

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        result.MessageKind.Should().Be(NoireWebsocketMessageKind.Binary);
        result.Payload.ToArray().Should().Equal(new byte[] { 1, 2, 3, 250 });
    }

    [Fact]
    public void TryRead_AnEmptyTextFrame_IsAMessage()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x1, true, []));

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        result.Payload.Length.Should().Be(0);
    }

    [Fact]
    public void TryRead_APingFrame_SurfacesWithItsPayload()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x9, true, Text("ping")));

        result.Event.Should().Be(WebsocketCodecEvent.Ping);
        Encoding.UTF8.GetString(result.Payload.Span).Should().Be("ping");
    }

    [Fact]
    public void TryRead_APongFrame_SurfacesWithItsPayload()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0xA, true, Text("pong")));

        result.Event.Should().Be(WebsocketCodecEvent.Pong);
        Encoding.UTF8.GetString(result.Payload.Span).Should().Be("pong");
    }

    [Fact]
    public void TryRead_ACloseFrame_CarriesTheCodeAndTheReason()
    {
        using var codec = NewCodec();
        var payload = new byte[2 + 5];

        BinaryPrimitives.WriteUInt16BigEndian(payload, 1000);
        Text("bye!!").CopyTo(payload, 2);

        var result = ReadOne(codec, ClientFrame(0x8, true, payload));

        result.Event.Should().Be(WebsocketCodecEvent.Close);
        result.Code.Should().Be(1000);
        result.Reason.Should().Be("bye!!");
    }

    [Fact]
    public void TryRead_ACloseFrameWithNoPayload_Reports1005()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x8, true, []));

        result.Event.Should().Be(WebsocketCodecEvent.Close);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.NoStatusReceived);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void TryRead_ACloseFrameOfOneByte_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x8, true, [0x03]));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_ACloseCodeThatNeverTravels_IsAProtocolError()
    {
        using var codec = NewCodec();
        var payload = new byte[2];

        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)NoireWebsocketCloseCode.AbnormalClosure);

        var result = ReadOne(codec, ClientFrame(0x8, true, payload));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_APrivateCloseCode_IsAccepted()
    {
        using var codec = NewCodec();
        var payload = new byte[2];

        BinaryPrimitives.WriteUInt16BigEndian(payload, 4001);

        var result = ReadOne(codec, ClientFrame(0x8, true, payload));

        result.Event.Should().Be(WebsocketCodecEvent.Close);
        result.Code.Should().Be(4001);
    }

    [Fact]
    public void TryRead_ACloseReasonThatIsNotUtf8_ClosesWithAnInvalidPayload()
    {
        using var codec = NewCodec();
        var payload = new byte[] { 0x03, 0xE8, 0xC3, 0x28 };

        var result = ReadOne(codec, ClientFrame(0x8, true, payload));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.InvalidPayload);
    }

    [Fact]
    public void TryRead_AnUnmaskedClientFrame_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x1, true, Text("Hello"), masked: false));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_AReservedBit_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x1, true, Text("Hello"), reserved: 0b100));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Theory]
    [InlineData((byte)0x3)]
    [InlineData((byte)0x7)]
    [InlineData((byte)0xB)]
    [InlineData((byte)0xF)]
    public void TryRead_AnUnknownOpcode_IsAProtocolError(byte opcode)
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(opcode, true, []));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_AFragmentedControlFrame_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x9, false, Text("ping")));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_AControlFrameOver125Bytes_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x9, true, new byte[126]));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_AControlFrameOfExactly125Bytes_IsAccepted()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x9, true, new byte[125]));

        result.Event.Should().Be(WebsocketCodecEvent.Ping);
        result.Payload.Length.Should().Be(125);
    }

    [Fact]
    public void TryRead_AFragmentedMessage_ReassemblesInOrder()
    {
        using var codec = NewCodec();

        codec.Deliver(ClientFrame(0x1, false, Text("Hel")));
        codec.Deliver(ClientFrame(0x0, false, Text("lo, ")));
        codec.Deliver(ClientFrame(0x0, true, Text("world")));

        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        Encoding.UTF8.GetString(result.Payload.Span).Should().Be("Hello, world");
    }

    [Fact]
    public void TryRead_AControlFrameBetweenFragments_IsDeliveredFirst()
    {
        using var codec = NewCodec();

        codec.Deliver(ClientFrame(0x2, false, [1, 2]));
        codec.Deliver(ClientFrame(0x9, true, Text("beat")));
        codec.Deliver(ClientFrame(0x0, true, [3, 4]));

        codec.TryRead(out var ping).Should().BeTrue();
        ping.Event.Should().Be(WebsocketCodecEvent.Ping);
        Encoding.UTF8.GetString(ping.Payload.Span).Should().Be("beat");

        codec.TryRead(out var message).Should().BeTrue();
        message.Event.Should().Be(WebsocketCodecEvent.Message);
        message.MessageKind.Should().Be(NoireWebsocketMessageKind.Binary);
        message.Payload.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void TryRead_AContinuationWithNothingInProgress_IsAProtocolError()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x0, true, Text("orphan")));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_ADataFrameWhileAMessageIsInProgress_IsAProtocolError()
    {
        using var codec = NewCodec();

        codec.Deliver(ClientFrame(0x1, false, Text("first")));
        codec.Deliver(ClientFrame(0x1, true, Text("second")));

        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_TextThatIsNotUtf8_ClosesWithAnInvalidPayload()
    {
        using var codec = NewCodec();

        var result = ReadOne(codec, ClientFrame(0x1, true, [0xC3, 0x28]));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.InvalidPayload);
    }

    [Fact]
    public void TryRead_TextSplitMidRuneAcrossFragments_ReassemblesRatherThanFailing()
    {
        using var codec = NewCodec();
        var rune = Encoding.UTF8.GetBytes("é");

        codec.Deliver(ClientFrame(0x1, false, [rune[0]]));
        codec.Deliver(ClientFrame(0x0, true, [rune[1]]));

        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        Encoding.UTF8.GetString(result.Payload.Span).Should().Be("é");
    }

    [Fact]
    public void TryRead_AFrameLargerThanTheCap_IsRefusedBeforeThePayloadArrives()
    {
        using var codec = NewCodec(maxFrame: 1024);
        var header = new byte[10];

        header[0] = 0x82;
        header[1] = 0xFF;
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(2, 8), 1_000_000);

        codec.Deliver(header);
        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
        codec.Buffered.Should().Be(10, "nothing was consumed; the listener never reserved the declared length");
    }

    [Fact]
    public void TryRead_ALengthWithItsHighBitSet_IsAProtocolError()
    {
        using var codec = NewCodec();
        var header = new byte[10];

        header[0] = 0x82;
        header[1] = 0xFF;
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(2, 8), 0x8000_0000_0000_0001UL);

        codec.Deliver(header);
        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.ProtocolError);
    }

    [Fact]
    public void TryRead_AMessageLargerThanTheCap_ClosesWithMessageTooBig()
    {
        using var codec = NewCodec(maxFrame: 4096, maxMessage: 4096);

        codec.Deliver(ClientFrame(0x2, false, new byte[3000]));
        codec.Deliver(ClientFrame(0x0, true, new byte[3000]));

        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
    }

    [Fact]
    public void TryRead_ASingleFrameOverTheMessageCap_ClosesWithMessageTooBig()
    {
        using var codec = NewCodec(maxFrame: 8192, maxMessage: 1024);

        var result = ReadOne(codec, ClientFrame(0x2, true, new byte[2048]));

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
    }

    [Fact]
    public void TryRead_MoreFragmentsThanTheCap_ClosesWithMessageTooBig()
    {
        using var codec = NewCodec(maxFragments: 4);

        codec.Deliver(ClientFrame(0x2, false, [0]));

        for (var index = 0; index < 5; index++)
            codec.Deliver(ClientFrame(0x0, false, [1]));

        codec.TryRead(out var result).Should().BeTrue();

        result.Event.Should().Be(WebsocketCodecEvent.Failure);
        result.Code.Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
    }

    [Theory]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(70000)]
    public void TryRead_EveryLengthEncoding_RoundTrips(int size)
    {
        using var codec = NewCodec(maxFrame: 128 * 1024);
        var payload = new byte[size];

        for (var index = 0; index < size; index++)
            payload[index] = (byte)index;

        var result = ReadOne(codec, ClientFrame(0x2, true, payload));

        result.Event.Should().Be(WebsocketCodecEvent.Message);
        result.Payload.ToArray().Should().Equal((System.Collections.Generic.IEnumerable<byte>)payload);
    }

    [Fact]
    public void TryRead_AFrameSplitAtEveryByteBoundary_StillReassembles()
    {
        var frame = ClientFrame(0x1, true, Text(new string('x', 200)));

        for (var split = 0; split <= frame.Length; split++)
        {
            using var codec = NewCodec();

            codec.Deliver(frame.AsSpan(0, split));

            if (split < frame.Length)
                codec.TryRead(out _).Should().BeFalse("the frame is incomplete after " + split + " bytes");

            codec.Deliver(frame.AsSpan(split));
            codec.TryRead(out var result).Should().BeTrue("the frame completed after the second delivery at " + split);

            result.Event.Should().Be(WebsocketCodecEvent.Message);
            Encoding.UTF8.GetString(result.Payload.Span).Should().Be(new string('x', 200));
        }
    }

    [Fact]
    public void TryRead_TwoMessagesInOneDelivery_BothSurfaceInOrder()
    {
        using var codec = NewCodec();
        var first = ClientFrame(0x1, true, Text("one"));
        var second = ClientFrame(0x1, true, Text("two"));
        var both = new byte[first.Length + second.Length];

        first.CopyTo(both, 0);
        second.CopyTo(both, first.Length);

        codec.Deliver(both);

        codec.TryRead(out var a).Should().BeTrue();
        Encoding.UTF8.GetString(a.Payload.Span).Should().Be("one");

        codec.TryRead(out var b).Should().BeTrue();
        Encoding.UTF8.GetString(b.Payload.Span).Should().Be("two");

        codec.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_AfterAFailure_StopsReading()
    {
        using var codec = NewCodec();

        ReadOne(codec, ClientFrame(0x1, true, Text("Hello"), masked: false)).Event.Should().Be(WebsocketCodecEvent.Failure);

        codec.Faulted.Should().BeTrue();
        codec.Deliver(ClientFrame(0x1, true, Text("Hello")));
        codec.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Payload_PointsIntoTheCodecsOwnBuffer_AcrossMessages()
    {
        using var codec = NewCodec();

        var first = ReadOne(codec, ClientFrame(0x1, true, Text("one")));
        MemoryMarshal.TryGetArray(first.Payload, out var firstSegment).Should().BeTrue();

        var second = ReadOne(codec, ClientFrame(0x1, true, Text("two")));
        MemoryMarshal.TryGetArray(second.Payload, out var secondSegment).Should().BeTrue();

        secondSegment.Array.Should().BeSameAs(firstSegment.Array,
            "the payload is a slice of one reused buffer; it is valid only for the duration of the callback");
    }

    [Fact]
    public void Write_EveryFrameTheServerSends_IsUnmasked()
    {
        byte[][] frames =
        [
            WebsocketFrameWriter.Message(NoireWebsocketMessageKind.Text, Text("hello")),
            WebsocketFrameWriter.Message(NoireWebsocketMessageKind.Binary, [1, 2, 3]),
            WebsocketFrameWriter.Ping(Text("beat")),
            WebsocketFrameWriter.Pong(Text("beat")),
            WebsocketFrameWriter.Close(1000, "bye"),
        ];

        foreach (var frame in frames)
            (frame[1] & 0x80).Should().Be(0, "a server never masks, and a peer that receives a masked frame must fail the connection");
    }

    [Fact]
    public void Write_AMessage_CarriesFinAndTheOpcode()
    {
        var frame = WebsocketFrameWriter.Message(NoireWebsocketMessageKind.Text, Text("hello"));

        frame[0].Should().Be(0x81);
        frame[1].Should().Be(5);
        Encoding.UTF8.GetString(frame.AsSpan(2)).Should().Be("hello");
    }

    [Theory]
    [InlineData(125, 2)]
    [InlineData(126, 4)]
    [InlineData(70000, 10)]
    public void Write_EveryLengthEncoding_UsesTheSmallestHeader(int size, int header)
    {
        var frame = WebsocketFrameWriter.Message(NoireWebsocketMessageKind.Binary, new byte[size]);

        frame.Length.Should().Be(header + size);
        WebsocketFrameWriter.HeaderSize(size).Should().Be(header);
    }

    [Fact]
    public void Write_AFragment_ClearsFin()
    {
        var frame = WebsocketFrameWriter.Frame(WebsocketFrameWriter.OpcodeText, false, Text("part"));

        (frame[0] & 0x80).Should().Be(0);
        (frame[0] & 0x0F).Should().Be(WebsocketFrameWriter.OpcodeText);
    }

    [Fact]
    public void Close_ALongReason_IsCutOnARuneBoundary()
    {
        var frame = WebsocketFrameWriter.Close(1000, new string('é', 100));
        var payload = frame.AsSpan(2);

        payload.Length.Should().Be(2 + 122, "a two-byte rune cannot start at byte 123 of the reason");
        Encoding.UTF8.GetString(payload.Slice(2)).Should().Be(new string('é', 61));
    }

    [Fact]
    public void Close_ACodeThatNeverTravels_CarriesNoPayload()
    {
        var frame = WebsocketFrameWriter.Close((int)NoireWebsocketCloseCode.AbnormalClosure, "dropped");

        frame[1].Should().Be(0);
        frame.Length.Should().Be(2);
    }

    [Fact]
    public void Close_ACodeAndReason_EncodeBigEndian()
    {
        var frame = WebsocketFrameWriter.Close(1009, "too big");

        frame[0].Should().Be(0x88);
        BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2)).Should().Be(1009);
        Encoding.UTF8.GetString(frame.AsSpan(4)).Should().Be("too big");
    }

    [Fact]
    public void Write_AControlFrameOverTheCap_Throws()
    {
        var write = () => WebsocketFrameWriter.Ping(new byte[126]);

        write.Should().Throw<ArgumentOutOfRangeException>();
    }
}
