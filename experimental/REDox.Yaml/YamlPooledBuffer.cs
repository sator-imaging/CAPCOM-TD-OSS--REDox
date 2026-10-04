// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;

namespace REDox.Yaml;

internal sealed class YamlPooledBuffer : IBufferWriter<byte>, IDisposable
{
    private const int MinimumGrowth = 256;

    private byte[] _buffer = [];
    private int _length;

    public YamlPooledBuffer(int initialCapacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        if (initialCapacity > 0)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }
    }

    public int Length => _length;

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));

        return _buffer.AsSpan(_length);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));

        return _buffer.AsMemory(_length);
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            unchecked((uint)count),
            (uint)(_buffer.Length - _length)
        );

        _length += count;
    }

    public byte this[int index] => _buffer[index];

    public ReadOnlySpan<byte> From(int offset) => _buffer.AsSpan(offset, _length - offset);

    public ReadOnlySpan<byte> Slice(int offset, int length) => _buffer.AsSpan(offset, length);

    public void TruncateTo(int length) => _length = length;

    public void Append(byte value)
    {
        EnsureCapacity(1);

        _buffer[_length++] = value;
    }

    public void Append(byte value, int count)
    {
        EnsureCapacity(count);
        _buffer.AsSpan(_length, count).Fill(value);

        _length += count;
    }

    public void Append(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));

        _length += value.Length;
    }

    public void AppendCodePoint(int codePoint)
    {
        EnsureCapacity(4);

        _length += new Rune(codePoint).EncodeToUtf8(_buffer.AsSpan(_length));
    }

    public YamlPooledBuffer Duplicate()
    {
        var copy = new YamlPooledBuffer();

        copy.Append(_buffer.AsSpan(0, _length));

        return copy;
    }

    public void Dispose()
    {
        Return(_buffer);

        _buffer = [];
        _length = 0;
    }

    private void EnsureCapacity(int additional)
    {
        var required = _length + additional;
        if (required <= _buffer.Length)
        {
            return;
        }

        var larger = ArrayPool<byte>.Shared.Rent(
            Math.Max(_buffer.Length * 2, required + MinimumGrowth)
        );

        _buffer.AsSpan(0, _length).CopyTo(larger);
        Return(_buffer);

        _buffer = larger;
    }

    private static void Return(byte[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer); // TODO: clearArray for sensitive data
        }
    }
}
