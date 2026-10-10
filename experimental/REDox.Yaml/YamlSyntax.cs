using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Yaml;

internal static partial class YamlSyntax
{
    // Numbers as the YAML 1.2 core schema reads them (decimal integers and floats).
    // Other parsers resolve these as numbers even where this library's parser would not, e.g. ".5" and "5.".
    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    public static partial Regex CoreNumberPattern();

    // The timestamp forms of YAML 1.1
    // "2001-12-14", "2001-12-14T21:59:43.10-05:00" and "2001-12-14 21:59:43.10 -5".
    // Groups: 1 year, 2 month, 3 day, 4 hour, 5 minute, 6 second, 7 fraction (without the dot), 8 zone.
    [GeneratedRegex(
        @"^([0-9]{4})-([0-9]{1,2})-([0-9]{1,2})(?:(?:[Tt]|[ \t]+)([0-9]{1,2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]*))?(?:[ \t]*(Z|[-+][0-9]{1,2}(?::[0-9]{2})?))?)?$"
    )]
    public static partial Regex TimestampPattern();

    // White space and line breaks. YAML knows no other white space: U+3000 and the like are ordinary text.
    public const byte Space = (byte)' ';
    public const byte Tab = (byte)'\t';
    public const byte LineFeed = (byte)'\n';
    public const byte CarriageReturn = (byte)'\r';

    // The value of a byte variable that holds no character yet (no quote opened, no chomping indicator read)
    public const byte NoCharacter = 0;

    // Characters that serve several roles
    public const byte Dash = (byte)'-';
    public const byte Plus = (byte)'+';
    public const byte Dot = (byte)'.';
    public const byte Slash = (byte)'/';
    public const byte Backslash = (byte)'\\';

    // Comments and mappings
    public const byte CommentIndicator = (byte)'#';
    public const byte MappingValueIndicator = (byte)':';
    public const byte ExplicitKeyIndicator = (byte)'?';

    // Node properties and directives (not supported, recognised so that they are rejected)
    public const byte AnchorIndicator = (byte)'&';
    public const byte AliasIndicator = (byte)'*';
    public const byte TagIndicator = (byte)'!';
    public const byte DirectiveIndicator = (byte)'%';

    // Indicators YAML reserves for future use
    public const byte ReservedAtIndicator = (byte)'@';
    public const byte ReservedBacktickIndicator = (byte)'`';

    // Block scalar styles
    public const byte LiteralBlockIndicator = (byte)'|';
    public const byte FoldedBlockIndicator = (byte)'>';

    // Flow collections
    public const byte FlowSeparator = (byte)',';
    public const byte FlowSequenceStart = (byte)'[';
    public const byte FlowSequenceEnd = (byte)']';
    public const byte FlowMappingStart = (byte)'{';
    public const byte FlowMappingEnd = (byte)'}';

    // Quoted scalars
    public const byte DoubleQuote = (byte)'"';
    public const byte SingleQuote = (byte)'\'';

    // The fields of a timestamp: "21:59:43" and the offset "-05:00"
    public const byte TimeFieldSeparator = (byte)':';

    // The exponent marker of a float
    public const byte ExponentLower = (byte)'e';
    public const byte ExponentUpper = (byte)'E';

    // The underscore that may stand between the digits of a number ("1_000")
    public const byte Underscore = (byte)'_';

    // The digits an indentation indicator is made of (1 to 9), and the zero that a digit's value is counted from
    public const byte DigitZero = (byte)'0';
    public const byte DigitOne = (byte)'1';
    public const byte DigitNine = (byte)'9';

    public const byte HexDigitUpperFirst = (byte)'A';
    public const byte HexDigitUpperLast = (byte)'F';
    public const byte HexDigitLowerFirst = (byte)'a';
    public const byte HexDigitLowerLast = (byte)'f';

    public const int HexLetterDigitValue = 10;

    public static readonly SearchValues<byte> HexDigits = SearchValues.Create(
        "0123456789abcdefABCDEF"u8
    );

    // Characters an escape sequence produces
    public const byte Bell = (byte)'\a';
    public const byte Backspace = (byte)'\b';
    public const byte VerticalTab = (byte)'\v';
    public const byte FormFeed = (byte)'\f';
    public const byte EscapeControlCharacter = 0x1b;
    public const char NextLine = (char)0x85;
    public const char NoBreakSpace = (char)0xa0;
    public const char LineSeparator = (char)0x2028;
    public const char ParagraphSeparator = (char)0x2029;

    // The delete character, which is a control character, and the C1 controls (U+0080 to U+009F): in UTF-8 a C1 control
    // is the lead byte 0xC2 followed by a second byte from 0x80 to 0x9F
    public const byte Delete = 0x7f;
    public const byte C1ControlLeadByte = 0xc2;
    public const byte C1ControlFirstSecondByte = 0x80;
    public const byte C1ControlLastSecondByte = 0x9f;

    // Characters that are not printable in YAML
    public const char ByteOrderMark = (char)0xfeff;
    public const char NonCharacterFffe = (char)0xfffe;
    public const char NonCharacterFfff = (char)0xffff;

    // The prefixes of the hexadecimal and octal integers of the YAML 1.2 core schema ("0x1F", "0o14")
    // and of the binary integers of YAML 1.1 ("0b101").
    // The other YAML 1.1 forms are not numbers here: "0755" is the decimal 755, not an octal,
    // and "190:20:30" is a string. The underscores that YAML 1.1 allows between digits ("1_000") are read.
    public static ReadOnlySpan<byte> BinaryPrefixUtf8 => "0b"u8;

    public static ReadOnlySpan<byte> HexPrefixUtf8 => "0x"u8;

    public static ReadOnlySpan<byte> OctalPrefixUtf8 => "0o"u8;

    // The designator of the UTC time zone at the end of a timestamp ("2001-12-14T21:59:43Z")
    public const byte UtcDesignator = (byte)'Z';

    // A text that is a single dash: the sequence entry indicator, which cannot be a plain scalar
    public static ReadOnlySpan<byte> DashTextUtf8 => "-"u8;

    // Document markers
    public static ReadOnlySpan<byte> DocumentStartUtf8 => "---"u8;

    public static ReadOnlySpan<byte> DocumentEndUtf8 => "..."u8;

    // Used to tell the sign and the kind of a special float apart (compared ignoring case).
    public static ReadOnlySpan<byte> NegativeInfinityPrefixUtf8 => "-."u8;
    public static ReadOnlySpan<byte> InfinitySuffixUtf8 => "inf"u8;
    public static ReadOnlySpan<byte> PositiveInfinitySuffixUtf8 => ".inf"u8;
    public static ReadOnlySpan<byte> NotANumberSuffixUtf8 => ".nan"u8;

    public static ReadOnlySpan<byte> EmptySequenceUtf8 => "[]"u8;
    public static ReadOnlySpan<byte> EmptyMappingUtf8 => "{}"u8;
    public static ReadOnlySpan<byte> InfinityUtf8 => ".inf"u8;
    public static ReadOnlySpan<byte> NegativeInfinityUtf8 => "-.inf"u8;
    public static ReadOnlySpan<byte> NotANumberUtf8 => ".nan"u8;

    // YAML resolves -0 as an integer, so a floating negative zero is written with a decimal point.
    public static ReadOnlySpan<byte> NegativeZeroUtf8 => "-0.0"u8;

    public static ReadOnlySpan<byte> WhiteSpace => " \t"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiDigit(byte value) => value is >= DigitZero and <= DigitNine;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiHexDigit(byte value) => HexDigits.Contains(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(byte value, bool hexadecimal) =>
        hexadecimal ? IsAsciiHexDigit(value) : IsAsciiDigit(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIndentDigit(byte value) => value is >= DigitOne and <= DigitNine;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsC1Control(int value) =>
        value is >= C1ControlFirstSecondByte and <= C1ControlLastSecondByte;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineEnd(byte value) => value is CarriageReturn or LineFeed;

    // Trivia scanning must use the same four bytes as IsWhiteSpaceOrLineEnd; other Unicode spaces are content.
    public static readonly SearchValues<byte> WhiteSpaceOrLineEndBytes = SearchValues.Create([
        Space,
        Tab,
        CarriageReturn,
        LineFeed,
    ]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteSpaceOrLineEnd(byte value) =>
        value is Space or Tab or CarriageReturn or LineFeed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFlowIndicator(byte value)
    {
        return value
            is FlowSeparator
            or FlowSequenceStart
            or FlowSequenceEnd
            or FlowMappingStart
            or FlowMappingEnd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNonPrintableCodePoint(int value)
    {
        return IsC1Control(value)
               || value
                   is LineSeparator
                   or ParagraphSeparator
                   or ByteOrderMark
                   or NonCharacterFffe
                   or NonCharacterFfff;
    }

    // Plain scalars use the YAML 1.1 boolean spellings.
    // YAML 1.1 booleans include y/n, yes/no and on/off: https://yaml.org/type/bool.html
    // Keep its exact spellings here, shared by scalar reading and string quoting; arbitrary mixed case is a string.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBooleanScalarStart(byte value) =>
        (value | 0x20) is (byte)'t' or (byte)'y' or (byte)'o' or (byte)'f' or (byte)'n';

    public static bool TryParseBooleanScalar(ReadOnlySpan<byte> text, out bool value)
    {
        if (text.IsEmpty || !IsBooleanScalarStart(text[0]))
        {
            value = default;

            return false;
        }

        value = text.Length switch
        {
            1 => text.SequenceEqual("y"u8) || text.SequenceEqual("Y"u8),
            2 => text.SequenceEqual("on"u8)
                 || text.SequenceEqual("On"u8)
                 || text.SequenceEqual("ON"u8),
            3 => text.SequenceEqual("yes"u8)
                 || text.SequenceEqual("Yes"u8)
                 || text.SequenceEqual("YES"u8),
            4 => text.SequenceEqual("true"u8)
                 || text.SequenceEqual("True"u8)
                 || text.SequenceEqual("TRUE"u8),
            _ => false,
        };

        return value
               || (
                   text.Length switch
                   {
                       1 => text.SequenceEqual("n"u8) || text.SequenceEqual("N"u8),
                       2 => text.SequenceEqual("no"u8)
                            || text.SequenceEqual("No"u8)
                            || text.SequenceEqual("NO"u8),
                       3 => text.SequenceEqual("off"u8)
                            || text.SequenceEqual("Off"u8)
                            || text.SequenceEqual("OFF"u8),
                       5 => text.SequenceEqual("false"u8)
                            || text.SequenceEqual("False"u8)
                            || text.SequenceEqual("FALSE"u8),
                       _ => false,
                   }
               );
    }

    public static bool IsNullScalar(ReadOnlySpan<byte> text) =>
        text.Length switch
        {
            0 => true,
            1 => text[0] == (byte)'~',
            4 => text.SequenceEqual("null"u8)
                 || text.SequenceEqual("Null"u8)
                 || text.SequenceEqual("NULL"u8),
            _ => false,
        };

    public static bool IsSpecialFloatScalar(ReadOnlySpan<byte> text)
    {
        return text.Length switch
        {
            4 => text[0] is Dot
                 && (
                     text.SequenceEqual(".inf"u8)
                     || text.SequenceEqual(".Inf"u8)
                     || text.SequenceEqual(".INF"u8)
                     || text.SequenceEqual(".nan"u8)
                     || text.SequenceEqual(".NaN"u8)
                     || text.SequenceEqual(".NAN"u8)
                 ),
            5 => text[0] is Plus or Dash
                 && (
                     text[1..].SequenceEqual(".inf"u8)
                     || text[1..].SequenceEqual(".Inf"u8)
                     || text[1..].SequenceEqual(".INF"u8)
                 ),
            _ => false,
        };
    }

    // Quote YAML 1.1 reserved words conservatively
    // Including mixed-case boolean words and the merge and value keys.
    // Boolean resolution accepts only the exact spellings above, merge and value keys remain strings here.
    // YAML 1.1 reserved words are ASCII; compare their UTF-8 directly without decoding each candidate string.
    public static bool IsYaml11ReservedWord(ReadOnlySpan<byte> text) =>
        text.Length switch
        {
            1 => Ascii.EqualsIgnoreCase(text, "y"u8)
                 || Ascii.EqualsIgnoreCase(text, "n"u8)
                 || text.SequenceEqual("="u8),
            2 => Ascii.EqualsIgnoreCase(text, "no"u8)
                 || Ascii.EqualsIgnoreCase(text, "on"u8)
                 || text.SequenceEqual("<<"u8),
            3 => Ascii.EqualsIgnoreCase(text, "yes"u8) || Ascii.EqualsIgnoreCase(text, "off"u8),
            _ => false,
        };

    // The only characters that can end a plain scalar
    //   - line break
    //   - "#" of a comment
    //   - ":" of a key and, in a flow collection, its indicators.
    // Whether one of them really ends the scalar depends on its neighbours.
    public static readonly SearchValues<byte> PlainBlockStops = SearchValues.Create([
        CarriageReturn,
        LineFeed,
        CommentIndicator,
        MappingValueIndicator,
    ]);

    // A plain block-map key needs closer inspection only at a separator, comment, or line end.
    public static readonly SearchValues<byte> BlockMapKeyStops = PlainBlockStops;

    public static readonly SearchValues<byte> PlainFlowStops = SearchValues.Create([
        CarriageReturn,
        LineFeed,
        CommentIndicator,
        MappingValueIndicator,
        FlowSeparator,
        FlowSequenceStart,
        FlowSequenceEnd,
        FlowMappingStart,
        FlowMappingEnd,
    ]);

    // A plain scalar must not start with one of these. The writer checks the UTF-8 bytes directly.
    public static readonly SearchValues<byte> IndicatorStarts = SearchValues.Create([
        Dash,
        ExplicitKeyIndicator,
        MappingValueIndicator,
        FlowSeparator,
        FlowSequenceStart,
        FlowSequenceEnd,
        FlowMappingStart,
        FlowMappingEnd,
        CommentIndicator,
        AnchorIndicator,
        AliasIndicator,
        TagIndicator,
        LiteralBlockIndicator,
        FoldedBlockIndicator,
        SingleQuote,
        DoubleQuote,
        DirectiveIndicator,
        ReservedAtIndicator,
        ReservedBacktickIndicator,
    ]);

    public static readonly SearchValues<byte> PlainAsciiSafeBytes = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-./"u8
    );

    public static readonly SearchValues<byte> DashDigitDot = SearchValues.Create([
        Dash,
        Dot,
        (byte)'0',
        (byte)'1',
        (byte)'2',
        (byte)'3',
        (byte)'4',
        (byte)'5',
        (byte)'6',
        (byte)'7',
        (byte)'8',
        (byte)'9',
    ]);

    // A core-schema number cannot contain bytes outside this set; use it to avoid UTF-16 conversion
    // for numeric-looking strings such as "30s" that can only be written as plain text.
    public static readonly SearchValues<byte> CoreNumberCandidateBytes = SearchValues.Create(
        "0123456789+-.eE"u8
    );

    private static readonly byte[] ExponentCharacters = [ExponentLower, ExponentUpper];

    public static readonly SearchValues<byte> Exponent = SearchValues.Create(ExponentCharacters);

    // The characters a quoted string needs escaped on top of what the JSON encoder always escapes (the C0 controls,
    // the quote and the backslash): DEL is added by the encoder policy, the C1 controls (U+0080-U+0084 and
    // U+0086-U+009F) and the non-characters U+FFFE and U+FFFF are these ranges. YAML cannot hold them as they are.
    public static readonly TextEscapeRange[] QuoteEscapeRanges =
    [
        new TextEscapeRange(firstCodePoint: 0x80, length: 5),
        new TextEscapeRange(firstCodePoint: 0x86, length: 26),
        new TextEscapeRange(firstCodePoint: 0xFFFE, length: 2),
    ];

    // The escapes a quoted YAML string needs independent of the serializer settings.
    // C0 controls and the quote characters (always escaped by the JSON encoder) and the characters YAML cannot hold:
    //   * DEL
    //   * C1 controls (U+0080-U+0084 and U+0086-U+009F)
    //   * Non-characters U+FFFE and U+FFFF.
    //
    // The text encoder policy of the settings is deliberately not used.
    public static readonly JsonTextEncoder QuoteEncoder = new TextEncoderPolicy(
        QuoteEscapeRanges,
        TextEscapeMask.Delete
    ).GetEncoder<JsonTextEncoder>();

    public static class EscapeLetter
    {
        // The letter after the backslash of a double-quoted escape sequence
        public const byte NullCharacter = (byte)'0';
        public const byte Bell = (byte)'a';
        public const byte Backspace = (byte)'b';
        public const byte Tab = (byte)'t';
        public const byte LineFeed = (byte)'n';
        public const byte VerticalTab = (byte)'v';
        public const byte FormFeed = (byte)'f';
        public const byte CarriageReturn = (byte)'r';
        public const byte EscapeControlCharacter = (byte)'e';
        public const byte Hex2Digits = (byte)'x';
        public const byte Hex4Digits = (byte)'u';
        public const byte Hex8Digits = (byte)'U';
        public const byte NextLine = (byte)'N';
        public const byte NoBreakSpace = (byte)'_';
        public const byte LineSeparator = (byte)'L';
        public const byte ParagraphSeparator = (byte)'P';
    }

    // UTF-8 ASCII controls can be searched as bytes; non-ASCII characters require their complete UTF-8 sequences.
    public static readonly SearchValues<byte> NonPrintableAscii = CreateNonPrintableAscii();

    private static SearchValues<byte> CreateNonPrintableAscii()
    {
        Span<byte> characters = stackalloc byte[Space + 1];
        for (var c = 0; c < Space; c++)
        {
            characters[c] = (byte)c;
        }

        characters[Space] = Delete;

        return SearchValues.Create(characters);
    }

    /// <summary>
    /// Search ASCII control characters.
    /// Compare non-ASCII characters by code point, never by continuation byte
    /// so the existing prohibited-character set keeps its meaning.
    /// </summary>
    /// <remarks>
    /// The caller must validate UTF-8 first.
    /// </remarks>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool ContainsNonPrintableCharacters(ReadOnlySpan<byte> value)
    {
        // Printable ASCII needs only one scan; most YAML keys and scalar values take this path.
        return value.IndexOfAnyExceptInRange(Space, (byte)(Delete - 1)) >= 0
               && (
                   value.ContainsAny(NonPrintableAscii) || ContainsNonPrintableUnicodeCharacters(value)
               );

        static bool ContainsNonPrintableUnicodeCharacters(ReadOnlySpan<byte> value)
        {
            var remaining = value;
            int nonAsciiOffset;
            while ((nonAsciiOffset = remaining.IndexOfAnyExceptInRange(NoCharacter, Delete)) >= 0)
            {
                remaining = remaining[nonAsciiOffset..];

                Rune.DecodeFromUtf8(remaining, out var character, out var consumed);

                // ASCII controls were checked by the caller; preserve the same C1 and non-printable code points.
                if (IsNonPrintableCodePoint(character.Value))
                {
                    return true;
                }

                remaining = remaining[consumed..];
            }

            return false;
        }
    }

    public static bool ContainsUnrepresentableBlockScalarCharacters(ReadOnlySpan<byte> value)
    {
        if (Ascii.IsValid(value))
        {
            // Multiline ASCII commonly contains LF, so skip allowed bytes without decoding each one as a Rune.
            while (!value.IsEmpty)
            {
                var invalidRange = value.IndexOfAnyExceptInRange(Space, (byte)(Delete - 1));
                if (invalidRange < 0)
                {
                    break;
                }

                if (value[invalidRange] is not (LineFeed or Tab))
                {
                    return true;
                }

                value = value[(invalidRange + 1)..];
            }

            return false;
        }

        while (!value.IsEmpty)
        {
            Rune.DecodeFromUtf8(value, out var character, out var consumed);

            // Preserve the control ranges, allowing only LF and TAB within those ranges.
            if (
                character.Value
                is (< Space or >= Delete and <= C1ControlLastSecondByte)
                and not (LineFeed or Tab)
            )
            {
                return true;
            }

            value = value[consumed..];
        }

        return false;
    }
}