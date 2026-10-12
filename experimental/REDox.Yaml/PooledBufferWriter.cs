using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace REDox.Yaml;

internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int MinimumGrowth = 256;

    // NOTE: array pool buffer may be larger than requested size
    //       so it may contain the content that previous consumer written
    private byte[] _buffer_must_be_trimmed_before_access;
    private int _length;

    public PooledBufferWriter(int initialCapacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        _buffer_must_be_trimmed_before_access =
            initialCapacity <= 0 ? [] : ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int Length => _length;

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));

        return _buffer_must_be_trimmed_before_access.AsSpan(_length);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));

        return _buffer_must_be_trimmed_before_access.AsMemory(_length);
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            unchecked((uint)count),
            (uint)(_buffer_must_be_trimmed_before_access.Length - _length)
        );

        _length += count;
    }

    // NOTE: ArrayPool may return larger array than requested size and
    //       it may contain spare bytes that are not part of the document (previous consumer writes).
    //       MUST ALWAYS slice the written region before access to prevent reading unknown data.
    public byte this[int index] => _buffer_must_be_trimmed_before_access.AsSpan(0, _length)[index];

    public ReadOnlyMemory<byte> WrittenMemory =>
        _buffer_must_be_trimmed_before_access.AsMemory(0, _length);

    // NOTE: As written above, slice before access to prevent reading unknown data without throwing exception.
    public ReadOnlySpan<byte> GetWrittenSpanFromOffset(int offset) =>
        _buffer_must_be_trimmed_before_access.AsSpan(0, _length)[offset..];

    // NOTE: As written above, slice before access to prevent reading unknown data without throwing exception.
    public ReadOnlySpan<byte> Slice(int offset, int length) =>
        _buffer_must_be_trimmed_before_access.AsSpan(0, _length).Slice(offset, length);

    public void TruncateTo(int length)
    {
        // Extending the written length here would expose bytes that were never part of the document.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unchecked((uint)length), (uint)_length);

        _length = length;
    }

    public void Append(byte value)
    {
        EnsureCapacity(1);

        _buffer_must_be_trimmed_before_access[_length++] = value;
    }

    public void Append(byte value, int count)
    {
        EnsureCapacity(count);
        _buffer_must_be_trimmed_before_access.AsSpan(_length, count).Fill(value);

        _length += count;
    }

    public void Append(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer_must_be_trimmed_before_access.AsSpan(_length));

        _length += value.Length;
    }

    public void AppendCodePoint(int codePoint)
    {
        var rune = new Rune(codePoint);

        EnsureCapacity(rune.Utf8SequenceLength);

        _length += rune.EncodeToUtf8(_buffer_must_be_trimmed_before_access.AsSpan(_length));
    }

    public PooledBufferWriter Duplicate()
    {
        var copy = new PooledBufferWriter(_length);

        copy.Append(_buffer_must_be_trimmed_before_access.AsSpan(0, _length));

        return copy;
    }

    public void Dispose()
    {
        Return(_buffer_must_be_trimmed_before_access);

        _buffer_must_be_trimmed_before_access = [];
        _length = 0;
    }

    private void EnsureCapacity(int additional)
    {
        Debug.Assert(additional >= 0);

        var required = checked(_length + additional);
        if (required <= _buffer_must_be_trimmed_before_access.Length)
        {
            return;
        }

        var larger = ArrayPool<byte>.Shared.Rent(
            Math.Max(_buffer_must_be_trimmed_before_access.Length * 2, required + MinimumGrowth)
        );

        _buffer_must_be_trimmed_before_access.AsSpan(0, _length).CopyTo(larger);
        Return(_buffer_must_be_trimmed_before_access);

        _buffer_must_be_trimmed_before_access = larger;
    }

    private static void Return(byte[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer); // TODO: clearArray for sensitive data
        }
    }
}