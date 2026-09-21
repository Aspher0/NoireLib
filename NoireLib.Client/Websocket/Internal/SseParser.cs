using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoireLib.Websocket.Internal;

// The server-sent-events wire format alone. Holds the data buffer, the pending event type, the last id, and a carriage return waiting for its line feed.
internal sealed class SseParser
{
    private const string DefaultEventType = "message";

    private readonly List<SseParserItem> completed = [];
    private readonly StringBuilder line = new();
    private readonly StringBuilder data = new();
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly int maxEventSize;

    private char[] buffer = new char[1024];
    private string? eventType;
    private bool pendingCarriageReturn;
    private bool byteOrderMarkChecked;

    public SseParser(int maxEventSize)
        => this.maxEventSize = maxEventSize > 0 ? maxEventSize : int.MaxValue;

    // Carried across reconnects.
    public string? LastEventId { get; set; }

    public TimeSpan? ServerRetry { get; private set; }

    public IReadOnlyList<SseParserItem> Feed(ReadOnlySpan<byte> bytes)
    {
        // The decoder holds an incomplete sequence across reads.
        var required = decoder.GetCharCount(bytes, false);

        if (buffer.Length < required)
            buffer = new char[required];

        var written = decoder.GetChars(bytes, buffer, false);
        return Feed(buffer.AsSpan(0, written));
    }

    public IReadOnlyList<SseParserItem> Feed(ReadOnlySpan<char> text)
    {
        completed.Clear();

        var start = 0;

        if (!byteOrderMarkChecked && text.Length > 0)
        {
            byteOrderMarkChecked = true;

            if (text[0] == '﻿')
                start = 1;
        }

        for (var i = start; i < text.Length; i++)
        {
            var current = text[i];

            if (pendingCarriageReturn)
            {
                pendingCarriageReturn = false;

                // The other half of a CRLF pair.
                if (current == '\n')
                    continue;
            }

            if (current == '\r')
            {
                pendingCarriageReturn = true;
                EndLine();
                continue;
            }

            if (current == '\n')
            {
                EndLine();
                continue;
            }

            line.Append(current);
        }

        return completed;
    }

    private void EndLine()
    {
        var text = line.ToString();
        line.Clear();

        if (text.Length == 0)
        {
            Dispatch();
            return;
        }

        var colon = text.IndexOf(':');
        string field;
        string value;

        if (colon < 0)
        {
            field = text;
            value = string.Empty;
        }
        else
        {
            field = text[..colon];
            value = text[(colon + 1)..];

            // Exactly one space after the colon is never part of the value.
            if (value.Length > 0 && value[0] == ' ')
                value = value[1..];
        }

        // A leading colon is a comment.
        if (field.Length == 0)
        {
            completed.Add(SseParserItem.FromComment(value));
            return;
        }

        Apply(field, value);
    }

    private void Apply(string field, string value)
    {
        switch (field)
        {
            case "event":
                eventType = value;
                break;

            case "data":
                data.Append(value).Append('\n');

                if (data.Length > maxEventSize)
                    throw new NoireSocketMessageTooLargeException(data.Length, maxEventSize);

                break;

            case "id":
                // An id containing a null character is refused.
                if (!value.Contains('\0'))
                    LastEventId = value;

                break;

            case "retry":
                if (IsDigits(value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
                    ServerRetry = TimeSpan.FromMilliseconds(milliseconds);

                break;
        }
    }

    private void Dispatch()
    {
        // A blank line after only an event type dispatches nothing. The type still resets.
        if (data.Length == 0)
        {
            eventType = null;
            return;
        }

        if (data[data.Length - 1] == '\n')
            data.Length -= 1;

        completed.Add(SseParserItem.FromEvent(new NoireSseEvent(eventType ?? DefaultEventType, data.ToString(), LastEventId)));

        data.Clear();
        eventType = null;
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }
}

internal readonly struct SseParserItem
{
    private SseParserItem(NoireSseEvent? dispatched, string? comment)
    {
        Event = dispatched;
        Comment = comment;
    }

    public NoireSseEvent? Event { get; }

    public string? Comment { get; }

    public static SseParserItem FromEvent(NoireSseEvent dispatched)
        => new(dispatched, null);

    public static SseParserItem FromComment(string comment)
        => new(null, comment);
}
