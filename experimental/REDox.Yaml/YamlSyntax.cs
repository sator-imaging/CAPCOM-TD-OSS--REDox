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

    // The block scalar header has one indentation digit, from 1 through 9.
    public const int MaxBlockScalarIndentIndicator = DigitNine - DigitZero;

    // YAML 1.2 defines the !!timestamp date format as YYYY-MM-DD (acepts only year-first format).
    public const int TimestampYearDigits = 4;
    public const int MinimumTimestampLength = 8;

    // Prefix letters are shared by integer recognition and integer decoding.
    public const byte BinaryPrefixLetter = (byte)'b';
    public const byte HexPrefixLetter = (byte)'x';
    public const byte OctalPrefixLetter = (byte)'o';

    // A plain null scalar can begin with the abbreviation or either permitted case of "null".
    public const byte NullAbbreviation = (byte)'~';
    public const byte NullLowercaseInitial = (byte)'n';
    public const byte NullUppercaseInitial = (byte)'N';
    public const byte NullSecondLetter = (byte)'u';

    // Keep UTF-8 literals as ReadOnlySpan properties so the compiler can use static data without allocating arrays.
    public static ReadOnlySpan<byte> NullLowercaseUtf8 => "null"u8;
    public static ReadOnlySpan<byte> NullCapitalizedUtf8 => "Null"u8;
    public static ReadOnlySpan<byte> NullUppercaseUtf8 => "NULL"u8;

    // YAML 1.1 accepts these exact boolean spellings; mixed case remains a string.
    public const byte YesFirstLetter = (byte)'y';
    public const byte TrueFirstLetter = (byte)'t';
    public const byte OnOffFirstLetter = (byte)'o';
    public const byte FalseFirstLetter = (byte)'f';

    public static ReadOnlySpan<byte> YesLowercaseUtf8 => "yes"u8;
    public static ReadOnlySpan<byte> YesCapitalizedUtf8 => "Yes"u8;
    public static ReadOnlySpan<byte> YesUppercaseUtf8 => "YES"u8;
    public static ReadOnlySpan<byte> TrueLowercaseUtf8 => "true"u8;
    public static ReadOnlySpan<byte> TrueCapitalizedUtf8 => "True"u8;
    public static ReadOnlySpan<byte> TrueUppercaseUtf8 => "TRUE"u8;
    public static ReadOnlySpan<byte> OnLowercaseUtf8 => "on"u8;
    public static ReadOnlySpan<byte> OnCapitalizedUtf8 => "On"u8;
    public static ReadOnlySpan<byte> OnUppercaseUtf8 => "ON"u8;
    public static ReadOnlySpan<byte> OffLowercaseUtf8 => "off"u8;
    public static ReadOnlySpan<byte> OffCapitalizedUtf8 => "Off"u8;
    public static ReadOnlySpan<byte> OffUppercaseUtf8 => "OFF"u8;
    public static ReadOnlySpan<byte> NoLowercaseUtf8 => "no"u8;
    public static ReadOnlySpan<byte> NoCapitalizedUtf8 => "No"u8;
    public static ReadOnlySpan<byte> NoUppercaseUtf8 => "NO"u8;
    public static ReadOnlySpan<byte> FalseLowercaseUtf8 => "false"u8;
    public static ReadOnlySpan<byte> FalseCapitalizedUtf8 => "False"u8;
    public static ReadOnlySpan<byte> FalseUppercaseUtf8 => "FALSE"u8;

    public const byte ValueKeyIndicator = (byte)'=';
    public static ReadOnlySpan<byte> MergeKeyUtf8 => "<<"u8;

    // The first and last letters reject ordinary numeric text before comparing a whole special-float spelling.
    public const byte InfinityFirstLetter = (byte)'i';
    public const byte NotANumberFirstLetter = (byte)'n';
    public const byte InfinityLastLetter = (byte)'f';
    public const byte NotANumberLastLetter = (byte)'n';

    // ASCII case folding is used only after the parser has narrowed a spelling to ASCII.
    public const byte AsciiLowercaseMask = 0x20;

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
    public const byte C1ControlBeforeNextLineLastSecondByte = 0x84;
    public const byte C1ControlAfterNextLineFirstSecondByte = 0x86;
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

    // YAML 1.2.2 §9.1.2 treats these as markers only when followed by a line break, white space, or EOF.
    // Keep the marker at exactly three bytes: "----" starts a plain scalar, not a document.
    // https://yaml.org/spec/1.2.2/#912-document-markers
    public static ReadOnlySpan<byte> DocumentStartUtf8 => "---"u8;

    public static ReadOnlySpan<byte> DocumentEndUtf8 => "..."u8;

    // Used to tell the sign and the kind of a special float apart (compared ignoring case).
    public static ReadOnlySpan<byte> NegativeInfinityPrefixUtf8 => "-."u8;
    public static ReadOnlySpan<byte> InfinitySuffixUtf8 => "inf"u8;
    public static ReadOnlySpan<byte> CapitalizedInfinityUtf8 => ".Inf"u8;
    public static ReadOnlySpan<byte> UppercaseInfinityUtf8 => ".INF"u8;
    public static ReadOnlySpan<byte> CapitalizedNotANumberUtf8 => ".NaN"u8;
    public static ReadOnlySpan<byte> UppercaseNotANumberUtf8 => ".NAN"u8;

    public static ReadOnlySpan<byte> EmptySequenceUtf8 => "[]"u8;
    public static ReadOnlySpan<byte> EmptyMappingUtf8 => "{}"u8;
    public static ReadOnlySpan<byte> InfinityUtf8 => ".inf"u8;
    public static ReadOnlySpan<byte> NegativeInfinityUtf8 => "-.inf"u8;
    public static ReadOnlySpan<byte> NotANumberUtf8 => ".nan"u8;

    // YAML resolves -0 as an integer, so a floating negative zero is written with a decimal point.
    public static ReadOnlySpan<byte> NegativeZeroUtf8 => "-0.0"u8;

    public static ReadOnlySpan<byte> WhiteSpace => " \t"u8;

    // Quoted key lookahead must stop at escapes and line breaks as well as the matching quote.
    public static ReadOnlySpan<byte> DoubleQuotedKeyStops => "\"\\\r\n"u8;
    public static ReadOnlySpan<byte> SingleQuotedKeyStops => "'\r\n"u8;

    // Plain keys have stricter quote handling than plain values.
    public static ReadOnlySpan<byte> PlainKeySyntaxCharacters => ":#'\""u8;
    public static ReadOnlySpan<byte> PlainValueSyntaxCharacters => ":#"u8;

    // Comments between physical lines end a plain scalar instead of joining its continuation.
    public static readonly SearchValues<byte> PlainContinuationStops = SearchValues.Create([
        CommentIndicator,
        CarriageReturn,
        LineFeed,
    ]);

    // These bounds are the printable ranges in YAML 1.2.2, section 5.1.
    public const byte PrintableAsciiLast = (byte)'~';
    public const int FirstNonAsciiByte = 0x80;
    public const int PrintableBmpBeforeSurrogatesLast = 0xd7ff;
    public const int PrintableBmpAfterSurrogatesFirst = 0xe000;
    public const int PrintableBmpAfterSurrogatesLast = 0xfffd;
    public const int PrintableSupplementaryFirst = 0x10000;
    public const int PrintableSupplementaryLast = 0x10ffff;

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

    // Line scans must treat CR-only, LF-only, and CRLF input using the same break bytes.
    public static readonly SearchValues<byte> LineEndBytes = SearchValues.Create([
        CarriageReturn,
        LineFeed,
    ]);

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
        (value | AsciiLowercaseMask)
        is TrueFirstLetter
        or YesFirstLetter
        or OnOffFirstLetter
        or FalseFirstLetter
        or NullLowercaseInitial;

    public static bool TryParseBooleanScalar(ReadOnlySpan<byte> text, out bool value)
    {
        value = false;

        if (text.IsEmpty)
        {
            return false;
        }

        // TODO: This method strictly follows the YAML 1.1 spec (yES, faLSE are not boolean).
        //       YAML 1.2 only supports 'true' and 'false' as boolean (case insensitive),
        //       so this check can be dropped or simplified by ignoring casing.

        // Each spelling family has its own first letter except on/off. Dispatch once so negative
        // spellings and ordinary keys do not pass through separate true and false length tables.
        switch ((byte)(text[0] | AsciiLowercaseMask))
        {
            case YesFirstLetter:
                value =
                    text.Length == 1
                    || text.Length == 3
                    && (
                        text.SequenceEqual(YesLowercaseUtf8)
                        || text.SequenceEqual(YesCapitalizedUtf8)
                        || text.SequenceEqual(YesUppercaseUtf8)
                    );

                return value;

            case TrueFirstLetter:
                value =
                    text.Length == 4
                    && (
                        text.SequenceEqual(TrueLowercaseUtf8)
                        || text.SequenceEqual(TrueCapitalizedUtf8)
                        || text.SequenceEqual(TrueUppercaseUtf8)
                    );

                return value;

            case OnOffFirstLetter:
                if (text.Length == 2)
                {
                    value =
                        text.SequenceEqual(OnLowercaseUtf8)
                        || text.SequenceEqual(OnCapitalizedUtf8)
                        || text.SequenceEqual(OnUppercaseUtf8);

                    return value;
                }

                return text.Length == 3
                       && (
                           text.SequenceEqual(OffLowercaseUtf8)
                           || text.SequenceEqual(OffCapitalizedUtf8)
                           || text.SequenceEqual(OffUppercaseUtf8)
                       );

            case NullLowercaseInitial:
                return text.Length == 1
                       || text.Length == 2
                       && (
                           text.SequenceEqual(NoLowercaseUtf8)
                           || text.SequenceEqual(NoCapitalizedUtf8)
                           || text.SequenceEqual(NoUppercaseUtf8)
                       );

            case FalseFirstLetter:
                return text.Length == 5
                       && (
                           text.SequenceEqual(FalseLowercaseUtf8)
                           || text.SequenceEqual(FalseCapitalizedUtf8)
                           || text.SequenceEqual(FalseUppercaseUtf8)
                       );

            default:
                return false;
        }
    }

    public static bool IsNullScalar(ReadOnlySpan<byte> text) =>
        text.Length switch
        {
            0 => true,
            1 => text[0] == NullAbbreviation,
            // Ordinary four-byte keys can be rejected before comparing all three null spellings.
            4 => (text[0] | AsciiLowercaseMask) == NullLowercaseInitial
                 && (text[1] | AsciiLowercaseMask) == NullSecondLetter
                 && (
                     text.SequenceEqual(NullLowercaseUtf8)
                     || text.SequenceEqual(NullCapitalizedUtf8)
                     || text.SequenceEqual(NullUppercaseUtf8)
                 ),
            _ => false,
        };

    public static bool IsSpecialFloatScalar(ReadOnlySpan<byte> text)
    {
        return text.Length switch
        {
            // Numeric-looking text such as ".123" or "-1234" cannot spell a special float.
            4 => text[0] is Dot
                 && (text[1] | AsciiLowercaseMask) is InfinityFirstLetter or NotANumberFirstLetter
                 && (
                     text.SequenceEqual(InfinityUtf8)
                     || text.SequenceEqual(CapitalizedInfinityUtf8)
                     || text.SequenceEqual(UppercaseInfinityUtf8)
                     || text.SequenceEqual(NotANumberUtf8)
                     || text.SequenceEqual(CapitalizedNotANumberUtf8)
                     || text.SequenceEqual(UppercaseNotANumberUtf8)
                 ),
            5 => text[0] is Plus or Dash
                 && text[1] == Dot
                 && (text[2] | AsciiLowercaseMask) == InfinityFirstLetter
                 && (
                     text[1..].SequenceEqual(InfinityUtf8)
                     || text[1..].SequenceEqual(CapitalizedInfinityUtf8)
                     || text[1..].SequenceEqual(UppercaseInfinityUtf8)
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
            // Most short keys cannot match a reserved word, so reject them before comparing whole words.
            1 => (text[0] | AsciiLowercaseMask) is YesFirstLetter or NullLowercaseInitial
                 || text[0] == ValueKeyIndicator,
            2 => (text[0] | AsciiLowercaseMask) == NullLowercaseInitial
                 && Ascii.EqualsIgnoreCase(text, NoLowercaseUtf8)
                 || (text[0] | AsciiLowercaseMask) == OnOffFirstLetter
                 && Ascii.EqualsIgnoreCase(text, OnLowercaseUtf8)
                 || text.SequenceEqual(MergeKeyUtf8),
            3 => (text[0] | AsciiLowercaseMask) == YesFirstLetter
                 && Ascii.EqualsIgnoreCase(text, YesLowercaseUtf8)
                 || (text[0] | AsciiLowercaseMask) == OnOffFirstLetter
                 && Ascii.EqualsIgnoreCase(text, OffLowercaseUtf8),
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
        new TextEscapeRange(
            firstCodePoint: C1ControlFirstSecondByte,
            length: C1ControlBeforeNextLineLastSecondByte - C1ControlFirstSecondByte + 1
        ),
        new TextEscapeRange(
            firstCodePoint: C1ControlAfterNextLineFirstSecondByte,
            length: C1ControlLastSecondByte - C1ControlAfterNextLineFirstSecondByte + 1
        ),
        new TextEscapeRange(
            firstCodePoint: NonCharacterFffe,
            length: NonCharacterFfff - NonCharacterFffe + 1
        ),
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
    public static readonly SearchValues<byte> NonPrintableAscii =
        BuildNonPrintableAsciiSearchValues();

    private static SearchValues<byte> BuildNonPrintableAsciiSearchValues()
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
        while (!value.IsEmpty)
        {
            // Scan printable ASCII in one step, including when it follows a multibyte character.
            // This avoids a separate full scan for ASCII controls before checking Unicode.
            var candidateOffset = value.IndexOfAnyExceptInRange(Space, (byte)(Delete - 1));
            if (candidateOffset < 0)
            {
                return false;
            }

            value = value[candidateOffset..];

            if (value[0] < FirstNonAsciiByte)
            {
                return NonPrintableAscii.Contains(value[0]);
            }

            Rune.DecodeFromUtf8(value, out var character, out var consumed);

            if (IsNonPrintableCodePoint(character.Value))
            {
                return true;
            }

            value = value[consumed..];
        }

        return false;
    }

    public static bool ContainsUnrepresentableBlockScalarCharacters(ReadOnlySpan<byte> value)
    {
        while (!value.IsEmpty)
        {
            // Skip printable ASCII in one search without first scanning the whole value for non-ASCII.
            var candidateOffset = value.IndexOfAnyExceptInRange(Space, (byte)(Delete - 1));
            if (candidateOffset < 0)
            {
                return false;
            }

            value = value[candidateOffset..];

            if (value[0] < FirstNonAsciiByte)
            {
                if (value[0] is not (LineFeed or Tab))
                {
                    return true;
                }

                value = value[1..];

                continue;
            }

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