using System;
using System.Buffers.Binary;
using System.Text;

namespace NoireLib.Websocket.Internal;

// RFC 6455 server-side encoding. No path sets the MASK bit: a masked server frame must fail the connection.
internal static class WebsocketFrameWriter
{
    public const byte OpcodeContinuation = 0x0;

    public const byte OpcodeText = 0x1;

    public const byte OpcodeBinary = 0x2;

    public const byte OpcodeClose = 0x8;

    public const byte OpcodePing = 0x9;

    public const byte OpcodePong = 0xA;

    public const int MaxControlPayload = 125;

    // Two of the 125 control bytes are the close code.
    public const int MaxCloseReasonBytes = 123;

    public static int HeaderSize(int payloadLength)
        => payloadLength <= 125 ? 2 : payloadLength <= ushort.MaxValue ? 4 : 10;

    public static int Write(Span<byte> destination, byte opcode, bool final, ReadOnlySpan<byte> payload)
    {
        if (IsControl(opcode) && payload.Length > MaxControlPayload)
            throw new ArgumentOutOfRangeException(nameof(payload), "A control frame carries at most " + MaxControlPayload + " bytes.");

        var header = HeaderSize(payload.Length);

        destination[0] = (byte)((final ? 0x80 : 0x00) | (opcode & 0x0F));

        if (payload.Length <= 125)
        {
            destination[1] = (byte)payload.Length;
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            destination[1] = 126;
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)payload.Length);
        }
        else
        {
            destination[1] = 127;
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(2, 8), (ulong)payload.Length);
        }

        payload.CopyTo(destination.Slice(header));

        return header + payload.Length;
    }

    public static byte[] Frame(byte opcode, bool final, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderSize(payload.Length) + payload.Length];
        Write(frame, opcode, final, payload);

        return frame;
    }

    public static byte[] Message(NoireWebsocketMessageKind kind, ReadOnlySpan<byte> payload)
        => Frame(kind == NoireWebsocketMessageKind.Text ? OpcodeText : OpcodeBinary, true, payload);

    public static byte[] Ping(ReadOnlySpan<byte> payload)
        => Frame(OpcodePing, true, payload);

    public static byte[] Pong(ReadOnlySpan<byte> payload)
        => Frame(OpcodePong, true, payload);

    // 1005 and 1006 are never encoded. A close built from either carries no payload. The peer reads it as 1005.
    public static byte[] Close(int code, string? reason)
    {
        if (code == (int)NoireWebsocketCloseCode.NoStatusReceived || code == (int)NoireWebsocketCloseCode.AbnormalClosure)
            return Frame(OpcodeClose, true, ReadOnlySpan<byte>.Empty);

        Span<byte> payload = stackalloc byte[2 + MaxCloseReasonBytes];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)code);

        var length = 2 + EncodeReason(reason, payload.Slice(2));

        return Frame(OpcodeClose, true, payload.Slice(0, length));
    }

    public static bool IsControl(byte opcode)
        => (opcode & 0x08) != 0;

    // Stops on a character boundary. A capped reason is never invalid UTF-8.
    private static int EncodeReason(string? reason, Span<byte> destination)
    {
        if (string.IsNullOrEmpty(reason))
            return 0;

        Encoding.UTF8.GetEncoder().Convert(reason.AsSpan(), destination, true, out _, out var written, out _);

        return written;
    }
}
