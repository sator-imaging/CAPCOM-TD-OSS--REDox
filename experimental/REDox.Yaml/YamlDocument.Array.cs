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
        return _text[position] == '-' &&
               (position + 1 == _text.Length || char.IsWhiteSpace(_text[position + 1]));
    }

    private uint ParseFlowArray(int depth)
    {
        var arrayId = AllocToken(DToken.MakeArray(0));
        _position++;
        SkipTrivia();
        while (_position < _text.Length && _text[_position] != ']')
        {
            if (_text[_position] == ',') throw Error(ParseErrorCode.InvalidFormat);
            ParseNode(depth + 1, true);
            IncToken(arrayId);
            SkipTrivia();
            if (_position >= _text.Length) throw Error(ParseErrorCode.InvalidFormat);
            if (_text[_position] == ']') break;
            if (_text[_position++] != ',') throw Error(ParseErrorCode.InvalidFormat);
            SkipTrivia();
        }

        if (_position >= _text.Length || _text[_position++] != ']') throw Error(ParseErrorCode.InvalidFormat);
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
            SkipInlineSpaces();
            if (_position == _text.Length || IsLineEnd(_text[_position]) || _text[_position] == '#')
            {
                SkipToLineEnd();
                ConsumeLineEnd();
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
                ParseNode(depth + 1, parentIndent: baseIndent);
            }

            IncToken(arrayId);
            SkipTrivia();
            var following = PeekNextContent();
            if (following.Indent != baseIndent || following.Position >= _text.Length)
            {
                break;
            }
            _position = following.Position;
            if (!IsBlockSequenceIndicator()) break;
        }

        CompleteContainer(arrayId);
        return arrayId;
    }

    private static void WriteArray(Utf8TextWriter writer, in DataReader reader, uint arrayId,
        YamlWriteOptions options, int depth)
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
            WriteTrivia(writer, reader, valueId, options, separatorWritten);
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
            if (--count > 0)
            {
                writer.WriteIndentNewLine();
                separatorWritten = true;
            }
        }
    }
}
