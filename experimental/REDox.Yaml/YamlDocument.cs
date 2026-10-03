// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Yaml;

/// <summary>
/// A YAML document read into the REDox document model. Block and flow collections, plain, quoted and block scalars,
/// comments, and map keys that are scalars (null, boolean, number, timestamp or string, plain or explicit with "?")
/// are supported. The keys of a map are read as values, so a key that is not a string is reached through
/// <see cref="DElement.AsMap" />. Anchors and aliases, tags, directives, several documents in one stream, and a
/// collection as a key are not: reading them fails with a <see cref="DocumentParseException" />. A stream with no
/// content reads as a single null value. Text in UTF-8 is read where it is, without a copy.
///
/// </summary>
public sealed partial class YamlDocument : Document
{
    // Decoded scalar bytes are packed into one buffer; tokens keep (length, offset) into it.
    private YamlScalarBuffer _scalars = new();
    private List<uint> _trailingTrivia = new();

    // The UTF-8 text that is being parsed, and the position in it. The source is only set while Read runs: the scalars
    // are copied to the scalar buffer, so nothing keeps the source afterwards.
    private ReadOnlyMemory<byte> _source;
    private int _position;
    private YamlDocumentOptions _parseOptions;

    private ReadOnlySpan<byte> Source => _source.Span;

    private YamlDocument(SerializerSettings settings)
        : base(settings) { }

    /// <summary>
    /// Copies the document. The copy has its own data, so editing either one never changes the other.
    /// </summary>
    /// <returns>The copy, which the caller disposes.</returns>
    public YamlDocument Duplicate()
    {
        var document = CreateSnapshot<YamlDocument>();

        // CreateSnapshot is a shallow clone; the copy needs its own buffers so later edits stay independent.
        document._scalars = _scalars.Duplicate();
        document._trailingTrivia = new List<uint>(_trailingTrivia);
        return document;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scalars.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Reads a YAML document from a stream. The stream is read to its end and is not closed.
    /// </summary>
    /// <param name="stream">The stream that holds the text: UTF-8 or, with <see cref="YamlDocumentOptions.AutoDetectEncoding" />, another encoding that starts with a byte order mark.</param>
    /// <param name="settings">The serializer settings of the document. The default settings when null.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> is null.</exception>
    /// <exception cref="DocumentParseException">The text is not valid YAML, or uses a feature that is not supported.</exception>
    public static YamlDocument Parse(
        Stream stream,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray(), settings, options);
    }

    /// <summary>
    /// Reads a YAML document from a string.
    /// </summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="settings">The serializer settings of the document. The default settings when null.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml" /> is null.</exception>
    /// <exception cref="DocumentParseException">The text is not valid YAML, or uses a feature that is not supported.</exception>
    public static YamlDocument Parse(
        string yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Parse(Encoding.UTF8.GetBytes(yaml), settings, options);
    }

    /// <summary>
    /// Reads a YAML document from UTF-8 bytes. A UTF-8 byte order mark is skipped. Text in another encoding is
    /// converted only when <see cref="YamlDocumentOptions.AutoDetectEncoding" /> is set.
    /// </summary>
    /// <param name="utf8Yaml">The UTF-8 text.</param>
    /// <param name="settings">The serializer settings of the document. The default settings when null.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Yaml" /> is null.</exception>
    /// <exception cref="DocumentParseException">The text is not valid UTF-8 or YAML, or uses a feature that is not supported.</exception>
    public static YamlDocument Parse(
        byte[] utf8Yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(utf8Yaml);
        return Parse(utf8Yaml.AsMemory(), settings, options);
    }

    /// <summary>
    /// Reads a YAML document from UTF-8 bytes. A UTF-8 byte order mark is skipped. Text in another encoding is
    /// converted only when <see cref="YamlDocumentOptions.AutoDetectEncoding" /> is set.
    /// </summary>
    /// <param name="utf8Yaml">The UTF-8 text.</param>
    /// <param name="settings">The serializer settings of the document. The default settings when null.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document, which the caller disposes.</returns>
    /// <exception cref="DocumentParseException">The text is not valid UTF-8 or YAML, or uses a feature that is not supported.</exception>
    public static YamlDocument Parse(
        ReadOnlyMemory<byte> utf8Yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        var document = new YamlDocument(settings ?? SerializerSettings.Default);
        try
        {
            document.Read(utf8Yaml, options);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a YAML document from UTF-8 bytes without throwing when the text cannot be read.
    /// </summary>
    /// <param name="utf8Yaml">The UTF-8 text.</param>
    /// <param name="document">The document, which the caller disposes, or null when the text could not be read.</param>
    /// <param name="settings">The serializer settings of the document. The default settings when null.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>True when the text was read; false when it is not valid UTF-8 or YAML, or uses a feature that is not supported.</returns>
    public static bool TryParse(
        ReadOnlySpan<byte> utf8Yaml,
        out YamlDocument? document,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        try
        {
            document = Parse(utf8Yaml.ToArray(), settings, options);
            return true;
        }
        catch (DocumentParseException)
        {
            document = null;
            return false;
        }
    }

    /// <summary>
    /// Writes an element as YAML, in block style, to a new array of UTF-8 bytes.
    /// </summary>
    /// <param name="element">The element to write. It can come from any document, not only a YAML document.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <returns>The UTF-8 bytes.</returns>
    /// <exception cref="NotSupportedException">A string cannot be written in the chosen <see cref="YamlStringMode" />.</exception>
    public static byte[] Encode(DElement element, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true,
            }
        );
        Write(writer, new DataReader(element), options);
        return writer.Encode();
    }

    /// <summary>
    /// Writes an element as YAML, in block style, to a string.
    /// </summary>
    /// <param name="element">The element to write. It can come from any document, not only a YAML document.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <returns>The YAML text.</returns>
    /// <exception cref="NotSupportedException">A string cannot be written in the chosen <see cref="YamlStringMode" />.</exception>
    public static string EncodeToString(DElement element, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true,
            }
        );
        Write(writer, new DataReader(element), options);
        return writer.EncodeToString();
    }

    /// <summary>
    /// Writes an element as YAML, in block style, as UTF-8 to a stream. The stream is not closed.
    /// </summary>
    /// <param name="element">The element to write. It can come from any document, not only a YAML document.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <exception cref="NotSupportedException">A string cannot be written in the chosen <see cref="YamlStringMode" />.</exception>
    public static void EncodeTo(DElement element, Stream stream, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(
            stream,
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true,
            }
        );
        Write(writer, new DataReader(element), options);
    }

    /// <summary>
    /// Writes an element as YAML, in block style, as UTF-8 to a buffer writer.
    /// </summary>
    /// <param name="element">The element to write. It can come from any document, not only a YAML document.</param>
    /// <param name="bufferWriter">The buffer writer to write to.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <exception cref="NotSupportedException">A string cannot be written in the chosen <see cref="YamlStringMode" />.</exception>
    public static void EncodeTo(
        DElement element,
        IBufferWriter<byte> bufferWriter,
        YamlWriteOptions options = default
    )
    {
        using var writer = new Utf8TextWriter(
            bufferWriter,
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true,
            }
        );
        Write(writer, new DataReader(element), options);
    }

    private void Read(ReadOnlyMemory<byte> source, YamlDocumentOptions options)
    {
        _parseOptions = options;

        // Text that is UTF-8 already is read where it is. Only text in another encoding is converted, into a buffer
        // that is rented for the time of the parsing.
        byte[]? rentedBuffer = null;
        try
        {
            try
            {
                _source = DecodeSource(source, options.AutoDetectEncoding, out rentedBuffer);
            }
            catch (DecoderFallbackException)
            {
                // Report invalid input as a parse error so that TryParse returns false.
                throw Error(ParseException.ErrorCode.InvalidFormat);
            }

            ReadDocument();
        }
        finally
        {
            _source = default;
            if (rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private void ReadDocument()
    {
        var options = _parseOptions;
        EnsureCapacity(_source.Length / 8);
        _position = 0;
        SkipTrivia();
        var contentOnMarkerLine = false;
        var markerLineStart = 0;
        if (AtDocumentMarker(YamlSyntax.DocumentStartUtf8))
        {
            markerLineStart = LastLineStart(_position);
            _position += 3;
            SkipInlineSpaces();

            // Content may follow the marker on its line ("--- text"), but then it cannot be a block collection.
            if (
                _position < Source.Length
                && !IsLineEnd(Source[_position])
                && Source[_position] != YamlSyntax.CommentIndicator
            )
            {
                contentOnMarkerLine = true;
            }
            else
            {
                SkipTrivia();
            }
        }

        if (_position >= Source.Length)
        {
            // An input with no content (empty, or only comments and blank lines) still gets a null root. Core has
            // no "empty document": a document is valid only while RootId > 0, and trivia is only ever attached to
            // the token that follows it. The null token keeps RootId valid and gives the comments a token to
            // attach to, so they are preserved. The cost is that an empty file and a file holding `null` look alike.
            RootId = ParseNull();
            return;
        }

        // The root node's parent indentation is -1, so block scalars may start at column 0.
        RootId = ParseNode(0, parentIndent: -1, mapValueOnKeyLine: contentOnMarkerLine);
        if (contentOnMarkerLine && LastLineStart(_position) == markerLineStart)
        {
            ExpectLineEnd();
        }

        SkipTrivia();
        if (options.PreserveTrivia)
        {
            var trailingStart = (uint)GetTokens().Length;
            while (trailingStart > RootId && GetToken(trailingStart - 1).Kind == DTokenKind.Trivia)
                trailingStart--;
            for (var triviaId = trailingStart; triviaId < GetTokens().Length; triviaId++)
            {
                if (GetToken(triviaId).Kind == DTokenKind.Trivia)
                    _trailingTrivia.Add(triviaId);
            }
        }
        if (_position < Source.Length)
        {
            throw Error(ParseException.ErrorCode.InvalidFormat);
        }
    }

    // Returns the text as UTF-8. UTF-8 is returned as it is (a byte order mark skipped), after it has been checked to be
    // valid; other encodings are converted into a rented buffer, which the caller returns.
    private static ReadOnlyMemory<byte> DecodeSource(
        ReadOnlyMemory<byte> source,
        bool detectEncoding,
        out byte[]? rentedBuffer
    )
    {
        rentedBuffer = null;
        if (!TextEncodingDetector.TryDetect(source.Span, out var kind, out var skipBytes))
        {
            return RequireValidUtf8(source);
        }

        // A UTF-8 byte order mark is always skipped; other encodings are converted only when requested.
        if (kind == TextEncodingKind.Utf8)
        {
            return RequireValidUtf8(source.Slice(skipBytes));
        }

        if (!detectEncoding)
        {
            return RequireValidUtf8(source);
        }

        Encoding encoding = kind switch
        {
            TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
            TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(true, false, true),
            TextEncodingKind.Utf32LittleEndian => new UTF32Encoding(false, false, true),
            _ => new UTF32Encoding(true, false, true),
        };

        // The encodings throw on invalid text, so the chars are valid UTF-16 and convert to UTF-8 without loss.
        var encoded = source.Span[skipBytes..];
        var chars = ArrayPool<char>.Shared.Rent(encoding.GetCharCount(encoded));
        try
        {
            var charCount = encoding.GetChars(encoded, chars);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(charCount));
            var length = Encoding.UTF8.GetBytes(chars.AsSpan(0, charCount), rentedBuffer);
            return rentedBuffer.AsMemory(0, length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private static ReadOnlyMemory<byte> RequireValidUtf8(ReadOnlyMemory<byte> text)
    {
        if (!System.Text.Unicode.Utf8.IsValid(text.Span))
        {
            throw new DecoderFallbackException("The text is not valid UTF-8.");
        }

        return text;
    }

    private uint ParseNode(
        int depth,
        bool flow = false,
        int parentIndent = 0,
        bool mapValueOnKeyLine = false,
        bool isKey = false
    )
    {
        if (_parseOptions.MaxDepth > 0 && depth > _parseOptions.MaxDepth)
        {
            throw Error(ParseException.ErrorCode.MaxDepthExceeded);
        }

        if (_position >= Source.Length)
        {
            return ParseNull();
        }

        // A block collection cannot start on the same line as the key it belongs to ("a: b: c", "a: - b").
        var blockAllowed = !flow && !mapValueOnKeyLine;

        if (
            Source[_position]
                is YamlSyntax.AnchorIndicator
                    or YamlSyntax.AliasIndicator
                    or YamlSyntax.TagIndicator
                    or YamlSyntax.DirectiveIndicator
            // An explicit key is read by the block map that holds it (see ParseExplicitMapEntry); anywhere else the
            // "?" that stands alone is not supported. "?x" is a plain scalar.
            || IsExplicitKeyStart(flow) && !(blockAllowed && IsExplicitKeyStart(false))
            || AtDocumentMarker(YamlSyntax.DocumentStartUtf8)
            || AtDocumentMarker(YamlSyntax.DocumentEndUtf8)
        )
        {
            throw Error(ParseException.ErrorCode.UnsupportedFeature);
        }

        return Source[_position] switch
        {
            YamlSyntax.FlowSequenceStart => ParseFlowArray(depth, parentIndent),
            YamlSyntax.FlowMappingStart => ParseFlowMap(depth, parentIndent),
            YamlSyntax.Dash when blockAllowed && IsBlockSequenceIndicator() => ParseBlockArray(
                depth
            ),
            _ when blockAllowed && (IsExplicitKeyStart(false) || IsBlockMapStart()) =>
                ParseBlockMap(depth),
            _ => ParseScalarNode(parentIndent: parentIndent, flow: flow, isKey: isKey),
        };
    }

    // "key" is an implicit key, which cannot span lines; "isKey" is any map key, explicit ones included (they can).
    private uint ParseScalarNode(
        bool key = false,
        int parentIndent = 0,
        bool flow = false,
        bool isKey = false
    )
    {
        if (_position >= Source.Length || IsLineEnd(Source[_position]))
        {
            return ParseNull();
        }

        var value = Source[_position] switch
        {
            YamlSyntax.DoubleQuote => ParseDoubleQuotedString(parentIndent),
            YamlSyntax.SingleQuote => ParseSingleQuotedString(parentIndent),
            YamlSyntax.LiteralBlockIndicator or YamlSyntax.FoldedBlockIndicator when !key =>
                ParseBlockString(parentIndent),
            _ => ParsePlainString(key, flow, parentIndent),
        };

        // A quoted scalar is always a string. A plain key is resolved like a plain value (null, boolean, number,
        // timestamp or string), so "1: a" is a map entry whose key is the integer 1, unless ReadKeysAsStrings is set.
        if (value.Quoted || (key || isKey) && _parseOptions.ReadKeysAsStrings)
        {
            return CommitScalar(DTokenVariant.String, value);
        }

        if (TryParseNull(value, out var nullId))
            return nullId;
        if (TryParseBoolean(value, out var booleanId))
            return booleanId;
        if (TryParseNumber(value, out var numberId))
            return numberId;
        if (TryParseTimestamp(value, out var timestampId))
            return timestampId;
        return CommitScalar(DTokenVariant.String, value);
    }

    // The bytes of a scalar that is being read: the scalar buffer, from Start to the end of what is stored, where the
    // reading of the scalar builds them. Nothing else is stored while a scalar is read. They become a token with
    // CommitScalar (a number keeps its text, as it was written), or are dropped with DiscardScalar (a null and a boolean
    // need no text of their own).
    private readonly record struct ParsedScalar(int Start, bool Quoted);

    private ParsedScalar BeginScalar(bool quoted) => new(_scalars.Length, quoted);

    private ReadOnlySpan<byte> ScalarText(ParsedScalar scalar) => _scalars.From(scalar.Start);

    private void DiscardScalar(ParsedScalar scalar) => _scalars.Truncate(scalar.Start);

    private uint CommitScalar(DTokenVariant variant, ParsedScalar scalar)
    {
        var length = _scalars.Length - scalar.Start;
        if (length > 0x1ffffff)
            throw Error(ParseException.ErrorCode.InvalidString);

        return AllocToken(
            DToken.Make(variant, DToken.EncodeLengthOffsetPayload(length, scalar.Start))
        );
    }

    // Stores bytes that are in the source as they are (a comment, white space).
    private uint StoreScalar(DTokenVariant variant, ReadOnlySpan<byte> text)
    {
        var scalar = BeginScalar(false);
        _scalars.Append(text);
        return CommitScalar(variant, scalar);
    }

    private ReadOnlySpan<byte> ScalarBytes(DToken token)
    {
        var (length, offset) = DToken.DecodeLengthOffsetPayload(token);
        return _scalars.Slice(offset, length);
    }

    private void CompleteContainer(uint containerId)
    {
        // Future edits rely on LinkId pointing after the last descendant, including an empty container.
        var token = GetToken(containerId);
        token.LinkId = (uint)GetTokens().Length;
        SetToken(containerId, token);
    }

    private static bool IsLineEnd(byte value) =>
        value is YamlSyntax.CarriageReturn or YamlSyntax.LineFeed;

    private bool AtDocumentMarker(ReadOnlySpan<byte> marker) => AtDocumentMarker(_position, marker);

    private bool IsDocumentMarkerAt(int position) =>
        AtDocumentMarker(position, YamlSyntax.DocumentStartUtf8)
        || AtDocumentMarker(position, YamlSyntax.DocumentEndUtf8);

    // A marker ends the line or is followed by white space ("--- text").
    private bool AtDocumentMarker(int position, ReadOnlySpan<byte> marker)
    {
        var source = Source;
        return source[position..].StartsWith(marker)
            && (
                position + marker.Length == source.Length
                || IsLineEnd(source[position + marker.Length])
                || source[position + marker.Length] is YamlSyntax.Space or YamlSyntax.Tab
            );
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, YamlWriteOptions options)
    {
        WriteTrivia(writer, reader, reader.RootId, options, false);
        WriteNode(writer, reader, reader.RootId, options, 0, false);

        // The comment ending the last line has no following token inside the value, so it was kept in the
        // document's trailing trivia.
        var document = options.PreserveTrivia ? reader.Document as YamlDocument : null;
        var whitespaceId = 0U;
        var commentId = 0U;
        var inlineComment =
            document != null
            && TryGetInlineComment(
                reader,
                document._trailingTrivia,
                out whitespaceId,
                out commentId
            );
        if (inlineComment)
        {
            WriteInlineCommentText(writer, reader, whitespaceId, commentId);
        }

        writer.WriteNewLine();
        if (document != null)
        {
            WriteTrailingTrivia(writer, reader, document._trailingTrivia, inlineComment);
        }
    }

    private static void WriteNode(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        YamlWriteOptions options,
        int depth,
        bool key
    )
    {
        var token = reader.GetToken(tokenId);
        if (token.Type == DTokenType.Array)
        {
            WriteArray(writer, reader, tokenId, options, depth);
            return;
        }

        if (token.Type == DTokenType.Map)
        {
            WriteMap(writer, reader, tokenId, options, depth);
            return;
        }

        switch (token.Kind)
        {
            case DTokenKind.Null:
                WriteNull(writer);
                break;
            case DTokenKind.Boolean:
                WriteBoolean(writer, reader, tokenId);
                break;
            case DTokenKind.Integer:
            case DTokenKind.Float:
            case DTokenKind.InlineFloat:
            case DTokenKind.BigNumber:
                WriteNumber(writer, reader, tokenId);
                break;
            case DTokenKind.Timestamp:
                WriteTimestamp(writer, reader, tokenId);
                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                WriteString(writer, reader, tokenId, options, depth, key);
                break;
            default:
                throw new NotSupportedException($"YAML does not support {token.Kind} values.");
        }
    }
}
