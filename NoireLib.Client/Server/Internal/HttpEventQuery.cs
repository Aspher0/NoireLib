using System;
using System.Collections.Generic;

namespace NoireLib.Remote.Internal;

// Built once from the request. Nothing parses at collect time.
internal sealed class HttpEventQuery
{
    public IReadOnlyList<string> Channels { get; private set; } = [NoireRemoteChannels.Events];

    public IReadOnlyList<string>? Topics { get; private set; }

    public IReadOnlyList<NoireRemoteEventFilter>? Filters { get; private set; }

    public bool Collapse { get; private set; }

    private Dictionary<string, long>? cursors;
    private long since;

    public long CursorFor(string channel)
    {
        if (cursors != null && cursors.TryGetValue(channel, out var cursor))
            return cursor;

        // The shipped body carries one cursor and no channel.
        return string.Equals(channel, NoireRemoteChannels.Events, StringComparison.OrdinalIgnoreCase) ? since : 0;
    }

    public void Advance(IReadOnlyDictionary<string, long>? written)
    {
        if (written == null)
            return;

        cursors ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in written)
            cursors[pair.Key] = pair.Value;
    }

    public static HttpEventQuery From(NoireRemoteEventRequest body, int maxFilterClauses, out HttpFailure? failure)
    {
        failure = null;

        var query = new HttpEventQuery
        {
            Topics = body.Topics,
            Collapse = body.Collapse,
            since = body.Since,
        };

        if (body.Channels is { Count: > 0 })
        {
            var names = new List<string>(body.Channels.Count);

            foreach (var name in body.Channels)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }

            if (names.Count > 0)
                query.Channels = names;
        }

        if (body.Cursors is { Count: > 0 })
        {
            query.cursors = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in body.Cursors)
                query.cursors[pair.Key] = pair.Value;
        }

        if (body.Filters is { Count: > 0 })
        {
            if (body.Filters.Count > maxFilterClauses)
            {
                failure = new HttpFailure(400, NoireRemoteErrorCodes.FilterInvalid,
                    "A poll takes at most " + maxFilterClauses + " filter clauses and " + body.Filters.Count + " were sent.");

                return query;
            }

            foreach (var clause in body.Filters)
            {
                if (!HttpEventFilter.IsKnownOperator(clause.Op))
                {
                    failure = new HttpFailure(400, NoireRemoteErrorCodes.FilterInvalid,
                        "'" + clause.Op + "' is not a filter operator this listener reads.", clause.Op);

                    return query;
                }

                if (string.IsNullOrWhiteSpace(clause.Path))
                {
                    failure = new HttpFailure(400, NoireRemoteErrorCodes.FilterInvalid, "A filter clause needs a path.");
                    return query;
                }
            }

            query.Filters = body.Filters;
        }

        return query;
    }
}
