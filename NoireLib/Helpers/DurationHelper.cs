using System;
using System.Globalization;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Reads and writes durations the way people type them: <c>90s</c>, <c>1m30s</c>, <c>1h30</c>, <c>2m 30s</c>,
/// <c>1:30</c>, <c>1.5h</c>.
/// </summary>
/// <remarks>
/// A setting measured in time is almost always stored as a number of milliseconds and shown as one, which makes every
/// value in it something the user has to do arithmetic on. This reads the shorthand instead, and
/// <see cref="Format(TimeSpan)"/> writes it back in the same shorthand, so a field round-trips through what was typed
/// rather than through what it was stored as.<br/>
/// Nothing here touches ImGui: it is string to <see cref="TimeSpan"/> and back, and is as useful behind a command
/// argument or a config importer as it is behind a text field.
/// </remarks>
/// <example>
/// <code>
/// DurationHelper.TryParse("1m30s", out var span);   // 90 seconds
/// DurationHelper.TryParse("1h30", out span);        // 90 minutes: a bare tail takes the next unit down
/// DurationHelper.TryParse("1:30", out span);        // 90 seconds
/// DurationHelper.Format(TimeSpan.FromSeconds(90));  // "1m30s"
/// </code>
/// </example>
public static class DurationHelper
{
    /// <summary>
    /// The longest input that will be looked at, so a pasted file cannot be walked character by character.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Reads a duration, taking a number written with no unit at all as seconds.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="value">The duration, or <see cref="TimeSpan.Zero"/> when it could not be read.</param>
    /// <returns>True when the whole of the text was a duration.</returns>
    public static bool TryParse(string? text, out TimeSpan value)
        => TryParse(text, DurationUnit.Seconds, out value);

    /// <summary>
    /// Reads a duration.
    /// </summary>
    /// <remarks>
    /// Every part of the text has to be understood. A trailing scrap that means nothing fails the whole parse rather
    /// than being ignored, because a field that reads "5 minuts" as five minutes is a field that silently saves the
    /// wrong number on the day it reads "5 monts" as five somethings.
    /// </remarks>
    /// <param name="text">The text to read.</param>
    /// <param name="bareUnit">The unit a leading number with no unit is measured in.</param>
    /// <param name="value">The duration, or <see cref="TimeSpan.Zero"/> when it could not be read.</param>
    /// <returns>True when the whole of the text was a duration.</returns>
    public static bool TryParse(string? text, DurationUnit bareUnit, out TimeSpan value)
    {
        value = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
            return false;

        var span = text.AsSpan().Trim();
        var negative = false;

        if (span[0] is '-' or '+')
        {
            negative = span[0] == '-';
            span = span[1..].TrimStart();
        }

        if (span.IsEmpty)
            return false;

        var read = span.IndexOf(':') >= 0
            ? TryReadClock(span, out var milliseconds)
            : TryReadUnits(span, bareUnit, out milliseconds);

        if (!read)
            return false;

        value = TimeSpan.FromMilliseconds(negative ? -milliseconds : milliseconds);
        return true;
    }

    /// <summary>
    /// Reads a duration, falling back rather than reporting failure.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="fallback">What to return when the text is not a duration.</param>
    /// <returns>The duration, or <paramref name="fallback"/>.</returns>
    public static TimeSpan Parse(string? text, TimeSpan fallback = default)
        => TryParse(text, out var value) ? value : fallback;

    /// <summary>
    /// Writes a duration in the shorthand this helper reads.
    /// </summary>
    /// <remarks>
    /// Round-trips: anything this writes, <see cref="TryParse(string?, out TimeSpan)"/> reads back to the same value.
    /// Parts that are zero are left out, so an hour is <c>1h</c> rather than <c>1h0m0s</c>, and a duration of nothing
    /// is <c>0s</c> rather than the empty string a field could not put a cursor in.
    /// </remarks>
    /// <param name="value">The duration to write.</param>
    /// <returns>The duration in shorthand.</returns>
    public static string Format(TimeSpan value)
    {
        if (value == TimeSpan.Zero)
            return "0s";

        var builder = new StringBuilder(16);

        if (value < TimeSpan.Zero)
        {
            builder.Append('-');
            value = value.Duration();
        }

        Append(builder, (long)value.TotalDays, 'd', string.Empty);
        Append(builder, value.Hours, 'h', string.Empty);
        Append(builder, value.Minutes, 'm', string.Empty);
        Append(builder, value.Seconds, 's', string.Empty);
        Append(builder, value.Milliseconds, 'm', "s");

        return builder.ToString();

        static void Append(StringBuilder builder, long amount, char unit, string suffix)
        {
            if (amount == 0)
                return;

            builder.Append(amount.ToString(CultureInfo.InvariantCulture)).Append(unit).Append(suffix);
        }
    }

    #region Reading

    /// <summary>
    /// Reads the <c>1h30m</c> form: a run of amounts, each with a unit or taking the next one down.
    /// </summary>
    /// <remarks>
    /// Units have to get smaller as the text goes on. That is not pedantry: it is what makes a bare tail readable at
    /// all, and it rejects the transpositions ("30s1m") that a tolerant parser would quietly add up into a number the
    /// user never asked for.
    /// </remarks>
    private static bool TryReadUnits(ReadOnlySpan<char> span, DurationUnit bareUnit, out double milliseconds)
    {
        milliseconds = 0d;

        var at = 0;
        var groups = 0;
        DurationUnit? previous = null;

        while (at < span.Length)
        {
            while (at < span.Length && char.IsWhiteSpace(span[at]))
                at++;

            if (at >= span.Length)
                break;

            var numberStart = at;

            while (at < span.Length && (char.IsAsciiDigit(span[at]) || span[at] is '.' or ','))
                at++;

            if (at == numberStart || !TryReadAmount(span[numberStart..at], out var amount))
                return false;

            while (at < span.Length && char.IsWhiteSpace(span[at]))
                at++;

            var unitStart = at;

            while (at < span.Length && char.IsAsciiLetter(span[at]))
                at++;

            DurationUnit unit;

            if (at == unitStart)
            {
                // A bare amount: the first one is measured in whatever the caller said, a later one in the unit below
                // the one before it, which is how "1h30" means an hour and a half.
                if (previous is null)
                    unit = bareUnit;
                else if (!TryStepDown(previous.Value, out unit))
                    return false;
            }
            else if (!TryResolveUnit(span[unitStart..at], out unit))
            {
                return false;
            }

            if (previous is not null && unit >= previous.Value)
                return false;

            milliseconds += amount * MillisecondsIn(unit);
            previous = unit;
            groups++;

            if (groups > 5 || double.IsNaN(milliseconds) || milliseconds > MaxMilliseconds)
                return false;
        }

        return groups > 0;
    }

    /// <summary>
    /// Reads the <c>1:30</c> form: minutes and seconds, or hours, minutes and seconds.
    /// </summary>
    private static bool TryReadClock(ReadOnlySpan<char> span, out double milliseconds)
    {
        milliseconds = 0d;

        Span<Range> parts = stackalloc Range[4];
        var count = span.Split(parts, ':');

        if (count is not (2 or 3))
            return false;

        // Only the leading part may run past its own unit, so "90:00" is ninety minutes while "1:90" is not a time.
        var units = count == 2
            ? (DurationUnit.Minutes, DurationUnit.Seconds, DurationUnit.Seconds)
            : (DurationUnit.Hours, DurationUnit.Minutes, DurationUnit.Seconds);

        for (var i = 0; i < count; i++)
        {
            var piece = span[parts[i]].Trim();

            if (piece.IsEmpty || !TryReadAmount(piece, out var amount))
                return false;

            var unit = i switch
            {
                0 => units.Item1,
                1 => units.Item2,
                _ => units.Item3,
            };

            if (i > 0 && amount >= 60d)
                return false;

            milliseconds += amount * MillisecondsIn(unit);
        }

        return !double.IsNaN(milliseconds) && milliseconds <= MaxMilliseconds;
    }

    /// <summary>
    /// Reads one number, accepting either decimal separator so a field does not depend on the machine's locale.
    /// </summary>
    private static bool TryReadAmount(ReadOnlySpan<char> text, out double amount)
    {
        amount = 0d;

        if (text.IsEmpty || text.Count('.') + text.Count(',') > 1)
            return false;

        Span<char> normalized = stackalloc char[MaxLength];

        if (text.Length > normalized.Length)
            return false;

        for (var i = 0; i < text.Length; i++)
            normalized[i] = text[i] == ',' ? '.' : text[i];

        return double.TryParse(normalized[..text.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out amount)
            && amount >= 0d
            && !double.IsInfinity(amount);
    }

    /// <summary>
    /// Matches a written unit, in every spelling worth accepting from someone typing quickly.
    /// </summary>
    private static bool TryResolveUnit(ReadOnlySpan<char> text, out DurationUnit unit)
    {
        unit = DurationUnit.Seconds;

        if (text.Equals("ms", StringComparison.OrdinalIgnoreCase)
            || text.Equals("msec", StringComparison.OrdinalIgnoreCase)
            || text.Equals("millisecond", StringComparison.OrdinalIgnoreCase)
            || text.Equals("milliseconds", StringComparison.OrdinalIgnoreCase))
        {
            unit = DurationUnit.Milliseconds;
            return true;
        }

        if (text.Equals("s", StringComparison.OrdinalIgnoreCase)
            || text.Equals("sec", StringComparison.OrdinalIgnoreCase)
            || text.Equals("secs", StringComparison.OrdinalIgnoreCase)
            || text.Equals("second", StringComparison.OrdinalIgnoreCase)
            || text.Equals("seconds", StringComparison.OrdinalIgnoreCase))
        {
            unit = DurationUnit.Seconds;
            return true;
        }

        if (text.Equals("m", StringComparison.OrdinalIgnoreCase)
            || text.Equals("min", StringComparison.OrdinalIgnoreCase)
            || text.Equals("mins", StringComparison.OrdinalIgnoreCase)
            || text.Equals("minute", StringComparison.OrdinalIgnoreCase)
            || text.Equals("minutes", StringComparison.OrdinalIgnoreCase))
        {
            unit = DurationUnit.Minutes;
            return true;
        }

        if (text.Equals("h", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hr", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hrs", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hour", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hours", StringComparison.OrdinalIgnoreCase))
        {
            unit = DurationUnit.Hours;
            return true;
        }

        if (text.Equals("d", StringComparison.OrdinalIgnoreCase)
            || text.Equals("day", StringComparison.OrdinalIgnoreCase)
            || text.Equals("days", StringComparison.OrdinalIgnoreCase))
        {
            unit = DurationUnit.Days;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The unit one step below another, or false when there is nothing below it.
    /// </summary>
    private static bool TryStepDown(DurationUnit unit, out DurationUnit smaller)
    {
        var exists = unit > DurationUnit.Milliseconds;
        smaller = exists ? unit - 1 : unit;
        return exists;
    }

    private static double MillisecondsIn(DurationUnit unit) => unit switch
    {
        DurationUnit.Milliseconds => 1d,
        DurationUnit.Seconds => 1_000d,
        DurationUnit.Minutes => 60_000d,
        DurationUnit.Hours => 3_600_000d,
        _ => 86_400_000d,
    };

    /// <summary>
    /// The largest duration that survives the trip through <see cref="TimeSpan.FromMilliseconds(double)"/>.
    /// </summary>
    private const double MaxMilliseconds = 922_337_203_685_477d;

    #endregion
}
