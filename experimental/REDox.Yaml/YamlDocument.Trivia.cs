using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        if (_position >= Source.Length || !YamlSyntax.IsLineEnd(Source[_position]))
        {
            return false;
        }

        if (
            Source[_position++] == YamlSyntax.CarriageReturn
            && _position < Source.Length
            && Source[_position] == YamlSyntax.LineFeed
        )
        {
            _position++;
        }

        return true;
    }

    private void SkipToLineEnd()
    {
        var remaining = Source[_position..];
        var lineEnd = remaining.IndexOfAny(YamlSyntax.CarriageReturn, YamlSyntax.LineFeed);

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
        var whitespaceBefore = false;
        while (_position < Source.Length)
        {
            var start = _position;
            if (YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position]))
            {
                // Trivia often contains long whitespace runs; search the whole run without a byte loop.
                var remaining = Source[_position..];
                var nextContent = remaining.IndexOfAnyExcept(YamlSyntax.WhiteSpaceOrLineEndBytes);
                _position += nextContent < 0 ? remaining.Length : nextContent;

                if (
                    _parseOptions.PreserveTrivia
                    && !(
                        !leaveSameLineCommentForNextNode
                        && _position < Source.Length
                        && Source[_position] == YamlSyntax.CommentIndicator
                        && CanAttachTrailingComment(_position)
                    )
                )
                {
                    CreateTriviaTokenFromUtf8Text(
                        DTokenVariant.TriviaWhitespace,
                        Source[start.._position]
                    );
                }

                whitespaceBefore = true;
            }
            else if (Source[_position] == YamlSyntax.CommentIndicator)
            {
                // A comment must be separated from what precedes it: "[a, b]#x" and "\"v\"#x" are not comments.
                if (start > 0 && !YamlSyntax.IsWhiteSpaceOrLineEnd(Source[start - 1]))
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                }

                // Without trivia preservation, a backward scan cannot affect the parsed value.
                var attachToNode =
                    _parseOptions.PreserveTrivia
                    && !leaveSameLineCommentForNextNode
                    && CanAttachTrailingComment(start);

                if (_parseOptions.PreserveTrivia && !whitespaceBefore && !attachToNode)
                {
                    StoreWhitespaceBeforeComment(start);
                }

                SkipToLineEnd();

                if (_parseOptions.PreserveTrivia)
                {
                    if (attachToNode)
                    {
                        var whitespaceStart =
                            Source[..start].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab)
                            + 1;

                        // Keep the comment with the value it follows so moving the next node cannot move it.
                        _trailingCommentsByNodeId.Add(
                            _lastParsedNodeId,
                            StoreTrailingComment(Source[whitespaceStart.._position])
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

                whitespaceBefore = false;
            }
            else
            {
                break;
            }
        }
    }

    private bool CanAttachTrailingComment(int commentStart)
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

    private void CheckFlowIndent(int parentIndent)
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

    private void SkipLineRemainder()
    {
        if (
            _parseOptions.PreserveTrivia
            && _position < Source.Length
            && Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            var start = _position;

            StoreWhitespaceBeforeComment(start);
            SkipToLineEnd();
            CreateTriviaTokenFromUtf8Text(
                DTokenVariant.TriviaLineComment,
                Source[start.._position]
            );
        }
        else
        {
            SkipToLineEnd();
        }

        ConsumeLineEnd();
    }

    // NOTE / TODO:
    //   Core (DOM/IR) only has leading trivia: every comment is attached to the token that FOLLOWS it,
    //   and nothing can say "this comment ended the previous value's line".
    //   Like JSON5, we encode that in the trivia sequence itself instead of changing Core for now.
    //   A comment that shares a line with the preceding value is stored as the following shape.
    //     --> [whitespace-without-line-break][line-comment]
    //   A comment on its own line is preceded by whitespace that contains the line break (or by nothing),
    //   so the two cases stay distinguishable.
    //   Scalar parsing trims the spaces in front of "#", so they have to be restored here.
    private void StoreWhitespaceBeforeComment(int commentStart)
    {
        var spaceStart =
            Source[..commentStart].LastIndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab) + 1;

        // No spaces, start of the text, or the comment begins its own line: nothing to restore.
        if (
            spaceStart == commentStart
            || spaceStart == 0
            || YamlSyntax.IsLineEnd(Source[spaceStart - 1])
        )
        {
            return;
        }

        CreateTriviaTokenFromUtf8Text(
            DTokenVariant.TriviaWhitespace,
            Source[spaceStart..commentStart]
        );
    }

    private (int Position, int Indent) PeekNextContent()
    {
        var position = _position;

        // Every caller has consumed the preceding line break, so this position is already a line start.
        // Searching backward for it on each nested value would repeat work on the previous line.
        var lineStart = position;
        while (position < Source.Length)
        {
            var indent = CountIndent(lineStart);
            position = Math.Max(position, lineStart + indent);

            if (position >= Source.Length)
            {
                return (position, -1);
            }

            // CountIndent already reached the content on space-indented lines. Only tabs or spaces after
            // the current position need another scan; a tab-only line is still blank, not scalar content.
            var contentStart = position;
            if (Source[position] is YamlSyntax.Space or YamlSyntax.Tab)
            {
                var remaining = Source[position..];
                var firstNonSpace = remaining.IndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab);
                contentStart = firstNonSpace < 0 ? Source.Length : position + firstNonSpace;
            }

            if (contentStart >= Source.Length)
            {
                return (contentStart, -1);
            }

            if (
                Source[contentStart] == YamlSyntax.CommentIndicator
                || YamlSyntax.IsLineEnd(Source[contentStart])
            )
            {
                position = contentStart;

                var lineEnd = Source[position..]
                    .IndexOfAny(YamlSyntax.CarriageReturn, YamlSyntax.LineFeed);

                position = lineEnd < 0 ? Source.Length : position + lineEnd;

                if (
                    position < Source.Length
                    && Source[position++] == YamlSyntax.CarriageReturn
                    && position < Source.Length
                    && Source[position] == YamlSyntax.LineFeed
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

    // Detects the '[whitespace-without-line-break][line-comment]' pair that starts a trivia sequence.
    //   --> see StoreWhitespaceBeforeComment
    // A comment that was on the same line as the previous value.
    private static bool IsTrailingCommentPair(
        in DataReader reader,
        ReadOnlySpan<uint> triviaIds,
        out uint whitespaceId,
        out uint commentId
    )
    {
        whitespaceId = 0;
        commentId = 0;

        if (
            triviaIds.Length < 2
            || reader.GetToken(triviaIds[0]).Variant != DTokenVariant.TriviaWhitespace
            || reader.GetToken(triviaIds[1]).Variant != DTokenVariant.TriviaLineComment
        )
        {
            return false;
        }

        var whitespace = reader.ReadTrivia(triviaIds[0]);
        if (whitespace.IndexOfAny(YamlSyntax.LineFeed, YamlSyntax.CarriageReturn) >= 0)
        {
            return false;
        }

        whitespaceId = triviaIds[0];
        commentId = triviaIds[1];

        return true;
    }

    private static bool TryGetTrailingComment(
        in DataReader reader,
        uint tokenId,
        out uint whitespaceId,
        out uint commentId
    )
    {
        Span<uint> triviaIds = stackalloc uint[2];
        var count = 0;
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            triviaIds[count++] = triviaId;

            if (count == triviaIds.Length)
            {
                break;
            }
        }

        return IsTrailingCommentPair(reader, triviaIds[..count], out whitespaceId, out commentId);
    }

    private static bool TryGetTrailingComment(
        in DataReader reader,
        List<uint> triviaIds,
        out uint whitespaceId,
        out uint commentId
    )
    {
        return IsTrailingCommentPair(
            reader,
            CollectionsMarshal.AsSpan(triviaIds),
            out whitespaceId,
            out commentId
        );
    }

    // Writes the comment that ended the previous value's line.
    // The writer calls this before it starts a new line because Core stores that comment as
    // 'leading trivia' of the NEXT token.
    private static void WriteTrailingComment(
        Utf8TextWriter writer,
        in DataReader reader,
        uint nextTokenId,
        in YamlWriteOptions options
    )
    {
        if (
            !options.PreserveTrivia
            || !TryGetTrailingComment(reader, nextTokenId, out var whitespaceId, out var commentId)
        )
        {
            return;
        }

        WriteTrailingCommentText(writer, reader, whitespaceId, commentId);
    }

    // The white space in front of the comment is written as it was read.
    //   ("some       # note" keeps its spaces and a tab stays a tab)
    // It is never replaced by a single space.
    private static void WriteTrailingCommentText(
        Utf8TextWriter writer,
        in DataReader reader,
        uint whitespaceId,
        uint commentId
    )
    {
        writer.WriteString(reader.ReadTrivia(whitespaceId));
        writer.WriteString(reader.ReadTrivia(commentId));
    }

    private static ReadOnlySpan<byte> GetAttachedTrailingComment(
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

    private static void WriteTrivia(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        in YamlWriteOptions options,
        bool separatorWritten,
        bool skipTrailingComment = false
    )
    {
        if (!options.PreserveTrivia)
        {
            return;
        }

        // skipTrailingComment
        // the leading pair was already written by WriteTrailingComment and must not be written again.
        var skip =
            skipTrailingComment && TryGetTrailingComment(reader, tokenId, out _, out _) ? 2 : 0;

        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            if (skip > 0)
            {
                skip--;

                continue;
            }

            var trivia = reader.GetToken(triviaId);
            if (trivia.Variant == DTokenVariant.TriviaWhitespace)
            {
                var whitespace = reader.ReadTrivia(triviaId);
                var index = 0;
                while (index < whitespace.Length)
                {
                    var nextBreak = whitespace[index..]
                        .IndexOfAny(YamlSyntax.CarriageReturn, YamlSyntax.LineFeed);

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

    private static void WriteTrailingTrivia(
        Utf8TextWriter writer,
        in DataReader reader,
        List<uint> triviaIds,
        bool skipTrailingComment
    )
    {
        var separatorWritten = true;
        var skip = skipTrailingComment ? 2 : 0;
        foreach (var triviaId in triviaIds)
        {
            if (skip > 0)
            {
                skip--;

                continue;
            }

            var token = reader.GetToken(triviaId);
            if (token.Variant == DTokenVariant.TriviaLineComment)
            {
                writer.WriteString(reader.ReadTrivia(triviaId));
                writer.WriteNewLine();

                separatorWritten = true;
            }
            else if (token.Variant == DTokenVariant.TriviaWhitespace)
            {
                var whitespace = reader.ReadTrivia(triviaId);
                var index = 0;
                while (index < whitespace.Length)
                {
                    // CR-only breaks need the same output as LF, while CRLF remains one break.
                    var nextBreak = whitespace[index..]
                        .IndexOfAny(YamlSyntax.CarriageReturn, YamlSyntax.LineFeed);

                    if (nextBreak < 0)
                    {
                        break;
                    }

                    index += nextBreak;

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
                        writer.WriteNewLine();
                    }

                    index++;
                }
            }
        }
    }
}