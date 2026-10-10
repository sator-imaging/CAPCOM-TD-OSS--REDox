using System;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    /// <inheritdoc />
    protected override DateTime DecodeDateTime(DToken token)
    {
        return TimestampHelper.ToDateTime(
            Encoding.UTF8.GetString(GetUtf8TextForToken(token)),
            AssumedTimeZone
        );
    }

    /// <inheritdoc />
    protected override DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return TimestampHelper.ToDateTimeOffset(
            Encoding.UTF8.GetString(GetUtf8TextForToken(token)),
            AssumedTimeZone
        );
    }

    private TimeZoneInfo AssumedTimeZone => _parseOptions.AssumedTimeZone ?? TimeZoneInfo.Utc;

    // Whether a plain value is a timestamp is decided by YamlTimestampHelper.
    // The text is stored exactly as it was written (see WriteTimestamp),
    // so nothing is normalized when the document is read.
    private bool TryParseTimestamp(ParsedScalar scalar, out uint tokenId)
    {
        tokenId = 0;

        var text = _utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start);
        if (
            !TimestampHelper.StartsLikeTimestamp(text)
            || !TimestampHelper.TryGetKind(Encoding.UTF8.GetString(text), out var kind)
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

        // Core reads a timestamp without an offset through DateTime and then converts it to a DateTimeOffset
        // which can only apply UTC (a Utc DateTime) or the local zone of the machine.
        // A zone given in the options would be ignored, so such a timestamp is stored as an offset timestamp
        // and read through DecodeDateTimeOffset which applies the zone.
        if (variant != DTokenVariant.TimestampOffsetDateTime && AssumedTimeZone != TimeZoneInfo.Utc)
        {
            variant = DTokenVariant.TimestampOffsetDateTime;
        }

        tokenId = CreateTokenForParsedScalar(variant, scalar);

        return true;
    }

    private static void WriteTimestamp(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        DToken token
    )
    {
        if (reader.Document is YamlDocument document && !token.IsExtended)
        {
            writer.WriteString(document.GetUtf8TextForToken(token));

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