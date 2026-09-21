using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoireLib.Remote.Internal;

// The stream route is a GET. What a poll sends in a body arrives here instead.
internal static class HttpQueryString
{
    public static Dictionary<string, string> Parse(string target)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mark = target.IndexOf('?');

        if (mark < 0 || mark == target.Length - 1)
            return values;

        foreach (var pair in target.Substring(mark + 1).Split('&'))
        {
            if (pair.Length == 0)
                continue;

            var equals = pair.IndexOf('=');

            if (equals < 0)
            {
                values[Decode(pair)] = string.Empty;
                continue;
            }

            values[Decode(pair.Substring(0, equals))] = Decode(pair.Substring(equals + 1));
        }

        return values;
    }

    public static string PathOf(string target)
    {
        var mark = target.IndexOf('?');
        return mark < 0 ? target : target.Substring(0, mark);
    }

    public static NoireRemoteEventRequest ToEventRequest(string target)
    {
        var query = Parse(target);
        var request = new NoireRemoteEventRequest
        {
            Channels = Split(query, "channels"),
            Topics = Split(query, "topics"),
        };

        if (query.TryGetValue("since", out var since) && long.TryParse(since, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cursor))
            request.Since = cursor;

        if (query.TryGetValue("minIntervalMs", out var interval) && int.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            request.WaitMs = milliseconds;

        if (query.TryGetValue("collapse", out var collapse))
            request.Collapse = !string.Equals(collapse, "false", StringComparison.OrdinalIgnoreCase) && collapse != "0";

        if (query.TryGetValue("cursors", out var cursors))
            request.Cursors = ParseCursors(cursors);

        return request;
    }

    // channel:sequence pairs, typeable in an address bar.
    private static Dictionary<string, long>? ParseCursors(string value)
    {
        Dictionary<string, long>? parsed = null;

        foreach (var pair in value.Split(','))
        {
            var separator = pair.LastIndexOf(':');

            if (separator <= 0)
                continue;

            if (!long.TryParse(pair.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cursor))
                continue;

            (parsed ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase))[pair.Substring(0, separator).Trim()] = cursor;
        }

        return parsed;
    }

    private static IReadOnlyList<string>? Split(Dictionary<string, string> query, string key)
    {
        if (!query.TryGetValue(key, out var value) || value.Length == 0)
            return null;

        var parts = value.Split(',');
        var list = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            if (trimmed.Length > 0)
                list.Add(trimmed);
        }

        return list.Count == 0 ? null : list;
    }

    public static string Decode(string value)
    {
        if (value.IndexOf('%') < 0 && value.IndexOf('+') < 0)
            return value;

        var bytes = new List<byte>(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (character == '+')
            {
                bytes.Add((byte)' ');
                continue;
            }

            if (character == '%' && index + 2 < value.Length
                && byte.TryParse(value.Substring(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var decoded))
            {
                bytes.Add(decoded);
                index += 2;
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }
}
