using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace REDox.Yaml;

/// <summary>
/// Simpifies control flow for stack allocation.
/// <code>
/// // use stack only for small array
/// var buffer = size &lt;= 128 ? stackalloc byte[size] : default;
///
/// // ImmediateBuffer replaces empty span with an ArrayPool buffer and
/// // returns it to pool on dispose. non-empty buffer remains unchanged
/// using var _ = ImmediateBuffer&lt;byte&gt;.Using(size, ref buffer);
/// </code>
/// </summary>
// NOTE: 'ref' is not required but added to limit use case
internal readonly ref struct ImmediateBuffer<T> : IDisposable
{
    private readonly T[] buffer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ImmediateBuffer(int size) => buffer = ArrayPool<T>.Shared.Rent(size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (buffer is T[] rental) // array is not nullable but may be null. see below
        {
            ArrayPool<T>.Shared.Return(rental);
        }
    }

    /// <inheritdoc cref="ImmediateBuffer{T}"/>
    /// <returns><see cref="IDisposable"/> returns buffer on dispose if required.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmediateBuffer<T> Using(
        int size,
        ref Span<T> pass_default_to_rent_array_pool_buffer
    )
    {
        if (!pass_default_to_rent_array_pool_buffer.IsEmpty)
        {
            Debug.Assert(default(ImmediateBuffer<T>).buffer is null); // ensure default ctor leaves buffer null

            return default;
        }
        else
        {
            var ret = new ImmediateBuffer<T>(size);
            pass_default_to_rent_array_pool_buffer = ret.buffer.AsSpan(0, size); // rented buffer may be larger

            return ret;
        }
    }
}