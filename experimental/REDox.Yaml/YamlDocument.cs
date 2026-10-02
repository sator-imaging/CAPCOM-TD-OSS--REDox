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

public sealed partial class YamlDocument : Document
{
    // Decoded scalar bytes are packed into one buffer; tokens keep (length, offset) into it.
    private byte[] _scalarBuffer = [];
    private int _scalarLength;
    private List<uint> _trailingTrivia = new();
    private string _text = string.Empty;
    private int _position;
    private YamlDocumentOptions _parseOptions;

    private YamlDocument(SerializerSettings settings)
        : base(settings) { }

    public YamlDocument Duplicate()
    {
        var document = CreateSnapshot<YamlDocument>();

        // CreateSnapshot is a shallow clone; the copy needs its own buffers so later edits stay independent.
        document._scalarBuffer = _scalarBuffer.AsSpan(0, _scalarLength).ToArray();
        document._trailingTrivia = new List<uint>(_trailingTrivia);
        return document;
    }

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

    public static YamlDocument Parse(
        string yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Parse(Encoding.UTF8.GetBytes(yaml), settings, options);
    }

    public static YamlDocument Parse(
        byte[] utf8Yaml,
        SerializerSettings? settings = null,
        YamlDocumentOptions options = default
    )
    {
        ArgumentNullException.ThrowIfNull(utf8Yaml);
        return Parse(utf8Yaml.AsMemory(), settings, options);
    }

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

            // The text is only needed while parsing; decoded values live in the scalar buffer.
            document._text = string.Empty;
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

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
        ReadOnlyMemory<byte> decoded;
        try
        {
            decoded = DecodeSource(source, options.AutoDetectEncoding);
            _text = new UTF8Encoding(false, true).GetString(decoded.Span);
        }
        catch (DecoderFallbackException)
        {
            // Report invalid input as a parse error so that TryParse returns false.
            throw Error(ParseErrorCode.InvalidFormat);
        }

        EnsureCapacity(decoded.Length / 8);
        _position = 0;
        SkipTrivia();
        var contentOnMarkerLine = false;
        var markerLineStart = 0;
        if (AtDocumentMarker("---"))
        {
            markerLineStart = LastLineStart(_position);
            _position += 3;
            SkipInlineSpaces();

            // Content may follow the marker on its line ("--- text"), but then it cannot be a block collection.
            if (_position < _text.Length && !IsLineEnd(_text[_position]) && _text[_position] != '#')
            {
                contentOnMarkerLine = true;
            }
            else
            {
                SkipTrivia();
            }
        }

        if (_position >= _text.Length)
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
        if (_position < _text.Length)
        {
            throw Error(ParseErrorCode.InvalidFormat);
        }
    }

    private static ReadOnlyMemory<byte> DecodeSource(
        ReadOnlyMemory<byte> source,
        bool detectEncoding
    )
    {
        if (!TextEncodingDetector.TryDetect(source.Span, out var kind, out var skipBytes))
        {
            return source;
        }

        // A UTF-8 byte order mark is always skipped; other encodings are converted only when requested.
        if (kind == TextEncodingKind.Utf8)
        {
            return source.Slice(skipBytes);
        }

        if (!detectEncoding)
        {
            return source;
        }

        Encoding encoding = kind switch
        {
            TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
            TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(true, false, true),
            TextEncodingKind.Utf32LittleEndian => new UTF32Encoding(false, false, true),
            _ => new UTF32Encoding(true, false, true),
        };

        return Encoding.UTF8.GetBytes(encoding.GetString(source.Span[skipBytes..]));
    }

    private uint ParseNode(
        int depth,
        bool flow = false,
        int parentIndent = 0,
        bool mapValueOnKeyLine = false
    )
    {
        if (_parseOptions.MaxDepth > 0 && depth > _parseOptions.MaxDepth)
        {
            throw Error(ParseErrorCode.MaxDepthExceeded);
        }

        if (_position >= _text.Length)
        {
            return ParseNull();
        }

        if (
            _text[_position] is '&' or '*' or '!' or '%' or '?'
            || AtDocumentMarker("---")
            || AtDocumentMarker("...")
        )
        {
            throw Error(ParseErrorCode.UnsupportedFeature);
        }

        // A block collection cannot start on the same line as the key it belongs to ("a: b: c", "a: - b").
        var blockAllowed = !flow && !mapValueOnKeyLine;

        return _text[_position] switch
        {
            '[' => ParseFlowArray(depth, parentIndent),
            '{' => ParseFlowMap(depth, parentIndent),
            '-' when blockAllowed && IsBlockSequenceIndicator() => ParseBlockArray(depth),
            _ when blockAllowed && IsBlockMapStart() => ParseBlockMap(depth),
            _ => ParseScalarNode(parentIndent: parentIndent, flow: flow),
        };
    }

    private uint ParseScalarNode(bool key = false, int parentIndent = 0, bool flow = false)
    {
        if (_position >= _text.Length || IsLineEnd(_text[_position]))
        {
            return ParseNull();
        }

        var value = _text[_position] switch
        {
            '"' => ParseDoubleQuotedString(parentIndent),
            '\'' => ParseSingleQuotedString(parentIndent),
            '|' or '>' when !key => ParseBlockString(parentIndent),
            _ => ParsePlainString(key, flow, parentIndent),
        };

        if (value.Quoted || key)
        {
            return ParseString(value.Text);
        }

        if (TryParseNull(value.Text, out var nullId))
            return nullId;
        if (TryParseBoolean(value.Text, out var booleanId))
            return booleanId;
        if (TryParseNumber(value.Text, out var numberId))
            return numberId;
        if (TryParseTimestamp(value.Text, out var timestampId))
            return timestampId;
        return ParseString(value.Text);
    }

    private uint StoreScalar(DTokenVariant variant, string text)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        if (length > 0x1ffffff)
            throw Error(ParseErrorCode.InvalidString);

        var offset = _scalarLength;
        if (offset + length > _scalarBuffer.Length)
        {
            Array.Resize(
                ref _scalarBuffer,
                Math.Max(_scalarBuffer.Length * 2, offset + length + 256)
            );
        }

        Encoding.UTF8.GetBytes(text, _scalarBuffer.AsSpan(offset, length));
        _scalarLength = offset + length;
        return AllocToken(DToken.Make(variant, DToken.EncodeLengthOffsetPayload(length, offset)));
    }

    private ReadOnlySpan<byte> ScalarBytes(DToken token)
    {
        var (length, offset) = DToken.DecodeLengthOffsetPayload(token);
        return _scalarBuffer.AsSpan(offset, length);
    }

    private void CompleteContainer(uint containerId)
    {
        // Future edits rely on LinkId pointing after the last descendant, including an empty container.
        var token = GetToken(containerId);
        token.LinkId = (uint)GetTokens().Length;
        SetToken(containerId, token);
    }

    private static bool IsLineEnd(char value) => value is '\r' or '\n';

    private bool AtDocumentMarker(string marker) => AtDocumentMarker(_position, marker);

    private bool IsDocumentMarkerAt(int position) =>
        AtDocumentMarker(position, "---") || AtDocumentMarker(position, "...");

    // A marker ends the line or is followed by white space ("--- text").
    private bool AtDocumentMarker(int position, string marker)
    {
        return _text.AsSpan(position).StartsWith(marker, StringComparison.Ordinal)
            && (
                position + marker.Length == _text.Length
                || IsLineEnd(_text[position + marker.Length])
                || _text[position + marker.Length] is ' ' or '\t'
            );
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, YamlWriteOptions options)
    {
        WriteTrivia(writer, reader, reader.RootId, options, false);
        WriteNode(writer, reader, reader.RootId, options, 0, false);

        // The comment ending the last line has no following token inside the value, so it was kept in the
        // document's trailing trivia.
        var document = options.PreserveTrivia ? reader.Document as YamlDocument : null;
        var commentId = 0U;
        var inlineComment =
            document != null
            && TryGetInlineComment(reader, document._trailingTrivia, out commentId);
        if (inlineComment)
        {
            writer.WriteUtf8Byte((byte)' ');
            writer.WriteString(reader.ReadTrivia(commentId));
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
