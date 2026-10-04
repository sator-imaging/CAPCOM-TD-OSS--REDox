// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;

namespace REDox.Yaml;

internal static class YamlTimestampHelper
{
    private readonly record struct Parts(DateTime WallClock, TimeSpan? Offset, TimestampKind Kind);

    /// <summary>
    /// Decides whether a text is a timestamp. This only looks at the text: nothing is converted or normalized.
    /// A text that looks like a timestamp but is not a real date or time, such as "2001-13-45", is not one.
    /// </summary>
    public static bool TryGetKind(string text, out TimestampKind kind)
    {
        if (TryParse(text, out var parts))
        {
            kind = parts.Kind;

            return true;
        }

        kind = default;

        return false;
    }

    /// <summary>
    /// Reads a timestamp as a <see cref="DateTimeOffset" />. A timestamp without an offset is placed in the time
    /// zone given by <paramref name="unspecifiedOffsetTimeZone" />.
    /// </summary>
    public static DateTimeOffset ToDateTimeOffset(
        string text,
        TimeZoneInfo unspecifiedOffsetTimeZone
    )
    {
        var parts = ParseOrThrow(text);
        var offset = parts.Offset ?? unspecifiedOffsetTimeZone.GetUtcOffset(parts.WallClock);

        return new DateTimeOffset(parts.WallClock, offset);
    }

    /// <summary>
    /// Reads a timestamp as a <see cref="DateTime" />: the wall-clock time as written, or the UTC time when the
    /// timestamp has an offset.
    /// </summary>
    public static DateTime ToDateTime(string text, TimeZoneInfo unspecifiedOffsetTimeZone)
    {
        var parts = ParseOrThrow(text);
        if (parts.Offset.HasValue)
        {
            return new DateTimeOffset(parts.WallClock, parts.Offset.Value).UtcDateTime;
        }

        return unspecifiedOffsetTimeZone == TimeZoneInfo.Utc
            ? DateTime.SpecifyKind(parts.WallClock, DateTimeKind.Utc)
            : parts.WallClock;
    }

    private static Parts ParseOrThrow(string text)
    {
        return TryParse(text, out var parts)
            ? parts
            : throw new FormatException($"'{text}' is not a YAML timestamp.");
    }

    private static bool TryParse(string text, out Parts parts)
    {
        parts = default;

        var match = YamlSyntax.TimestampPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        // The year 0000 does not exist in DateTime.
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        var wallClock = new DateTime(year, month, day);
        if (!match.Groups[4].Success)
        {
            parts = new Parts(wallClock, null, TimestampKind.LocalDate);

            return true;
        }

        var hour = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        wallClock = wallClock.AddHours(hour).AddMinutes(minute).AddSeconds(second);

        // A fraction finer than 100 ns (7 digits) is cut off in the DateTime value only; the written text is kept.
        if (match.Groups[7].Success && match.Groups[7].Length > 0)
        {
            var digits = match.Groups[7].Value;
            digits = digits.Length > 7 ? digits[..7] : digits.PadRight(7, '0');
            wallClock = wallClock.AddTicks(long.Parse(digits, CultureInfo.InvariantCulture));
        }

        if (!match.Groups[8].Success)
        {
            parts = new Parts(wallClock, null, TimestampKind.LocalDateTime);

            return true;
        }

        if (!TryParseOffset(match.Groups[8].Value, out var offset))
        {
            return false;
        }

        parts = new Parts(wallClock, offset, TimestampKind.OffsetDateTime);

        return true;
    }

    private static bool TryParseOffset(string zone, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        if (zone.Length == 1 && zone[0] == YamlSyntax.UtcDesignator)
        {
            return true;
        }

        var parts = zone[1..].Split((char)YamlSyntax.TimeFieldSeparator);
        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;

        // DateTimeOffset accepts offsets of up to 14 hours.
        if (minutes > 59 || hours * 60 + minutes > 14 * 60)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, 0);

        if (zone[0] == YamlSyntax.Dash)
        {
            offset = -offset;
        }

        return true;
    }
}
