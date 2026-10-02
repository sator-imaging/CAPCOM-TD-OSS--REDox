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
        var previous = _text.LastIndexOf('\n', Math.Max(0, position - 1));
        return previous + 1;
    }

    private int CountIndent(int lineStart)
    {
        var index = lineStart;
        while (index < _text.Length && _text[index] == ' ') index++;
        return index - lineStart;
    }

    private int CurrentBlockIndent() => CountIndent(LastLineStart(_position));

    private bool ConsumeLineEnd()
    {
        if (_position >= _text.Length || !IsLineEnd(_text[_position])) return false;
        if (_text[_position++] == '\r' && _position < _text.Length && _text[_position] == '\n') _position++;
        return true;
    }

    private void SkipToLineEnd()
    {
        while (_position < _text.Length && !IsLineEnd(_text[_position])) _position++;
    }

    private void SkipInlineSpaces()
    {
        while (_position < _text.Length && _text[_position] is ' ' or '\t') _position++;
    }

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            var start = _position;
            if (_text[_position] == '#')
            {
                SkipToLineEnd();
                if (_parseOptions.PreserveTrivia)
                    StoreScalar(DTokenVariant.TriviaLineComment, _text[start.._position]);
            }
            else if (char.IsWhiteSpace(_text[_position]))
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
                if (_parseOptions.PreserveTrivia)
                    StoreScalar(DTokenVariant.TriviaWhitespace, _text[start.._position]);
            }
            else
            {
                break;
            }
        }
    }

    private (int Position, int Indent) PeekNextContent()
    {
        var position = _position;
        while (position < _text.Length)
        {
            var lineStart = LastLineStart(position);
            var indent = CountIndent(lineStart);
            position = Math.Max(position, lineStart + indent);
            if (position >= _text.Length) return (position, -1);
            if (_text[position] == '#' || IsLineEnd(_text[position]))
            {
                while (position < _text.Length && !IsLineEnd(_text[position])) position++;
                if (position < _text.Length && _text[position++] == '\r' &&
                    position < _text.Length && _text[position] == '\n') position++;
                continue;
            }
            return (position, indent);
        }

        return (position, -1);
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        return _scalarValues[(uint)DToken.DecodeLengthOffsetPayload(token).offset];
    }

    private static void WriteTrivia(Utf8TextWriter writer, in DataReader reader, uint tokenId,
        YamlWriteOptions options, bool separatorWritten)
    {
        if (!options.PreserveTrivia) return;
        foreach (var triviaId in reader.EnumerateTrivia(tokenId))
        {
            var trivia = reader.GetToken(triviaId);
            if (trivia.Variant == DTokenVariant.TriviaWhitespace)
            {
                var whitespace = reader.ReadTrivia(triviaId);
                for (var index = 0; index < whitespace.Length; index++)
                {
                    if (whitespace[index] != '\n' &&
                        (whitespace[index] != '\r' || index + 1 < whitespace.Length && whitespace[index + 1] == '\n'))
                        continue;

                    if (separatorWritten) separatorWritten = false;
                    else writer.WriteIndentNewLine();
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

    private static void WriteTrailingTrivia(Utf8TextWriter writer, in DataReader reader,
        IReadOnlyList<uint> triviaIds)
    {
        var separatorWritten = true;
        foreach (var triviaId in triviaIds)
        {
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
                    if (character != '\n') continue;
                    if (separatorWritten) separatorWritten = false;
                    else writer.WriteNewLine();
                }
            }
        }
    }
}
