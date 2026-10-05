// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

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
        var arrayId = AllocToken(DToken.MakeArray(0));
        _position++;

        SkipTrivia();
        CheckFlowIndent(parentIndent);

        while (_position < Source.Length && Source[_position] != YamlSyntax.FlowSequenceEnd)
        {
            if (Source[_position] == YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (
                IsExplicitKeyStart(true)
                || Source[_position]
                    is not (YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart)
                    && IsFlowSinglePairStart()
            )
            {
                ParseFlowSinglePair(depth + 1, parentIndent);
            }
            else
            {
                ParseNode(depth + 1, true, parentIndent);
            }

            IncToken(arrayId);
            SkipTrivia();
            CheckFlowIndent(parentIndent);

            if (_position >= Source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (Source[_position] == YamlSyntax.FlowSequenceEnd)
            {
                break;
            }

            if (Source[_position++] != YamlSyntax.FlowSeparator)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            SkipTrivia();
            CheckFlowIndent(parentIndent);
        }

        if (_position >= Source.Length || Source[_position++] != YamlSyntax.FlowSequenceEnd)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        CompleteContainer(arrayId);

        return arrayId;
    }

    private uint ParseBlockArray(int depth)
    {
        var baseIndent = _position - FindCurrentLineStart(_position);
        var arrayId = AllocToken(DToken.MakeArray(0));
        while (_position < Source.Length && IsBlockSequenceIndicator(_position))
        {
            RequireAllowedDepth(depth + 1);

            _position++;

            var separatorStart = _position;

            SkipInlineSpaces();

            var tabSeparated = Source[separatorStart.._position].Contains(YamlSyntax.Tab);
            if (
                _position == Source.Length
                || IsLineEnd(Source[_position])
                || Source[_position] == YamlSyntax.CommentIndicator
            )
            {
                SkipLineRemainder();

                var (Position, Indent) = PeekNextContent();

                if (Indent > baseIndent)
                {
                    SkipTrivia();
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
                if (tabSeparated && (IsBlockSequenceIndicator(_position) || IsBlockMapStart()))
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
            SkipTrivia();

            var following = PeekNextContent();
            if (following.Indent != baseIndent || following.Position >= Source.Length)
            {
                break;
            }

            _position = following.Position;

            if (IndentationContainsTab(_position))
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (!IsBlockSequenceIndicator(_position))
            {
                break;
            }
        }

        CompleteContainer(arrayId);

        return arrayId;
    }

    private static bool WriteArray(
        Utf8TextWriter writer,
        in DataReader reader,
        uint arrayId,
        YamlWriteOptions options,
        int depth,
        uint followingTokenId
    )
    {
        var count = reader.GetValueCount(arrayId);
        if (count == 0)
        {
            WriteEmptyCollection(writer, isMap: false, afterIndicator: false);

            return false;
        }

        var separatorWritten = false;
        var endsWithBlockScalar = false;
        var values = reader.EnumerateArray(arrayId).GetEnumerator();
        var hasValue = values.MoveNext();
        while (hasValue)
        {
            var valueId = values.Current;
            hasValue = values.MoveNext();

            var nextTokenId = hasValue ? values.Current : followingTokenId;
            if (separatorWritten)
            {
                // See WriteMap: the previous entry's line break is deferred so its trailing comment can be added.
                if (!endsWithBlockScalar)
                {
                    WriteInlineComment(writer, reader, valueId, options);
                }

                writer.WriteIndentNewLine();
            }

            WriteTrivia(
                writer,
                reader,
                valueId,
                options,
                separatorWritten,
                skipInlineComment: separatorWritten
            );

            WriteCollectionIndicator(writer, mapValue: false);

            var value = reader.GetToken(valueId);

            WriteValueSeparator(
                writer,
                blockValue: value.IsContainer && reader.GetValueCount(valueId) > 0
            );

            endsWithBlockScalar = WriteNode(
                writer,
                reader,
                valueId,
                options,
                depth + 1,
                false,
                nextTokenId
            );

            writer.PopIndent();

            separatorWritten = true;
        }

        return endsWithBlockScalar;
    }
}
