using System;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private bool IsBlockSequenceIndicator(int position)
    {
        return Source[position] == YamlSyntax.Dash
               && (
                   position + 1 == Source.Length
                   || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position + 1])
               );
    }

    private uint ParseFlowArray(int depth, int parentIndent)
    {
        var arrayId = AllocToken(DToken.MakeArray(count: 0));
        _lastParsedNodeId = arrayId;
        _position++;

        SkipTrivia(leaveSameLineCommentForNextNode: true);
        CheckFlowIndent(parentIndent);

        while (_position < Source.Length && Source[_position] != YamlSyntax.FlowSequenceEnd)
        {
            if (Source[_position] == YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (
                IsExplicitKeyStart(flow: true)
                || Source[_position]
                    is not (YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart)
                && IsFlowSinglePairStart()
            )
            {
                ParseFlowSinglePair(depth + 1, parentIndent);
            }
            else
            {
                ParseNode(depth + 1, flow: true, parentIndent);
            }

            IncToken(arrayId);
            SkipTrivia(leaveSameLineCommentForNextNode: false);
            CheckFlowIndent(parentIndent);

            if (_position >= Source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (Source[_position] == YamlSyntax.FlowSequenceEnd)
            {
                break;
            }

            // Keep the cursor on an unexpected byte so the parse error reports its position.
            if (Source[_position] != YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            _position++;

            SkipTrivia(leaveSameLineCommentForNextNode: false);
            CheckFlowIndent(parentIndent);
        }

        if (_position >= Source.Length || Source[_position] != YamlSyntax.FlowSequenceEnd)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        _position++;

        CompleteContainer(arrayId);

        return arrayId;
    }

    private uint ParseBlockArray(int depth)
    {
        var baseIndent = _position - FindCurrentLineStart(_position);
        var arrayId = AllocToken(DToken.MakeArray(count: 0));
        _lastParsedNodeId = arrayId;

        // ParseNode already confirmed the first dash. Only subsequent entries need this lookahead.
        var firstEntry = true;
        while (_position < Source.Length && (firstEntry || IsBlockSequenceIndicator(_position)))
        {
            firstEntry = false;

            RequireAllowedDepth(depth + 1);

            _position++;

            var separatorStart = _position;

            SkipInlineSpaces();

            if (
                _position == Source.Length
                || YamlSyntax.IsLineEnd(Source[_position])
                || Source[_position] == YamlSyntax.CommentIndicator
            )
            {
                SkipLineRemainder();

                var (Position, Indent) = PeekNextContent();

                if (Indent > baseIndent)
                {
                    AdvanceToPeekedContent(Position);
                    ParseNode(depth + 1, parentIndent: baseIndent);
                }
                else
                {
                    ParseNull();
                }
            }
            else
            {
                // A tab cannot separate the "-" from a nested block collection: its column would be undefined.
                if (
                    Source[separatorStart.._position].Contains(YamlSyntax.Tab)
                    && (IsBlockSequenceIndicator(_position) || IsBlockMapStart())
                )
                {
                    ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                }

                var lineStart = FindCurrentLineStart(_position);

                ParseNode(depth + 1, parentIndent: baseIndent);

                if (FindCurrentLineStart(_position) == lineStart)
                {
                    ExpectLineEnd();
                }
            }

            IncToken(arrayId);
            SkipTrivia(leaveSameLineCommentForNextNode: false);

            if (_position >= Source.Length)
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
        while (hasValue)
        {
            var valueId = values.Current;
            hasValue = values.MoveNext();

            var nextTokenId = hasValue ? values.Current : followingTokenId;
            if (separatorWritten)
            {
                // See WriteMap: the previous entry's line break is deferred so its trailing comment can be added.
                if (options.PreserveTrivia && !endsWithBlockScalar)
                {
                    WriteTrailingComment(writer, reader, valueId, options);
                }

                writer.WriteIndentNewLine();
            }

            // Most writes omit trivia. Avoid entering its enumerator for every array element.
            if (options.PreserveTrivia)
            {
                WriteTrivia(
                    writer,
                    reader,
                    valueId,
                    options,
                    separatorWritten,
                    skipTrailingComment: separatorWritten
                );
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