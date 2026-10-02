// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private TimeZoneInfo UnspecifiedOffsetTimeZone =>
        _parseOptions.TimeZoneForUnspecifiedOffset ?? TimeZoneInfo.Utc;

    // Whether a plain value is a timestamp is decided by YamlTimestampHelper. The text is stored exactly as it was
    // written (see WriteTimestamp), so nothing is normalized when the document is read.
    private bool TryParseTimestamp(string text, out uint tokenId)
    {
        tokenId = 0;
        if (!YamlTimestampHelper.TryGetKind(text, out var kind))
        {
            return false;
        }

        var variant = kind switch
        {
            TimestampKind.LocalDate => DTokenVariant.TimestampLocalDate,
            TimestampKind.LocalDateTime => DTokenVariant.TimestampLocalDateTime,
            _ => DTokenVariant.TimestampOffsetDateTime,
        };

        tokenId = StoreScalar(variant, text);
        return true;
    }

    protected override DateTime DecodeDateTime(DToken token)
    {
        return YamlTimestampHelper.ToDateTime(
            Encoding.UTF8.GetString(ScalarBytes(token)),
            UnspecifiedOffsetTimeZone
        );
    }

    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return YamlTimestampHelper.ToDateTimeOffset(
            Encoding.UTF8.GetString(ScalarBytes(token)),
            UnspecifiedOffsetTimeZone
        );
    }

    private static void WriteTimestamp(Utf8TextWriter writer, in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        // A timestamp that was read from YAML and not edited is written back exactly as it was written: the text
        // is never reformatted.
        if (reader.Document is YamlDocument document && !token.IsExtended)
        {
            writer.WriteString(document.ScalarBytes(token));
            return;
        }

        // A value of another format (or an edited value) has no YAML text to keep. Core's own timestamp writer
        // produces its text, so no format is defined here.
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
