// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private bool IsBlockMapStart()
    {
        var position = _position;
        char quote = '\0';
        while (position < _text.Length && !IsLineEnd(_text[position]))
        {
            var current = _text[position];
            if (current == quote)
            {
                if (quote == '\'' && position + 1 < _text.Length && _text[position + 1] == '\'') position++;
                else quote = '\0';
            }
            else if (quote == '\0')
            {
                if (current is '"' or '\'') quote = current;
                else if (current == ':' && (position + 1 == _text.Length ||
                                             char.IsWhiteSpace(_text[position + 1]))) return true;
                else if (current == '#') break;
            }
            else if (quote == '"' && current == '\\') position++;
            position++;
        }
        return false;
    }

    private uint ParseFlowMap(int depth)
    {
        var mapId = AllocToken(DToken.MakeMap(0));
        HashSet<string>? keys = _parseOptions.EnableValueValidation ? new(StringComparer.Ordinal) : null;
        _position++;
        SkipTrivia();
        while (_position < _text.Length && _text[_position] != '}')
        {
            var keyId = ParseScalarNode(true, flow: true);
            ValidateUniqueKey(keys, keyId);
            SkipInlineSpaces();
            if (_position >= _text.Length || _text[_position++] != ':') throw Error(ParseErrorCode.InvalidFormat);
            SkipTrivia();
            if (_position < _text.Length && _text[_position] is ',' or '}') ParseNull();
            else ParseNode(depth + 1, true);
            IncToken(mapId);
            SkipTrivia();
            if (_position >= _text.Length) throw Error(ParseErrorCode.InvalidFormat);
            if (_text[_position] == '}') break;
            if (_text[_position++] != ',') throw Error(ParseErrorCode.InvalidFormat);
            SkipTrivia();
        }

        if (_position >= _text.Length || _text[_position++] != '}') throw Error(ParseErrorCode.InvalidFormat);
        CompleteContainer(mapId);
        return mapId;
    }

    private uint ParseBlockMap(int depth)
    {
        var baseIndent = _position - LastLineStart(_position);
        var mapId = AllocToken(DToken.MakeMap(0));
        HashSet<string>? keys = _parseOptions.EnableValueValidation ? new(StringComparer.Ordinal) : null;
        while (_position < _text.Length && IsBlockMapStart())
        {
            var keyId = ParseScalarNode(true);
            ValidateUniqueKey(keys, keyId);
            SkipInlineSpaces();
            if (_position >= _text.Length || _text[_position++] != ':') throw Error(ParseErrorCode.InvalidFormat);
            SkipInlineSpaces();
            if (_position >= _text.Length || IsLineEnd(_text[_position]) || _text[_position] == '#')
            {
                SkipToLineEnd();
                ConsumeLineEnd();
                var following = PeekNextContent();
                // A block sequence used as a mapping value may start at the key's indentation.
                if (following.Indent > baseIndent ||
                    following.Indent == baseIndent && IsBlockSequenceIndicator(following.Position))
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

            IncToken(mapId);
            SkipTrivia();
            var next = PeekNextContent();
            if (next.Indent != baseIndent || next.Position >= _text.Length) break;
            _position = next.Position;
            if (!IsBlockMapStart()) break;
        }

        CompleteContainer(mapId);
        return mapId;
    }

    private void ValidateUniqueKey(HashSet<string>? keys, uint keyId)
    {
        if (keys != null && !keys.Add(Encoding.UTF8.GetString(_scalarValues[keyId])))
        {
            throw Error(ParseErrorCode.InvalidFormat);
        }
    }

    private static void WriteMap(Utf8TextWriter writer, in DataReader reader, uint mapId,
        YamlWriteOptions options, int depth)
    {
        var count = reader.GetValueCount(mapId);
        if (count == 0)
        {
            writer.WriteString("{}"u8);
            return;
        }

        var separatorWritten = false;
        foreach (var pair in reader.EnumerateMap(mapId))
        {
            WriteTrivia(writer, reader, pair.Key, options, separatorWritten);
            WriteNode(writer, reader, pair.Key, options, depth, true);
            writer.WriteUtf8Byte((byte)':');
            var value = reader.GetToken(pair.Value);
            writer.PushIndent();
            if (value.IsContainer && reader.GetValueCount(pair.Value) > 0)
            {
                writer.WriteIndentNewLine();
            }
            else
            {
                writer.WriteUtf8Byte((byte)' ');
            }
            WriteNode(writer, reader, pair.Value, options, depth + 1, false);
            writer.PopIndent();
            if (--count > 0)
            {
                writer.WriteIndentNewLine();
                separatorWritten = true;
            }
        }
    }
}
