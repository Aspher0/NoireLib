using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.Remote.Internal;

// Clauses joined by and, nothing else.
internal static class HttpEventFilter
{
    public const string Equal = "eq";
    public const string NotEqual = "ne";
    public const string GreaterThan = "gt";
    public const string GreaterOrEqual = "ge";
    public const string LessThan = "lt";
    public const string LessOrEqual = "le";
    public const string Contains = "contains";
    public const string StartsWith = "startsWith";
    public const string Exists = "exists";

    private static readonly string[] Operators =
    [
        Equal, NotEqual, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Contains, StartsWith, Exists,
    ];

    public static bool IsKnownOperator(string? op)
    {
        if (string.IsNullOrEmpty(op))
            return false;

        foreach (var known in Operators)
        {
            if (string.Equals(known, op, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static List<NoireRemoteEvent> Apply(IReadOnlyList<NoireRemoteEvent> events, IReadOnlyList<NoireRemoteEventFilter>? filters)
    {
        var kept = new List<NoireRemoteEvent>(events.Count);

        foreach (var item in events)
        {
            if (Passes(item, filters))
                kept.Add(item);
        }

        return kept;
    }

    private static bool Passes(NoireRemoteEvent item, IReadOnlyList<NoireRemoteEventFilter>? filters)
    {
        if (filters == null || filters.Count == 0)
            return true;

        foreach (var clause in filters)
        {
            if (!Passes(item, clause))
                return false;
        }

        return true;
    }

    private static bool Passes(NoireRemoteEvent item, NoireRemoteEventFilter clause)
    {
        JToken? found;

        try
        {
            found = item.Data?.SelectToken(clause.Path);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }

        if (string.Equals(clause.Op, Exists, StringComparison.OrdinalIgnoreCase))
        {
            var wanted = clause.Value == null || clause.Value.Type != JTokenType.Boolean || (bool)clause.Value;
            return (found != null && found.Type != JTokenType.Null) == wanted;
        }

        if (found == null)
            return false;

        switch (clause.Op.ToLowerInvariant())
        {
            case Equal:
                return JToken.DeepEquals(found, clause.Value) || TextOf(found) == TextOf(clause.Value);

            case NotEqual:
                return !JToken.DeepEquals(found, clause.Value) && TextOf(found) != TextOf(clause.Value);

            case GreaterThan:
                return Compare(found, clause.Value) is { } greater && greater > 0;

            case GreaterOrEqual:
                return Compare(found, clause.Value) is { } atLeast && atLeast >= 0;

            case LessThan:
                return Compare(found, clause.Value) is { } less && less < 0;

            case LessOrEqual:
                return Compare(found, clause.Value) is { } atMost && atMost <= 0;

            case "contains":
                return ContainsValue(found, clause.Value);

            case "startswith":
                return TextOf(found).StartsWith(TextOf(clause.Value), StringComparison.OrdinalIgnoreCase);

            default:
                return false;
        }
    }

    private static bool ContainsValue(JToken found, JToken? wanted)
    {
        if (found is JArray array)
        {
            foreach (var item in array)
            {
                if (JToken.DeepEquals(item, wanted) || TextOf(item) == TextOf(wanted))
                    return true;
            }

            return false;
        }

        return TextOf(found).IndexOf(TextOf(wanted), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int? Compare(JToken left, JToken? right)
    {
        if (right == null)
            return null;

        if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        if (TryDate(left, out var leftDate) && TryDate(right, out var rightDate))
            return leftDate.CompareTo(rightDate);

        return string.Compare(TextOf(left), TextOf(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(JToken token, out double value)
    {
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = token.Value<double>();
            return true;
        }

        return double.TryParse(TextOf(token), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDate(JToken token, out DateTimeOffset value)
    {
        if (token.Type is JTokenType.Date)
        {
            value = token.Value<DateTimeOffset>();
            return true;
        }

        return DateTimeOffset.TryParse(TextOf(token), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    private static string TextOf(JToken? token)
        => token == null || token.Type == JTokenType.Null
            ? string.Empty
            : token.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : token.ToString(Newtonsoft.Json.Formatting.None);
}
