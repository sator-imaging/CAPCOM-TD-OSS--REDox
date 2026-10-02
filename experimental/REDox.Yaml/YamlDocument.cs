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
    private readonly Dictionary<uint, byte[]> _scalarValues = new();
    private readonly List<uint> _trailingTrivia = new();
    private ReadOnlyMemory<byte> _source;
    private string _text = string.Empty;
    private int _position;
    private YamlDocumentOptions _parseOptions;

    private YamlDocument(SerializerSettings settings) : base(settings)
    {
    }

    public YamlDocument Duplicate() => CreateSnapshot<YamlDocument>();

    public static YamlDocument Parse(Stream stream, SerializerSettings? settings = null,
        YamlDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray(), settings, options);
    }

    public static YamlDocument Parse(string yaml, SerializerSettings? settings = null,
        YamlDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return Parse(Encoding.UTF8.GetBytes(yaml), settings, options);
    }

    public static YamlDocument Parse(byte[] utf8Yaml, SerializerSettings? settings = null,
        YamlDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Yaml);
        return Parse(utf8Yaml.AsMemory(), settings, options);
    }

    public static YamlDocument Parse(ReadOnlyMemory<byte> utf8Yaml, SerializerSettings? settings = null,
        YamlDocumentOptions options = default)
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

    public static bool TryParse(ReadOnlySpan<byte> utf8Yaml, out YamlDocument? document,
        SerializerSettings? settings = null, YamlDocumentOptions options = default)
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
        using var writer = new Utf8TextWriter(element.Document.Settings,
            options.TextWriteOptions with { WriteIndented = true });
        Write(writer, new DataReader(element), options);
        return writer.Encode();
    }

    public static string EncodeToString(DElement element, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(element.Document.Settings,
            options.TextWriteOptions with { WriteIndented = true });
        Write(writer, new DataReader(element), options);
        return writer.EncodeToString();
    }

    public static void EncodeTo(DElement element, Stream stream, YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(stream, element.Document.Settings,
            options.TextWriteOptions with { WriteIndented = true });
        Write(writer, new DataReader(element), options);
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        YamlWriteOptions options = default)
    {
        using var writer = new Utf8TextWriter(bufferWriter, element.Document.Settings,
            options.TextWriteOptions with { WriteIndented = true });
        Write(writer, new DataReader(element), options);
    }

    private void Read(ReadOnlyMemory<byte> source, YamlDocumentOptions options)
    {
        _parseOptions = options;
        _source = DecodeSource(source, options.AutoDetectEncoding);
        _text = new UTF8Encoding(false, true).GetString(_source.Span);
        EnsureCapacity(_source.Length / 8);
        _position = 0;
        SkipTrivia();
        if (AtDocumentMarker("---"))
        {
            _position += 3;
            SkipTrivia();
        }

        if (_position >= _text.Length)
        {
            RootId = ParseNull();
            return;
        }

        RootId = ParseNode(0);
        SkipTrivia();
        if (options.PreserveTrivia)
        {
            var trailingStart = (uint)GetTokens().Length;
            while (trailingStart > RootId && GetToken(trailingStart - 1).Kind == DTokenKind.Trivia)
                trailingStart--;
            for (var triviaId = trailingStart; triviaId < GetTokens().Length; triviaId++)
            {
                if (GetToken(triviaId).Kind == DTokenKind.Trivia) _trailingTrivia.Add(triviaId);
            }
        }
        if (_position < _text.Length)
        {
            throw Error(ParseErrorCode.InvalidFormat);
        }
    }

    private static ReadOnlyMemory<byte> DecodeSource(ReadOnlyMemory<byte> source, bool detectEncoding)
    {
        var bytes = source.Span;
        if (detectEncoding)
        {
            Encoding? encoding = null;
            var skip = 0;
            if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            {
                encoding = new UTF8Encoding(false, true);
                skip = 3;
            }
            else if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }))
            {
                encoding = new UTF32Encoding(false, true, true);
                skip = 4;
            }
            else if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
            {
                encoding = new UTF32Encoding(true, true, true);
                skip = 4;
            }
            else if (bytes.StartsWith(new byte[] { 0xff, 0xfe }))
            {
                encoding = new UnicodeEncoding(false, true, true);
                skip = 2;
            }
            else if (bytes.StartsWith(new byte[] { 0xfe, 0xff }))
            {
                encoding = new UnicodeEncoding(true, true, true);
                skip = 2;
            }
            else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0)
            {
                encoding = new UTF32Encoding(true, false, true);
            }
            else if (bytes.Length >= 4 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
            {
                encoding = new UTF32Encoding(false, false, true);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0 && bytes[1] != 0)
            {
                encoding = new UnicodeEncoding(true, false, true);
            }
            else if (bytes.Length >= 2 && bytes[0] != 0 && bytes[1] == 0)
            {
                encoding = new UnicodeEncoding(false, false, true);
            }

            if (encoding != null)
            {
                return Encoding.UTF8.GetBytes(encoding.GetString(bytes[skip..]));
            }
        }

        return source;
    }

    private uint ParseNode(int depth, bool flow = false, int parentIndent = 0)
    {
        if (_parseOptions.MaxDepth > 0 && depth > _parseOptions.MaxDepth)
        {
            throw Error(ParseErrorCode.MaxDepthExceeded);
        }

        if (_position >= _text.Length)
        {
            return ParseNull();
        }

        if (_text[_position] is '&' or '*' or '!' or '%' or '?' ||
            AtDocumentMarker("---") || AtDocumentMarker("..."))
        {
            throw Error(ParseErrorCode.UnsupportedFeature);
        }

        return _text[_position] switch
        {
            '[' => ParseFlowArray(depth),
            '{' => ParseFlowMap(depth),
            '-' when !flow && IsBlockSequenceIndicator() => ParseBlockArray(depth),
            _ when !flow && IsBlockMapStart() => ParseBlockMap(depth),
            _ => ParseScalarNode(parentIndent: parentIndent, flow: flow)
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
            '"' => ParseDoubleQuotedString(),
            '\'' => ParseSingleQuotedString(),
            '|' or '>' when !key => ParseBlockString(parentIndent),
            _ => ParsePlainString(key, flow, parentIndent)
        };

        if (value.Quoted || key)
        {
            return ParseString(value.Text);
        }

        if (TryParseNull(value.Text, out var nullId)) return nullId;
        if (TryParseBoolean(value.Text, out var booleanId)) return booleanId;
        if (TryParseNumber(value.Text, out var numberId)) return numberId;
        return ParseString(value.Text);
    }

    private uint StoreScalar(DTokenVariant variant, string text)
    {
        var tokenId = AllocToken(DToken.Make(variant, 0));
        SetToken(tokenId, DToken.Make(variant, DToken.EncodeLengthOffsetPayload(0, (int)tokenId)));
        _scalarValues.Add(tokenId, Encoding.UTF8.GetBytes(text));
        return tokenId;
    }

    private void CompleteContainer(uint containerId)
    {
        // Future edits rely on LinkId pointing after the last descendant, including an empty container.
        var token = GetToken(containerId);
        token.LinkId = (uint)GetTokens().Length;
        SetToken(containerId, token);
    }

    private static bool IsLineEnd(char value) => value is '\r' or '\n';

    private bool AtDocumentMarker(string marker)
    {
        return _text.AsSpan(_position).StartsWith(marker, StringComparison.Ordinal) &&
               (_position + marker.Length == _text.Length || IsLineEnd(_text[_position + marker.Length]));
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, YamlWriteOptions options)
    {
        WriteTrivia(writer, reader, reader.RootId, options, false);
        WriteNode(writer, reader, reader.RootId, options, 0, false);
        writer.WriteNewLine();
        if (options.PreserveTrivia && reader.Document is YamlDocument document)
        {
            WriteTrailingTrivia(writer, reader, document._trailingTrivia);
        }
    }

    private static void WriteNode(Utf8TextWriter writer, in DataReader reader, uint tokenId,
        YamlWriteOptions options, int depth, bool key)
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
            case DTokenKind.String:
            case DTokenKind.Symbol:
                WriteString(writer, reader, tokenId, options, depth, key);
                break;
            default:
                throw new NotSupportedException($"YAML does not support {token.Kind} values.");
        }
    }
}
