// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private ParsedScalar ParseDoubleQuotedString(int parentIndent)
    {
        _position++;

        var scalar = BeginScalar(true);

        // Whitespace produced by escapes must survive the trimming done at line breaks.
        // it is kept from this length.
        var protectedLength = 0;
        while (_position < Source.Length)
        {
            var current = Source[_position++];
            if (current == YamlSyntax.DoubleQuote)
            {
                return scalar;
            }

            if (current != YamlSyntax.Backslash)
            {
                if (IsLineEnd(current))
                {
                    _position--;

                    FoldQuotedBreak(scalar, protectedLength, parentIndent);

                    continue;
                }

                if (IsUnprintableStart(current))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                _scalars.Append(current);

                continue;
            }

            if (_position >= Source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidString);
            }

            current = Source[_position++];

            // TODO: Working but it seems so bad.
            //       Comprehensive update is required to improve performance.
            switch (current)
            {
                case YamlSyntax.EscapeLetter.NullCharacter:
                    _scalars.Append(YamlSyntax.NoCharacter);

                    break;

                case YamlSyntax.EscapeLetter.Bell:
                    _scalars.Append(YamlSyntax.Bell);

                    break;

                case YamlSyntax.EscapeLetter.Backspace:
                    _scalars.Append(YamlSyntax.Backspace);

                    break;

                case YamlSyntax.EscapeLetter.Tab:
                case YamlSyntax.Tab:
                    _scalars.Append(YamlSyntax.Tab);

                    break;

                case YamlSyntax.EscapeLetter.LineFeed:
                    _scalars.Append(YamlSyntax.LineFeed);

                    break;

                case YamlSyntax.EscapeLetter.VerticalTab:
                    _scalars.Append(YamlSyntax.VerticalTab);

                    break;

                case YamlSyntax.EscapeLetter.FormFeed:
                    _scalars.Append(YamlSyntax.FormFeed);

                    break;

                case YamlSyntax.EscapeLetter.CarriageReturn:
                    _scalars.Append(YamlSyntax.CarriageReturn);

                    break;

                case YamlSyntax.EscapeLetter.EscapeControlCharacter:
                    _scalars.Append(YamlSyntax.EscapeControlCharacter);

                    break;

                case YamlSyntax.Space:
                    _scalars.Append(YamlSyntax.Space);

                    break;

                case YamlSyntax.DoubleQuote:
                    _scalars.Append(YamlSyntax.DoubleQuote);

                    break;

                case YamlSyntax.Slash:
                    _scalars.Append(YamlSyntax.Slash);

                    break;

                case YamlSyntax.Backslash:
                    _scalars.Append(YamlSyntax.Backslash);

                    break;

                case YamlSyntax.CarriageReturn:
                    if (_position < Source.Length && Source[_position] == YamlSyntax.LineFeed)
                    {
                        _position++;
                    }

                    SkipInlineSpaces();

                    break;

                case YamlSyntax.LineFeed:
                    SkipInlineSpaces();

                    break;

                case YamlSyntax.EscapeLetter.NextLine:
                    _scalars.AppendCodePoint(YamlSyntax.NextLine);

                    break;

                case YamlSyntax.EscapeLetter.NoBreakSpace:
                    _scalars.AppendCodePoint(YamlSyntax.NoBreakSpace);

                    break;

                case YamlSyntax.EscapeLetter.LineSeparator:
                    _scalars.AppendCodePoint(YamlSyntax.LineSeparator);

                    break;

                case YamlSyntax.EscapeLetter.ParagraphSeparator:
                    _scalars.AppendCodePoint(YamlSyntax.ParagraphSeparator);

                    break;

                case YamlSyntax.EscapeLetter.Hex2Digits:
                    _scalars.AppendCodePoint(ParseEscapedCodePoint(2));

                    break;

                case YamlSyntax.EscapeLetter.Hex4Digits:
                case YamlSyntax.EscapeLetter.Hex8Digits:
                    var codePoint = ParseEscapedCodePoint(
                        current == YamlSyntax.EscapeLetter.Hex4Digits ? 4 : 8
                    );

                    if (codePoint is >= 0xd800 and <= 0xdbff)
                    {
                        // A high surrogate is only valid when the escape of its low surrogate follows.
                        if (
                            _position + 2 > Source.Length
                            || Source[_position + 1] != YamlSyntax.EscapeLetter.Hex4Digits
                            || Source[_position] != YamlSyntax.Backslash
                        )
                        {
                            ThrowParseError(ParseException.ErrorCode.InvalidString);
                        }

                        _position += 2;

                        var low = ParseEscapedCodePoint(4);
                        if (low is < 0xdc00 or > 0xdfff)
                        {
                            ThrowParseError(ParseException.ErrorCode.InvalidString);
                        }

                        codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + low - 0xdc00;
                    }
                    // Eight hexadecimal digits can parse as a negative int; reject them before constructing a Rune.
                    else if (codePoint is >= 0xdc00 and <= 0xdfff || codePoint is < 0 or > 0x10ffff)
                    {
                        ThrowParseError(ParseException.ErrorCode.InvalidString);
                    }

                    _scalars.AppendCodePoint(codePoint);

                    break;

                default:
                    ThrowParseError(ParseException.ErrorCode.InvalidString);

                    break;
            }

            protectedLength = _scalars.Length - scalar.Start;
        }

        throw ParseError(ParseException.ErrorCode.InvalidString);
    }

    // Whether a byte of a double-quoted scalar, which is neither a line break nor the start of an escape,
    // is the start of a character that cannot appear as it is.
    // A control character (the tab is allowed), which is a C0 control, the delete character or a C1 control
    // (the lead byte 0xC2 and a second byte from 0x80 to 0x9F).
    // The next line character (U+0085) is a C1 control that is printable in YAML 1.2, so it may appear as it is.
    private bool IsUnprintableStart(byte current)
    {
        if (current < YamlSyntax.Space)
        {
            return current != YamlSyntax.Tab;
        }

        if (current == YamlSyntax.Delete)
        {
            return true;
        }

        if (current != YamlSyntax.C1ControlLeadByte || _position >= Source.Length)
        {
            return false;
        }

        var second = Source[_position];

        return YamlSyntax.IsC1Control(second) && second != (byte)YamlSyntax.NextLine;
    }

    // Folds a line break inside a quoted scalar.
    //   * One break becomes a space, N breaks become N-1 newlines.
    //   * Trailing whitespace before the break and leading whitespace after it are dropped.
    private void FoldQuotedBreak(ParsedScalar scalar, int protectedLength, int parentIndent)
    {
        var end = _scalars.Length;
        var protectedEnd = scalar.Start + protectedLength;
        while (end > protectedEnd && _scalars[end - 1] is YamlSyntax.Space or YamlSyntax.Tab)
        {
            end--;
        }

        _scalars.TruncateTo(end);

        var breaks = 0;
        while (_position < Source.Length && IsLineEnd(Source[_position]))
        {
            ConsumeLineEnd();

            breaks++;

            CheckQuotedContinuation(parentIndent);
            SkipInlineSpaces();
        }

        if (breaks == 1)
        {
            _scalars.Append(YamlSyntax.Space);
        }
        else
        {
            _scalars.Append(YamlSyntax.LineFeed, breaks - 1);
        }
    }

    // A line that continues a quoted scalar must be indented deeper than the parent
    // (tabs do not count as indentation), and a document marker cannot appear inside the scalar.
    private void CheckQuotedContinuation(int parentIndent)
    {
        if (IsDocumentMarkerAt(_position))
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        var indent = CountIndent(_position);
        var content = _position + indent;
        while (content < Source.Length && Source[content] is YamlSyntax.Space or YamlSyntax.Tab)
        {
            content++;
        }

        // A blank line has no indentation to check.
        if (content >= Source.Length || IsLineEnd(Source[content]))
        {
            return;
        }

        if (indent <= parentIndent)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }
    }

    private int ParseEscapedCodePoint(int digits)
    {
        if (
            _position + digits > Source.Length
            || !int.TryParse(
                Source.Slice(_position, digits),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var codePoint
            )
        )
        {
            throw ParseError(ParseException.ErrorCode.InvalidString);
        }

        _position += digits;

        return codePoint;
    }

    private ParsedScalar ParseSingleQuotedString(int parentIndent)
    {
        _position++;

        var scalar = BeginScalar(true);
        while (_position < Source.Length)
        {
            var current = Source[_position++];
            if (current == YamlSyntax.SingleQuote)
            {
                if (_position < Source.Length && Source[_position] == YamlSyntax.SingleQuote)
                {
                    _scalars.Append(YamlSyntax.SingleQuote);

                    _position++;
                }
                else
                {
                    return scalar;
                }
            }
            else if (IsLineEnd(current))
            {
                _position--;

                FoldQuotedBreak(scalar, 0, parentIndent);
            }
            else
            {
                _scalars.Append(current);
            }
        }

        throw ParseError(ParseException.ErrorCode.InvalidString);
    }

    // Whether a flow indicator, or the ":" that separates a key from its value, starts at the position.
    private bool IsFlowTokenAt(int position)
    {
        var current = Source[position];
        if (YamlSyntax.IsFlowIndicator(current))
        {
            return true;
        }

        return current == YamlSyntax.MappingValueIndicator
            && (
                position + 1 == Source.Length
                || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position + 1])
                || Source[position + 1]
                    is YamlSyntax.FlowSeparator
                        or YamlSyntax.FlowSequenceEnd
                        or YamlSyntax.FlowMappingEnd
            );
    }

    // TODO: Optimize
    private ParsedScalar ParsePlainString(bool key, bool flow, int parentIndent)
    {
        // A plain scalar cannot start with "- "
        // Reaching here means a sequence entry in an invalid position.
        // In a flow collection a lone "-" (as in "[-]" or "[-, -]") is invalid as well.
        if (
            IsBlockSequenceIndicator(_position)
            || flow
                && Source[_position] == YamlSyntax.Dash
                && Source[_position + 1]
                    is YamlSyntax.FlowSeparator
                        or YamlSyntax.FlowSequenceEnd
                        or YamlSyntax.FlowMappingEnd
        )
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        var start = _position;
        var stops = flow ? YamlSyntax.PlainFlowStops : YamlSyntax.PlainBlockStops;
        while (_position < Source.Length)
        {
            // Only the stop characters can end the scalar, so everything in between is skipped in one step.
            var offset = Source[_position..].IndexOfAny(stops);
            if (offset < 0)
            {
                _position = Source.Length;

                break;
            }

            _position += offset;

            var current = Source[_position];
            if (IsLineEnd(current) || (flow && YamlSyntax.IsFlowIndicator(current)))
            {
                break;
            }

            if (
                current == YamlSyntax.CommentIndicator
                && (_position == start || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position - 1]))
            )
            {
                break;
            }

            // The colon of "key: value" ends a plain scalar
            // A key included, only when white space follows it.
            // Any other colon is part of the text ("a:b", "key::: value").
            if (
                current == YamlSyntax.MappingValueIndicator
                && (
                    _position + 1 == Source.Length
                    || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position + 1])
                    // Outside a flow collection a flow indicator after the colon is ordinary text (":,").
                    || flow
                        && Source[_position + 1]
                            is YamlSyntax.FlowSeparator
                                or YamlSyntax.FlowMappingEnd
                                or YamlSyntax.FlowSequenceEnd
                )
            )
            {
                break;
            }

            _position++;
        }

        var scalar = BeginScalar(false);

        _scalars.Append(Source[start.._position].Trim(YamlSyntax.WhiteSpace));

        // A plain scalar continues on the next lines
        // In a block as well as in a flow collection. Only an implicit block key has to stay on one line.
        if (!key || flow)
        {
            while (_position < Source.Length && IsLineEnd(Source[_position]))
            {
                var lineEnd = _position;

                ConsumeLineEnd();

                var (Position, Indent) = PeekNextContent();

                // A comment line ends the scalar: it cannot continue after the comment.
                if (
                    Indent <= parentIndent
                    || Position >= Source.Length
                    || Indent == 0 && IsDocumentMarkerAt(Position)
                    || Source
                        .Slice(lineEnd, Position - lineEnd)
                        .Contains(YamlSyntax.CommentIndicator)
                    // A line that starts with ",", "}" or ": " belongs to the collection, not to the scalar.
                    || flow && IsFlowTokenAt(Position)
                )
                {
                    _position = lineEnd;

                    break;
                }

                // A single physical break folds to a space; additional blank lines remain line breaks.
                // Do not add another separator before the continuation: the break already supplies it.
                var breaks = 0;
                for (var position = lineEnd; position < Position; position++)
                {
                    if (
                        Source[position] == YamlSyntax.LineFeed
                        || Source[position] == YamlSyntax.CarriageReturn
                            && (
                                position + 1 == Source.Length
                                || Source[position + 1] != YamlSyntax.LineFeed
                            )
                    )
                    {
                        breaks++;
                    }
                }

                if (breaks == 1)
                {
                    _scalars.Append(YamlSyntax.Space);
                }
                else
                {
                    _scalars.Append(YamlSyntax.LineFeed, breaks - 1);
                }

                _position = Position;

                var continuationStart = _position;
                while (_position < Source.Length)
                {
                    var offset = Source[_position..].IndexOfAny(stops);
                    if (offset < 0)
                    {
                        _position = Source.Length;

                        break;
                    }

                    _position += offset;

                    var current = Source[_position];
                    if (IsLineEnd(current))
                    {
                        break;
                    }

                    // " #" starts a comment, which ends the scalar.
                    if (
                        current == YamlSyntax.CommentIndicator
                        && _position > continuationStart
                        && Source[_position - 1] is YamlSyntax.Space or YamlSyntax.Tab
                    )
                    {
                        break;
                    }

                    // A flow indicator ends the scalar.
                    if (flow && YamlSyntax.IsFlowIndicator(current))
                    {
                        break;
                    }

                    // In a flow collection ": " ends a key. In a block a continuation line cannot hold a mapping
                    // key ("this\n is\n  invalid: x").
                    if (
                        current == YamlSyntax.MappingValueIndicator
                        && (
                            _position + 1 == Source.Length
                            || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position + 1])
                            || flow
                                && Source[_position + 1]
                                    is YamlSyntax.FlowSeparator
                                        or YamlSyntax.FlowMappingEnd
                                        or YamlSyntax.FlowSequenceEnd
                        )
                    )
                    {
                        if (flow)
                        {
                            break;
                        }

                        ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                    }

                    _position++;
                }

                _scalars.Append(
                    Source
                        .Slice(continuationStart, _position - continuationStart)
                        .Trim(YamlSyntax.WhiteSpace)
                );

                if (_position < Source.Length && !IsLineEnd(Source[_position]))
                {
                    break;
                }
            }
        }

        if (_scalars.Length == scalar.Start && _parseOptions.EnableValueValidation)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        return scalar;
    }

    // TODO: Optimize
    private ParsedScalar ParseBlockString(int parentIndent)
    {
        var style = Source[_position++];
        var chomping = YamlSyntax.NoCharacter;
        var explicitIndent = 0;
        while (_position < Source.Length && !IsLineEnd(Source[_position]))
        {
            var current = Source[_position++];
            if (current is YamlSyntax.Plus or YamlSyntax.Dash)
            {
                if (chomping != YamlSyntax.NoCharacter)
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                chomping = current;
            }
            else if (YamlSyntax.IsIndentDigit(current))
            {
                if (explicitIndent != 0)
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                explicitIndent = current - YamlSyntax.DigitZero;
            }
            else if (current == YamlSyntax.CommentIndicator)
            {
                // The comment must be separated from the indicators: ">#x" is invalid.
                if (Source[_position - 2] is not (YamlSyntax.Space or YamlSyntax.Tab))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                // Header comments are trivia of this scalar. Preserve them before committing its token;
                // unlike a hash on a content line, they must never become part of the string value.
                _position--;

                var commentStart = _position;
                if (_parseOptions.PreserveTrivia)
                {
                    StoreWhitespaceBeforeComment(commentStart);
                }

                SkipToLineEnd();

                if (_parseOptions.PreserveTrivia)
                {
                    StoreScalar(DTokenVariant.TriviaLineComment, Source[commentStart.._position]);
                }

                break;
            }
            else if (current != YamlSyntax.Space)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidString);
            }
        }

        ConsumeLineEnd();

        // At the root (no parent indentation) the explicit indentation indicator counts from RootIndentationLevel.
        var contentIndent =
            explicitIndent == 0
                ? -1
                : (parentIndent < 0 ? _parseOptions.RootIndentationLevel : parentIndent)
                    + explicitIndent;

        // The lines of the scalar, as ranges of the source with the indentation cut off.
        var lines = new List<(int Start, int End)>();

        // EOF is not a line break: chomping must preserve only breaks actually consumed from the source.
        var lastLineHasLineBreak = false;
        var seenContent = false;
        var leadingBlankIndent = 0;
        while (_position < Source.Length)
        {
            var lineStart = _position;
            var indent = CountIndent(lineStart);
            var contentStart = lineStart + indent;
            var empty = contentStart >= Source.Length || IsLineEnd(Source[contentStart]);
            if (!empty && contentIndent < 0)
            {
                // A line that is not indented deeper than the parent is not content: the scalar is empty and
                // that line belongs to the parent ("strip: >-" followed by "clip: >").
                if (indent <= parentIndent)
                {
                    // A tab cannot be indentation, so such a line is neither content nor a sibling entry.
                    if (Source[contentStart] == YamlSyntax.Tab)
                    {
                        ThrowParseError(ParseException.ErrorCode.InvalidString);
                    }

                    break;
                }

                contentIndent = indent;
            }

            if (!empty && indent < contentIndent)
            {
                break;
            }

            if (empty && !seenContent)
            {
                leadingBlankIndent = Math.Max(leadingBlankIndent, indent);
            }
            else if (!empty && !seenContent)
            {
                // Blank lines before the first content line cannot hold more spaces than that line is indented.
                seenContent = true;

                if (leadingBlankIndent > contentIndent)
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }
            }

            _position = contentStart;

            SkipToLineEnd();

            var lineEnd = _position;
            var cut = contentIndent < 0 ? indent : Math.Min(indent, contentIndent);

            lines.Add((lineStart + cut, lineEnd));

            lastLineHasLineBreak = ConsumeLineEnd();

            if (!lastLineHasLineBreak)
            {
                break;
            }
        }

        var last = lines.Count - 1;
        while (last >= 0 && lines[last].End == lines[last].Start)
        {
            last--;
        }

        var trailingBreaks = Math.Max(0, lines.Count - 1 - last - (lastLineHasLineBreak ? 0 : 1));

        var scalar = BeginScalar(true);
        if (last < 0)
        {
            // No content: only "keep" chomping preserves the (empty) lines.
            if (chomping == YamlSyntax.Plus)
            {
                _scalars.Append(
                    YamlSyntax.LineFeed,
                    Math.Max(0, lines.Count - (lastLineHasLineBreak ? 0 : 1))
                );
            }

            return scalar;
        }

        var previous = -1;
        for (var index = 0; index <= last; index++)
        {
            if (lines[index].End == lines[index].Start)
            {
                continue;
            }

            if (previous < 0)
            {
                // Leading empty lines are kept as line breaks in both styles.
                _scalars.Append(YamlSyntax.LineFeed, index);
            }
            else
            {
                var gap = index - previous - 1;

                // Folding applies only between two ordinary lines; more-indented lines keep their breaks.
                var keepBreak =
                    style == YamlSyntax.LiteralBlockIndicator
                    || IsMoreIndented(lines[previous])
                    || IsMoreIndented(lines[index]);

                if (keepBreak)
                {
                    _scalars.Append(YamlSyntax.LineFeed, gap + 1);
                }
                else if (gap == 0)
                {
                    _scalars.Append(YamlSyntax.Space);
                }
                else
                {
                    _scalars.Append(YamlSyntax.LineFeed, gap);
                }
            }

            _scalars.Append(Source[lines[index].Start..lines[index].End]);

            previous = index;
        }

        // The last line of the scalar ends with a line break or with the end of the text. Chomping keeps only a line break
        // that is in the text: the end of the text adds no line feed (YAML 1.2, b-chomped-last).
        if (chomping != YamlSyntax.Dash && (last < lines.Count - 1 || lastLineHasLineBreak))
        {
            _scalars.Append(YamlSyntax.LineFeed);
        }

        if (chomping == YamlSyntax.Plus)
        {
            _scalars.Append(YamlSyntax.LineFeed, trailingBreaks);
        }

        return scalar;
    }

    private bool IsMoreIndented((int Start, int End) line) =>
        line.End > line.Start && Source[line.Start] is YamlSyntax.Space or YamlSyntax.Tab;

    /// <inheritdoc />
    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        return ScalarBytes(token);
    }

    // Binary data (a byte string, a Guid) has no YAML form without the !!binary tag.
    // And tags are not supported, so it is written as the string it has in text formats.
    // The Guid in its usual form, the other kinds as Base64, Base64Url or Base16. It reads back as that string.
    private static void WriteByteString(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        YamlWriteOptions options,
        int depth,
        bool key,
        ReadOnlySpan<byte> commentWhitespace,
        ReadOnlySpan<byte> commentText
    )
    {
        var kind = reader.GetToken(tokenId).ByteStringKind;
        var text = ByteStringText(reader.ReadByteString(tokenId), kind);

        WriteStringValue(writer, text, options, depth, key, commentWhitespace, commentText);
    }

    internal static ReadOnlySpan<byte> ByteStringText(ReadOnlySpan<byte> bytes, ByteStringKind kind)
    {
        var encodeKind = kind is ByteStringKind.Raw or ByteStringKind.Inherit
            ? ByteStringKind.Default
            : kind;

        return Utf8Helper.EncodeByteString(bytes, encodeKind);
    }

    internal static void WriteStringValue(
        Utf8TextWriter writer,
        ReadOnlySpan<byte> value,
        YamlWriteOptions options,
        int depth,
        bool key,
        ReadOnlySpan<byte> commentWhitespace = default,
        ReadOnlySpan<byte> commentText = default
    )
    {
        // ReadUtf8String can expose invalid bytes that ReadString previously replaced. Preserve that replacement
        // only for invalid input; valid UTF-8 must reach the encoder directly without a UTF-16 round trip.
        if (!Utf8.IsValid(value))
        {
            value = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(value));
        }

        if (!key && options.StringStyle == YamlStringStyle.Multiline)
        {
            WriteBlockString(writer, value, depth, options, commentWhitespace, commentText);

            return;
        }

        if (YamlStringHelper.CanWritePlain(value, key))
        {
            writer.WriteString(value);

            return;
        }

        var encoder = QuoteEncoder;
        var output = writer.BeginWriteUtf8Bytes(checked(value.Length * 6 + 2));

        writer.EndWriteUtf8Bytes(encoder.Encode(output, value));
    }

    // The escapes a quoted YAML string needs, independent of the serializer settings: the C0 controls and the
    // quote characters (always escaped by the JSON encoder), and the characters YAML cannot hold as they are: DEL,
    // the C1 controls (U+0080-U+0084 and U+0086-U+009F) and the non-characters U+FFFE and U+FFFF. The text encoder
    // policy of the settings is deliberately not used.
    private static readonly JsonTextEncoder QuoteEncoder = new TextEncoderPolicy(
        YamlSyntax.QuoteEscapeRanges,
        TextEscapeMask.Delete
    ).GetEncoder<JsonTextEncoder>();

    private static void WriteBlockString(
        Utf8TextWriter writer,
        ReadOnlySpan<byte> value,
        int depth,
        YamlWriteOptions options,
        ReadOnlySpan<byte> commentWhitespace,
        ReadOnlySpan<byte> commentText
    )
    {
        // TODO: Add a Mixed string mode when callers need quoted single-line values.
        if (options.IndentSize < 1)
        {
            ThrowHelper.ThrowNotSupported(
                "YAML block scalars require an indentation of at least 1 space."
            );
        }

        if (YamlSyntax.ContainsUnrepresentableBlockScalarCharacters(value))
        {
            ThrowHelper.ThrowNotSupported("This string cannot be written as a YAML block scalar.");
        }

        var trailingNewlines = value.Length - value.TrimEnd(YamlSyntax.LineFeed).Length;

        writer.WriteUtf8Byte(YamlSyntax.LiteralBlockIndicator);

        if (trailingNewlines == 0)
        {
            writer.WriteUtf8Byte(YamlSyntax.Dash);
        }
        else if (trailingNewlines > 1)
        {
            writer.WriteUtf8Byte(YamlSyntax.Plus);
        }

        var lines = value.Split(YamlSyntax.LineFeed);
        var lineCount = value.Count(YamlSyntax.LineFeed) + (trailingNewlines > 0 ? 0 : 1);

        // Only the first character of the first non-empty line is inspected; skip leading empty lines without copying.
        var firstContentLine = value.TrimStart(YamlSyntax.LineFeed);
        if (
            firstContentLine is { Length: > 0 }
            && YamlSyntax.IsWhiteSpaceOrLineEnd(firstContentLine[0])
        )
        {
            // An explicit indent keeps leading spaces in the value instead of treating them as indentation.
            if (options.IndentSize > 9)
            {
                ThrowHelper.ThrowNotSupported(
                    "Leading whitespace requires a YAML indentation indicator of 1 to 9."
                );
            }

            writer.WriteUtf8Byte((byte)('0' + options.IndentSize));
        }

        // Write trailing comments before the first content line: a hash within the body is string data.
        writer.WriteString(commentWhitespace);
        writer.WriteString(commentText);

        if (depth == 0)
        {
            writer.PushIndent();
        }

        var index = 0;
        foreach (var lineRange in lines)
        {
            if (index++ >= lineCount)
            {
                break;
            }

            writer.WriteIndentNewLine();
            writer.WriteString(value[lineRange]);
        }

        if (depth == 0)
        {
            writer.PopIndent();
        }
    }
}
