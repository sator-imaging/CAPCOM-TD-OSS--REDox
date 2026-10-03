// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private int LastLineStart(int position)
    {
        // The line feed is searched backwards from the byte before the position.
        var searched = Math.Min(Source.Length, Math.Max(1, position));
        var previous = Source[..searched].LastIndexOf(YamlSyntax.LineFeed);
        return previous + 1;
    }

    private int CountIndent(int lineStart)
    {
        var index = lineStart;
        while (index < Source.Length && Source[index] == YamlSyntax.Space)
            index++;
        return index - lineStart;
    }

    private bool ConsumeLineEnd()
    {
        if (_position >= Source.Length || !IsLineEnd(Source[_position]))
            return false;
        if (
            Source[_position++] == YamlSyntax.CarriageReturn
            && _position < Source.Length
            && Source[_position] == YamlSyntax.LineFeed
        )
            _position++;
        return true;
    }

    private void SkipToLineEnd()
    {
        while (_position < Source.Length && !IsLineEnd(Source[_position]))
            _position++;
    }

    private void SkipInlineSpaces()
    {
        while (_position < Source.Length && Source[_position] is YamlSyntax.Space or YamlSyntax.Tab)
            _position++;
    }

    private void SkipTrivia()
    {
        var whitespaceBefore = false;
        while (_position < Source.Length)
        {
            var start = _position;
            if (Source[_position] == YamlSyntax.CommentIndicator)
            {
                // A comment must be separated from what precedes it: "[a, b]#x" and "\"v\"#x" are not comments.
                if (start > 0 && !YamlSyntax.IsWhiteSpaceOrLineEnd(Source[start - 1]))
                    throw Error(ParseException.ErrorCode.InvalidFormat);

                if (_parseOptions.PreserveTrivia && !whitespaceBefore)
                    StoreWhitespaceBeforeComment(start);

                SkipToLineEnd();
                if (_parseOptions.PreserveTrivia)
                    StoreScalar(DTokenVariant.TriviaLineComment, Source[start.._position]);
                whitespaceBefore = false;
            }
            else if (YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position]))
            {
                while (
                    _position < Source.Length && YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position])
                )
                    _position++;
                if (_parseOptions.PreserveTrivia)
                    StoreScalar(DTokenVariant.TriviaWhitespace, Source[start.._position]);
                whitespaceBefore = true;
            }
            else
            {
                break;
            }
        }
    }

    // Anything left on the line after a complete node (such as the ": c" of "a: b: c") is invalid, except a comment.
    private void ExpectLineEnd()
    {
        SkipInlineSpaces();
        if (
            _position < Source.Length
            && !IsLineEnd(Source[_position])
            && Source[_position] != YamlSyntax.CommentIndicator
        )
        {
            throw Error(ParseException.ErrorCode.InvalidFormat);
        }
    }

    // Indentation is made of spaces: a tab before the first token of a line cannot indent a block entry.
    private bool IndentationContainsTab(int position)
    {
        for (var index = LastLineStart(position); index < position; index++)
        {
            if (Source[index] == YamlSyntax.Tab)
                return true;
        }

        return false;
    }

    // Lines of a flow collection that start with a token must be indented deeper than the parent block.
    private void CheckFlowIndent(int parentIndent)
    {
        if (_position >= Source.Length)
            return;

        var lineStart = LastLineStart(_position);
        for (var index = lineStart; index < _position; index++)
        {
            // Something precedes the token on its line, so it is not a continuation line.
            if (Source[index] is not (YamlSyntax.Space or YamlSyntax.Tab))
                return;
        }

        if (CountIndent(lineStart) <= parentIndent)
            throw Error(ParseException.ErrorCode.InvalidFormat);
    }

    // Consumes the rest of the current line, which holds nothing but an optional comment (such as "key: # note").
    // The comment is kept as trivia, like SkipTrivia does.
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
            StoreScalar(DTokenVariant.TriviaLineComment, Source[start.._position]);
        }
        else
        {
            SkipToLineEnd();
        }

        ConsumeLineEnd();
    }

    // Core (DOM/IR) only has leading trivia: every comment is attached to the token that FOLLOWS it, and
    // nothing can say "this comment ended the previous value's line". Like JSON5, we encode that in the trivia
    // sequence itself instead of changing Core: a comment that shares a line with the preceding value is stored as
    // [whitespace without a line break][line comment]. A comment on its own line is preceded by whitespace that
    // contains the line break (or by nothing), so the two cases stay distinguishable.
    // Scalar parsing trims the spaces in front of "#", so they have to be restored here.
    private void StoreWhitespaceBeforeComment(int commentStart)
    {
        var spaceStart = commentStart;
        while (spaceStart > 0 && Source[spaceStart - 1] is YamlSyntax.Space or YamlSyntax.Tab)
            spaceStart--;

        // No spaces, start of the text, or the comment begins its own line: nothing to restore.
        if (spaceStart == commentStart || spaceStart == 0 || IsLineEnd(Source[spaceStart - 1]))
            return;

        StoreScalar(DTokenVariant.TriviaWhitespace, Source[spaceStart..commentStart]);
    }

    private (int Position, int Indent) PeekNextContent()
    {
        var position = _position;
        while (position < Source.Length)
        {
            var lineStart = LastLineStart(position);
            var indent = CountIndent(lineStart);
            position = Math.Max(position, lineStart + indent);
            if (position >= Source.Length)
                return (position, -1);

            // Indentation is made of spaces only, but a line that holds nothing except spaces and tabs is still
            // blank (a tab-only line inside a multiline plain scalar is an empty line, not content).
            var contentStart = position;
            while (
                contentStart < Source.Length
                && Source[contentStart] is YamlSyntax.Space or YamlSyntax.Tab
            )
                contentStart++;
            if (contentStart >= Source.Length)
                return (contentStart, -1);

            if (
                Source[contentStart] == YamlSyntax.CommentIndicator
                || IsLineEnd(Source[contentStart])
            )
            {
                position = contentStart;
                while (position < Source.Length && !IsLineEnd(Source[position]))
                    position++;
                if (
                    position < Source.Length
                    && Source[position++] == YamlSyntax.CarriageReturn
                    && position < Source.Length
                    && Source[position] == YamlSyntax.LineFeed
                )
                    position++;
                continue;
            }
            return (position, indent);
        }

        return (position, -1);
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        return ScalarBytes(token);
    }

    // Detects the [whitespace without a line break][line comment] pair that starts a trivia sequence (see
    // StoreWhitespaceBeforeComment): a comment that was on the same line as the previous value.
    private static bool IsInlineCommentPair(
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
        for (var index = 0; index < whitespace.Length; index++)
        {
            if (whitespace[index] is (byte)YamlSyntax.LineFeed or (byte)YamlSyntax.CarriageReturn)
                return false;
        }

        whitespaceId = triviaIds[0];
        commentId = triviaIds[1];
        return true;
    }

    private static bool TryGetInlineComment(
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
                break;
        }

        return IsInlineCommentPair(reader, triviaIds[..count], out whitespaceId, out commentId);
    }

    private static bool TryGetInlineComment(
        in DataReader reader,
        IReadOnlyList<uint> triviaIds,
        out uint whitespaceId,
        out uint commentId
    )
    {
        Span<uint> firstTwo = stackalloc uint[2];
        var count = Math.Min(triviaIds.Count, firstTwo.Length);
        for (var index = 0; index < count; index++)
            firstTwo[index] = triviaIds[index];

        return IsInlineCommentPair(reader, firstTwo[..count], out whitespaceId, out commentId);
    }

    // Writes the comment that ended the previous value's line. The writer calls this before it starts a new line,
    // because Core stores that comment as leading trivia of the NEXT token.
    private static void WriteInlineComment(
        Utf8TextWriter writer,
        in DataReader reader,
        uint nextTokenId,
        YamlWriteOptions options
    )
    {
        if (
            !options.PreserveTrivia
            || !TryGetInlineComment(reader, nextTokenId, out var whitespaceId, out var commentId)
        )
            return;

        WriteInlineCommentText(writer, reader, whitespaceId, commentId);
    }

    // The white space in front of the comment is written as it was read ("some       # note" keeps its spaces, and
    // a tab stays a tab). It is never replaced by a single space.
    private static void WriteInlineCommentText(
        Utf8TextWriter writer,
        in DataReader reader,
        uint whitespaceId,
        uint commentId
    )
    {
        writer.WriteString(reader.ReadTrivia(whitespaceId));
        writer.WriteString(reader.ReadTrivia(commentId));
    }

    // skipInlineComment: the leading pair was already written by WriteInlineComment and must not be written again.
    private static void WriteTrivia(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        YamlWriteOptions options,
        bool separatorWritten,
        bool skipInlineComment = false
    )
    {
        if (!options.PreserveTrivia)
            return;

        var skip = skipInlineComment && TryGetInlineComment(reader, tokenId, out _, out _) ? 2 : 0;
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
                for (var index = 0; index < whitespace.Length; index++)
                {
                    if (
                        whitespace[index] != YamlSyntax.LineFeed
                        && (
                            whitespace[index] != YamlSyntax.CarriageReturn
                            || index + 1 < whitespace.Length
                                && whitespace[index + 1] == YamlSyntax.LineFeed
                        )
                    )
                        continue;

                    if (separatorWritten)
                        separatorWritten = false;
                    else
                        writer.WriteIndentNewLine();
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
        IReadOnlyList<uint> triviaIds,
        bool skipInlineComment
    )
    {
        var separatorWritten = true;
        var skip = skipInlineComment ? 2 : 0;
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
                foreach (var character in whitespace)
                {
                    if (character != YamlSyntax.LineFeed)
                        continue;
                    if (separatorWritten)
                        separatorWritten = false;
                    else
                        writer.WriteNewLine();
                }
            }
        }
    }
}
