using System;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private bool IsBlockSequenceIndicator(int position)
    {
        var source = Source;

        return source[position] == YamlSyntax.Dash
               && (
                   position + 1 == source.Length
                   || YamlSyntax.IsWhiteSpaceOrLineEnd(source[position + 1])
               );
    }

    private uint ReadFlowArray(int depth, int parentIndent)
    {
        // The input buffer remains fixed throughout parsing, so reuse its span across entries.
        var source = Source;
        var arrayId = AllocToken(DToken.MakeArray(count: 0));
        _lastParsedNodeId = arrayId;
        _position++;

        SkipTrivia(leaveSameLineCommentForNextNode: true);
        ValidateFlowContinuationIndent(parentIndent);

        while (_position < source.Length && source[_position] != YamlSyntax.FlowSequenceEnd)
        {
            if (source[_position] == YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (
                IsExplicitKeyStart(flow: true)
                || source[_position]
                    is not (YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart)
                && IsFlowSinglePairStart()
            )
            {
                ReadFlowSinglePair(depth + 1, parentIndent);
            }
            else
            {
                ReadNode(depth + 1, flow: true, parentIndent);
            }

            IncToken(arrayId);
            SkipTrivia(leaveSameLineCommentForNextNode: false);
            ValidateFlowContinuationIndent(parentIndent);

            if (_position >= source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (source[_position] == YamlSyntax.FlowSequenceEnd)
            {
                break;
            }

            // Keep the cursor on an unexpected byte so the parse error reports its position.
            if (source[_position] != YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            _position++;

            SkipTrivia(leaveSameLineCommentForNextNode: false);
            ValidateFlowContinuationIndent(parentIndent);
        }

        if (_position >= source.Length || source[_position] != YamlSyntax.FlowSequenceEnd)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        _position++;

        CompleteContainer(arrayId);

        return arrayId;
    }

    private uint ReadBlockArray(int depth)
    {
        // Nested node parsing moves the cursor but never replaces the input buffer.
        var source = Source;
        var baseIndent = _position - FindCurrentLineStart(_position);
        var arrayId = AllocToken(DToken.MakeArray(count: 0));
        _lastParsedNodeId = arrayId;

        // ReadNode already confirmed the first dash. Only subsequent entries need this lookahead.
        var firstEntry = true;
        while (_position < source.Length && (firstEntry || IsBlockSequenceIndicator(_position)))
        {
            firstEntry = false;

            RequireAllowedDepth(depth + 1);

            _position++;

            var separatorStart = _position;

            SkipInlineSpaces();

            if (
                _position == source.Length
                || YamlSyntax.IsLineEnd(source[_position])
                || source[_position] == YamlSyntax.CommentIndicator
            )
            {
                SkipRestOfLineAndLineBreak(commentOwnerId: null);

                var (Position, Indent) = PeekNextNonTriviaContentLine();

                if (Indent > baseIndent)
                {
                    AdvanceToPeekedContent(Position);
                    ReadNode(depth + 1, parentIndent: baseIndent);
                }
                else
                {
                    CreateNullToken();
                }
            }
            else
            {
                // A tab cannot separate the "-" from a nested block collection: its column would be undefined.
                if (
                    source[separatorStart.._position].Contains(YamlSyntax.Tab)
                    && (IsBlockSequenceIndicator(_position) || IsBlockMapStart())
                )
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                }

                var lineStart = FindCurrentLineStart(_position);

                ReadNode(depth + 1, parentIndent: baseIndent);

                if (FindCurrentLineStart(_position) == lineStart)
                {
                    ExpectLineEnd();
                }
            }

            IncToken(arrayId);
            SkipTrivia(leaveSameLineCommentForNextNode: false);

            if (_position >= source.Length)
            {
                break;
            }

            // SkipTrivia already passed blank and comment lines, so only this line's indentation remains to check.
            var indent = CountIndent(FindCurrentLineStart(_position));
            if (indent != baseIndent)
            {
                break;
            }

            if (IndentationContainsTab(_position))
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            // Checking the indicator here would repeat the loop guard's check on the same byte.
        }

        CompleteContainer(arrayId);

        return arrayId;
    }

    private static bool WriteArray(
        Utf8TextWriter writer,
        in DataReader reader,
        uint arrayId,
        in YamlWriteOptions options,
        int depth,
        uint followingTokenId
    )
    {
        // The first MoveNext is needed for writing anyway, so use it to detect an empty array.
        var values = reader.EnumerateArray(arrayId).GetEnumerator();
        var hasValue = values.MoveNext();
        if (!hasValue)
        {
            WriteEmptyCollection(writer, isMap: false, afterIndicator: false);

            return false;
        }

        var separatorWritten = false;
        var endsWithBlockScalar = false;
        var preserveTrivia = options.PreserveTrivia;
        while (hasValue)
        {
            var valueId = values.Current;
            hasValue = values.MoveNext();

            var nextTokenId = hasValue ? values.Current : followingTokenId;
            if (separatorWritten)
            {
                writer.WriteIndentNewLine();
            }

            // Most writes omit trivia. Avoid entering its enumerator for every array element.
            if (preserveTrivia)
            {
                WriteLeadingTriviaForToken(writer, reader, valueId, options, separatorWritten);
            }

            WriteCollectionIndicator(writer, mapValue: false);

            var value = reader.GetToken(valueId);

            WriteValueSeparator(
                writer,
                // Reuse the parsed token count. Edited containers keep their count in extension data.
                blockValue: value.IsContainer
                            && (value.IsExtended ? reader.GetValueCount(valueId) : value.Count) > 0
            );

            endsWithBlockScalar = WriteNode(
                writer,
                reader,
                valueId,
                value,
                options,
                depth + 1,
                isKey: false,
                nextTokenId
            );

            writer.PopIndent();

            separatorWritten = true;
        }

        return endsWithBlockScalar;
    }
}