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
        byte quote = YamlSyntax.NoCharacter;
        while (position < Source.Length && !IsLineEnd(Source[position]))
        {
            var current = Source[position];
            if (current == quote)
            {
                if (
                    quote == YamlSyntax.SingleQuote
                    && position + 1 < Source.Length
                    && Source[position + 1] == YamlSyntax.SingleQuote
                )
                    position++;
                else
                    quote = YamlSyntax.NoCharacter;
            }
            else if (quote == YamlSyntax.NoCharacter)
            {
                // A quote starts a quoted key only at the start of the key; inside a plain key it is text. The
                // same goes for "#", which starts a comment only after white space.
                if (
                    current is YamlSyntax.DoubleQuote or YamlSyntax.SingleQuote
                    && position == _position
                )
                    quote = current;
                else if (
                    current == YamlSyntax.MappingValueIndicator
                    && (
                        position + 1 == Source.Length
                        || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position + 1])
                    )
                )
                    return true;
                else if (
                    current == YamlSyntax.CommentIndicator
                    && (
                        position == _position
                        || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position - 1])
                    )
                )
                    break;
            }
            else if (quote == YamlSyntax.DoubleQuote && current == YamlSyntax.Backslash)
                position++;
            position++;
        }
        return false;
    }

    private uint ParseFlowMap(int depth, int parentIndent)
    {
        var mapId = AllocToken(DToken.MakeMap(0));
        HashSet<string>? keys = _parseOptions.EnableValueValidation
            ? new(StringComparer.Ordinal)
            : null;
        _position++;
        SkipTrivia();
        CheckFlowIndent(parentIndent);
        while (_position < Source.Length && Source[_position] != YamlSyntax.FlowMappingEnd)
        {
            ParseFlowPair(mapId, depth, parentIndent, keys);
            SkipTrivia();
            CheckFlowIndent(parentIndent);
            if (_position >= Source.Length)
                throw Error(ParseException.ErrorCode.InvalidFormat);
            if (Source[_position] == YamlSyntax.FlowMappingEnd)
                break;
            if (Source[_position++] != YamlSyntax.FlowSeparator)
                throw Error(ParseException.ErrorCode.InvalidFormat);
            SkipTrivia();
            CheckFlowIndent(parentIndent);
        }

        if (_position >= Source.Length || Source[_position++] != YamlSyntax.FlowMappingEnd)
            throw Error(ParseException.ErrorCode.InvalidFormat);
        CompleteContainer(mapId);
        return mapId;
    }

    // One "key: value" entry of a flow mapping, also used for the single pair in a flow sequence. The colon may be on
    // a later line than its key, and a key without a colon has a null value ("{ a, b: c }").
    private void ParseFlowPair(uint mapId, int depth, int parentIndent, HashSet<string>? keys)
    {
        uint keyId;
        if (IsExplicitKeyStart(true))
        {
            // "? key": the key may span lines, and nothing after the "?" is a null key. A collection is not supported.
            _position++;
            SkipTrivia();
            CheckFlowIndent(parentIndent);
            if (_position >= Source.Length)
                throw Error(ParseException.ErrorCode.InvalidFormat);

            if (
                Source[_position]
                is YamlSyntax.FlowSeparator
                    or YamlSyntax.FlowMappingEnd
                    or YamlSyntax.FlowSequenceEnd
                    or YamlSyntax.MappingValueIndicator
            )
                keyId = ParseNull();
            else if (
                Source[_position] is YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart
            )
                throw Error(ParseException.ErrorCode.UnsupportedFeature);
            else
                keyId = ParseScalarNode(false, parentIndent, true, isKey: true);
        }
        else
        {
            keyId = ParseScalarNode(true, parentIndent, true);
        }

        ValidateKey(keys, keyId);
        SkipTrivia();
        CheckFlowIndent(parentIndent);
        if (_position >= Source.Length)
            throw Error(ParseException.ErrorCode.InvalidFormat);

        if (Source[_position] == YamlSyntax.MappingValueIndicator)
        {
            _position++;
            SkipTrivia();
            CheckFlowIndent(parentIndent);
            if (
                _position < Source.Length
                && Source[_position]
                    is YamlSyntax.FlowSeparator
                        or YamlSyntax.FlowMappingEnd
                        or YamlSyntax.FlowSequenceEnd
            )
                ParseNull();
            else
                ParseNode(depth + 1, true, parentIndent);
        }
        else if (
            Source[_position]
            is YamlSyntax.FlowSeparator
                or YamlSyntax.FlowMappingEnd
                or YamlSyntax.FlowSequenceEnd
        )
        {
            ParseNull();
        }
        else
        {
            throw Error(ParseException.ErrorCode.InvalidFormat);
        }

        IncToken(mapId);
    }

    // A single "key: value" pair directly inside a flow sequence is a one-entry mapping ("[ foo: bar ]").
    private uint ParseFlowSinglePair(int depth, int parentIndent)
    {
        var mapId = AllocToken(DToken.MakeMap(0));
        ParseFlowPair(mapId, depth, parentIndent, null);
        CompleteContainer(mapId);
        return mapId;
    }

    // Looks ahead on the current line: a quoted or plain scalar followed by ":" starts a single pair. An implicit
    // key never spans lines.
    private bool IsFlowSinglePairStart()
    {
        var position = _position;
        if (Source[position] is YamlSyntax.DoubleQuote or YamlSyntax.SingleQuote)
        {
            var quote = Source[position++];
            while (position < Source.Length && !IsLineEnd(Source[position]))
            {
                var current = Source[position++];
                if (quote == YamlSyntax.DoubleQuote && current == YamlSyntax.Backslash)
                {
                    position++;
                    continue;
                }

                if (current != quote)
                    continue;

                if (
                    quote == YamlSyntax.SingleQuote
                    && position < Source.Length
                    && Source[position] == YamlSyntax.SingleQuote
                )
                {
                    position++;
                    continue;
                }

                while (
                    position < Source.Length
                    && Source[position] is YamlSyntax.Space or YamlSyntax.Tab
                )
                    position++;

                return position < Source.Length
                    && Source[position] == YamlSyntax.MappingValueIndicator;
            }

            return false;
        }

        while (position < Source.Length)
        {
            var current = Source[position];
            if (IsLineEnd(current) || YamlSyntax.IsFlowIndicator(current))
                return false;
            if (
                current == YamlSyntax.CommentIndicator
                && position > _position
                && YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position - 1])
            )
                return false;
            if (
                current == YamlSyntax.MappingValueIndicator
                && (
                    position + 1 == Source.Length
                    || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[position + 1])
                    || Source[position + 1]
                        is YamlSyntax.FlowSeparator
                            or YamlSyntax.FlowSequenceEnd
                            or YamlSyntax.FlowMappingEnd
                )
            )
                return true;
            position++;
        }

        return false;
    }

    // "?" is the explicit key indicator only when it stands alone: followed by white space or the end of the text, or,
    // in a flow collection, by a flow indicator. "?x" is a plain scalar.
    private bool IsExplicitKeyStart(bool flow)
    {
        if (Source[_position] != YamlSyntax.ExplicitKeyIndicator)
            return false;

        return _position + 1 == Source.Length
            || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position + 1])
            || flow
                && Source[_position + 1]
                    is YamlSyntax.FlowSeparator
                        or YamlSyntax.FlowSequenceEnd
                        or YamlSyntax.FlowMappingEnd;
    }

    private uint ParseBlockMap(int depth)
    {
        var baseIndent = _position - LastLineStart(_position);
        var mapId = AllocToken(DToken.MakeMap(0));
        HashSet<string>? keys = _parseOptions.EnableValueValidation
            ? new(StringComparer.Ordinal)
            : null;
        while (_position < Source.Length && (IsExplicitKeyStart(false) || IsBlockMapStart()))
        {
            if (IsExplicitKeyStart(false))
            {
                ParseExplicitMapEntry(depth, baseIndent, keys);
            }
            else
            {
                ParseImplicitMapEntry(depth, baseIndent, keys);
            }

            IncToken(mapId);
            SkipTrivia();
            var next = PeekNextContent();
            if (next.Indent != baseIndent || next.Position >= Source.Length)
                break;
            _position = next.Position;
            if (IndentationContainsTab(_position))
                throw Error(ParseException.ErrorCode.InvalidFormat);
            if (!IsExplicitKeyStart(false) && !IsBlockMapStart())
                break;
        }

        CompleteContainer(mapId);
        return mapId;
    }

    // "key: value"
    private void ParseImplicitMapEntry(int depth, int baseIndent, HashSet<string>? keys)
    {
        var keyId = ParseScalarNode(true);
        ValidateKey(keys, keyId);
        SkipInlineSpaces();
        if (_position >= Source.Length || Source[_position++] != YamlSyntax.MappingValueIndicator)
            throw Error(ParseException.ErrorCode.InvalidFormat);
        SkipInlineSpaces();
        if (
            _position >= Source.Length
            || IsLineEnd(Source[_position])
            || Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            ParseMapValueOnFollowingLines(depth, baseIndent);
        }
        else
        {
            var lineStart = LastLineStart(_position);
            ParseNode(depth + 1, parentIndent: baseIndent, mapValueOnKeyLine: true);

            if (LastLineStart(_position) == lineStart)
            {
                ExpectLineEnd();
            }
        }
    }

    // The value of an entry that has nothing after its ":" on the line: the following lines, or null.
    private void ParseMapValueOnFollowingLines(int depth, int baseIndent)
    {
        SkipLineRemainder();
        var following = PeekNextContent();
        // A block sequence used as a mapping value may start at the key's indentation.
        if (
            following.Indent > baseIndent
            || following.Indent == baseIndent && IsBlockSequenceIndicator(following.Position)
        )
        {
            SkipTrivia();
            ParseNode(depth + 1, parentIndent: baseIndent);
        }
        else
        {
            ParseNull();
        }
    }

    // "? key" and the optional ": value" at the same indentation. Only a scalar can be the key; a collection as a key is
    // not supported. The value may start on the line of the ":" even when it is a block collection ("? a" and
    // ": - b"), and an entry without ": value" has a null value.
    private void ParseExplicitMapEntry(int depth, int baseIndent, HashSet<string>? keys)
    {
        _position++;
        var keyId = ParseExplicitKey(depth, baseIndent);
        ValidateKey(keys, keyId);
        SkipTrivia();

        if (
            _position < Source.Length
            && Source[_position] == YamlSyntax.MappingValueIndicator
            && _position - LastLineStart(_position) == baseIndent
            && (
                _position + 1 == Source.Length
                || YamlSyntax.IsWhiteSpaceOrLineEnd(Source[_position + 1])
            )
        )
        {
            _position++;
            SkipInlineSpaces();
            if (
                _position >= Source.Length
                || IsLineEnd(Source[_position])
                || Source[_position] == YamlSyntax.CommentIndicator
            )
            {
                ParseMapValueOnFollowingLines(depth, baseIndent);
            }
            else
            {
                var lineStart = LastLineStart(_position);
                ParseNode(depth + 1, parentIndent: baseIndent);
                if (LastLineStart(_position) == lineStart)
                {
                    ExpectLineEnd();
                }
            }
        }
        else
        {
            ParseNull();
        }
    }

    // The key after "?": a scalar on the same line, or on the following lines, or nothing (a null key).
    private uint ParseExplicitKey(int depth, int baseIndent)
    {
        SkipInlineSpaces();
        if (
            _position >= Source.Length
            || IsLineEnd(Source[_position])
            || Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            SkipLineRemainder();
            var following = PeekNextContent();
            if (following.Indent > baseIndent)
            {
                SkipTrivia();
                return ParseExplicitKeyScalar(depth, baseIndent);
            }

            // A sequence at the indentation of the "?" is the key (a collection).
            if (following.Indent == baseIndent && IsBlockSequenceIndicator(following.Position))
                throw Error(ParseException.ErrorCode.UnsupportedFeature);

            return ParseNull();
        }

        return ParseExplicitKeyScalar(depth, baseIndent);
    }

    private uint ParseExplicitKeyScalar(int depth, int baseIndent)
    {
        if (
            Source[_position] is YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart
            || IsBlockSequenceIndicator()
            || IsExplicitKeyStart(false)
            || IsBlockMapStart()
        )
        {
            throw Error(ParseException.ErrorCode.UnsupportedFeature);
        }

        return ParseNode(depth + 1, parentIndent: baseIndent, mapValueOnKeyLine: true, isKey: true);
    }

    private void ValidateKey(HashSet<string>? keys, uint keyId)
    {
        var token = GetToken(keyId);

        // A key with no text is a null key; with ReadMapKeyAsString there is no string to read ("null" as a key is
        // text, and is read as the string "null").
        if (_parseOptions.ReadMapKeyAsString && token.Kind == DTokenKind.Null)
        {
            throw Error(ParseException.ErrorCode.InvalidFormat);
        }

        // Keys are the same when their types and their texts are: the integer 1 and the string "1" are different keys.
        if (
            keys != null
            && !keys.Add($"{token.Kind}:{Encoding.UTF8.GetString(ScalarBytes(token))}")
        )
        {
            throw Error(ParseException.ErrorCode.InvalidFormat);
        }
    }

    private static void WriteMap(
        Utf8TextWriter writer,
        in DataReader reader,
        uint mapId,
        YamlWriteOptions options,
        int depth
    )
    {
        var count = reader.GetValueCount(mapId);
        if (count == 0)
        {
            writer.WriteString(YamlSyntax.EmptyMappingUtf8);
            return;
        }

        var separatorWritten = false;
        foreach (var pair in reader.EnumerateMap(mapId))
        {
            if (separatorWritten)
            {
                // The line break after the previous entry is written here, not at the end of that entry, so that
                // the comment which ended its line (stored as leading trivia of this key) can still be appended.
                WriteInlineComment(writer, reader, pair.Key, options);
                writer.WriteIndentNewLine();
            }

            WriteTrivia(
                writer,
                reader,
                pair.Key,
                options,
                separatorWritten,
                skipInlineComment: separatorWritten
            );
            WriteNode(writer, reader, pair.Key, options, depth, true);
            writer.WriteUtf8Byte((byte)YamlSyntax.MappingValueIndicator);
            var value = reader.GetToken(pair.Value);
            var block = value.IsContainer && reader.GetValueCount(pair.Value) > 0;
            writer.PushIndent();
            if (block)
            {
                writer.WriteIndentNewLine();
                // Comments between "key:" and a nested block are leading trivia of that block.
                WriteTrivia(writer, reader, pair.Value, options, true);
            }
            else
            {
                writer.WriteUtf8Byte((byte)YamlSyntax.Space);
            }
            WriteNode(writer, reader, pair.Value, options, depth + 1, false);
            // "key: # note" followed by a non-block value: only a block scalar would swallow the comment.
            if (!block && value.Kind is not (DTokenKind.String or DTokenKind.Symbol))
            {
                WriteInlineComment(writer, reader, pair.Value, options);
            }

            writer.PopIndent();
            separatorWritten = true;
        }
    }
}
