// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using REDox.Serialization;

namespace REDox.Yaml;

internal static partial class YamlSyntax
{
    // How the characters below are named. A character that has one role in YAML is named after that role
    // (CommentIndicator, FlowSequenceStart). A character that serves several roles is named after the character itself:
    // "-" starts a sequence entry, strips a block scalar and is a minus sign. Code points are written as numbers where
    // the character itself would be invisible or confusing in the source.
    //
    // The parser reads UTF-8 bytes, so every ASCII character is a byte. The characters that are not ASCII (further
    // below) are chars: they are code points, which the parser writes as UTF-8 and the writer checks in strings.
    //
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

    // The digits an indentation indicator is made of (1 to 9), and the zero that a digit's value is counted from
    public const byte DigitZero = (byte)'0';
    public const byte DigitOne = (byte)'1';
    public const byte DigitNine = (byte)'9';

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

    // The prefixes of the hexadecimal and octal integers of the YAML 1.2 core schema ("0x1F", "0o14"). The YAML 1.1
    // forms ("0b101", "0755" as octal, "1_000", "190:20:30") are not numbers here.
    public static ReadOnlySpan<byte> HexPrefixUtf8 => "0x"u8;

    public static ReadOnlySpan<byte> OctalPrefixUtf8 => "0o"u8;

    // The designator of the UTC time zone at the end of a timestamp ("2001-12-14T21:59:43Z")
    public const string UtcDesignator = "Z";

    // A text that is a single dash: the sequence entry indicator, which cannot be a plain scalar
    public const string DashText = "-";

    // Document markers
    public const string DocumentStart = "---";
    public const string DocumentEnd = "...";

    public static ReadOnlySpan<byte> DocumentStartUtf8 => "---"u8;

    public static ReadOnlySpan<byte> DocumentEndUtf8 => "..."u8;

    // The words a plain scalar is read as null, true or false (YAML 1.2 core schema).
    private static readonly string[] NullWordList = new string[] { "null", "Null", "NULL", "~" };

    public static readonly FrozenSet<string> NullWords = NullWordList.ToFrozenSet();

    private static readonly string[] TrueWordList = new string[] { "true", "True", "TRUE" };

    public static readonly FrozenSet<string> TrueWords = TrueWordList.ToFrozenSet();

    private static readonly string[] FalseWordList = new string[] { "false", "False", "FALSE" };

    public static readonly FrozenSet<string> FalseWords = FalseWordList.ToFrozenSet();

    // Infinity and not-a-number as the core schema spells them.
    private static readonly string[] SpecialFloatWordList = new string[]
    {
        ".inf",
        ".Inf",
        ".INF",
        "+.inf",
        "+.Inf",
        "+.INF",
        "-.inf",
        "-.Inf",
        "-.INF",
        ".nan",
        ".NaN",
        ".NAN",
    };

    // The same words as UTF-8, for the parser, which compares bytes. They are made from the lists above, so the two
    // forms cannot differ. (The special floats are only read, so they only have this form.)
    public static readonly byte[][] NullWordsUtf8 = ToUtf8(NullWordList);

    public static readonly byte[][] TrueWordsUtf8 = ToUtf8(TrueWordList);

    public static readonly byte[][] FalseWordsUtf8 = ToUtf8(FalseWordList);

    public static readonly byte[][] SpecialFloatWordsUtf8 = ToUtf8(SpecialFloatWordList);

    private static byte[][] ToUtf8(string[] words)
    {
        return Array.ConvertAll(words, word => System.Text.Encoding.UTF8.GetBytes(word));
    }

    // Whether the bytes are one of the words.
    public static bool IsOneOf(ReadOnlySpan<byte> text, byte[][] words)
    {
        foreach (var word in words)
        {
            if (text.SequenceEqual(word))
                return true;
        }

        return false;
    }

    // Used to tell the sign and the kind of a special float apart (compared ignoring case).
    public const string NegativeInfinityPrefix = "-.";
    public const string InfinitySuffix = "inf";
    public const string PositiveInfinitySuffix = ".inf";
    public const string NotANumberSuffix = ".nan";

    // Words that only YAML 1.1 parsers read as something other than a string (booleans, and the merge and value
    // keys). This library reads them as strings, but a string equal to one is quoted so that it survives them.
    private static readonly string[] Yaml11WordList = new string[]
    {
        "y",
        "n",
        "yes",
        "no",
        "on",
        "off",
        "=",
        "<<",
    };

    public static readonly FrozenSet<string> Yaml11Words = Yaml11WordList.ToFrozenSet(
        StringComparer.OrdinalIgnoreCase
    );

    // Text the writer emits as it is, as UTF-8. A literal is the only allocation-free way to hold it.
    public static ReadOnlySpan<byte> EmptySequenceUtf8 => "[]"u8;

    public static ReadOnlySpan<byte> EmptyMappingUtf8 => "{}"u8;

    public static ReadOnlySpan<byte> InfinityUtf8 => ".inf"u8;

    public static ReadOnlySpan<byte> NegativeInfinityUtf8 => "-.inf"u8;

    public static ReadOnlySpan<byte> NotANumberUtf8 => ".nan"u8;

    // YAML resolves -0 as an integer, so a floating negative zero is written with a decimal point.
    public static ReadOnlySpan<byte> NegativeZeroUtf8 => "-0.0"u8;

    // White space as YAML defines it, for Trim.
    public static ReadOnlySpan<byte> WhiteSpace => " \t"u8;

    // The only characters that can end a plain scalar: a line break, the "#" of a comment, the ":" of a key and, in
    // a flow collection, its indicators. Whether one of them really ends the scalar depends on its neighbours.
    private static readonly byte[] PlainBlockStopCharacters = new byte[]
    {
        CarriageReturn,
        LineFeed,
        CommentIndicator,
        MappingValueIndicator,
    };

    public static readonly SearchValues<byte> PlainBlockStops = SearchValues.Create(
        PlainBlockStopCharacters
    );

    private static readonly byte[] PlainFlowStopCharacters = new byte[]
    {
        CarriageReturn,
        LineFeed,
        CommentIndicator,
        MappingValueIndicator,
        FlowSeparator,
        FlowSequenceStart,
        FlowSequenceEnd,
        FlowMappingStart,
        FlowMappingEnd,
    };

    public static readonly SearchValues<byte> PlainFlowStops = SearchValues.Create(
        PlainFlowStopCharacters
    );

    // A plain scalar must not start with one of these. The writer checks strings, so it uses the set of chars.
    private static readonly byte[] IndicatorStartCharacters = new byte[]
    {
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
    };

    public static readonly SearchValues<char> IndicatorStarts = SearchValues.Create(
        ToChars(IndicatorStartCharacters)
    );

    private static readonly byte[] DashDigitDotCharacters = new byte[]
    {
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
    };

    public static readonly SearchValues<char> DashDigitDot = SearchValues.Create(
        ToChars(DashDigitDotCharacters)
    );

    private static readonly byte[] ExponentCharacters = new byte[] { ExponentLower, ExponentUpper };

    public static readonly SearchValues<byte> Exponent = SearchValues.Create(ExponentCharacters);

    private static char[] ToChars(byte[] ascii)
    {
        return Array.ConvertAll(ascii, value => (char)value);
    }

    // The characters a quoted string needs escaped on top of what the JSON encoder always escapes (the C0 controls,
    // the quote and the backslash): DEL is added by the encoder policy, the C1 controls (U+0080-U+0084 and
    // U+0086-U+009F) and the non-characters U+FFFE and U+FFFF are these ranges. YAML cannot hold them as they are.
    public static readonly TextEscapeRange[] QuoteEscapeRanges = new TextEscapeRange[]
    {
        new TextEscapeRange(0x80, 5),
        new TextEscapeRange(0x86, 26),
        new TextEscapeRange(0xFFFE, 2),
    };

    // The non-printable characters beyond the control ranges.
    private static readonly char[] OtherNonPrintableCharacters = new char[]
    {
        LineSeparator,
        ParagraphSeparator,
        ByteOrderMark,
        NonCharacterFffe,
        NonCharacterFfff,
    };

    // Characters that are not printable in YAML: the C0 controls (line breaks and tabs included, which need an
    // escape or a block scalar), DEL, the C1 controls, the line and paragraph separators, the byte order mark and the
    // non-characters U+FFFE and U+FFFF.
    public static readonly SearchValues<char> NonPrintable = CreateNonPrintable();

    // The controls a block scalar cannot hold: every control character except the line feed and the tab.
    public static readonly SearchValues<char> UnrepresentableInBlockScalar =
        CreateUnrepresentableInBlockScalar();

    // Whether a character is one of the five indicators of a flow collection: the separator and the start and end
    // of a flow sequence and of a flow mapping.
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

    // Whether a character is YAML white space or a line break. The only white space YAML knows is the space and the
    // tab. Other Unicode white space (such as U+3000, the ideographic space) is ordinary text and must be kept, so
    // char.IsWhiteSpace is not used.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteSpaceOrLineEnd(byte value)
    {
        return value is Space or Tab or CarriageReturn or LineFeed;
    }

    // The same for the chars of a string that is about to be written.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteSpaceOrLineEnd(char value)
    {
        return value <= byte.MaxValue && IsWhiteSpaceOrLineEnd((byte)value);
    }

    // Numbers as the YAML 1.2 core schema reads them (decimal integers and floats). Other parsers resolve these
    // as numbers even where this library's parser would not, e.g. ".5" and "5.".
    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    public static partial Regex CoreNumberPattern();

    // The timestamp forms of YAML 1.1: "2001-12-14", "2001-12-14T21:59:43.10-05:00" and "2001-12-14 21:59:43.10 -5".
    // Groups: 1 year, 2 month, 3 day, 4 hour, 5 minute, 6 second, 7 fraction (without the dot), 8 zone.
    [GeneratedRegex(
        @"^([0-9]{4})-([0-9]{1,2})-([0-9]{1,2})(?:(?:[Tt]|[ \t]+)([0-9]{1,2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]*))?(?:[ \t]*(Z|[-+][0-9]{1,2}(?::[0-9]{2})?))?)?$"
    )]
    public static partial Regex TimestampPattern();

    private static SearchValues<char> CreateNonPrintable()
    {
        var characters = new List<char>();
        for (var c = 0; c < 0x20; c++)
            characters.Add((char)c);
        for (var c = 0x7f; c <= 0x9f; c++)
            characters.Add((char)c);
        characters.AddRange(OtherNonPrintableCharacters);
        return SearchValues.Create(characters.ToArray());
    }

    private static SearchValues<char> CreateUnrepresentableInBlockScalar()
    {
        var characters = new List<char>();
        for (var c = 0; c < 0xa0; c++)
        {
            if (char.IsControl((char)c) && c != LineFeed && c != Tab)
                characters.Add((char)c);
        }

        return SearchValues.Create(characters.ToArray());
    }
}
