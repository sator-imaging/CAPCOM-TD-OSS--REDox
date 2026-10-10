using System;
using System.Globalization;

namespace REDox.Yaml;

internal static class TimestampHelper
{
    private readonly record struct ParsedTimestampParts(
        DateTime WallClock,
        TimeSpan? Offset,
        TimestampKind Kind
    );

    // TODO: Avoid string operation.
    /// <summary>
    /// Decides whether a text is a timestamp.
    /// A text that looks like a timestamp but is not a real date or time is ignored (e.g., "2001-13-45").
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
    /// A timestamp without an offset is placed in the given time zone <paramref name="unspecifiedOffsetTimeZone" />.
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

    public static DateTime ToDateTime(string text, TimeZoneInfo unspecifiedOffsetTimeZone)
    {
        var parts = ParseOrThrow(text);

        return parts.Offset.HasValue
            ? new DateTimeOffset(parts.WallClock, parts.Offset.Value).UtcDateTime
            : unspecifiedOffsetTimeZone == TimeZoneInfo.Utc
                ? DateTime.SpecifyKind(parts.WallClock, DateTimeKind.Utc)
                : parts.WallClock;
    }

    private static ParsedTimestampParts ParseOrThrow(string text)
    {
        return TryParse(text, out var parts)
            ? parts
            : throw YamlDocument.GenericError(
                YamlDocument.ParseException.ErrorCode.InvalidValue,
                $"'{text}' is not a YAML timestamp."
            );
    }

    public static bool StartsLikeTimestamp(ReadOnlySpan<byte> text)
    {
        // TODO: i18n support. 4 digits followed by '-' is very limited.
        //       Add option something like RecognizeExtendedDateTimeFormat.
        return text.Length >= 8
               && text[4] == YamlSyntax.Dash
               && YamlSyntax.IsAsciiDigit(text[0])
               && YamlSyntax.IsAsciiDigit(text[1])
               && YamlSyntax.IsAsciiDigit(text[2])
               && YamlSyntax.IsAsciiDigit(text[3]);
    }

    private static bool TryParse(string text, out ParsedTimestampParts parts)
    {
        parts = default;

        // TODO: i18n support. 4 digits followed by '-' is very limited.
        //       Add option something like RecognizeExtendedDateTimeFormat.
        if (text.Length < 8 || text[4] != YamlSyntax.Dash)
        {
            return false;
        }

        var match = YamlSyntax.TimestampPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture);

        // The year 0000 does not exist in DateTime.
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        var wallClock = new DateTime(year, month, day);
        if (!match.Groups[4].Success)
        {
            parts = new ParsedTimestampParts(wallClock, Offset: null, TimestampKind.LocalDate);

            return true;
        }

        var hour = int.Parse(match.Groups[4].ValueSpan, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups[5].ValueSpan, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups[6].ValueSpan, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        wallClock = new DateTime(year, month, day, hour, minute, second);

        // A fraction finer than 100 ns (7 digits) is cut off in the DateTime value only; the written text is kept.
        if (match.Groups[7].Success && match.Groups[7].Length > 0)
        {
            var digits = match.Groups[7].ValueSpan;
            digits = digits.Length > 7 ? digits[..7] : digits;

            var ticks = long.Parse(digits, CultureInfo.InvariantCulture);
            for (var width = digits.Length; width < 7; width++)
            {
                ticks *= 10;
            }

            wallClock = wallClock.AddTicks(ticks);
        }

        if (!match.Groups[8].Success)
        {
            parts = new ParsedTimestampParts(wallClock, Offset: null, TimestampKind.LocalDateTime);

            return true;
        }

        if (!TryParseOffset(match.Groups[8].Value, out var offset))
        {
            return false;
        }

        parts = new ParsedTimestampParts(wallClock, offset, TimestampKind.OffsetDateTime);

        return true;
    }

    private static bool TryParseOffset(string zone, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        if (zone.Length == 1 && zone[0] == YamlSyntax.UtcDesignator)
        {
            return true;
        }

        var time = zone.AsSpan(1);
        var separator = time.IndexOf((char)YamlSyntax.TimeFieldSeparator);
        var hours = int.Parse(
            separator < 0 ? time : time[..separator],
            CultureInfo.InvariantCulture
        );

        var minutes =
            separator < 0 ? 0 : int.Parse(time[(separator + 1)..], CultureInfo.InvariantCulture);

        // DateTimeOffset accepts offsets of up to 14 hours.
        if (minutes > 59 || hours * 60 + minutes > 14 * 60)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, seconds: 0);

        if (zone[0] == YamlSyntax.Dash)
        {
            offset = -offset;
        }

        return true;
    }
}