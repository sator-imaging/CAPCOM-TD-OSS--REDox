// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Unicode;
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
    private YamlPooledBuffer _scalars = new();
    private List<uint> _trailingTrivia = [];

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
        // CreateSnapshot is a shallow clone.
        // The copy needs its own buffers so later edits stay independent.
        var document = CreateSnapshot<YamlDocument>();

        document._scalars = _scalars.Duplicate();
        document._trailingTrivia = [.. _trailingTrivia];

        return document;
    }

    /// <summary>
    /// Verifies that this document retains its value when written as JSON, read back, then written and read as YAML.
    /// </summary>
    /// <param name="options">How the YAML is written.</param>
    /// <param name="expectedOriginalElement">An optional source element to compare with the JSON read back.</param>
    /// <exception cref="DocumentParseException">A conversion changes the parsed value.</exception>
    public void ValidateJsonYamlRoundTripPreservesValues(
        YamlWriteOptions options = default,
        DElement? expectedOriginalElement = null
    )
    {
        var yamlToJson = JsonDocument.EncodeToString(RootElement);

        using var readbackJsonObj = JsonDocument.Parse(yamlToJson, Settings);

        // A loss in JSON must be detected before writing YAML can change the value again.
        if (!DValue.DeepEquals(RootElement.AsValue(), readbackJsonObj.RootElement.AsValue()))
        {
            ThrowParseError(
                ParseException.ErrorCode.InvalidValue,
                "Writing and reading JSON changed the YAML value."
            );
        }

        if (
            expectedOriginalElement is { } expected
            && !DValue.DeepEquals(expected.AsValue(), readbackJsonObj.RootElement.AsValue())
        )
        {
            ThrowParseError(
                ParseException.ErrorCode.InvalidValue,
                "The JSON value produced from YAML differs from the expected original value."
            );
        }

        var yamlViaReadbackJsonObj = EncodeToString(readbackJsonObj.RootElement, options);

        using var readbackYamlObj = Parse(yamlViaReadbackJsonObj, Settings);

        if (!DValue.DeepEquals(RootElement.AsValue(), readbackYamlObj.RootElement.AsValue()))
        {
            ThrowParseError(
                ParseException.ErrorCode.InvalidValue,
                "Writing and reading YAML from the parsed JSON changed the original YAML value."
            );
        }
    }

    /// <summary>
    /// Returns the buffer that holds the scalars of the document to the array pool, and then disposes the document as
    /// the base class does. The values of the document cannot be read afterwards.
    /// </summary>
    /// <param name="disposing">True when the document is disposed, false when it is finalized.</param>
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

        var bufferSize = (settings ?? SerializerSettings.Default).DefaultBufferSize;

        using var buffer = new YamlPooledBuffer(bufferSize);

        int bytesRead;
        while ((bytesRead = stream.Read(buffer.GetSpan(bufferSize))) > 0)
        {
            buffer.Advance(bytesRead);
        }

        // Parse releases the source before returning, so the using scope can dispose the input buffer.
        return Parse(buffer.WrittenMemory, settings, options);
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
    /// <exception cref="DocumentParseException">A string cannot be written in the chosen <see cref="YamlStringStyle" />.</exception>
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
    /// <exception cref="DocumentParseException">A string cannot be written in the chosen <see cref="YamlStringStyle" />.</exception>
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
    /// <exception cref="DocumentParseException">A string cannot be written in the chosen <see cref="YamlStringStyle" />.</exception>
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
    /// <exception cref="DocumentParseException">A string cannot be written in the chosen <see cref="YamlStringStyle" />.</exception>
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

        // Text that is UTF-8 already is read where it is. Only text in another encoding rents buffer storage.
        using var decodedBuffer = new YamlPooledBuffer(0);

        try
        {
            try
            {
                var detected = TextEncodingDetector.TryDetect(
                    source.Span,
                    out var kind,
                    out var skipBytes
                );

                if (!detected || kind == TextEncodingKind.Utf8 || !options.AutoDetectEncoding)
                {
                    _source = RequireValidUtf8(
                        detected && kind == TextEncodingKind.Utf8 ? source.Slice(skipBytes) : source
                    );
                }
                else
                {
                    Encoding encoding = kind switch
                    {
                        TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(
                            false,
                            false,
                            true
                        ),
                        TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(true, false, true),
                        TextEncodingKind.Utf32LittleEndian => new UTF32Encoding(false, false, true),
                        _ => new UTF32Encoding(true, false, true),
                    };

                    var text = encoding.GetString(source.Span[skipBytes..]);
                    var length = Encoding.UTF8.GetBytes(
                        text.AsSpan(),
                        decodedBuffer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length))
                    );

                    decodedBuffer.Advance(length);

                    _source = decodedBuffer.WrittenMemory;
                }
            }
            catch (DecoderFallbackException)
            {
                // Report invalid input as a parse error so that TryParse returns false.
                ThrowParseError(ParseException.ErrorCode.InvalidEncoding);
            }

            ReadDocument();
        }
        finally
        {
            _source = default;
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
        if (IsMarkerAtPosition(_position, YamlSyntax.DocumentStartUtf8))
        {
            markerLineStart = FindCurrentLineStart(_position);
            _position += 3;

            SkipInlineSpaces();

            // Content may follow the marker on its line ("--- text") but then it cannot be a block collection.
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
            // NOTE / TODO:
            //   An input with no content (empty, or only comments and blank lines) still gets a null root.
            //   Core has no "empty document" classification. A document is valid only while RootId > 0,
            //   and trivia is only ever attached to the token that follows it.
            //   The null token keeps RootId valid and gives the comments a token to attach to, so they are preserved.
            RootId = ParseNull();

            return;
        }

        // The root node's parent indentation is -1, so block scalars may start at column 0.
        RootId = ParseNode(0, parentIndent: -1, mapValueOnKeyLine: contentOnMarkerLine);

        if (contentOnMarkerLine && FindCurrentLineStart(_position) == markerLineStart)
        {
            ExpectLineEnd();
        }

        SkipTrivia();

        if (options.PreserveTrivia)
        {
            var trailingStart = (uint)GetTokens().Length;
            while (trailingStart > RootId && GetToken(trailingStart - 1).Kind == DTokenKind.Trivia)
            {
                trailingStart--;
            }

            for (var triviaId = trailingStart; triviaId < GetTokens().Length; triviaId++)
            {
                if (GetToken(triviaId).Kind == DTokenKind.Trivia)
                {
                    _trailingTrivia.Add(triviaId);
                }
            }
        }

        if (_position < Source.Length)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }
    }

    private ReadOnlyMemory<byte> RequireValidUtf8(ReadOnlyMemory<byte> text)
    {
        if (!Utf8.IsValid(text.Span))
        {
            ThrowParseError(
                ParseException.ErrorCode.InvalidEncoding,
                "The text is not valid UTF-8."
            );
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
        RequireAllowedDepth(depth);

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
            || IsMarkerAtPosition(_position, YamlSyntax.DocumentStartUtf8)
            || IsMarkerAtPosition(_position, YamlSyntax.DocumentEndUtf8)
        )
        {
            ThrowParseError(ParseException.ErrorCode.UnsupportedFeature);
        }

        return Source[_position] switch
        {
            YamlSyntax.FlowSequenceStart => ParseFlowArray(depth, parentIndent),
            YamlSyntax.FlowMappingStart => ParseFlowMap(depth, parentIndent),
            YamlSyntax.Dash when blockAllowed && IsBlockSequenceIndicator(_position) =>
                ParseBlockArray(depth),
            _ when blockAllowed && (IsExplicitKeyStart(false) || IsBlockMapStart()) =>
                ParseBlockMap(depth),
            _ => ParseScalarNode(parentIndent: parentIndent, flow: flow, isKey: isKey),
        };
    }

    // Keys, omitted values and implicit flow maps also create nodes without passing through ParseNode.
    // Keep their depth checks identical to those for ordinary values.
    private void RequireAllowedDepth(int depth)
    {
        if (_parseOptions.MaxDepth > 0 && depth > _parseOptions.MaxDepth)
        {
            ThrowParseError(ParseException.ErrorCode.MaxDepthExceeded);
        }
    }

    // "key" is an implicit key, which cannot span lines.
    // "isKey" is any map key, explicit ones included (they can).
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

        var scalarSourceStart = _position;
        var value = Source[_position] switch
        {
            YamlSyntax.DoubleQuote => ParseDoubleQuotedString(parentIndent),
            YamlSyntax.SingleQuote => ParseSingleQuotedString(parentIndent),
            YamlSyntax.LiteralBlockIndicator or YamlSyntax.FoldedBlockIndicator when !key =>
                ParseBlockString(parentIndent),
            _ => ParsePlainString(key, flow, parentIndent),
        };

        if (_parseOptions.EnableValueValidation)
        {
            ValidateScalarSourceCharacters(scalarSourceStart);
        }

        // A quoted scalar is always a string.
        // A plain key is resolved like a plain value (null, boolean, number, timestamp or string),
        // so "1: a" is a map entry whose key is the integer 1, unless ReadMapKeyAsString is set.
        return value.Quoted || (key || isKey) && _parseOptions.ReadMapKeyAsString
                ? CommitScalar(DTokenVariant.String, value)
            : TryParseNull(value, out var nullId) ? nullId
            : TryParseBoolean(value, out var booleanId) ? booleanId
            : TryParseNumber(value, out var numberId) ? numberId
            : TryParseTimestamp(value, out var timestampId) ? timestampId
            : CommitScalar(DTokenVariant.String, value);
    }

    private void ValidateScalarSourceCharacters(int scalarSourceStart)
    {
        // YAML 1.2.2 section 5.1, c-printable (production [1])
        // Raw source permits TAB, LF, CR, U+0020..U+007E, U+0085, U+00A0..U+D7FF, U+E000..U+FFFD and U+10000..U+10FFFF.
        //   https://yaml.org/spec/1.2.2/#51-character-set
        // Its JSON compatibility rule also permits non-C0 characters inside quoted scalars.
        // A block scalar is not quoted even though ParsedScalar.Quoted prevents its implicit type resolution.
        // Validate the source, never the decoded scalar buffer: double-quoted escapes may legitimately produce NUL
        // or other non-printable values. UTF-8 has already been checked, so each decoded Rune is valid.
        var isQuotedScalar =
            Source[scalarSourceStart] is YamlSyntax.SingleQuote or YamlSyntax.DoubleQuote;

        var scalarSourceEnd = _position;
        for (var position = scalarSourceStart; position < scalarSourceEnd; )
        {
            Rune.DecodeFromUtf8(
                Source[position..scalarSourceEnd],
                out var character,
                out var consumed
            );

            var codePoint = character.Value;
            var allowedCharacter =
                codePoint is YamlSyntax.Tab or YamlSyntax.LineFeed or YamlSyntax.CarriageReturn
                || (
                    isQuotedScalar
                        ? codePoint >= 0x20
                        : codePoint
                            is >= 0x20
                                and <= 0x7e
                                or 0x85
                                or >= 0xa0
                                and <= 0xd7ff
                                or >= 0xe000
                                and <= 0xfffd
                                or >= 0x10000
                                and <= 0x10ffff
                );

            if (!allowedCharacter)
            {
                // Report the offending source byte, including when earlier characters use multiple UTF-8 bytes.
                _position = position;

                ThrowParseError(ParseException.ErrorCode.InvalidString);
            }

            position += consumed;
        }
    }

    /// <summary>
    /// The bytes of a scalar that is being read.
    /// The scalar buffer, from Start to the end of what is stored, where the reading of the scalar builds them.
    /// Nothing else is stored while a scalar is read.
    /// They become a token with CommitScalar (a number keeps its text, as it was written),
    /// or are dropped by truncating the scalar buffer (a null and a boolean need no text of their own).
    /// </summary>
    private readonly record struct ParsedScalar(int Start, bool Quoted);

    private ParsedScalar BeginScalar(bool quoted) => new(_scalars.Length, quoted);

    private uint CommitScalar(DTokenVariant variant, ParsedScalar scalar)
    {
        var length = _scalars.Length - scalar.Start;
        if (length > 0x1ffffff)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        return AllocToken(
            DToken.Make(variant, DToken.EncodeLengthOffsetPayload(length, scalar.Start))
        );
    }

    private uint StoreScalar(DTokenVariant variant, ReadOnlySpan<byte> text)
    {
        var scalar = BeginScalar(false);

        _scalars.Append(text);

        return CommitScalar(variant, scalar);
    }

    /// <summary>
    /// Scalar readers use this span immediately; they must not retain it across scalar buffer growth.
    /// </summary>
    private ReadOnlySpan<byte> GetStoredScalarBytes(DToken token)
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

    private bool IsDocumentMarkerAt(int position) =>
        IsMarkerAtPosition(position, YamlSyntax.DocumentStartUtf8)
        || IsMarkerAtPosition(position, YamlSyntax.DocumentEndUtf8);

    // A marker ends the line or is followed by white space ("--- text").
    private bool IsMarkerAtPosition(int position, ReadOnlySpan<byte> marker)
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

        var endsWithBlockScalar = WriteNode(writer, reader, reader.RootId, options, 0, false);

        // The comment ending the last line has no following token inside the value,
        // so it was kept in the document's trailing trivia.
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

        if (inlineComment && !endsWithBlockScalar)
        {
            WriteInlineCommentText(writer, reader, whitespaceId, commentId);
        }

        writer.WriteNewLine();

        if (document != null)
        {
            WriteTrailingTrivia(writer, reader, document._trailingTrivia, inlineComment);
        }
    }

    internal static void WriteCollectionIndicator(Utf8TextWriter writer, bool mapValue)
    {
        writer.WriteUtf8Byte(mapValue ? YamlSyntax.MappingValueIndicator : YamlSyntax.Dash);
        writer.PushIndent();
    }

    internal static void WriteValueSeparator(Utf8TextWriter writer, bool blockValue)
    {
        if (blockValue)
        {
            writer.WriteIndentNewLine();
        }
        else
        {
            writer.WriteUtf8Byte(YamlSyntax.Space);
        }
    }

    internal static void WriteEmptyCollection(
        Utf8TextWriter writer,
        bool isMap,
        bool afterIndicator
    )
    {
        if (afterIndicator)
        {
            WriteValueSeparator(writer, false);
        }

        writer.WriteString(isMap ? YamlSyntax.EmptyMappingUtf8 : YamlSyntax.EmptySequenceUtf8);
    }

    private static bool WriteNode(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        YamlWriteOptions options,
        int depth,
        bool key,
        uint followingTokenId = 0
    )
    {
        var token = reader.GetToken(tokenId);
        if (token.Type == DTokenType.Array)
        {
            return WriteArray(writer, reader, tokenId, options, depth, followingTokenId);
        }

        if (token.Type == DTokenType.Map)
        {
            return WriteMap(writer, reader, tokenId, options, depth, followingTokenId);
        }

        var blockScalar =
            !key
            && options.StringStyle == YamlStringStyle.Multiline
            && token.Kind is DTokenKind.String or DTokenKind.Symbol or DTokenKind.ByteString;

        var whitespaceId = 0U;
        var commentId = 0U;
        if (blockScalar && options.PreserveTrivia)
        {
            // A trailing comment belongs on the block header, never on a content line. Pass the next
            // token through containers so their last descendant can consume its comment exactly once.
            if (followingTokenId != 0)
            {
                TryGetInlineComment(reader, followingTokenId, out whitespaceId, out commentId);
            }
            else if (reader.Document is YamlDocument document)
            {
                TryGetInlineComment(
                    reader,
                    document._trailingTrivia,
                    out whitespaceId,
                    out commentId
                );
            }
        }

        var commentWhitespace = whitespaceId == 0 ? default : reader.ReadTrivia(whitespaceId);
        var commentText = commentId == 0 ? default : reader.ReadTrivia(commentId);

        switch (token.Kind)
        {
            case DTokenKind.Null:
                writer.WriteNull();

                break;

            case DTokenKind.Boolean:
                writer.WriteBoolean(reader.ReadBoolean(tokenId));

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
                WriteStringValue(
                    writer,
                    reader.ReadUtf8String(tokenId),
                    options,
                    depth,
                    key,
                    commentWhitespace,
                    commentText
                );

                break;

            case DTokenKind.ByteString:
                WriteByteString(
                    writer,
                    reader,
                    tokenId,
                    options,
                    depth,
                    key,
                    commentWhitespace,
                    commentText
                );

                break;

            default:
                ThrowGenericError(
                    ParseException.ErrorCode.UnsupportedFeature,
                    $"YAML does not support {token.Kind} values."
                );

                break;
        }

        return blockScalar;
    }
}
