// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private readonly record struct ParsedString(string Text, bool Quoted);

    private uint ParseString(string text) => StoreScalar(DTokenVariant.String, text);

    private ParsedString ParseDoubleQuotedString()
    {
        _position++;
        var builder = new StringBuilder();
        while (_position < _text.Length)
        {
            var current = _text[_position++];
            if (current == '"')
            {
                return new ParsedString(builder.ToString(), true);
            }

            if (current != '\\')
            {
                if (char.IsControl(current) && !IsLineEnd(current)) throw Error(ParseErrorCode.InvalidString);
                if (IsLineEnd(current))
                {
                    if (current == '\r' && _position < _text.Length && _text[_position] == '\n') _position++;
                    SkipInlineSpaces();
                    builder.Append(' ');
                    continue;
                }
                builder.Append(current);
                continue;
            }

            if (_position >= _text.Length) throw Error(ParseErrorCode.InvalidString);
            current = _text[_position++];
            switch (current)
            {
                case '0': builder.Append('\0'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 't':
                case '\t': builder.Append('\t'); break;
                case 'n': builder.Append('\n'); break;
                case 'v': builder.Append('\v'); break;
                case 'f': builder.Append('\f'); break;
                case 'r': builder.Append('\r'); break;
                case 'e': builder.Append('\x1b'); break;
                case ' ': builder.Append(' '); break;
                case '"': builder.Append('"'); break;
                case '/': builder.Append('/'); break;
                case '\\': builder.Append('\\'); break;
                case '\r':
                    if (_position < _text.Length && _text[_position] == '\n') _position++;
                    SkipInlineSpaces();
                    break;
                case '\n':
                    SkipInlineSpaces();
                    break;
                case 'N': builder.Append('\x85'); break;
                case '_': builder.Append('\xa0'); break;
                case 'L': builder.Append('\u2028'); break;
                case 'P': builder.Append('\u2029'); break;
                case 'x': builder.Append((char)ParseEscapedCodePoint(2)); break;
                case 'u':
                case 'U':
                    var codePoint = ParseEscapedCodePoint(current == 'u' ? 4 : 8);
                    if (codePoint is >= 0xd800 and <= 0xdbff)
                    {
                        if (_position + 2 > _text.Length || _text[_position] != '\\' ||
                            _text[_position + 1] != 'u') throw Error(ParseErrorCode.InvalidString);
                        _position += 2;
                        var low = ParseEscapedCodePoint(4);
                        if (low is < 0xdc00 or > 0xdfff) throw Error(ParseErrorCode.InvalidString);
                        codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + low - 0xdc00;
                    }
                    else if (codePoint is >= 0xdc00 and <= 0xdfff || codePoint > 0x10ffff)
                    {
                        throw Error(ParseErrorCode.InvalidString);
                    }
                    builder.Append(char.ConvertFromUtf32(codePoint));
                    break;
                default:
                    throw Error(ParseErrorCode.InvalidString);
            }
        }

        throw Error(ParseErrorCode.InvalidString);
    }

    private int ParseEscapedCodePoint(int digits)
    {
        if (_position + digits > _text.Length ||
            !int.TryParse(_text.AsSpan(_position, digits), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var codePoint))
        {
            throw Error(ParseErrorCode.InvalidString);
        }

        _position += digits;
        return codePoint;
    }

    private ParsedString ParseSingleQuotedString()
    {
        _position++;
        var builder = new StringBuilder();
        while (_position < _text.Length)
        {
            var current = _text[_position++];
            if (current == '\'')
            {
                if (_position < _text.Length && _text[_position] == '\'')
                {
                    builder.Append('\'');
                    _position++;
                }
                else
                {
                    return new ParsedString(builder.ToString(), true);
                }
            }
            else
            {
                builder.Append(current);
            }
        }

        throw Error(ParseErrorCode.InvalidString);
    }

    private ParsedString ParsePlainString(bool key, bool flow, int parentIndent)
    {
        var start = _position;
        while (_position < _text.Length)
        {
            var current = _text[_position];
            if (IsLineEnd(current) || (flow && current is ',' or ']' or '}')) break;
            if (current == '#' && (_position == start || char.IsWhiteSpace(_text[_position - 1]))) break;
            if (!key && current == ':' && (_position + 1 == _text.Length ||
                                          char.IsWhiteSpace(_text[_position + 1]) ||
                                          _text[_position + 1] is ',' or '}' or ']')) break;
            if (key && current == ':') break;
            _position++;
        }

        var text = _text[start.._position].Trim();
        if (!key && !flow)
        {
            var builder = new StringBuilder(text);
            while (_position < _text.Length && IsLineEnd(_text[_position]))
            {
                var lineEnd = _position;
                ConsumeLineEnd();
                var next = PeekNextContent();
                if (next.Indent <= parentIndent || next.Position >= _text.Length)
                {
                    _position = lineEnd;
                    break;
                }

                // A single physical break folds to a space; additional blank lines remain line breaks.
                // Do not add another separator before the continuation: the break already supplies it.
                var breaks = 0;
                for (var position = lineEnd; position < next.Position; position++)
                {
                    if (_text[position] == '\n' || _text[position] == '\r' &&
                        (position + 1 == _text.Length || _text[position + 1] != '\n')) breaks++;
                }
                if (breaks == 1) builder.Append(' ');
                else builder.Append('\n', breaks - 1);
                _position = next.Position;
                var continuationStart = _position;
                while (_position < _text.Length && !IsLineEnd(_text[_position])) _position++;
                builder.Append(_text.AsSpan(continuationStart, _position - continuationStart).Trim());
            }
            text = builder.ToString();
        }
        if (text.Length == 0 && _parseOptions.EnableValueValidation)
        {
            throw Error(ParseErrorCode.InvalidString);
        }

        return new ParsedString(text, false);
    }

    private ParsedString ParseBlockString(int parentIndent)
    {
        var style = _text[_position++];
        var chomping = '\0';
        var explicitIndent = 0;
        while (_position < _text.Length && !IsLineEnd(_text[_position]))
        {
            var current = _text[_position++];
            if (current is '+' or '-')
            {
                if (chomping != '\0') throw Error(ParseErrorCode.InvalidString);
                chomping = current;
            }
            else if (current is >= '1' and <= '9')
            {
                if (explicitIndent != 0) throw Error(ParseErrorCode.InvalidString);
                explicitIndent = current - '0';
            }
            else if (current == '#')
            {
                SkipToLineEnd();
                break;
            }
            else if (current != ' ')
            {
                throw Error(ParseErrorCode.InvalidString);
            }
        }

        ConsumeLineEnd();
        var contentIndent = explicitIndent == 0 ? -1 : parentIndent + explicitIndent;
        var lines = new System.Collections.Generic.List<string>();
        while (_position < _text.Length)
        {
            var lineStart = _position;
            var indent = CountIndent(lineStart);
            var contentStart = lineStart + indent;
            var empty = contentStart >= _text.Length || IsLineEnd(_text[contentStart]);
            if (!empty && contentIndent < 0)
            {
                if (indent <= parentIndent) throw Error(ParseErrorCode.InvalidString);
                contentIndent = indent;
            }
            if (!empty && indent < contentIndent) break;

            _position = contentStart;
            SkipToLineEnd();
            var lineEnd = _position;
            var cut = contentIndent < 0 ? indent : Math.Min(indent, contentIndent);
            lines.Add(_text[(lineStart + cut)..lineEnd]);
            if (!ConsumeLineEnd()) break;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                var fold = style == '>' && lines[index - 1].Length > 0 && lines[index].Length > 0;
                builder.Append(fold ? ' ' : '\n');
            }
            builder.Append(lines[index]);
        }

        if (lines.Count > 0) builder.Append('\n');
        var value = builder.ToString().TrimEnd('\n');
        if (chomping == '+') value = builder.ToString();
        else if (chomping != '-' && lines.Count > 0) value += "\n";
        return new ParsedString(value, true);
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        return _scalarValues[(uint)DToken.DecodeLengthOffsetPayload(token).offset];
    }

    private static void WriteString(Utf8TextWriter writer, in DataReader reader, uint tokenId,
        YamlWriteOptions options, int depth, bool key)
    {
        var value = reader.ReadString(tokenId);
        if (!key)
        {
            value = options.NormalizeStringNewlineAtEnd switch
            {
                YamlStringNewlineNormalization.Remove => value.TrimEnd('\n'),
                YamlStringNewlineNormalization.EnsureOne => value.TrimEnd('\n') + "\n",
                _ => value
            };
        }

        if (!key && options.StringMode == YamlStringMode.Multiline)
        {
            WriteBlockString(writer, value, depth, options);
            return;
        }

        var policy = writer.Settings.TextEncoderPolicy;
        var encoder = new TextEncoderPolicy(policy.EscapeRanges.ToArray(),
            policy.EscapeMask | TextEscapeMask.Delete)
        {
            UnicodeEscapes = policy.UnicodeEscapes,
            UpperCaseHexEscapes = policy.UpperCaseHexEscapes
        }.GetEncoder<JsonTextEncoder>();
        var utf8 = Encoding.UTF8.GetBytes(value);
        var output = writer.BeginWriteUtf8Bytes(checked(utf8.Length * 6 + 2));
        writer.EndWriteUtf8Bytes(encoder.Encode(output, utf8));
    }

    private static void WriteBlockString(Utf8TextWriter writer, string value, int depth,
        YamlWriteOptions options)
    {
        // TODO: Add a Mixed string mode when callers need quoted single-line values.
        if (options.IndentCharacter != ' ' || options.IndentSize < 1)
        {
            throw new NotSupportedException("YAML block scalars require space indentation.");
        }

        foreach (var character in value)
        {
            if (character is '\r' or '\0' || char.IsControl(character) && character is not '\n' and not '\t')
            {
                throw new NotSupportedException("This string cannot be written as a YAML block scalar.");
            }
        }

        var trailingNewlines = value.Length - value.TrimEnd('\n').Length;
        writer.WriteUtf8Byte((byte)'|');
        if (trailingNewlines == 0) writer.WriteUtf8Byte((byte)'-');
        else if (trailingNewlines > 1) writer.WriteUtf8Byte((byte)'+');

        var lines = value.Split('\n');
        var lineCount = trailingNewlines > 0 ? lines.Length - 1 : lines.Length;
        var firstContentLine = Array.Find(lines, line => line.Length > 0);
        if (firstContentLine is { Length: > 0 } && char.IsWhiteSpace(firstContentLine[0]))
        {
            // An explicit indent keeps leading spaces in the value instead of treating them as indentation.
            if (options.IndentSize > 9)
                throw new NotSupportedException("Leading whitespace requires a YAML indentation indicator of 1 to 9.");
            writer.WriteUtf8Byte((byte)('0' + options.IndentSize));
        }

        if (depth == 0) writer.PushIndent();
        for (var index = 0; index < lineCount; index++)
        {
            writer.WriteIndentNewLine();
            writer.WriteString(lines[index]);
        }
        if (depth == 0) writer.PopIndent();
    }
}
