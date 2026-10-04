// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private TimeZoneInfo AssumedTimeZone => _parseOptions.AssumedTimeZone ?? TimeZoneInfo.Utc;

    private static bool StartsLikeTimestamp(ReadOnlySpan<byte> text)
    {
        return text.Length >= 8
            && text[4] == YamlSyntax.Dash
            && YamlSyntax.IsAsciiDigit(text[3])
            && YamlSyntax.IsAsciiDigit(text[2])
            && YamlSyntax.IsAsciiDigit(text[1])
            && YamlSyntax.IsAsciiDigit(text[0]);
    }

    // Whether a plain value is a timestamp is decided by YamlTimestampHelper.
    // The text is stored exactly as it was written (see WriteTimestamp),
    // so nothing is normalized when the document is read.
    private bool TryParseTimestamp(ParsedScalar scalar, out uint tokenId)
    {
        tokenId = 0;

        var text = ScalarText(scalar);
        if (
            !StartsLikeTimestamp(text)
            || !YamlTimestampHelper.TryGetKind(Encoding.UTF8.GetString(text), out var kind)
        )
        {
            return false;
        }

        var variant = kind switch
        {
            TimestampKind.LocalDate => DTokenVariant.TimestampLocalDate,
            TimestampKind.LocalDateTime => DTokenVariant.TimestampLocalDateTime,
            _ => DTokenVariant.TimestampOffsetDateTime,
        };

        // Core reads a timestamp without an offset through DateTime and then converts it to a DateTimeOffset, which
        // can only apply UTC (a Utc DateTime) or the local zone of the machine. A zone given in the options would be
        // ignored, so such a timestamp is stored as an offset timestamp and read through DecodeDateTimeOffset, which
        // applies the zone. The text, and so what is written, is the same either way.
        if (variant != DTokenVariant.TimestampOffsetDateTime && AssumedTimeZone != TimeZoneInfo.Utc)
        {
            variant = DTokenVariant.TimestampOffsetDateTime;
        }

        tokenId = CommitScalar(variant, scalar);

        return true;
    }

    /// <inheritdoc />
    protected override DateTime DecodeDateTime(DToken token)
    {
        return YamlTimestampHelper.ToDateTime(
            Encoding.UTF8.GetString(ScalarBytes(token)),
            AssumedTimeZone
        );
    }

    /// <inheritdoc />
    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return YamlTimestampHelper.ToDateTimeOffset(
            Encoding.UTF8.GetString(ScalarBytes(token)),
            AssumedTimeZone
        );
    }

    private static void WriteTimestamp(Utf8TextWriter writer, in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        if (reader.Document is YamlDocument document && !token.IsExtended)
        {
            writer.WriteString(document.ScalarBytes(token));

            return;
        }

        // A value of another format (or an edited value) has no YAML text to keep.
        // Core's own timestamp writer produces its text, so no format is defined here.
        if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
        {
            writer.WriteTimestamp(reader.ReadDateTimeOffset(tokenId));

            return;
        }

        var kind = token.Variant switch
        {
            DTokenVariant.TimestampLocalDate => TimestampKind.LocalDate,
            DTokenVariant.TimestampLocalDateTime => TimestampKind.LocalDateTime,
            DTokenVariant.TimestampLocalTime => TimestampKind.LocalTime,
            _ => TimestampKind.Default,
        };

        writer.WriteTimestamp(reader.ReadDateTime(tokenId), kind);
    }
}
