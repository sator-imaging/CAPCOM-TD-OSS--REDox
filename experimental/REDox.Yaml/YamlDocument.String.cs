using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    /// <inheritdoc />
    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        return GetUtf8TextForToken(token);
    }

    private ParsedScalar ParseDoubleQuotedString(int parentIndent)
    {
        _position++;

        var scalar = BeginScalar(quoted: true);

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
                if (YamlSyntax.IsLineEnd(current))
                {
                    _position--;

                    FoldQuotedBreak(scalar, protectedLength, parentIndent);

                    continue;
                }

                if (IsUnprintableStart(current))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                _utf8TextForTokens.Append(current);

                continue;
            }

            if (_position >= Source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidString);
            }

            current = Source[_position++];

            switch (current)
            {
                case YamlSyntax.EscapeLetter.NullCharacter:
                    _utf8TextForTokens.Append(YamlSyntax.NoCharacter);

                    break;

                case YamlSyntax.EscapeLetter.Bell:
                    _utf8TextForTokens.Append(YamlSyntax.Bell);

                    break;

                case YamlSyntax.EscapeLetter.Backspace:
                    _utf8TextForTokens.Append(YamlSyntax.Backspace);

                    break;

                case YamlSyntax.EscapeLetter.Tab:
                case YamlSyntax.Tab:
                    _utf8TextForTokens.Append(YamlSyntax.Tab);

                    break;

                case YamlSyntax.EscapeLetter.LineFeed:
                    _utf8TextForTokens.Append(YamlSyntax.LineFeed);

                    break;

                case YamlSyntax.EscapeLetter.VerticalTab:
                    _utf8TextForTokens.Append(YamlSyntax.VerticalTab);

                    break;

                case YamlSyntax.EscapeLetter.FormFeed:
                    _utf8TextForTokens.Append(YamlSyntax.FormFeed);

                    break;

                case YamlSyntax.EscapeLetter.CarriageReturn:
                    _utf8TextForTokens.Append(YamlSyntax.CarriageReturn);

                    break;

                case YamlSyntax.EscapeLetter.EscapeControlCharacter:
                    _utf8TextForTokens.Append(YamlSyntax.EscapeControlCharacter);

                    break;

                case YamlSyntax.Space:
                    _utf8TextForTokens.Append(YamlSyntax.Space);

                    break;

                case YamlSyntax.DoubleQuote:
                    _utf8TextForTokens.Append(YamlSyntax.DoubleQuote);

                    break;

                case YamlSyntax.Slash:
                    _utf8TextForTokens.Append(YamlSyntax.Slash);

                    break;

                case YamlSyntax.Backslash:
                    _utf8TextForTokens.Append(YamlSyntax.Backslash);

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
                    _utf8TextForTokens.AppendCodePoint(YamlSyntax.NextLine);

                    break;

                case YamlSyntax.EscapeLetter.NoBreakSpace:
                    _utf8TextForTokens.AppendCodePoint(YamlSyntax.NoBreakSpace);

                    break;

                case YamlSyntax.EscapeLetter.LineSeparator:
                    _utf8TextForTokens.AppendCodePoint(YamlSyntax.LineSeparator);

                    break;

                case YamlSyntax.EscapeLetter.ParagraphSeparator:
                    _utf8TextForTokens.AppendCodePoint(YamlSyntax.ParagraphSeparator);

                    break;

                case YamlSyntax.EscapeLetter.Hex2Digits:
                    _utf8TextForTokens.AppendCodePoint(ParseEscapedCodePoint(digits: 2));

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

                        var low = ParseEscapedCodePoint(digits: 4);
                        if (low is < 0xdc00 or > 0xdfff)
                        {
                            ThrowParseError(ParseException.ErrorCode.InvalidString);
                        }

                        codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + low - 0xdc00;
                    }
                    // Eight hexadecimal digits can parse as a negative int; reject them before constructing a Rune.
                    else if (codePoint is >= 0xdc00 and <= 0xdfff or < 0 or > 0x10ffff)
                    {
                        ThrowParseError(ParseException.ErrorCode.InvalidString);
                    }

                    _utf8TextForTokens.AppendCodePoint(codePoint);

                    break;

                default:
                    ThrowParseError(ParseException.ErrorCode.InvalidString);

                    break;
            }

            protectedLength = _utf8TextForTokens.Length - scalar.Start;
        }

        throw ParseError(ParseException.ErrorCode.InvalidString);
    }

    // Whether a byte of a quoted scalar which is neither a line break nor the start of
    // an escape is the start of a character that cannot appear as it is.
    // A control character (the tab is allowed) which is a C0 control the delete character or a C1 control
    // (the lead byte 0xC2 and a second byte from 0x80 to 0x9F).
    // The next line character (U+0085) is a C1 control that is printable in YAML 1.2 so it may appear as it is.
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
        var protectedEnd = scalar.Start + protectedLength;

        // Search only after the last escape so escaped trailing whitespace remains in the scalar.
        var unprotected = _utf8TextForTokens.GetWrittenSpanFromOffset(protectedEnd);
        var end =
            protectedEnd + unprotected.LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

        _utf8TextForTokens.TruncateTo(end);

        var breaks = 0;
        while (_position < Source.Length && YamlSyntax.IsLineEnd(Source[_position]))
        {
            ConsumeLineEnd();

            breaks++;

            CheckQuotedContinuation(parentIndent);
            SkipInlineSpaces();
        }

        if (breaks == 1)
        {
            _utf8TextForTokens.Append(YamlSyntax.Space);
        }
        else
        {
            _utf8TextForTokens.Append(YamlSyntax.LineFeed, breaks - 1);
        }
    }

    // A line that continues a quoted scalar must be indented deeper than the parent (tabs do not count as indentation)
    // and a document marker cannot appear inside the scalar.
    private void CheckQuotedContinuation(int parentIndent)
    {
        if (IsDocumentMarkerAt(_position))
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        var indent = CountIndent(_position);
        var content = _position + indent;
        var firstNonSpace = Source[content..].IndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab);
        content = firstNonSpace < 0 ? Source.Length : content + firstNonSpace;

        // A blank line has no indentation to check.
        if (content >= Source.Length || YamlSyntax.IsLineEnd(Source[content]))
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
            digits > Source.Length - _position
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

        var scalar = BeginScalar(quoted: true);
        while (_position < Source.Length)
        {
            var current = Source[_position++];
            if (current == YamlSyntax.SingleQuote)
            {
                if (_position < Source.Length && Source[_position] == YamlSyntax.SingleQuote)
                {
                    _utf8TextForTokens.Append(YamlSyntax.SingleQuote);

                    _position++;
                }
                else
                {
                    return scalar;
                }
            }
            else if (YamlSyntax.IsLineEnd(current))
            {
                _position--;

                FoldQuotedBreak(scalar, protectedLength: 0, parentIndent);
            }
            else
            {
                // Single-quoted text has no escapes for control characters, so it must reject them here.
                if (IsUnprintableStart(current))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                _utf8TextForTokens.Append(current);
            }
        }

        throw ParseError(ParseException.ErrorCode.InvalidString);
    }

    // Whether a flow indicator, or the ":" that separates a key from its value starts at the position.
    private bool IsFlowTokenAt(int position)
    {
        var source = Source;
        var current = source[position];
        if (YamlSyntax.IsFlowIndicator(current))
        {
            return true;
        }

        if (current != YamlSyntax.MappingValueIndicator)
        {
            return false;
        }

        // A colon at EOF still separates a key, so check the boundary before reading its next byte.
        if (position + 1 == source.Length)
        {
            return true;
        }

        var next = source[position + 1];

        return YamlSyntax.IsWhiteSpaceOrLineEnd(next)
               || next
                   is YamlSyntax.FlowSeparator
                   or YamlSyntax.FlowSequenceEnd
                   or YamlSyntax.FlowMappingEnd;
    }

    // TODO: 200+ LoC
    private ParsedScalar ParsePlainString(bool key, bool flow, int parentIndent)
    {
        // A plain scalar cannot start with "- " Reaching here means a sequence entry in an invalid position.
        // In a flow collection a lone "-" (as in "[-]" or "[-, -]") is invalid as well.
        // An unfinished flow scalar may also end at '-', so check EOF before reading its next byte.
        if (
            IsBlockSequenceIndicator(_position)
            || flow
            && Source[_position] == YamlSyntax.Dash
            && (
                _position + 1 == Source.Length
                || Source[_position + 1]
                    is YamlSyntax.FlowSeparator
                    or YamlSyntax.FlowSequenceEnd
                    or YamlSyntax.FlowMappingEnd
            )
        )
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        var start = _position;
        var stops = flow ? YamlSyntax.PlainFlowStops : YamlSyntax.PlainBlockStops;
        while (_position < Source.Length)
        {
            // Only the stop characters can end the scalar so everything in between is skipped in one step.
            var offset = Source[_position..].IndexOfAny(stops);
            if (offset < 0)
            {
                _position = Source.Length;

                break;
            }

            _position += offset;

            var current = Source[_position];
            if (YamlSyntax.IsLineEnd(current) || (flow && YamlSyntax.IsFlowIndicator(current)))
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

            // The colon of "key: value" ends a plain scalar.
            // A key included only when white space follows it.
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

        var scalar = BeginScalar(quoted: false);

        _utf8TextForTokens.Append(Source[start.._position].Trim(YamlSyntax.WhiteSpace));

        // A plain scalar continues on the next lines.
        // In a block as well as in a flow collection. Only an implicit block key has to stay on one line.
        if ((!key || flow) && _position < Source.Length && YamlSyntax.IsLineEnd(Source[_position]))
        {
            ParsePlainStringContinuations(this, stops, flow, parentIndent);

            static void ParsePlainStringContinuations(
                YamlDocument document,
                SearchValues<byte> stops,
                bool flow,
                int parentIndent
            )
            {
                ref var _position = ref document._position;
                var Source = document.Source;
                var _scalars = document._utf8TextForTokens;

                do
                {
                    var lineEnd = _position;

                    document.ConsumeLineEnd();

                    var (Position, Indent) = document.PeekNextContent();

                    // A comment line ends the scalar: it cannot continue after the comment.
                    if (
                        Indent <= parentIndent
                        || Position >= Source.Length
                        || Indent == 0 && document.IsDocumentMarkerAt(Position)
                        || Source[lineEnd..Position].Contains(YamlSyntax.CommentIndicator)
                        // A line that starts with ",", "}" or ": " belongs to the collection, not to the scalar.
                        || flow && document.IsFlowTokenAt(Position)
                    )
                    {
                        _position = lineEnd;

                        break;
                    }

                    // A single physical break folds to a space: additional blank lines remain line breaks.
                    // Do not add another separator before the continuation: the break already supplies it.
                    var breaks = 0;
                    var betweenLines = Source[lineEnd..Position];
                    var breakPosition = 0;
                    while (breakPosition < betweenLines.Length)
                    {
                        // Blank lines may carry indentation, so jump directly to their line breaks.
                        var nextBreak = betweenLines[breakPosition..]
                            .IndexOfAny(YamlSyntax.CarriageReturn, YamlSyntax.LineFeed);

                        if (nextBreak < 0)
                        {
                            break;
                        }

                        breakPosition += nextBreak;

                        if (
                            betweenLines[breakPosition] == YamlSyntax.CarriageReturn
                            && breakPosition + 1 < betweenLines.Length
                            && betweenLines[breakPosition + 1] == YamlSyntax.LineFeed
                        )
                        {
                            breakPosition++;
                        }

                        breaks++;
                        breakPosition++;
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
                        if (YamlSyntax.IsLineEnd(current))
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

                        // In a flow collection ": " ends a key.
                        // In a block a continuation line cannot hold a mapping key ("this\n is\n  invalid: x").
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

                            document.ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                        }

                        _position++;
                    }

                    _scalars.Append(
                        Source[continuationStart.._position].Trim(YamlSyntax.WhiteSpace)
                    );

                    if (_position < Source.Length && !YamlSyntax.IsLineEnd(Source[_position]))
                    {
                        break;
                    }
                } while (_position < Source.Length && YamlSyntax.IsLineEnd(Source[_position]));
            }
        }

        if (_utf8TextForTokens.Length == scalar.Start && _parseOptions.EnableValueValidation)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        return scalar;
    }

    // TODO: 200+ LoC
    private ParsedScalar ParseBlockString(int parentIndent)
    {
        // A block scalar scans every physical line, so reuse the source span throughout the scan.
        var source = Source;
        var style = source[_position++];
        var chomping = YamlSyntax.NoCharacter;
        var explicitIndent = 0;
        var headerHasSeparation = false;
        TrailingComment? trailingComment = null;
        while (_position < source.Length && !YamlSyntax.IsLineEnd(source[_position]))
        {
            var current = source[_position++];
            if (current is YamlSyntax.Plus or YamlSyntax.Dash)
            {
                if (headerHasSeparation || chomping != YamlSyntax.NoCharacter)
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                chomping = current;
            }
            else if (YamlSyntax.IsIndentDigit(current))
            {
                if (headerHasSeparation || explicitIndent != 0)
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                explicitIndent = current - YamlSyntax.DigitZero;
            }
            else if (current == YamlSyntax.CommentIndicator)
            {
                // The comment must be separated from the indicators: ">#x" is invalid.
                if (source[_position - 2] is not (YamlSyntax.Space or YamlSyntax.Tab))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidString);
                }

                // The scalar token does not exist yet, so retain its header comment until CommitScalar can attach it.
                _position--;

                var commentStart = _position;

                SkipToLineEnd();

                if (_parseOptions.PreserveTrivia)
                {
                    var whitespaceStart =
                        source[..commentStart]
                            .LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

                    trailingComment = StoreTrailingComment(source[whitespaceStart.._position]);
                }

                break;
            }
            else if (current is YamlSyntax.Space or YamlSyntax.Tab)
            {
                // Indicators must be adjacent; spaces and tabs may only precede a comment or line break.
                headerHasSeparation = true;
            }
            else
            {
                ThrowParseError(ParseException.ErrorCode.InvalidString);
            }
        }

        ConsumeLineEnd();

        // At the root (no parent indentation), the explicit indentation indicator counts from 0.
        var contentIndent =
            explicitIndent == 0 ? -1 : (parentIndent < 0 ? 0 : parentIndent) + explicitIndent;

        // EOF is not a line break: chomping must preserve only breaks actually consumed from the source.
        var lastLineHasLineBreak = false;

        // A blank line with extra spaces can add value bytes before any nonblank content line appears.
        // Keep these states separate so later indentation checks still see that first content line.
        var seenContent = false;
        var leadingBlankIndent = 0;
        var emptyLineCount = 0;
        var hasScalarContent = false;
        var previousLineWasMoreIndented = false;
        var scalar = BeginScalar(quoted: true, trailingComment);
        while (_position < source.Length)
        {
            var lineStart = _position;
            var indent = CountIndent(lineStart);
            var contentStart = lineStart + indent;
            var empty = contentStart >= source.Length || YamlSyntax.IsLineEnd(source[contentStart]);
            if (!empty && contentIndent < 0)
            {
                // A line that is not indented deeper than the parent is not content.
                // The scalar is empty and that line belongs to the parent ("strip: >-" followed by "clip: >").
                if (indent <= parentIndent)
                {
                    // A tab cannot be indentation so such a line is neither content nor a sibling entry.
                    if (source[contentStart] == YamlSyntax.Tab)
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
            var valueStart = lineStart + cut;

            if (valueStart == lineEnd)
            {
                // Defer empty lines until the next content line determines whether folding or chomping owns them.
                emptyLineCount++;
            }
            else
            {
                var lineIsMoreIndented = source[valueStart] is YamlSyntax.Space or YamlSyntax.Tab;
                if (!hasScalarContent)
                {
                    _utf8TextForTokens.Append(YamlSyntax.LineFeed, emptyLineCount);
                }
                else if (
                    style == YamlSyntax.LiteralBlockIndicator
                    || previousLineWasMoreIndented
                    || lineIsMoreIndented
                )
                {
                    _utf8TextForTokens.Append(YamlSyntax.LineFeed, emptyLineCount + 1);
                }
                else if (emptyLineCount == 0)
                {
                    _utf8TextForTokens.Append(YamlSyntax.Space);
                }
                else
                {
                    _utf8TextForTokens.Append(YamlSyntax.LineFeed, emptyLineCount);
                }

                _utf8TextForTokens.Append(source[valueStart..lineEnd]);

                hasScalarContent = true;
                previousLineWasMoreIndented = lineIsMoreIndented;
                emptyLineCount = 0;
            }

            lastLineHasLineBreak = ConsumeLineEnd();

            if (!lastLineHasLineBreak)
            {
                break;
            }
        }

        var trailingBreaks = Math.Max(0, emptyLineCount - (lastLineHasLineBreak ? 0 : 1));
        if (!hasScalarContent)
        {
            // No content: only "keep" chomping preserves the (empty) lines.
            if (chomping == YamlSyntax.Plus)
            {
                _utf8TextForTokens.Append(YamlSyntax.LineFeed, trailingBreaks);
            }

            return scalar;
        }

        // The last line of the scalar ends with a line break or with the end of the text.
        // Chomping keeps only a line break that is in the text.
        // The end of the text adds no line feed (YAML 1.2, b-chomped-last).
        if (chomping != YamlSyntax.Dash && (emptyLineCount > 0 || lastLineHasLineBreak))
        {
            _utf8TextForTokens.Append(YamlSyntax.LineFeed);
        }

        if (chomping == YamlSyntax.Plus)
        {
            _utf8TextForTokens.Append(YamlSyntax.LineFeed, trailingBreaks);
        }

        return scalar;
    }

    // Binary data (byte string, Guid, etc) has no YAML form without the '!!binary' tag.
    // Tags are not supported so it is written as a string.
    private static void WriteByteString(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        DToken token,
        in YamlWriteOptions options,
        int depth,
        bool key,
        ReadOnlySpan<byte> commentWhitespace,
        ReadOnlySpan<byte> commentText
    )
    {
        var text = EncodeByteStringAsYamlText(reader.ReadByteString(tokenId), token.ByteStringKind);

        WriteStringValue(writer, text, options, depth, key, commentWhitespace, commentText);
    }

    internal static ReadOnlySpan<byte> EncodeByteStringAsYamlText(
        ReadOnlySpan<byte> bytes,
        ByteStringKind kind
    )
    {
        var encodeKind = kind is ByteStringKind.Raw or ByteStringKind.Inherit
            ? ByteStringKind.Default
            : kind;

        return Utf8Helper.EncodeByteString(bytes, encodeKind);
    }

    internal static void WriteStringValue(
        Utf8TextWriter writer,
        ReadOnlySpan<byte> value,
        in YamlWriteOptions options,
        int depth,
        bool key,
        ReadOnlySpan<byte> commentWhitespace = default,
        ReadOnlySpan<byte> commentText = default,
        bool alreadyValidatedUtf8 = false
    )
    {
        // ReadUtf8String can expose invalid bytes that ReadString previously replaced.
        // Preserve that replacement only for invalid input to avoid unnecessary allocation.
        // Original YAML tokens were validated while reading, but edited and foreign values need this check.
        if (!alreadyValidatedUtf8 && !Utf8.IsValid(value))
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

        var encoder = YamlSyntax.QuoteEncoder;
        var output = writer.BeginWriteUtf8Bytes(checked(value.Length * 6 + 2));

        writer.EndWriteUtf8Bytes(encoder.Encode(output, value));
    }

    private static void WriteBlockString(
        Utf8TextWriter writer,
        ReadOnlySpan<byte> value,
        int depth,
        in YamlWriteOptions options,
        ReadOnlySpan<byte> commentWhitespace,
        ReadOnlySpan<byte> commentText
    )
    {
        if (options.IndentSize < 1)
        {
            ThrowGenericError(
                ParseException.ErrorCode.InvalidOption,
                "YAML block scalars require an indentation of at least 1 space."
            );
        }

        if (YamlSyntax.ContainsUnrepresentableBlockScalarCharacters(value))
        {
            ThrowGenericError(
                ParseException.ErrorCode.UnsupportedFeature,
                "This string cannot be written as a YAML block scalar."
            );
        }

        var trailingNewlines = value.Length - value.TrimEnd(YamlSyntax.LineFeed).Length;

        // Leading whitespace requires an explicit indent.
        // Reject an unrepresentable indent before writing bytes.
        var firstContentLine = value.TrimStart(YamlSyntax.LineFeed);
        var requiresExplicitIndent =
            firstContentLine is { Length: > 0 }
            && YamlSyntax.IsWhiteSpaceOrLineEnd(firstContentLine[0]);

        if (requiresExplicitIndent && options.IndentSize > 9)
        {
            ThrowGenericError(
                ParseException.ErrorCode.UnsupportedFeature,
                "Leading whitespace requires a YAML indentation indicator of 1 to 9."
            );
        }

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
        if (requiresExplicitIndent)
        {
            // An explicit indent keeps leading spaces in the value instead of treating them as indentation.
            writer.WriteUtf8Byte((byte)('0' + options.IndentSize));
        }

        // Write trailing comments before the first content line.
        writer.WriteString(commentWhitespace);
        writer.WriteString(commentText);

        if (depth == 0)
        {
            writer.PushIndent();
        }

        foreach (var lineRange in lines)
        {
            // A terminal LF creates an empty split range but not another content line.
            if (trailingNewlines > 0 && lineRange.Start.GetOffset(value.Length) == value.Length)
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