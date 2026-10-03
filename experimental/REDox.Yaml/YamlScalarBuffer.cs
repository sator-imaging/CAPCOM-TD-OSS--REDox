// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;

namespace REDox.Yaml;

// The bytes of the scalars of a document, packed one after the other: tokens keep (length, offset) into them. A
// scalar is built at the end while it is read and then either kept, by a token, or dropped with Truncate. The array
// is rented from the array pool and grows by renting a larger one, so it is returned when the buffer is disposed.
internal sealed class YamlScalarBuffer : IDisposable
{
    // What a larger array has at least in addition to what is needed, so that a run of small scalars does not rent one
    // array after another.
    private const int MinimumGrowth = 256;

    private byte[] _buffer = Array.Empty<byte>();
    private int _length;

    // The number of bytes stored, which is where the next scalar starts.
    public int Length => _length;

    // The byte at the index, which is before Length.
    public byte this[int index] => _buffer[index];

    // The bytes from the offset to the end.
    public ReadOnlySpan<byte> From(int offset) => _buffer.AsSpan(offset, _length - offset);

    public ReadOnlySpan<byte> Slice(int offset, int length) => _buffer.AsSpan(offset, length);

    // Drops the bytes from the length on.
    public void Truncate(int length) => _length = length;

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

    // Writes a code point as UTF-8. The caller has checked that it is a Unicode scalar value.
    public void AppendCodePoint(int codePoint)
    {
        EnsureCapacity(4);
        _length += new Rune(codePoint).EncodeToUtf8(_buffer.AsSpan(_length));
    }

    // A buffer of its own, with the same bytes.
    public YamlScalarBuffer Duplicate()
    {
        var copy = new YamlScalarBuffer();
        copy.Append(_buffer.AsSpan(0, _length));
        return copy;
    }

    public void Dispose()
    {
        Return(_buffer);
        _buffer = Array.Empty<byte>();
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

    // The empty array that stands for "nothing rented yet" is not the pool's.
    private static void Return(byte[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
