// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private bool IsBlockSequenceIndicator()
    {
        return IsBlockSequenceIndicator(_position);
    }

    private bool IsBlockSequenceIndicator(int position)
    {
        return _text[position] == '-'
            && (position + 1 == _text.Length || char.IsWhiteSpace(_text[position + 1]));
    }

    private uint ParseFlowArray(int depth, int parentIndent)
    {
        var arrayId = AllocToken(DToken.MakeArray(0));
        _position++;
        SkipTrivia();
        CheckFlowIndent(parentIndent);
        while (_position < _text.Length && _text[_position] != ']')
        {
            if (_text[_position] == ',')
                throw Error(ParseErrorCode.InvalidFormat);
            ParseNode(depth + 1, true, parentIndent);
            IncToken(arrayId);
            SkipTrivia();
            CheckFlowIndent(parentIndent);
            if (_position >= _text.Length)
                throw Error(ParseErrorCode.InvalidFormat);
            if (_text[_position] == ']')
                break;
            if (_text[_position++] != ',')
                throw Error(ParseErrorCode.InvalidFormat);
            SkipTrivia();
            CheckFlowIndent(parentIndent);
        }

        if (_position >= _text.Length || _text[_position++] != ']')
            throw Error(ParseErrorCode.InvalidFormat);
        CompleteContainer(arrayId);
        return arrayId;
    }

    private uint ParseBlockArray(int depth)
    {
        var baseIndent = _position - LastLineStart(_position);
        var arrayId = AllocToken(DToken.MakeArray(0));
        while (_position < _text.Length && IsBlockSequenceIndicator())
        {
            _position++;
            var separatorStart = _position;
            SkipInlineSpaces();
            var tabSeparated = _text.AsSpan(separatorStart.._position).Contains('\t');
            if (_position == _text.Length || IsLineEnd(_text[_position]) || _text[_position] == '#')
            {
                SkipLineRemainder();
                var next = PeekNextContent();
                if (next.Indent > baseIndent)
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
                if (tabSeparated && (IsBlockSequenceIndicator() || IsBlockMapStart()))
                {
                    throw Error(ParseErrorCode.InvalidFormat);
                }

                var lineStart = LastLineStart(_position);
                ParseNode(depth + 1, parentIndent: baseIndent);
                if (LastLineStart(_position) == lineStart)
                {
                    ExpectLineEnd();
                }
            }

            IncToken(arrayId);
            SkipTrivia();
            var following = PeekNextContent();
            if (following.Indent != baseIndent || following.Position >= _text.Length)
            {
                break;
            }
            _position = following.Position;
            if (IndentationContainsTab(_position))
                throw Error(ParseErrorCode.InvalidFormat);
            if (!IsBlockSequenceIndicator())
                break;
        }

        CompleteContainer(arrayId);
        return arrayId;
    }

    private static void WriteArray(
        Utf8TextWriter writer,
        in DataReader reader,
        uint arrayId,
        YamlWriteOptions options,
        int depth
    )
    {
        var count = reader.GetValueCount(arrayId);
        if (count == 0)
        {
            writer.WriteString("[]"u8);
            return;
        }

        var separatorWritten = false;
        foreach (var valueId in reader.EnumerateArray(arrayId))
        {
            if (separatorWritten)
            {
                // See WriteMap: the previous entry's line break is deferred so its trailing comment can be added.
                WriteInlineComment(writer, reader, valueId, options);
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
            writer.WriteUtf8Byte((byte)'-');
            var value = reader.GetToken(valueId);
            writer.PushIndent();
            if (value.IsContainer && reader.GetValueCount(valueId) > 0)
            {
                writer.WriteIndentNewLine();
            }
            else
            {
                writer.WriteUtf8Byte((byte)' ');
            }
            WriteNode(writer, reader, valueId, options, depth + 1, false);
            writer.PopIndent();
            separatorWritten = true;
        }
    }
}
