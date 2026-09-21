using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace NoireLib.Websocket.Internal;

internal enum WebsocketCodecEvent
{
    None,
    Message,
    Ping,
    Pong,
    Close,
    Failure,
}

// On Close, Code and Reason are the peer's. On Failure they are what the connection must close with.
internal readonly struct WebsocketCodecResult
{
    private WebsocketCodecResult(WebsocketCodecEvent codecEvent, NoireWebsocketMessageKind kind, ReadOnlyMemory<byte> payload, int code, string? reason)
    {
        Event = codecEvent;
        MessageKind = kind;
        Payload = payload;
        Code = code;
        Reason = reason;
    }

    public WebsocketCodecEvent Event { get; }

    public NoireWebsocketMessageKind MessageKind { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public int Code { get; }

    public string? Reason { get; }

    public static WebsocketCodecResult Message(NoireWebsocketMessageKind kind, ReadOnlyMemory<byte> payload)
        => new(WebsocketCodecEvent.Message, kind, payload, 0, null);

    public static WebsocketCodecResult Control(WebsocketCodecEvent codecEvent, ReadOnlyMemory<byte> payload)
        => new(codecEvent, NoireWebsocketMessageKind.Binary, payload, 0, null);

    public static WebsocketCodecResult Close(int code, string? reason)
        => new(WebsocketCodecEvent.Close, NoireWebsocketMessageKind.Binary, ReadOnlyMemory<byte>.Empty, code, reason);

    public static WebsocketCodecResult Failure(NoireWebsocketCloseCode code, string reason)
        => new(WebsocketCodecEvent.Failure, NoireWebsocketMessageKind.Binary, ReadOnlyMemory<byte>.Empty, (int)code, reason);
}

internal readonly record struct WebsocketCodecLimits(int MaxFrameBytes, int MaxMessageBytes, int MaxFragments);

// RFC 6455 server-side frame decoding. Pure and synchronous, with no socket.
// A payload points into the codec's rented buffer and is valid until the next call.
internal sealed class WebsocketCodec : IDisposable
{
    private const int InitialBuffer = 4096;

    private readonly WebsocketCodecLimits limits;

    private byte[] buffer;
    private int start;
    private int end;

    private byte[] assembly = [];
    private int assembled;
    private int fragments;
    private byte messageOpcode;

    private bool faulted;
    private bool disposed;

    public WebsocketCodec(WebsocketCodecLimits limits)
    {
        this.limits = limits;
        buffer = ArrayPool<byte>.Shared.Rent(InitialBuffer);
    }

    public bool Faulted => faulted;

    public int Buffered => end - start;

    public void Deliver(ReadOnlySpan<byte> bytes)
    {
        if (disposed || bytes.IsEmpty)
            return;

        Reserve(bytes.Length);
        bytes.CopyTo(buffer.AsSpan(end));
        end += bytes.Length;
    }

    // A fragment only adding to the message in progress never surfaces. A caller drains with a plain while.
    public bool TryRead(out WebsocketCodecResult result)
    {
        result = default;

        while (!faulted && !disposed)
        {
            if (!TryReadFrame(out result))
                return false;

            if (result.Event != WebsocketCodecEvent.None)
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = [];

        if (assembly.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(assembly);
            assembly = [];
        }
    }

    private bool TryReadFrame(out WebsocketCodecResult result)
    {
        result = default;

        var available = end - start;

        if (available < 2)
            return false;

        var first = buffer[start];
        var second = buffer[start + 1];
        var final = (first & 0x80) != 0;
        var opcode = (byte)(first & 0x0F);
        var masked = (second & 0x80) != 0;
        var control = WebsocketFrameWriter.IsControl(opcode);

        if ((first & 0x70) != 0)
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A reserved bit is set on a frame and no extension was negotiated.");

        if (!IsKnownOpcode(opcode))
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "Opcode 0x" + opcode.ToString("X") + " is not one this connection speaks.");

        long length = second & 0x7F;
        var header = 2;

        if (control)
        {
            if (!final)
                return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A control frame is never fragmented.");

            // An extended length encoding is already over the control cap.
            if (length > WebsocketFrameWriter.MaxControlPayload)
                return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A control frame carries at most " + WebsocketFrameWriter.MaxControlPayload + " bytes.");
        }
        else if (length == 126)
        {
            if (available < 4)
                return false;

            length = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(start + 2, 2));
            header = 4;
        }
        else if (length == 127)
        {
            if (available < 10)
                return false;

            var extended = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(start + 2, 8));

            if ((extended & 0x8000000000000000UL) != 0)
                return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A frame length with its high bit set is not a length.");

            length = (long)extended;
            header = 10;
        }

        if (!masked)
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A frame arrived from a client with no mask.");

        // Refused off the length field. Eight bytes cannot make the listener reserve a gigabyte.
        if (!control && length > limits.MaxFrameBytes)
            return Fail(out result, NoireWebsocketCloseCode.MessageTooBig, "A frame of " + length + " bytes passed the " + limits.MaxFrameBytes + " byte frame cap.");

        var maskAt = start + header;
        var payloadAt = maskAt + 4;
        var frameBytes = (long)header + 4 + length;

        if (available < frameBytes)
        {
            Reserve((int)(frameBytes - available));
            return false;
        }

        var payloadLength = (int)length;

        Unmask(buffer.AsSpan(payloadAt, payloadLength), buffer.AsSpan(maskAt, 4));

        start += (int)frameBytes;

        if (control)
            return ReadControl(opcode, payloadAt, payloadLength, out result);

        return ReadData(opcode, final, payloadAt, payloadLength, out result);
    }

    private bool ReadControl(byte opcode, int payloadStart, int payloadLength, out WebsocketCodecResult result)
    {
        if (opcode == WebsocketFrameWriter.OpcodePing)
        {
            result = WebsocketCodecResult.Control(WebsocketCodecEvent.Ping, buffer.AsMemory(payloadStart, payloadLength));
            return true;
        }

        if (opcode == WebsocketFrameWriter.OpcodePong)
        {
            result = WebsocketCodecResult.Control(WebsocketCodecEvent.Pong, buffer.AsMemory(payloadStart, payloadLength));
            return true;
        }

        if (payloadLength == 0)
        {
            // A close frame with no payload reports 1005. 1005 is never on the wire.
            result = WebsocketCodecResult.Close((int)NoireWebsocketCloseCode.NoStatusReceived, null);
            return true;
        }

        if (payloadLength == 1)
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A close frame carries either no payload or a two-byte code.");

        var code = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(payloadStart, 2));

        if (!IsCloseCodeOnTheWire(code))
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "Close code " + code + " is not one a peer may send.");

        var reasonBytes = buffer.AsSpan(payloadStart + 2, payloadLength - 2);

        if (!Utf8.IsValid(reasonBytes))
            return Fail(out result, NoireWebsocketCloseCode.InvalidPayload, "A close reason was not valid UTF-8.");

        result = WebsocketCodecResult.Close(code, reasonBytes.Length == 0 ? null : Encoding.UTF8.GetString(reasonBytes));
        return true;
    }

    private bool ReadData(byte opcode, bool final, int payloadStart, int payloadLength, out WebsocketCodecResult result)
    {
        if (opcode == WebsocketFrameWriter.OpcodeContinuation)
        {
            if (messageOpcode == 0)
                return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A continuation frame arrived with no message in progress.");
        }
        else if (messageOpcode != 0)
        {
            return Fail(out result, NoireWebsocketCloseCode.ProtocolError, "A data frame arrived while a fragmented message was still in progress.");
        }
        else if (final)
        {
            if (payloadLength > limits.MaxMessageBytes)
                return Fail(out result, NoireWebsocketCloseCode.MessageTooBig, "A message passed the " + limits.MaxMessageBytes + " byte cap.");

            // Delivered out of the receive buffer with no copy.
            return Complete(opcode, payloadStart, payloadLength, out result);
        }
        else
        {
            messageOpcode = opcode;
            assembled = 0;
            fragments = 0;
        }

        if (assembled + (long)payloadLength > limits.MaxMessageBytes)
        {
            Reset();
            return Fail(out result, NoireWebsocketCloseCode.MessageTooBig, "A message passed the " + limits.MaxMessageBytes + " byte cap.");
        }

        if (++fragments > limits.MaxFragments)
        {
            Reset();
            return Fail(out result, NoireWebsocketCloseCode.MessageTooBig, "A message arrived in more than " + limits.MaxFragments + " fragments.");
        }

        Append(payloadStart, payloadLength);

        if (!final)
        {
            result = default;
            return true;
        }

        var kind = messageOpcode;
        var length = assembled;

        Reset();

        return Complete(kind, -1, length, out result);
    }

    // A negative payloadStart means the reassembly buffer, a non-negative one a slice of the receive buffer.
    private bool Complete(byte opcode, int payloadStart, int payloadLength, out WebsocketCodecResult result)
    {
        var memory = payloadStart < 0 ? assembly.AsMemory(0, payloadLength) : buffer.AsMemory(payloadStart, payloadLength);

        if (opcode == WebsocketFrameWriter.OpcodeText && !Utf8.IsValid(memory.Span))
            return Fail(out result, NoireWebsocketCloseCode.InvalidPayload, "A text message was not valid UTF-8.");

        var kind = opcode == WebsocketFrameWriter.OpcodeText ? NoireWebsocketMessageKind.Text : NoireWebsocketMessageKind.Binary;

        result = WebsocketCodecResult.Message(kind, memory);
        return true;
    }

    private void Append(int payloadStart, int payloadLength)
    {
        if (payloadLength == 0)
            return;

        if (assembly.Length < assembled + payloadLength)
        {
            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(assembled + payloadLength, InitialBuffer));
            assembly.AsSpan(0, assembled).CopyTo(grown);

            if (assembly.Length > 0)
                ArrayPool<byte>.Shared.Return(assembly);

            assembly = grown;
        }

        buffer.AsSpan(payloadStart, payloadLength).CopyTo(assembly.AsSpan(assembled));
        assembled += payloadLength;
    }

    private void Reset()
    {
        messageOpcode = 0;
        fragments = 0;
    }

    private bool Fail(out WebsocketCodecResult result, NoireWebsocketCloseCode code, string reason)
    {
        faulted = true;
        result = WebsocketCodecResult.Failure(code, reason);

        return true;
    }

    // Unread bytes move to the front before growing. Small frames never grow the buffer.
    private void Reserve(int count)
    {
        if (end + count <= buffer.Length)
            return;

        var remaining = end - start;

        if (start > 0 && remaining + count <= buffer.Length)
        {
            buffer.AsSpan(start, remaining).CopyTo(buffer);
            start = 0;
            end = remaining;

            return;
        }

        var grown = ArrayPool<byte>.Shared.Rent(remaining + count);
        buffer.AsSpan(start, remaining).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(buffer);

        buffer = grown;
        start = 0;
        end = remaining;
    }

    private static void Unmask(Span<byte> payload, ReadOnlySpan<byte> key)
    {
        for (var index = 0; index < payload.Length; index++)
            payload[index] ^= key[index & 3];
    }

    private static bool IsKnownOpcode(byte opcode)
        => opcode is WebsocketFrameWriter.OpcodeContinuation or WebsocketFrameWriter.OpcodeText or WebsocketFrameWriter.OpcodeBinary
            or WebsocketFrameWriter.OpcodeClose or WebsocketFrameWriter.OpcodePing or WebsocketFrameWriter.OpcodePong;

    // 1004, 1005, 1006 and 1015 never travel on a connection. 1016 to 2999 are reserved.
    private static bool IsCloseCodeOnTheWire(int code)
        => code is (>= 1000 and <= 1003) or (>= 1007 and <= 1014) or (>= 3000 and <= 4999);
}
