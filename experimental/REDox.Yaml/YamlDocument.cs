using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Unicode;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Yaml;

/// <summary>
/// A YAML document read into the REDox document model.
/// </summary>
public sealed partial class YamlDocument : Document
{
    private enum ScalarKeyContext
    {
        Value,
        ImplicitMapKey,
        ExplicitMapKey,
    }

    internal const int StackLimit = 128;

    // ReadDocument sizes this buffer from the input, so renting the default small array first only causes a second rent.
    // Tokens retain offsets into this buffer after parsing, so it must live as long as the document.
    private PooledBufferWriter _utf8TextForTokens = new(initialCapacity: 0);

    private readonly record struct TrailingComment(int Offset, int Length);

    private Dictionary<uint, TrailingComment> _trailingCommentsByNodeId = [];
    private uint _lastParsedNodeId;

    private ReadOnlyMemory<byte> _source;
    private int _position;
    private YamlDocumentOptions _parseOptions;

    private YamlDocument(SerializerSettings settings)
        : base(settings)
    {
    }

    /// <summary>
    /// Deep copies the document.
    /// </summary>
    /// <returns>The copy which the caller needs to dispose.</returns>
    public YamlDocument Duplicate()
    {
        // CreateSnapshot is a shallow clone.
        // The copy needs its own buffers so later edits stay independent.
        var document = CreateSnapshot<YamlDocument>();

        document._utf8TextForTokens = _utf8TextForTokens.Duplicate();
        document._trailingCommentsByNodeId = new Dictionary<uint, TrailingComment>(
            _trailingCommentsByNodeId
        );

        return document;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _utf8TextForTokens.Dispose();
        }

        base.Dispose(disposing);
    }

    private ReadOnlySpan<byte> Source => _source.Span;

    /// <summary>
    /// Verifies that the document retains its original value during format conversion.
    /// <code>
    /// 1. Writes the document as JSON.
    /// 2. Reads written JSON back as a REDox document.
    ///    Compares it with the original document and throws if values are different.
    /// 3. Writes the document from JSON as YAML.
    /// 4. Reads written YAML back as a REDox document.
    ///    Compares its value with the original document and throws if values are different.
    /// </code>
    /// </summary>
    /// <param name="options">How the YAML is written.</param>
    /// <param name="expectedOriginalElement">An optional source element to compare with the JSON read back.</param>
    /// <exception cref="DocumentParseException"/>
    public void ValidateJsonAndYamlRoundTrip(
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
                ParseException.ErrorCode.FormatCompatibility,
                "Writing and reading JSON changed the YAML value."
            );
        }

        if (
            expectedOriginalElement is { } expected
            && !DValue.DeepEquals(expected.AsValue(), readbackJsonObj.RootElement.AsValue())
        )
        {
            ThrowParseError(
                ParseException.ErrorCode.FormatCompatibility,
                "The JSON value produced from YAML differs from the expected original value."
            );
        }

        var yamlViaReadbackJsonObj = EncodeToString(readbackJsonObj.RootElement, options);

        using var readbackYamlObj = Parse(yamlViaReadbackJsonObj, Settings);

        if (!DValue.DeepEquals(RootElement.AsValue(), readbackYamlObj.RootElement.AsValue()))
        {
            ThrowParseError(
                ParseException.ErrorCode.FormatCompatibility,
                "Writing and reading YAML from the parsed JSON changed the original YAML value."
            );
        }
    }

    /// <summary>
    /// Reads a YAML document from a stream.
    /// </summary>
    /// <remarks>
    /// The stream is read to its end and is not closed.
    /// </remarks>
    /// <param name="stream">
    /// The UTF-8 byte stream.
    /// Set <see cref="YamlDocumentOptions.AutoDetectEncoding"/> to convert from another encoding.
    /// </param>
    /// <param name="settings">The serializer settings of the document.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document which the caller needs to dispose.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="DocumentParseException"/>
    public static YamlDocument Parse(
        Stream stream,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bufferSize = (settings ?? SerializerSettings.Default).DefaultBufferSize;

        // Parse releases the source before returning, so the using scope can dispose the input buffer.
        using var buffer = new PooledBufferWriter(bufferSize);

        int bytesRead;
        while ((bytesRead = stream.Read(buffer.GetSpan(bufferSize))) > 0)
        {
            buffer.Advance(bytesRead);
        }

        return Parse(buffer.WrittenMemory, settings, options);
    }

    /// <summary>
    /// Reads a YAML document from a string.
    /// </summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="settings">The serializer settings of the document.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document which the caller needs to dispose.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="DocumentParseException"/>
    public static YamlDocument Parse(
        string yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(yaml);

        // Parse releases the source before returning, so the using scope can dispose the input buffer.
        using var buffer = new PooledBufferWriter(yaml.Length);

        Encoding.UTF8.GetBytes(yaml, buffer);

        return Parse(buffer.WrittenMemory, settings, options);
    }

    /// <summary>
    /// Reads a YAML document from UTF-8 bytes.
    /// Non-UTF-8 is converted only when <see cref="YamlDocumentOptions.AutoDetectEncoding"/> is set.
    /// </summary>
    /// <param name="utf8Yaml">The UTF-8 text.</param>
    /// <param name="settings">The serializer settings of the document.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document which the caller needs to dispose.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="DocumentParseException"/>
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
    /// Reads a YAML document from UTF-8 bytes.
    /// Non-UTF-8 is converted only when <see cref="YamlDocumentOptions.AutoDetectEncoding"/> is set.
    /// </summary>
    /// <param name="utf8Yaml">The UTF-8 text.</param>
    /// <param name="settings">The serializer settings of the document.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>The document which the caller needs to dispose.</returns>
    /// <exception cref="DocumentParseException"/>
    public static YamlDocument Parse(
        ReadOnlyMemory<byte> utf8Yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        // Don't dispose if successfully loaded
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
    /// <param name="document">
    /// The document which the caller needs to dispose.
    /// <see langword="null"/> when the text could not be read.
    /// </param>
    /// <param name="settings">The serializer settings of the document.</param>
    /// <param name="options">How the text is read.</param>
    /// <returns>Whether text was read and <paramref name="document"/> is not null.</returns>
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Yaml,
        [NotNullWhen(true)] out YamlDocument? document,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        try
        {
            document = Parse(utf8Yaml, settings, options);

            return true;
        }
        catch (DocumentParseException)
        {
            document = null; // Just set to null as Parse disposes on failure

            return false;
        }
    }

    /// <summary>
    /// Writes an element as YAML to a new array of UTF-8 bytes.
    /// </summary>
    /// <param name="element">The element to write. Not only a YAML document.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <returns>The UTF-8 bytes.</returns>
    /// <exception cref="DocumentParseException"/>
    public static byte[] Encode(DElement element, YamlWriteOptions options = default)
    {
        // Reset before each use so the cached writer keeps its pooled buffer without retaining prior output.
        using var cache = Helper.InstanceCache<Utf8TextWriter>.Get(static () =>
            new Utf8TextWriter(SerializerSettings.Default)
        );

        var writer = cache.Value;

        writer.Reset(
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true, // Always true
            }
        );

        WriteDocumentRoot(writer, new DataReader(element), options);

        return writer.Encode();
    }

    /// <summary>
    /// Writes an element as YAML as UTF-8 to a stream.
    /// </summary>
    /// <remarks>
    /// The stream is not closed on write.
    /// </remarks>
    /// <param name="element">The element to write. Not only a YAML document.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <exception cref="DocumentParseException"/>
    public static void EncodeTo(DElement element, Stream stream, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(
            stream,
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true, // Always true
            }
        );

        WriteDocumentRoot(writer, new DataReader(element), options);
    }

    /// <summary>
    /// Writes an element as YAML as UTF-8 to a buffer writer.
    /// </summary>
    /// <param name="element">The element to write. Not only a YAML document.</param>
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
                WriteIndented = true, // Always true
            }
        );

        WriteDocumentRoot(writer, new DataReader(element), options);
    }

    /// <summary>
    /// Writes an element as YAML text.
    /// </summary>
    /// <param name="element">The element to write. Not only a YAML document.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <returns>The YAML text.</returns>
    /// <exception cref="DocumentParseException">A string cannot be written in the chosen <see cref="YamlStringStyle" />.</exception>
    public static string EncodeToString(DElement element, YamlWriteOptions options = default)
    {
        // The text path benefits from the same resettable pooled buffer as the byte-array path.
        using var cache = Helper.InstanceCache<Utf8TextWriter>.Get(static () =>
            new Utf8TextWriter(SerializerSettings.Default)
        );

        var writer = cache.Value;

        writer.Reset(
            element.Document.Settings,
            options.TextWriteOptions with
            {
                WriteIndented = true, // Always true
            }
        );

        WriteDocumentRoot(writer, new DataReader(element), options);

        return writer.EncodeToString();
    }

    private void Read(ReadOnlyMemory<byte> source, in YamlDocumentOptions options)
    {
        _parseOptions = options;

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
                    var candidate =
                        detected && kind == TextEncodingKind.Utf8 ? source[skipBytes..] : source;

                    if (!Utf8.IsValid(candidate.Span))
                    {
                        ThrowParseError(
                            ParseException.ErrorCode.InvalidEncoding,
                            "The text is not valid UTF-8."
                        );
                    }

                    _source = candidate;
                }
                else
                {
                    _source = ConvertToUtf8(kind, skipBytes, source);

                    static ReadOnlyMemory<byte> ConvertToUtf8(
                        TextEncodingKind kind,
                        int skipBytes,
                        ReadOnlyMemory<byte> source
                    )
                    {
                        using var decodedBuffer = new PooledBufferWriter();

                        Encoding encoding = kind switch
                        {
                            TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(
                                bigEndian: false,
                                byteOrderMark: false,
                                throwOnInvalidBytes: true
                            ),
                            TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(
                                bigEndian: true,
                                byteOrderMark: false,
                                throwOnInvalidBytes: true
                            ),
                            TextEncodingKind.Utf32LittleEndian => new UTF32Encoding(
                                bigEndian: false,
                                byteOrderMark: false,
                                throwOnInvalidCharacters: true
                            ),
                            _ => new UTF32Encoding(
                                bigEndian: true,
                                byteOrderMark: false,
                                throwOnInvalidCharacters: true
                            ),
                        };

                        var text = encoding.GetString(source.Span[skipBytes..]);
                        var length = Encoding.UTF8.GetBytes(
                            text.AsSpan(),
                            decodedBuffer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length))
                        );

                        decodedBuffer.Advance(length);

                        return decodedBuffer.WrittenMemory;
                    }
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

        // TODO: Scalar text usually occupies less than half the input (not profiled).
        _utf8TextForTokens.EnsureCapacity(_source.Length >> 1);

        _position = 0;

        SkipTrivia(leaveSameLineCommentForNextNode: false);

        var contentOnMarkerLine = false;
        var markerLineStart = 0;

        // TODO: support multiple documents in a file.
        // NOTE: this is required to pass YAML test suite.
        if (IsMarkerAtPosition(_position, YamlSyntax.DocumentStartUtf8))
        {
            markerLineStart = FindCurrentLineStart(_position);
            _position += 3;

            SkipInlineSpaces();

            // Content may follow the marker on its line ("--- text") but then it cannot be a block collection.
            if (
                _position < Source.Length
                && !YamlSyntax.IsLineEnd(Source[_position])
                && Source[_position] != YamlSyntax.CommentIndicator
            )
            {
                contentOnMarkerLine = true;
            }
            else
            {
                SkipTrivia(leaveSameLineCommentForNextNode: false);
            }
        }

        if (_position >= Source.Length)
        {
            // NOTE / TODO:
            //   An input with no content (empty, or only comments and blank lines) still gets a null root.
            //   Core data model has no "empty document" classification. A document is valid only while RootId > 0,
            //   and trivia is only ever attached to the token that follows it.
            //   The null token keeps RootId valid and gives the comments a token to attach to, so they are preserved.
            RootId = CreateNullToken();

            return;
        }

        // The root node's parent indentation is -1, so block scalars may start at column 0.
        RootId = ParseNode(depth: 0, parentIndent: -1, mapValueOnKeyLine: contentOnMarkerLine);

        if (contentOnMarkerLine && FindCurrentLineStart(_position) == markerLineStart)
        {
            ExpectLineEnd();
        }

        SkipTrivia(leaveSameLineCommentForNextNode: false);

        if (_position < Source.Length)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidFormat);
        }
    }

    private uint ParseNode(
        int depth,
        bool flow = false,
        int parentIndent = 0,
        bool mapValueOnKeyLine = false,
        ScalarKeyContext keyContext = ScalarKeyContext.Value
    )
    {
        RequireAllowedDepth(depth);

        if (_position >= Source.Length)
        {
            return CreateNullToken();
        }

        // A block collection cannot start on the same line as the key it belongs to ("a: b: c", "a: - b").
        var blockAllowed = !flow && !mapValueOnKeyLine;
        var first = Source[_position];

        if (
            first
                is YamlSyntax.AnchorIndicator
                or YamlSyntax.AliasIndicator
                or YamlSyntax.TagIndicator
                or YamlSyntax.DirectiveIndicator
            // An explicit key is read by the block map that holds it (see ParseExplicitMapEntry); anywhere else the
            // "?" that stands alone is not supported. "?x" is a plain scalar.
            || !blockAllowed && first == YamlSyntax.ExplicitKeyIndicator && IsExplicitKeyStart(flow)
            // Only these first bytes can begin a document marker; most nodes need no marker comparison.
            || first == YamlSyntax.Dash
            && IsMarkerAtPosition(_position, YamlSyntax.DocumentStartUtf8)
            || first == YamlSyntax.Dot && IsMarkerAtPosition(_position, YamlSyntax.DocumentEndUtf8)
        )
        {
            ThrowParseError(ParseException.ErrorCode.UnsupportedFeature);
        }

        var nodeId = first switch
        {
            YamlSyntax.FlowSequenceStart => ParseFlowArray(depth, parentIndent),
            YamlSyntax.FlowMappingStart => ParseFlowMap(depth, parentIndent),
            YamlSyntax.Dash when blockAllowed && IsBlockSequenceIndicator(_position) =>
                ParseBlockArray(depth),
            _ when blockAllowed && (IsExplicitKeyStart(flow: false) || IsBlockMapStart()) =>
                ParseBlockMap(depth),
            _ => ParseScalarNode(keyContext, parentIndent, flow),
        };

        _lastParsedNodeId = nodeId;

        return nodeId;
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

    private uint ParseScalarNode(
        ScalarKeyContext keyContext,
        int parentIndent = 0,
        bool flow = false
    )
    {
        if (_position >= Source.Length || YamlSyntax.IsLineEnd(Source[_position]))
        {
            return CreateNullToken();
        }

        var scalarSourceStart = _position;
        var value = Source[_position] switch
        {
            YamlSyntax.DoubleQuote => ParseDoubleQuotedString(parentIndent),
            YamlSyntax.SingleQuote => ParseSingleQuotedString(parentIndent),
            YamlSyntax.LiteralBlockIndicator
                or YamlSyntax.FoldedBlockIndicator when keyContext != ScalarKeyContext.ImplicitMapKey =>
                ParseLiteralOrFoldedBlockScalarText(parentIndent),
            _ => ParsePlainScalarText(
                keyContext == ScalarKeyContext.ImplicitMapKey,
                flow,
                parentIndent
            ),
        };

        if (_parseOptions.EnableValueValidation)
        {
            ValidateDisallowedCharactersInYamlScalarSourceText(scalarSourceStart);
        }

        // A quoted scalar is always a string.
        // A plain key is resolved like a plain value (null, boolean, number, timestamp or string),
        // so "1: a" is a map entry whose key is the integer 1, unless ReadMapKeyAsString is set.
        if (
            value.ResolveAsString
            || keyContext != ScalarKeyContext.Value && _parseOptions.ReadMapKeyAsString
        )
        {
            return CreateTokenForParsedScalar(DTokenVariant.String, value);
        }

        var text = _utf8TextForTokens.GetWrittenSpanFromOffset(value.Start);
        if (
            (text.IsEmpty || text[0] is (byte)'~' or (byte)'n' or (byte)'N')
            && TryParseNull(value, out var nullId)
        )
        {
            return nullId;
        }

        // A plain scalar can enter only the type readers whose spellings begin with its first byte.
        // Avoid invoking every reader for ordinary map keys while preserving null, boolean, number,
        // timestamp and string resolution in that order.
        return !text.IsEmpty
               && YamlSyntax.IsBooleanScalarStart(text[0])
               && TryParseBoolean(value, out var booleanId)
            ? booleanId
            : !text.IsEmpty
              && (
                  YamlSyntax.IsAsciiDigit(text[0])
                  || text[0] is YamlSyntax.Plus or YamlSyntax.Dash or YamlSyntax.Dot
              )
              && TryParseNumber(value, out var numberId)
                ? numberId
                : !text.IsEmpty
                  && YamlSyntax.IsAsciiDigit(text[0])
                  && TryParseTimestamp(value, out var timestampId)
                    ? timestampId
                    : CreateTokenForParsedScalar(DTokenVariant.String, value);
    }

    private void ValidateDisallowedCharactersInYamlScalarSourceText(int scalarSourceStart)
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
        for (var position = scalarSourceStart; position < scalarSourceEnd;)
        {
            // An ASCII byte is already a complete code point, so only multibyte text needs Rune decoding.
            var codePoint = (int)Source[position];
            var consumed = 1;
            if (codePoint >= 0x80)
            {
                Rune.DecodeFromUtf8(
                    Source[position..scalarSourceEnd],
                    out var character,
                    out consumed
                );

                codePoint = character.Value;
            }

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

    private readonly record struct ParsedScalar(
        int Start,
        bool ResolveAsString,
        TrailingComment? TrailingComment = null
    );

    private ParsedScalar RecordScalarTextStart(bool quoted, TrailingComment? trailingComment = null)
    {
        return new(_utf8TextForTokens.Length, quoted, trailingComment);
    }

    private TrailingComment StoreTrailingComment(ReadOnlySpan<byte> comment)
    {
        // Parsing releases the input buffer, so comments share the document-owned scalar buffer.
        var offset = _utf8TextForTokens.Length;

        _utf8TextForTokens.Append(comment);

        return new(offset, comment.Length);
    }

    private uint CreateTokenForParsedScalar(DTokenVariant variant, ParsedScalar scalar)
    {
        var length = _utf8TextForTokens.Length - scalar.Start;
        if (length > 0x1ffffff)
        {
            ThrowParseError(ParseException.ErrorCode.InvalidString);
        }

        var tokenId = AllocToken(
            DToken.Make(variant, DToken.EncodeLengthOffsetPayload(length, scalar.Start))
        );

        if (scalar.TrailingComment is { } trailingComment)
        {
            _trailingCommentsByNodeId.Add(tokenId, trailingComment);
        }

        return tokenId;
    }

    private uint CreateTriviaTokenFromUtf8Text(DTokenVariant variant, ReadOnlySpan<byte> text)
    {
        var scalar = RecordScalarTextStart(quoted: false);

        _utf8TextForTokens.Append(text);

        return CreateTokenForParsedScalar(variant, scalar);
    }

    /// <remarks>
    /// Scalar readers must use this span before internal buffer growth.
    /// </remarks>
    private ReadOnlySpan<byte> GetUtf8TextForToken(DToken token)
    {
        var (length, offset) = DToken.DecodeLengthOffsetPayload(token);

        return _utf8TextForTokens.Slice(offset, length);
    }

    private void CompleteContainer(uint containerId)
    {
        // Future edits rely on LinkId pointing after the last descendant, including an empty container.
        var token = GetToken(containerId);
        token.LinkId = (uint)GetTokens().Length;

        SetToken(containerId, token);
    }

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
                   || YamlSyntax.IsWhiteSpaceOrLineEnd(source[position + marker.Length])
               );
    }

    private static void WriteDocumentRoot(
        Utf8TextWriter writer,
        in DataReader reader,
        in YamlWriteOptions options
    )
    {
        WriteLeadingTriviaForToken(writer, reader, reader.RootId, options, separatorWritten: false);

        WriteNode(
            writer,
            reader,
            reader.RootId,
            reader.GetToken(reader.RootId),
            options,
            depth: 0,
            isKey: false
        );

        writer.WriteNewLine();
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
            WriteValueSeparator(writer, blockValue: false);
        }

        writer.WriteString(isMap ? YamlSyntax.EmptyMappingUtf8 : YamlSyntax.EmptySequenceUtf8);
    }

    private static bool WriteNode(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        DToken token,
        in YamlWriteOptions options,
        int depth,
        bool isKey,
        uint followingTokenId = 0
    )
    {
        // Container writers already read the token to choose its layout, so reuse it through scalar writers too.
        if (token.Type == DTokenType.Array)
        {
            var endsWithBlockScalar = WriteArray(
                writer,
                reader,
                tokenId,
                options,
                depth,
                followingTokenId
            );

            if (!isKey && options.PreserveTrivia)
            {
                writer.WriteString(GetTrailingComment(reader, tokenId, options));
            }

            return endsWithBlockScalar;
        }

        if (token.Type == DTokenType.Map)
        {
            var endsWithBlockScalar = WriteMap(
                writer,
                reader,
                tokenId,
                options,
                depth,
                followingTokenId
            );

            if (!isKey && options.PreserveTrivia)
            {
                writer.WriteString(GetTrailingComment(reader, tokenId, options));
            }

            return endsWithBlockScalar;
        }

        var blockScalar =
            !isKey
            && options.StringStyle == YamlStringStyle.Multiline
            && token.Kind is DTokenKind.String or DTokenKind.Symbol or DTokenKind.ByteString;

        // Most writes omit trivia.
        var trailingComment =
            !isKey && options.PreserveTrivia
                ? GetTrailingComment(reader, tokenId, options)
                : default;

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
                WriteNumber(writer, reader, tokenId, token);

                break;

            case DTokenKind.Timestamp:
                WriteTimestamp(writer, reader, tokenId, token);

                break;

            case DTokenKind.String:
            case DTokenKind.Symbol:
                // An original YAML scalar already points into this document's UTF-8 buffer.
                // Reading it through DataReader would look up the same token again for every key and value.
                var sourceYamlDocument = reader.Document as YamlDocument;
                var hasStoredUtf8 = sourceYamlDocument != null && !token.IsExtended;
                var utf8String = hasStoredUtf8
                    ? sourceYamlDocument!.GetUtf8TextForToken(token)
                    : reader.ReadUtf8String(tokenId);

                WriteYamlStringScalar(
                    writer,
                    utf8String,
                    options,
                    depth,
                    isKey,
                    default,
                    blockScalar ? trailingComment : default,
                    alreadyValidatedUtf8: hasStoredUtf8
                );

                break;

            case DTokenKind.ByteString:
                WriteByteString(
                    writer,
                    reader,
                    tokenId,
                    token,
                    options,
                    depth,
                    isKey,
                    default,
                    blockScalar ? trailingComment : default
                );

                break;

            default:
                ThrowGenericError(
                    ParseException.ErrorCode.UnsupportedFeature,
                    $"YAML does not support {token.Kind} values."
                );

                break;
        }

        if (!blockScalar && !isKey)
        {
            writer.WriteString(trailingComment);
        }

        return blockScalar;
    }
}