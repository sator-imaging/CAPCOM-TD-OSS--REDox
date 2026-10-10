using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

/// <summary>
/// Writes the values as block style YAML without building a document first.
/// </summary>
public sealed class YamlWriter : DataWriter, IDisposable
{
    private struct Frame
    {
        public bool IsMap;
        public bool ValueNext;
        public bool AfterIndicator;
        public int Depth;
        public int Count;
    }

    private readonly Utf8TextWriter _writer;
    private Frame[] _frames = [];
    private int _top;
    private bool _rootWritten;
    private bool _completed;
    private YamlWriteOptions _options;

    /// <inheritdoc cref="YamlWriter"/>
    public YamlWriter(SerializerSettings? settings = null, YamlWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;
        _writer = new Utf8TextWriter(
            settings,
            options.TextWriteOptions with
            {
                WriteIndented = true,
            }
        );

        base.Reset(settings);

        _options = options;
    }

    /// <summary>
    /// Reset document state.
    /// </summary>
    public void Reset(SerializerSettings settings, YamlWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        base.Reset(settings);
        _writer.Reset(settings, options.TextWriteOptions with { WriteIndented = true });

        _options = options;
        _top = 0;
        _rootWritten = false;
        _completed = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writer.Dispose();
    }

    /// <summary>
    /// Returns the UTF-8 bytes of the YAML.
    /// </summary>
    /// <returns>The UTF-8 bytes.</returns>
    /// <exception cref="DocumentParseException"></exception>
    public byte[] Encode()
    {
        if (!_completed)
        {
            if (_top != 0 || !_rootWritten)
            {
                YamlDocument.ThrowGenericError(
                    YamlDocument.ParseException.ErrorCode.InvalidOperation,
                    "The YAML document is not complete."
                );
            }

            _writer.WriteNewLine();

            _completed = true;
        }

        return _writer.Encode();
    }

    private Frame[] ExpandFrameCapacity()
    {
        if (_frames.Length == 0)
        {
            _frames = new Frame[8];
        }
        else
        {
            Array.Resize(ref _frames, _top * 2);
        }

        return _frames;
    }

    private void BeginEntry(ref Frame frame)
    {
        if (frame.Count > 0 || frame.AfterIndicator)
        {
            YamlDocument.WriteValueSeparator(_writer, blockValue: true);
        }
    }

    /// <returns>
    /// Returns true when it is a map key.
    /// </returns>
    private bool BeginScalar(out int depth)
    {
        if (_top == 0)
        {
            StartRoot();

            depth = 0;

            return false;
        }

        ref var frame = ref _frames[_top - 1];
        if (frame.IsMap && !frame.ValueNext)
        {
            BeginEntry(ref frame);

            depth = frame.Depth;

            return true;
        }

        if (!frame.IsMap)
        {
            BeginEntry(ref frame);
            YamlDocument.WriteCollectionIndicator(_writer, mapValue: false);
        }

        YamlDocument.WriteValueSeparator(_writer, blockValue: false);

        depth = frame.Depth + 1;

        return false;
    }

    private void EndScalar(bool key)
    {
        if (_top == 0)
        {
            return;
        }

        ref var frame = ref _frames[_top - 1];
        if (key)
        {
            YamlDocument.WriteCollectionIndicator(_writer, mapValue: true);

            frame.ValueNext = true;

            return;
        }

        EndEntry(ref frame);
    }

    private void EndEntry(ref Frame frame)
    {
        _writer.PopIndent();

        frame.ValueNext = false;
        frame.Count++;
    }

    private void StartRoot()
    {
        if (_rootWritten)
        {
            YamlDocument.ThrowGenericError(
                YamlDocument.ParseException.ErrorCode.InvalidOperation,
                "A YAML document has one root value."
            );
        }

        _rootWritten = true;
    }

    private void BeginContainer(bool isMap)
    {
        // Reject excessive nesting before writing an indicator or changing the root state.
        if (_top >= SerializerSettings.DefaultMaxDepth)
        {
            YamlDocument.ThrowGenericError(
                YamlDocument.ParseException.ErrorCode.InvalidOperation,
                $"Exceeded maximum depth of {SerializerSettings.DefaultMaxDepth}."
            );
        }

        var afterIndicator = false;
        var depth = 0;
        if (_top == 0)
        {
            StartRoot();
        }
        else
        {
            ref var parent = ref _frames[_top - 1];
            if (parent.IsMap && !parent.ValueNext)
            {
                YamlDocument.ThrowGenericError(
                    YamlDocument.ParseException.ErrorCode.UnsupportedFeature,
                    "YAML collection keys are not supported."
                );
            }

            if (!parent.IsMap)
            {
                BeginEntry(ref parent);
                YamlDocument.WriteCollectionIndicator(_writer, mapValue: false);
            }

            afterIndicator = true;
            depth = parent.Depth + 1;
        }

        if (_top == _frames.Length)
        {
            _frames = ExpandFrameCapacity();
        }

        _frames[_top++] = new Frame
        {
            IsMap = isMap,
            AfterIndicator = afterIndicator,
            Depth = depth,
        };
    }

    private void EndContainer(bool isMap)
    {
        if (_top == 0 || _frames[_top - 1].IsMap != isMap)
        {
            YamlDocument.ThrowGenericError(
                YamlDocument.ParseException.ErrorCode.InvalidOperation,
                "The end does not match the start of the collection."
            );
        }

        var frame = _frames[_top - 1];
        if (frame.ValueNext)
        {
            // An invalid end must leave the open map on the stack for the next write attempt.
            YamlDocument.ThrowGenericError(
                YamlDocument.ParseException.ErrorCode.InvalidOperation,
                "A map key has no value."
            );
        }

        _top--;

        // A collection with no entries is written on the line of its key or dash, as the flow form.
        if (frame.Count == 0)
        {
            YamlDocument.WriteEmptyCollection(_writer, isMap, frame.AfterIndicator);
        }

        if (_top > 0)
        {
            EndEntry(ref _frames[_top - 1]);
        }
    }

    /// <inheritdoc />
    public override void WriteStartMap(int? definiteLength)
    {
        BeginContainer(isMap: true);
    }

    /// <inheritdoc />
    public override void WriteEndMap()
    {
        EndContainer(isMap: true);
    }

    /// <inheritdoc />
    public override void WriteStartArray(int? definiteLength)
    {
        BeginContainer(isMap: false);
    }

    /// <inheritdoc />
    public override void WriteEndArray()
    {
        EndContainer(isMap: false);
    }

    /// <inheritdoc />
    public override void WriteNull()
    {
        var key = BeginScalar(out _);

        _writer.WriteNull();
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteBoolean(bool value)
    {
        var key = BeginScalar(out _);

        _writer.WriteBoolean(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteInt64(long value)
    {
        var key = BeginScalar(out _);

        _writer.WriteInt64(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteUInt64(ulong value)
    {
        var key = BeginScalar(out _);

        _writer.WriteUInt64(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteHalf(Half value)
    {
        var key = BeginScalar(out _);
        if (!YamlDocument.TryWriteSpecialFloat(_writer, (double)value))
        {
            _writer.WriteHalf(value);
        }

        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteSingle(float value)
    {
        var key = BeginScalar(out _);
        if (!YamlDocument.TryWriteSpecialFloat(_writer, value))
        {
            _writer.WriteSingle(value);
        }

        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteDouble(double value)
    {
        var key = BeginScalar(out _);
        if (!YamlDocument.TryWriteSpecialFloat(_writer, value))
        {
            _writer.WriteDouble(value);
        }

        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteDecimal(decimal value)
    {
        var key = BeginScalar(out _);

        _writer.WriteDecimal(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteBigNumber(
        ReadOnlySpan<byte> value,
        BigNumberKind kind = BigNumberKind.Default
    )
    {
        var key = BeginScalar(out _);

        _writer.WriteString(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteDateTime(DateTime value)
    {
        var key = BeginScalar(out _);

        _writer.WriteTimestamp(value);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteDateTimeOffset(DateTimeOffset value)
    {
        var key = BeginScalar(out _);

        _writer.WriteTimestamp(value);
        EndScalar(key);
    }

    // NOTE: Optimized for UTF-8. May be slower on UTF-16/32 output.

    /// <inheritdoc />
    public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        var key = BeginScalar(out var depth);

        YamlDocument.WriteStringValue(_writer, utf8Bytes, _options, depth, key);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteString(ReadOnlySpan<char> value)
    {
        int len = value.Length * 4; // multiply by 4 is enough. Encoding.UTF8.GetMaxByteCount(value.Length);
        var span = len <= YamlDocument.StackLimit ? stackalloc byte[len] : default;

        using var _ = ImmediateBuffer<byte>.Using(len, ref span);

        int written = Encoding.UTF8.GetBytes(value, span);

        WriteString(span[..written]);
    }

    /// <inheritdoc />
    public override void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(value.AsSpan());
    }

    /// <inheritdoc />
    public override void WriteChar(char value)
    {
        // A lone surrogate has no UTF-8 encoding; keep the encoder's replacement behavior.
        var rune = Rune.TryCreate(value, out var validRune) ? validRune : Rune.ReplacementChar;

        Span<byte> span = stackalloc byte[rune.Utf8SequenceLength];
        int written = rune.EncodeToUtf8(span);

        WriteString(span[..written]);
    }

    /// <inheritdoc />
    public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
    {
        WriteString(value.AsSpan());
    }

    /// <inheritdoc />
    public override void WriteGuid(Guid value)
    {
        Span<byte> utf8Bytes = stackalloc byte[36];
        var formatted = Utf8Formatter.TryFormat(value, utf8Bytes, out var bytesWritten);

        Debug.Assert(formatted);

        WriteString(utf8Bytes[..bytesWritten]);
    }

    /// <inheritdoc />
    public override void WriteByteString(
        ReadOnlySpan<byte> value,
        ByteStringKind kind = ByteStringKind.Default
    )
    {
        WriteString(YamlDocument.EncodeByteStringAsYamlText(value, kind));
    }
}