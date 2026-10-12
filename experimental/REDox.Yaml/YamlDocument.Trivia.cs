using System;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    /// <inheritdoc />
    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        return GetUtf8TextForToken(token);
    }

    private int FindCurrentLineStart(int position)
    {
        var source = Source;
        var searched = Math.Min(source.Length, Math.Max(0, position));

        // A position between CR and LF still belongs to the preceding line because CRLF is one break.
        if (
            searched > 0
            && searched < source.Length
            && source[searched - 1] == YamlSyntax.CarriageReturn
            && source[searched] == YamlSyntax.LineFeed
        )
        {
            searched--;
        }

        // CR-only input must use the same indentation boundary as LF and CRLF input.
        var previous = source[..searched]
            .LastIndexOfAny(YamlSyntax.LineFeed, YamlSyntax.CarriageReturn);

        return previous + 1;
    }

    private int CountIndent(int lineStart)
    {
        var line = Source[lineStart..];
        var firstNonSpace = line.IndexOfAnyExcept(YamlSyntax.Space);

        return firstNonSpace < 0 ? line.Length : firstNonSpace;
    }

    private bool ConsumeLineEnd()
    {
        // CRLF checks touch the same input several times, so keep one span for this line break.
        var source = Source;
        if (_position >= source.Length || !YamlSyntax.IsLineEnd(source[_position]))
        {
            return false;
        }

        if (
            source[_position++] == YamlSyntax.CarriageReturn
            && _position < source.Length
            && source[_position] == YamlSyntax.LineFeed
        )
        {
            _position++;
        }

        return true;
    }

    private void SkipToLineEnd()
    {
        var remaining = Source[_position..];
        var lineEnd = remaining.IndexOfAny(YamlSyntax.LineEndBytes);

        _position += lineEnd < 0 ? remaining.Length : lineEnd;
    }

    private void SkipInlineSpaces()
    {
        var remaining = Source[_position..];
        var firstNonSpace = remaining.IndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab);

        _position += firstNonSpace < 0 ? remaining.Length : firstNonSpace;
    }

    private void SkipTrivia(bool leaveSameLineCommentForNextNode)
    {
        // Trivia scanning only advances the cursor.
        var source = Source;
        var preserveTrivia = _parseOptions.PreserveTrivia;
        var checkedCommentStart = -1;
        var canAttachCheckedComment = false;

        while (_position < source.Length)
        {
            var start = _position;
            if (YamlSyntax.IsWhiteSpaceOrLineEnd(source[_position]))
            {
                // Trivia often contains long whitespace runs; search the whole run without a byte loop.
                var remaining = source[_position..];
                var nextContent = remaining.IndexOfAnyExcept(YamlSyntax.WhiteSpaceOrLineEndBytes);
                _position += nextContent < 0 ? remaining.Length : nextContent;

                if (preserveTrivia)
                {
                    // The next iteration examines this same comment, so keep its ownership check once.
                    checkedCommentStart =
                        _position < source.Length
                        && source[_position] == YamlSyntax.CommentIndicator
                            ? _position
                            : -1;

                    canAttachCheckedComment =
                        checkedCommentStart >= 0
                        && ShouldAttachTrailingCommentToLastNode(checkedCommentStart);

                    if (!canAttachCheckedComment)
                    {
                        CreateTriviaTokenFromUtf8Text(
                            DTokenVariant.TriviaWhitespace,
                            source[start.._position]
                        );
                    }
                }
            }
            else if (source[_position] == YamlSyntax.CommentIndicator)
            {
                // A comment must be separated from what precedes it: "[a, b]#x" and "\"v\"#x" are not comments.
                if (start > 0 && !YamlSyntax.IsWhiteSpaceOrLineEnd(source[start - 1]))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                }

                // Without trivia preservation, a backward scan cannot affect the parsed value.
                var attachToNode =
                    preserveTrivia
                    && !leaveSameLineCommentForNextNode
                    && (
                        checkedCommentStart == start
                            ? canAttachCheckedComment
                            : ShouldAttachTrailingCommentToLastNode(start)
                    );

                SkipToLineEnd();

                if (preserveTrivia)
                {
                    if (attachToNode)
                    {
                        var whitespaceStart =
                            source[..start].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab)
                            + 1;

                        // Keep the comment with the value it follows so moving the next node cannot move it.
                        _trailingCommentsByNodeId.Add(
                            _lastParsedNodeId,
                            CreateTrailingComment(source[whitespaceStart.._position])
                        );
                    }
                    else
                    {
                        CreateTriviaTokenFromUtf8Text(
                            DTokenVariant.TriviaLineComment,
                            source[start.._position]
                        );
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    private bool ShouldAttachTrailingCommentToLastNode(int commentStart)
    {
        if (!_parseOptions.PreserveTrivia || _lastParsedNodeId == 0)
        {
            return false;
        }

        var whitespaceStart =
            Source[..commentStart].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

        // A comment on its own line must stay leading trivia of the next node.
        return whitespaceStart > 0
               && whitespaceStart < commentStart
               && !YamlSyntax.IsLineEnd(Source[whitespaceStart - 1]);
    }

    private void ExpectLineEnd()
    {
        SkipInlineSpaces();

        if (
            _position < Source.Length
            && !YamlSyntax.IsLineEnd(Source[_position])
            && Source[_position] != YamlSyntax.CommentIndicator
        )
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }
    }

    private bool IndentationContainsTab(int position)
    {
        return Source[FindCurrentLineStart(position)..position].Contains(YamlSyntax.Tab);
    }

    private void ValidateFlowContinuationIndent(int parentIndent)
    {
        var source = Source;
        if (_position >= source.Length)
        {
            return;
        }

        // Only the whitespace immediately before the token affects whether this is a new line.
        var lineStart =
            source[.._position].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

        // CR-only breaks also start a continuation line, so they need the same indentation check as LF.
        if (lineStart > 0 && !YamlSyntax.IsLineEnd(source[lineStart - 1]))
        {
            return;
        }

        if (CountIndent(lineStart) <= parentIndent)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }
    }

    private void SkipRestOfLineAndLineBreak(uint? commentOwnerId)
    {
        if (
            _parseOptions.PreserveTrivia
            && _position < Source.Length
            && Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            var start = _position;

            SkipToLineEnd();

            if (commentOwnerId is { } ownerId)
            {
                var whitespaceStart =
                    Source[..start].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

                _trailingCommentsByNodeId.Add(
                    ownerId,
                    CreateTrailingComment(Source[whitespaceStart.._position])
                );
            }
            else
            {
                CreateTriviaTokenFromUtf8Text(
                    DTokenVariant.TriviaLineComment,
                    Source[start.._position]
                );
            }
        }
        else
        {
            SkipToLineEnd();
        }

        ConsumeLineEnd();
    }

    private (int Position, int Indent) PeekNextNonTriviaContentLine()
    {
        // Looking ahead calls indentation helpers but never changes the input buffer.
        var source = Source;
        var position = _position;

        // Every caller has consumed the preceding line break, so this position is already a line start.
        // Searching backward for it on each nested value would repeat work on the previous line.
        var lineStart = position;
        while (position < source.Length)
        {
            // Lookahead already holds the input span, so avoid reacquiring it for every skipped line.
            var indent = source[lineStart..].IndexOfAnyExcept(YamlSyntax.Space);
            if (indent < 0)
            {
                return (source.Length, -1);
            }

            // Each pass starts at lineStart, and indentation can only advance that position.
            position = lineStart + indent;

            // The indent scan stops at the first non-space byte, so only tabs can need another scan.
            // A tab-only line is still blank, not scalar content.
            var contentStart = position;
            if (source[position] == YamlSyntax.Tab)
            {
                var remaining = source[position..];
                var firstNonSpace = remaining.IndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab);
                contentStart = firstNonSpace < 0 ? source.Length : position + firstNonSpace;
            }

            if (contentStart >= source.Length)
            {
                return (contentStart, -1);
            }

            if (
                source[contentStart] == YamlSyntax.CommentIndicator
                || YamlSyntax.IsLineEnd(source[contentStart])
            )
            {
                position = contentStart;

                var lineEnd = source[position..].IndexOfAny(YamlSyntax.LineEndBytes);

                position = lineEnd < 0 ? source.Length : position + lineEnd;

                if (
                    position < source.Length
                    && source[position++] == YamlSyntax.CarriageReturn
                    && position < source.Length
                    && source[position] == YamlSyntax.LineFeed
                )
                {
                    position++;
                }

                // After consuming a complete line break, the current position is already the next line's start.
                lineStart = position;

                continue;
            }

            return (position, indent);
        }

        return (position, -1);
    }

    private void AdvanceToPeekedContent(int position)
    {
        // PeekNextContent already scanned the intervening lines.
        // With trivia disabled, scanning them again only repeats work.
        // Keep SkipTrivia for preserved trivia and unusual whitespace before the content.
        if (
            _parseOptions.PreserveTrivia
            || position >= Source.Length
            || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position])
            || Source[position] == YamlSyntax.CommentIndicator
        )
        {
            SkipTrivia(leaveSameLineCommentForNextNode: false);

            return;
        }

        _position = position;
    }

    private static ReadOnlySpan<byte> GetTrailingComment(
        in DataReader reader,
        uint tokenId,
        in YamlWriteOptions options
    )
    {
        return
            options.PreserveTrivia
            && reader.Document is YamlDocument document
            && document._trailingCommentsByNodeId.TryGetValue(tokenId, out var comment)
                ? document._utf8TextForTokens.Slice(comment.Offset, comment.Length)
                : default;
    }

    private static void WriteLeadingTriviaForToken(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        in YamlWriteOptions options,
        bool separatorWritten
    )
    {
        if (!options.PreserveTrivia)
        {
            return;
        }

        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var trivia = reader.GetToken(triviaId);
            if (trivia.Variant == DTokenVariant.TriviaWhitespace)
            {
                var whitespace = reader.ReadTrivia(triviaId);
                var index = 0;
                while (index < whitespace.Length)
                {
                    var nextBreak = whitespace[index..].IndexOfAny(YamlSyntax.LineEndBytes);

                    if (nextBreak < 0)
                    {
                        break;
                    }

                    index += nextBreak;

                    // CRLF is one break; writing it twice would add an empty line.
                    if (
                        whitespace[index] == YamlSyntax.CarriageReturn
                        && index + 1 < whitespace.Length
                        && whitespace[index + 1] == YamlSyntax.LineFeed
                    )
                    {
                        index++;
                    }

                    if (separatorWritten)
                    {
                        separatorWritten = false;
                    }
                    else
                    {
                        writer.WriteIndentNewLine();
                    }

                    index++;
                }
            }
            else if (trivia.Variant == DTokenVariant.TriviaLineComment)
            {
                writer.WriteString(reader.ReadTrivia(triviaId));
                writer.WriteIndentNewLine();

                separatorWritten = true;
            }
        }
    }
}