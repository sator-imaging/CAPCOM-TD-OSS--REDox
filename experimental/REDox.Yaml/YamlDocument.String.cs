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

    private ParsedString ParseDoubleQuotedString(int parentIndent)
    {
        _position++;
        var builder = new StringBuilder();
        // Whitespace produced by escapes must survive the trimming done at line breaks.
        var protectedLength = 0;
        while (_position < _text.Length)
        {
            var current = _text[_position++];
            if (current == '"')
            {
                return new ParsedString(builder.ToString(), true);
            }

            if (current != '\\')
            {
                if (IsLineEnd(current))
                {
                    _position--;
                    FoldQuotedBreak(builder, protectedLength, parentIndent);
                    continue;
                }

                if (char.IsControl(current) && current != '\t')
                    throw Error(ParseErrorCode.InvalidString);
                builder.Append(current);
                continue;
            }

            if (_position >= _text.Length)
                throw Error(ParseErrorCode.InvalidString);
            current = _text[_position++];
            switch (current)
            {
                case '0':
                    builder.Append('\0');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 't':
                case '\t':
                    builder.Append('\t');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'e':
                    builder.Append('\x1b');
                    break;
                case ' ':
                    builder.Append(' ');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '/':
                    builder.Append('/');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '\r':
                    if (_position < _text.Length && _text[_position] == '\n')
                        _position++;
                    SkipInlineSpaces();
                    break;
                case '\n':
                    SkipInlineSpaces();
                    break;
                case 'N':
                    builder.Append('\x85');
                    break;
                case '_':
                    builder.Append('\xa0');
                    break;
                case 'L':
                    builder.Append('\u2028');
                    break;
                case 'P':
                    builder.Append('\u2029');
                    break;
                case 'x':
                    builder.Append((char)ParseEscapedCodePoint(2));
                    break;
                case 'u':
                case 'U':
                    var codePoint = ParseEscapedCodePoint(current == 'u' ? 4 : 8);
                    if (codePoint is >= 0xd800 and <= 0xdbff)
                    {
                        if (
                            _position + 2 > _text.Length
                            || _text[_position] != '\\'
                            || _text[_position + 1] != 'u'
                        )
                            throw Error(ParseErrorCode.InvalidString);
                        _position += 2;
                        var low = ParseEscapedCodePoint(4);
                        if (low is < 0xdc00 or > 0xdfff)
                            throw Error(ParseErrorCode.InvalidString);
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

            protectedLength = builder.Length;
        }

        throw Error(ParseErrorCode.InvalidString);
    }

    // Folds a line break inside a quoted scalar: one break becomes a space, N breaks become N-1 newlines.
    // Trailing whitespace before the break and leading whitespace after it are dropped.
    private void FoldQuotedBreak(StringBuilder builder, int protectedLength, int parentIndent)
    {
        var end = builder.Length;
        while (end > protectedLength && builder[end - 1] is ' ' or '\t')
            end--;
        builder.Length = end;

        var breaks = 0;
        while (_position < _text.Length && IsLineEnd(_text[_position]))
        {
            ConsumeLineEnd();
            breaks++;
            CheckQuotedContinuation(parentIndent);
            SkipInlineSpaces();
        }

        if (breaks == 1)
            builder.Append(' ');
        else
            builder.Append('\n', breaks - 1);
    }

    // A line that continues a quoted scalar must be indented deeper than the parent (tabs do not count as
    // indentation), and a document marker cannot appear inside the scalar.
    private void CheckQuotedContinuation(int parentIndent)
    {
        if (IsDocumentMarkerAt(_position))
            throw Error(ParseErrorCode.InvalidString);

        var indent = CountIndent(_position);
        var content = _position + indent;
        while (content < _text.Length && _text[content] is ' ' or '\t')
            content++;

        // A blank line has no indentation to check.
        if (content >= _text.Length || IsLineEnd(_text[content]))
            return;

        if (indent <= parentIndent)
            throw Error(ParseErrorCode.InvalidString);
    }

    private int ParseEscapedCodePoint(int digits)
    {
        if (
            _position + digits > _text.Length
            || !int.TryParse(
                _text.AsSpan(_position, digits),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var codePoint
            )
        )
        {
            throw Error(ParseErrorCode.InvalidString);
        }

        _position += digits;
        return codePoint;
    }

    private ParsedString ParseSingleQuotedString(int parentIndent)
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
            else if (IsLineEnd(current))
            {
                _position--;
                FoldQuotedBreak(builder, 0, parentIndent);
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
        // A plain scalar cannot start with "- "; reaching here means a sequence entry in an invalid position.
        // In a flow collection a lone "-" (as in "[-]" or "[-, -]") is invalid as well.
        if (
            IsBlockSequenceIndicator(_position)
            || flow && _text[_position] == '-' && _text[_position + 1] is ',' or ']' or '}'
        )
        {
            throw Error(ParseErrorCode.InvalidFormat);
        }

        var start = _position;
        while (_position < _text.Length)
        {
            var current = _text[_position];
            if (IsLineEnd(current) || (flow && current is ',' or ']' or '}'))
                break;
            if (current == '#' && (_position == start || char.IsWhiteSpace(_text[_position - 1])))
                break;
            if (
                !key
                && current == ':'
                && (
                    _position + 1 == _text.Length
                    || char.IsWhiteSpace(_text[_position + 1])
                    || _text[_position + 1] is ',' or '}' or ']'
                )
            )
                break;
            if (key && current == ':')
                break;
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
                // A comment line ends the scalar: it cannot continue after the comment.
                if (
                    next.Indent <= parentIndent
                    || next.Position >= _text.Length
                    || next.Indent == 0 && IsDocumentMarkerAt(next.Position)
                    || _text.AsSpan(lineEnd, next.Position - lineEnd).Contains('#')
                )
                {
                    _position = lineEnd;
                    break;
                }

                // A single physical break folds to a space; additional blank lines remain line breaks.
                // Do not add another separator before the continuation: the break already supplies it.
                var breaks = 0;
                for (var position = lineEnd; position < next.Position; position++)
                {
                    if (
                        _text[position] == '\n'
                        || _text[position] == '\r'
                            && (position + 1 == _text.Length || _text[position + 1] != '\n')
                    )
                        breaks++;
                }
                if (breaks == 1)
                    builder.Append(' ');
                else
                    builder.Append('\n', breaks - 1);
                _position = next.Position;
                var continuationStart = _position;
                while (_position < _text.Length && !IsLineEnd(_text[_position]))
                {
                    var current = _text[_position];

                    // " #" starts a comment, which ends the scalar.
                    if (
                        current == '#'
                        && _position > continuationStart
                        && _text[_position - 1] is ' ' or '\t'
                    )
                        break;

                    // A continuation line cannot hold a mapping key ("this\n is\n  invalid: x").
                    if (
                        current == ':'
                        && (
                            _position + 1 == _text.Length || char.IsWhiteSpace(_text[_position + 1])
                        )
                    )
                    {
                        throw Error(ParseErrorCode.InvalidFormat);
                    }

                    _position++;
                }

                builder.Append(
                    _text.AsSpan(continuationStart, _position - continuationStart).Trim()
                );
                if (_position < _text.Length && !IsLineEnd(_text[_position]))
                    break;
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
                if (chomping != '\0')
                    throw Error(ParseErrorCode.InvalidString);
                chomping = current;
            }
            else if (current is >= '1' and <= '9')
            {
                if (explicitIndent != 0)
                    throw Error(ParseErrorCode.InvalidString);
                explicitIndent = current - '0';
            }
            else if (current == '#')
            {
                // The comment must be separated from the indicators: ">#x" is invalid.
                if (_text[_position - 2] is not (' ' or '\t'))
                    throw Error(ParseErrorCode.InvalidString);

                SkipToLineEnd();
                break;
            }
            else if (current != ' ')
            {
                throw Error(ParseErrorCode.InvalidString);
            }
        }

        ConsumeLineEnd();
        // Like libyaml, the explicit indentation indicator counts from column 0 at the root.
        var contentIndent = explicitIndent == 0 ? -1 : Math.Max(parentIndent, 0) + explicitIndent;
        var lines = new System.Collections.Generic.List<string>();
        var seenContent = false;
        var leadingBlankIndent = 0;
        while (_position < _text.Length)
        {
            var lineStart = _position;
            var indent = CountIndent(lineStart);
            var contentStart = lineStart + indent;
            var empty = contentStart >= _text.Length || IsLineEnd(_text[contentStart]);
            if (!empty && contentIndent < 0)
            {
                if (indent <= parentIndent)
                    throw Error(ParseErrorCode.InvalidString);
                contentIndent = indent;
            }
            if (!empty && indent < contentIndent)
                break;

            if (empty && !seenContent)
            {
                leadingBlankIndent = Math.Max(leadingBlankIndent, indent);
            }
            else if (!empty && !seenContent)
            {
                // Blank lines before the first content line cannot hold more spaces than that line is indented.
                seenContent = true;
                if (leadingBlankIndent > contentIndent)
                    throw Error(ParseErrorCode.InvalidString);
            }

            _position = contentStart;
            SkipToLineEnd();
            var lineEnd = _position;
            var cut = contentIndent < 0 ? indent : Math.Min(indent, contentIndent);
            lines.Add(_text[(lineStart + cut)..lineEnd]);
            if (!ConsumeLineEnd())
                break;
        }

        var last = lines.Count - 1;
        while (last >= 0 && lines[last].Length == 0)
            last--;
        var trailingBreaks = lines.Count - 1 - last;

        if (last < 0)
        {
            // No content: only "keep" chomping preserves the (empty) lines.
            return new ParsedString(
                chomping == '+' ? new string('\n', lines.Count) : string.Empty,
                true
            );
        }

        var builder = new StringBuilder();
        var previous = -1;
        for (var index = 0; index <= last; index++)
        {
            if (lines[index].Length == 0)
                continue;

            if (previous < 0)
            {
                // Leading empty lines are kept as line breaks in both styles.
                builder.Append('\n', index);
            }
            else
            {
                var gap = index - previous - 1;
                // Folding applies only between two ordinary lines; more-indented lines keep their breaks.
                var keepBreak =
                    style == '|' || IsMoreIndented(lines[previous]) || IsMoreIndented(lines[index]);
                if (keepBreak)
                    builder.Append('\n', gap + 1);
                else if (gap == 0)
                    builder.Append(' ');
                else
                    builder.Append('\n', gap);
            }

            builder.Append(lines[index]);
            previous = index;
        }

        if (chomping != '-')
            builder.Append('\n');
        if (chomping == '+')
            builder.Append('\n', trailingBreaks);
        return new ParsedString(builder.ToString(), true);
    }

    private static bool IsMoreIndented(string line) => line.Length > 0 && line[0] is ' ' or '\t';

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        return ScalarBytes(token);
    }

    private static void WriteString(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        YamlWriteOptions options,
        int depth,
        bool key
    )
    {
        var value = reader.ReadString(tokenId);
        if (!key)
        {
            value = options.NormalizeStringNewlineAtEnd switch
            {
                YamlStringNewlineNormalization.Remove => value.TrimEnd('\n'),
                YamlStringNewlineNormalization.EnsureOne => value.TrimEnd('\n') + "\n",
                _ => value,
            };
        }

        if (!key && options.StringMode == YamlStringMode.Multiline)
        {
            WriteBlockString(writer, value, depth, options);
            return;
        }

        if (YamlStringHelper.CanWritePlain(value, key))
        {
            writer.WriteString(value);
            return;
        }

        var encoder = GetQuoteEncoder(writer.Settings.TextEncoderPolicy);
        var utf8 = Encoding.UTF8.GetBytes(value);
        var output = writer.BeginWriteUtf8Bytes(checked(utf8.Length * 6 + 2));
        writer.EndWriteUtf8Bytes(encoder.Encode(output, utf8));
    }

    [ThreadStatic]
    private static TextEncoderPolicy? s_quotePolicy;

    [ThreadStatic]
    private static JsonTextEncoder? s_quoteEncoder;

    private static JsonTextEncoder GetQuoteEncoder(TextEncoderPolicy policy)
    {
        // The encoder is rebuilt only when the settings' policy instance changes.
        if (s_quoteEncoder != null && ReferenceEquals(s_quotePolicy, policy))
            return s_quoteEncoder;

        var encoder = new TextEncoderPolicy(
            policy.EscapeRanges.ToArray(),
            policy.EscapeMask | TextEscapeMask.Delete
        )
        {
            UnicodeEscapes = policy.UnicodeEscapes,
            UpperCaseHexEscapes = policy.UpperCaseHexEscapes,
        }.GetEncoder<JsonTextEncoder>();

        s_quotePolicy = policy;
        s_quoteEncoder = encoder;
        return encoder;
    }

    private static void WriteBlockString(
        Utf8TextWriter writer,
        string value,
        int depth,
        YamlWriteOptions options
    )
    {
        // TODO: Add a Mixed string mode when callers need quoted single-line values.
        if (options.IndentCharacter != ' ' || options.IndentSize < 1)
        {
            throw new NotSupportedException("YAML block scalars require space indentation.");
        }

        foreach (var character in value)
        {
            if (
                character is '\r' or '\0'
                || char.IsControl(character) && character is not '\n' and not '\t'
            )
            {
                throw new NotSupportedException(
                    "This string cannot be written as a YAML block scalar."
                );
            }
        }

        var trailingNewlines = value.Length - value.TrimEnd('\n').Length;
        writer.WriteUtf8Byte((byte)'|');
        if (trailingNewlines == 0)
            writer.WriteUtf8Byte((byte)'-');
        else if (trailingNewlines > 1)
            writer.WriteUtf8Byte((byte)'+');

        var lines = value.Split('\n');
        var lineCount = trailingNewlines > 0 ? lines.Length - 1 : lines.Length;
        var firstContentLine = Array.Find(lines, line => line.Length > 0);
        if (firstContentLine is { Length: > 0 } && char.IsWhiteSpace(firstContentLine[0]))
        {
            // An explicit indent keeps leading spaces in the value instead of treating them as indentation.
            if (options.IndentSize > 9)
                throw new NotSupportedException(
                    "Leading whitespace requires a YAML indentation indicator of 1 to 9."
                );
            writer.WriteUtf8Byte((byte)('0' + options.IndentSize));
        }

        if (depth == 0)
            writer.PushIndent();
        for (var index = 0; index < lineCount; index++)
        {
            writer.WriteIndentNewLine();
            writer.WriteString(lines[index]);
        }
        if (depth == 0)
            writer.PopIndent();
    }
}
