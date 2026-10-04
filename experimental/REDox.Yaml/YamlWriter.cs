// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Serialization;

namespace REDox.Yaml;

/// <summary>
/// Writes the values it is given as block style YAML, as they come, without building a document first. It is what
/// <see cref="YamlSerializer" /> uses to write an object. A map is written as its keys and values in turn, and a
/// collection key is not supported.
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
    private Frame[] _frames = new Frame[8];
    private int _top;
    private bool _rootWritten;
    private bool _completed;
    private YamlWriteOptions _options;

    /// <summary>
    /// Creates a writer that collects the YAML in memory, to be taken with <see cref="Encode" />.
    /// </summary>
    /// <param name="settings">The serializer settings. The default settings when null.</param>
    /// <param name="options">How the YAML is written.</param>
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
    /// Starts a new document, discarding what was written.
    /// </summary>
    /// <param name="settings">The serializer settings.</param>
    /// <param name="options">How the YAML is written.</param>
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
    /// Ends the document and returns the UTF-8 bytes of the YAML.
    /// </summary>
    /// <returns>The UTF-8 bytes.</returns>
    /// <exception cref="InvalidOperationException">No value was written, or a map or an array is not ended.</exception>
    public byte[] Encode()
    {
        if (!_completed)
        {
            if (_top != 0 || !_rootWritten)
            {
                ThrowHelper.ThrowInvalidOperation("The YAML document is not complete.");
            }

            _writer.WriteNewLine();

            _completed = true;
        }

        return _writer.Encode();
    }

    private void BeginEntry(ref Frame frame)
    {
        if (frame.Count > 0 || frame.AfterIndicator)
        {
            _writer.WriteIndentNewLine();
        }
    }

    // Starts a value that is not a collection. Returns true when it is a map key.
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
            _writer.WriteUtf8Byte(YamlSyntax.Dash);
            _writer.PushIndent();
        }

        _writer.WriteUtf8Byte(YamlSyntax.Space);

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
            _writer.WriteUtf8Byte(YamlSyntax.MappingValueIndicator);
            _writer.PushIndent();

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
            ThrowHelper.ThrowInvalidOperation("A YAML document has one root value.");
        }

        _rootWritten = true;
    }

    private void BeginContainer(bool isMap)
    {
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
                ThrowHelper.ThrowNotSupported("YAML collection keys are not supported.");
            }

            if (!parent.IsMap)
            {
                BeginEntry(ref parent);
                _writer.WriteUtf8Byte(YamlSyntax.Dash);
                _writer.PushIndent();
            }

            afterIndicator = true;
            depth = parent.Depth + 1;
        }

        if (_top >= SerializerSettings.DefaultMaxDepth)
        {
            ThrowHelper.ThrowInvalidOperation(
                $"Exceeded maximum depth of {SerializerSettings.DefaultMaxDepth}."
            );
        }

        if (_top == _frames.Length)
        {
            Array.Resize(ref _frames, _top * 2);
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
            ThrowHelper.ThrowInvalidOperation(
                "The end does not match the start of the collection."
            );
        }

        var frame = _frames[--_top];
        if (frame.ValueNext)
        {
            ThrowHelper.ThrowInvalidOperation("A map key has no value.");
        }

        // A collection with no entries is written on the line of its key or dash, as the flow form.
        if (frame.Count == 0)
        {
            if (frame.AfterIndicator)
            {
                _writer.WriteUtf8Byte(YamlSyntax.Space);
            }

            _writer.WriteString(isMap ? YamlSyntax.EmptyMappingUtf8 : YamlSyntax.EmptySequenceUtf8);
        }

        if (_top > 0)
        {
            EndEntry(ref _frames[_top - 1]);
        }
    }

    /// <inheritdoc />
    public override void WriteStartMap(int? definiteLength)
    {
        BeginContainer(true);
    }

    /// <inheritdoc />
    public override void WriteEndMap()
    {
        EndContainer(true);
    }

    /// <inheritdoc />
    public override void WriteStartArray(int? definiteLength)
    {
        BeginContainer(false);
    }

    /// <inheritdoc />
    public override void WriteEndArray()
    {
        EndContainer(false);
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

    /// <inheritdoc />
    public override void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var key = BeginScalar(out var depth);

        YamlDocument.WriteStringValue(
            _writer,
            Utf8Helper.GetUtf8String(value),
            _options,
            depth,
            key
        );

        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteString(ReadOnlySpan<char> value)
    {
        WriteString(value.ToString());
    }

    /// <inheritdoc />
    public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
    {
        var key = BeginScalar(out var depth);

        YamlDocument.WriteStringValue(_writer, utf8Bytes, _options, depth, key);
        EndScalar(key);
    }

    /// <inheritdoc />
    public override void WriteChar(char value)
    {
        WriteString(value.ToString());
    }

    /// <inheritdoc />
    public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
    {
        WriteString(value.ToString());
    }

    /// <inheritdoc />
    public override void WriteGuid(Guid value)
    {
        WriteString(value.ToString());
    }

    /// <inheritdoc />
    public override void WriteByteString(
        ReadOnlySpan<byte> value,
        ByteStringKind kind = ByteStringKind.Default
    )
    {
        WriteString(YamlDocument.ByteStringText(value, kind));
    }
}
