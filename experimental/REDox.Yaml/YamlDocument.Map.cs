using System;
using System.Collections.Generic;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private bool IsBlockMapStart()
    {
        // Quoted keys need byte-wise escape handling; plain keys can jump to their syntax bytes.
        var source = Source;
        var position = _position;
        byte quote = YamlSyntax.NoCharacter;
        while (position < source.Length && !YamlSyntax.IsLineEnd(source[position]))
        {
            if (quote == YamlSyntax.NoCharacter && position > _position)
            {
                var next = source[position..].IndexOfAny(YamlSyntax.BlockMapKeyStops);
                if (next < 0)
                {
                    return false;
                }

                position += next;

                if (YamlSyntax.IsLineEnd(source[position]))
                {
                    break;
                }
            }

            var current = source[position];
            if (current == quote)
            {
                if (
                    quote == YamlSyntax.SingleQuote
                    && position + 1 < source.Length
                    && source[position + 1] == YamlSyntax.SingleQuote
                )
                {
                    position++;
                }
                else
                {
                    quote = YamlSyntax.NoCharacter;
                }
            }
            else if (quote == YamlSyntax.NoCharacter)
            {
                // A quote starts a quoted key only at the start of the key; inside a plain key it is text. The
                // same goes for "#", which starts a comment only after white space.
                if (
                    current is YamlSyntax.DoubleQuote or YamlSyntax.SingleQuote
                    && position == _position
                )
                {
                    quote = current;
                }
                else if (
                    current == YamlSyntax.MappingValueIndicator
                    && (
                        position + 1 == source.Length
                        || YamlSyntax.IsWhiteSpaceOrLineEnd(source[position + 1])
                    )
                )
                {
                    return true;
                }
                else if (
                    current == YamlSyntax.CommentIndicator
                    && (
                        position == _position
                        || YamlSyntax.IsWhiteSpaceOrLineEnd(source[position - 1])
                    )
                )
                {
                    break;
                }
            }
            else if (quote == YamlSyntax.DoubleQuote && current == YamlSyntax.Backslash)
            {
                position++;
            }

            position++;
        }

        return false;
    }

    private uint ParseFlowMap(int depth, int parentIndent)
    {
        // Child parsing advances the cursor without replacing the input buffer.
        var source = Source;
        var mapId = AllocToken(DToken.MakeMap(count: 0));
        _lastParsedNodeId = mapId;

        _position++;

        SkipTrivia(leaveSameLineCommentForNextNode: true);
        ValidateFlowContinuationIndent(parentIndent);

        HashSet<DElement>? keys = _parseOptions.EnableValueValidation
            ? new(new MapKeyContentComparer())
            : null;

        while (_position < source.Length && source[_position] != YamlSyntax.FlowMappingEnd)
        {
            ParseFlowPair(mapId, depth, parentIndent, keys);
            SkipTrivia(leaveSameLineCommentForNextNode: false);
            ValidateFlowContinuationIndent(parentIndent);

            if (_position >= source.Length)
            {
                ThrowParseError(ParseException.ErrorCode.InvalidFormat);
            }

            if (source[_position] == YamlSyntax.FlowMappingEnd)
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

        if (_position >= source.Length || source[_position] != YamlSyntax.FlowMappingEnd)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        _position++;

        CompleteContainer(mapId);

        return mapId;
    }

    // One "key: value" entry of a flow mapping, also used for the single pair in a flow sequence.
    // The colon may be on a later line than its key, and a key without a colon has a null value ("{ a, b: c }").
    private void ParseFlowPair(uint mapId, int depth, int parentIndent, HashSet<DElement>? keys)
    {
        RequireAllowedDepth(depth + 1);

        uint keyId;
        if (IsExplicitKeyStart(flow: true))
        {
            keyId = ParseExplicitFlowKey(this, parentIndent);

            static uint ParseExplicitFlowKey(YamlDocument document, int parentIndent)
            {
                // "? key": the key may span lines, and nothing after the "?" is a null key. A collection is not supported.
                document._position++;

                document.SkipTrivia(leaveSameLineCommentForNextNode: true);
                document.ValidateFlowContinuationIndent(parentIndent);

                if (document._position >= document.Source.Length)
                {
                    document.ThrowParseError(ParseException.ErrorCode.InvalidFormat);
                }

                var keyStart = document.Source[document._position];

                return keyStart
                    is YamlSyntax.FlowSeparator
                    or YamlSyntax.FlowMappingEnd
                    or YamlSyntax.FlowSequenceEnd
                    or YamlSyntax.MappingValueIndicator
                    ? document.CreateNullToken()
                    : keyStart is YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart
                        ? throw document.ParseError(ParseException.ErrorCode.UnsupportedFeature)
                        : document.ParseScalarNode(
                            ScalarKeyContext.ExplicitMapKey,
                            parentIndent,
                            flow: true
                        );
            }
        }
        else
        {
            keyId = ParseScalarNode(ScalarKeyContext.ImplicitMapKey, parentIndent, flow: true);
        }

        ValidateMissingStringOrDuplicateMapKey(keys, keyId);

        _lastParsedNodeId = keyId;

        SkipTrivia(leaveSameLineCommentForNextNode: false);
        ValidateFlowContinuationIndent(parentIndent);

        if (_position >= Source.Length)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        var separator = Source[_position];
        if (separator == YamlSyntax.MappingValueIndicator)
        {
            _position++;

            SkipTrivia(leaveSameLineCommentForNextNode: false);
            ValidateFlowContinuationIndent(parentIndent);

            if (
                _position < Source.Length
                && Source[_position]
                    is YamlSyntax.FlowSeparator
                    or YamlSyntax.FlowMappingEnd
                    or YamlSyntax.FlowSequenceEnd
            )
            {
                CreateNullToken();
            }
            else
            {
                ParseNode(depth + 1, flow: true, parentIndent);
            }
        }
        else if (
            separator
            is YamlSyntax.FlowSeparator
            or YamlSyntax.FlowMappingEnd
            or YamlSyntax.FlowSequenceEnd
        )
        {
            CreateNullToken();
        }
        else
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        IncToken(mapId);
    }

    // A single "key: value" pair directly inside a flow sequence is a one-entry mapping ("[ foo: bar ]").
    private uint ParseFlowSinglePair(int depth, int parentIndent)
    {
        RequireAllowedDepth(depth);

        var mapId = AllocToken(DToken.MakeMap(count: 0));
        _lastParsedNodeId = mapId;

        ParseFlowPair(mapId, depth, parentIndent, keys: null);
        CompleteContainer(mapId);

        return mapId;
    }

    // Looks ahead on the current line
    // A quoted or plain scalar followed by ":" starts a single pair. An implicit key never spans lines.
    private bool IsFlowSinglePairStart()
    {
        // Lookahead does not change the source so reuse its span across both key styles.
        var source = Source;
        var position = _position;
        if (source[position] is YamlSyntax.DoubleQuote or YamlSyntax.SingleQuote)
        {
            var quote = source[position++];
            while (position < source.Length && !YamlSyntax.IsLineEnd(source[position]))
            {
                var current = source[position++];
                if (quote == YamlSyntax.DoubleQuote && current == YamlSyntax.Backslash)
                {
                    position++;

                    continue;
                }

                if (current != quote)
                {
                    continue;
                }

                if (
                    quote == YamlSyntax.SingleQuote
                    && position < source.Length
                    && source[position] == YamlSyntax.SingleQuote
                )
                {
                    position++;

                    continue;
                }

                var firstNonSpace = source[position..]
                    .IndexOfAnyExcept(YamlSyntax.Space, YamlSyntax.Tab);

                position = firstNonSpace < 0 ? source.Length : position + firstNonSpace;

                return position < source.Length
                       && source[position] == YamlSyntax.MappingValueIndicator;
            }

            return false;
        }

        while (position < source.Length)
        {
            var current = source[position];
            if (YamlSyntax.IsLineEnd(current) || YamlSyntax.IsFlowIndicator(current))
            {
                return false;
            }

            if (
                current == YamlSyntax.CommentIndicator
                && position > _position
                && YamlSyntax.IsWhiteSpaceOrLineEnd(source[position - 1])
            )
            {
                return false;
            }

            if (
                current == YamlSyntax.MappingValueIndicator
                && (
                    position + 1 == source.Length
                    || YamlSyntax.IsWhiteSpaceOrLineEnd(source[position + 1])
                    || source[position + 1]
                        is YamlSyntax.FlowSeparator
                        or YamlSyntax.FlowSequenceEnd
                        or YamlSyntax.FlowMappingEnd
                )
            )
            {
                return true;
            }

            position++;
        }

        return false;
    }

    // "?" is the explicit key indicator only when it stands alone
    // Followed by white space or the end of the text, or, in a flow collection,
    // by a flow indicator. "?x" is a plain scalar.
    private bool IsExplicitKeyStart(bool flow)
    {
        var source = Source;
        if (source[_position] != YamlSyntax.ExplicitKeyIndicator)
        {
            return false;
        }

        var followingPosition = _position + 1;
        if (followingPosition == source.Length)
        {
            return true;
        }

        // A following character is enough to distinguish an explicit key from plain text such as "?x".
        var following = source[followingPosition];

        return YamlSyntax.IsWhiteSpaceOrLineEnd(following)
               || flow
               && following
                   is YamlSyntax.FlowSeparator
                   or YamlSyntax.FlowSequenceEnd
                   or YamlSyntax.FlowMappingEnd;
    }

    private uint ParseBlockMap(int depth)
    {
        // Entry parsing changes positions, not the source span shared by all entries.
        var source = Source;
        var baseIndent = _position - FindCurrentLineStart(_position);

        var mapId = AllocToken(DToken.MakeMap(count: 0));
        _lastParsedNodeId = mapId;

        HashSet<DElement>? keys = _parseOptions.EnableValueValidation
            ? new(new MapKeyContentComparer())
            : null;

        // ParseNode already confirmed the first entry starts a block map. Scanning its line again only delays parsing.
        var firstEntry = true;
        while (_position < source.Length)
        {
            // Reuse this lookahead result when choosing which entry parser to call.
            var isExplicitKey = IsExplicitKeyStart(flow: false);
            if (!firstEntry && !isExplicitKey && !IsBlockMapStart())
            {
                break;
            }

            firstEntry = false;

            if (isExplicitKey)
            {
                ParseExplicitMapEntry(depth, baseIndent, keys);
            }
            else
            {
                ParseImplicitMapEntry(depth, baseIndent, keys);
            }

            IncToken(mapId);
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

            // Rechecking the next key here would duplicate the loop guard's scan of the same line.
        }

        CompleteContainer(mapId);

        return mapId;
    }

    private void ParseImplicitMapEntry(int depth, int baseIndent, HashSet<DElement>? keys)
    {
        RequireAllowedDepth(depth + 1);

        var keyId = ParseScalarNode(ScalarKeyContext.ImplicitMapKey);
        _lastParsedNodeId = keyId;

        ValidateMissingStringOrDuplicateMapKey(keys, keyId);
        SkipInlineSpaces();

        // Leave the cursor on an unexpected separator so the parse error points to it.
        if (_position >= Source.Length || Source[_position] != YamlSyntax.MappingValueIndicator)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }

        _position++;

        SkipInlineSpaces();

        if (
            _position >= Source.Length
            || YamlSyntax.IsLineEnd(Source[_position])
            || Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            ParseMapValueAfterEmptyIndicatorLine(depth, baseIndent, keyId);
        }
        else
        {
            var lineStart = FindCurrentLineStart(_position);

            ParseNode(depth + 1, parentIndent: baseIndent, mapValueOnKeyLine: true);

            if (FindCurrentLineStart(_position) == lineStart)
            {
                ExpectLineEnd();
            }
        }
    }

    // The value of an entry that has nothing after its ":" on the line: the following lines, or null.
    private void ParseMapValueAfterEmptyIndicatorLine(int depth, int baseIndent, uint keyId)
    {
        SkipRestOfLineAndLineBreak(commentOwnerId: keyId);

        var (Position, Indent) = PeekNextNonTriviaContentLine();

        // A block sequence used as a mapping value may start at the key's indentation.
        if (Indent > baseIndent || Indent == baseIndent && IsBlockSequenceIndicator(Position))
        {
            AdvanceToPeekedContent(Position);
            ParseNode(depth + 1, parentIndent: baseIndent);
        }
        else
        {
            CreateNullToken();
        }
    }

    // "? key" and the optional ": value" at the same indentation.
    // Only a scalar can be the key; a collection as a key is not supported.
    // The value may start on the line of the ":" even when it is a block collection ("? a" and ": - b"),
    // and an entry without ": value" has a null value.
    private void ParseExplicitMapEntry(int depth, int baseIndent, HashSet<DElement>? keys)
    {
        RequireAllowedDepth(depth + 1);

        _position++;

        var keyId = ParseExplicitKey(depth, baseIndent);
        _lastParsedNodeId = keyId;

        ValidateMissingStringOrDuplicateMapKey(keys, keyId);
        SkipTrivia(leaveSameLineCommentForNextNode: false);

        if (
            _position < Source.Length
            && Source[_position] == YamlSyntax.MappingValueIndicator
            && _position - FindCurrentLineStart(_position) == baseIndent
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
                || YamlSyntax.IsLineEnd(Source[_position])
                || Source[_position] == YamlSyntax.CommentIndicator
            )
            {
                ParseMapValueAfterEmptyIndicatorLine(depth, baseIndent, keyId);
            }
            else
            {
                var lineStart = FindCurrentLineStart(_position);

                ParseNode(depth + 1, parentIndent: baseIndent);

                if (FindCurrentLineStart(_position) == lineStart)
                {
                    ExpectLineEnd();
                }
            }
        }
        else
        {
            CreateNullToken();
        }
    }

    // The key after "?": a scalar on the same line, or on the following lines, or nothing (a null key).
    private uint ParseExplicitKey(int depth, int baseIndent)
    {
        SkipInlineSpaces();

        if (
            _position >= Source.Length
            || YamlSyntax.IsLineEnd(Source[_position])
            || Source[_position] == YamlSyntax.CommentIndicator
        )
        {
            SkipRestOfLineAndLineBreak(commentOwnerId: null);

            var (Position, Indent) = PeekNextNonTriviaContentLine();

            if (Indent > baseIndent)
            {
                AdvanceToPeekedContent(Position);

                return ParseExplicitKeyScalar(depth, baseIndent);
            }

            // A sequence at the indentation of the "?" is the key (a collection).
            if (Indent == baseIndent && IsBlockSequenceIndicator(Position))
            {
                ThrowParseError(ParseException.ErrorCode.UnsupportedFeature);
            }

            return CreateNullToken();
        }

        return ParseExplicitKeyScalar(depth, baseIndent);
    }

    private uint ParseExplicitKeyScalar(int depth, int baseIndent)
    {
        if (
            Source[_position] is YamlSyntax.FlowSequenceStart or YamlSyntax.FlowMappingStart
            || IsBlockSequenceIndicator(_position)
            || IsExplicitKeyStart(flow: false)
            || IsBlockMapStart()
        )
        {
            ThrowParseError(ParseException.ErrorCode.UnsupportedFeature);
        }

        return ParseNode(
            depth + 1,
            parentIndent: baseIndent,
            mapValueOnKeyLine: true,
            keyContext: ScalarKeyContext.ExplicitMapKey
        );
    }

    private void ValidateMissingStringOrDuplicateMapKey(HashSet<DElement>? keys, uint keyId)
    {
        // A key with no text is a null key; with ReadMapKeyAsString there is no string to read ("null" as a key is
        // text, and is read as the string "null").
        if (_parseOptions.ReadMapKeyAsString && GetToken(keyId).Kind == DTokenKind.Null)
        {
            ThrowParseError(ParseException.ErrorCode.MissingStringKey);
        }

        // Compare resolved values with their YAML types; integer 1 and string "1" remain distinct.
        if (keys != null && !keys.Add(new DElement(this, keyId)))
        {
            ThrowParseError(ParseException.ErrorCode.DuplicateKey);
        }
    }

    private static bool WriteMap(
        Utf8TextWriter writer,
        in DataReader reader,
        uint mapId,
        in YamlWriteOptions options,
        int depth,
        uint followingTokenId
    )
    {
        // The first MoveNext is needed for writing anyway, so use it to detect an empty map.
        var pairs = reader.EnumerateMap(mapId).GetEnumerator();
        var hasPair = pairs.MoveNext();
        if (!hasPair)
        {
            WriteEmptyCollection(writer, isMap: true, afterIndicator: false);

            return false;
        }

        var separatorWritten = false;
        var endsWithBlockScalar = false;
        var preserveTrivia = options.PreserveTrivia;
        while (hasPair)
        {
            var pair = pairs.Current;
            hasPair = pairs.MoveNext();

            var nextTokenId = hasPair ? pairs.Current.Key : followingTokenId;
            if (separatorWritten)
            {
                writer.WriteIndentNewLine();
            }

            // Most writes omit trivia; avoid entering its enumerator for every map entry.
            if (preserveTrivia)
            {
                WriteLeadingTriviaForToken(writer, reader, pair.Key, options, separatorWritten);
            }

            WriteNode(
                writer,
                reader,
                pair.Key,
                reader.GetToken(pair.Key),
                options,
                depth,
                isKey: true
            );

            WriteCollectionIndicator(writer, mapValue: true);

            var keyTrailingComment = preserveTrivia
                ? GetTrailingComment(reader, pair.Key, options)
                : default;

            writer.WriteString(keyTrailingComment);

            var value = reader.GetToken(pair.Value);

            // Parsed containers carry their count in this token; only edited ones need another lookup.
            var block =
                !keyTrailingComment.IsEmpty
                || value.IsContainer
                && (value.IsExtended ? reader.GetValueCount(pair.Value) : value.Count) > 0;

            if (block)
            {
                WriteValueSeparator(writer, blockValue: true);

                // Comments between "key:" and a nested block are leading trivia of that block.
                if (preserveTrivia)
                {
                    WriteLeadingTriviaForToken(
                        writer,
                        reader,
                        pair.Value,
                        options,
                        separatorWritten: true
                    );
                }
            }
            else
            {
                WriteValueSeparator(writer, blockValue: false);

                // A block header comment is leading trivia of its scalar after parsing. Emit it before
                // the value so subsequent writes preserve it without turning it into scalar content.
                if (
                    preserveTrivia
                    && value.Kind
                        is (DTokenKind.String or DTokenKind.Symbol or DTokenKind.ByteString)
                )
                {
                    WriteLeadingTriviaForToken(
                        writer,
                        reader,
                        pair.Value,
                        options,
                        separatorWritten: false
                    );
                }
            }

            endsWithBlockScalar = WriteNode(
                writer,
                reader,
                pair.Value,
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