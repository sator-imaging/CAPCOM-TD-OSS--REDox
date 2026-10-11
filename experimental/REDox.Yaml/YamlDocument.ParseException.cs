using System.Diagnostics.CodeAnalysis;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    // NOTE: Declare exception and its enum as nested type to align to other packages.
    //       Visibility is intentionally changed to internal for unit test reliability.
    internal sealed class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            UNKNOWN,
            InvalidFormat,
            InvalidString,
            MaxDepthExceeded,
            UnsupportedFeature,
            InvalidEncoding,
            DuplicateKey,
            MissingStringKey,
            InvalidOperation,
            InvalidOption,
            InvalidValue,
            FormatCompatibility,
        }

        public ParseException(int position, ErrorCode code, string? message = null)
            : base(
                nameof(YamlDocument),
                position,
                message is null
                    ? $"{nameof(YamlDocument)} Error ({code}): BytePosition: {position}"
                    : $"{nameof(YamlDocument)} Error ({code}): BytePosition: {position} ({message})"
            )
        {
        }
    }

    [DoesNotReturn]
    internal void ThrowParseError(ParseException.ErrorCode code, string? message = null) =>
        throw new ParseException(_position, code, message);

    [DoesNotReturn]
    internal static void ThrowGenericError(ParseException.ErrorCode code, string? message = null) =>
        throw new ParseException(-1, code, message);

    internal DocumentParseException CreateParseError(
        ParseException.ErrorCode code,
        string? message = null
    ) => new ParseException(_position, code, message);

    internal static DocumentParseException CreateGenericError(
        ParseException.ErrorCode code,
        string? message = null
    ) => new ParseException(-1, code, message);
}