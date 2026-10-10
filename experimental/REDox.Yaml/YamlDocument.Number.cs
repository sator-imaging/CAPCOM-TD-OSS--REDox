using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    /// <inheritdoc />
    protected override long DecodeInteger(DToken token)
    {
        var bytes = GetUtf8TextForToken(token);
        if (bytes.Contains(YamlSyntax.Underscore))
        {
            // The separators were validated when the scalar was read
            var size = bytes.Length;
            Span<byte> cleaned = size <= StackLimit ? stackalloc byte[size] : default;

            using var buffer = ImmediateBuffer<byte>.Using(size, ref cleaned);

            var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

            Debug.Assert(valid);

            return DecodeIntegerScalarBytes(cleaned[..length]);
        }

        return DecodeIntegerScalarBytes(bytes);
    }

    private long DecodeIntegerScalarBytes(ReadOnlySpan<byte> bytes)
    {
        return Utf8Parser.TryParse(bytes, out long value, out var consumed)
               && consumed == bytes.Length
            ? value
            : Utf8Parser.TryParse(bytes, out ulong unsigned, out consumed)
              && consumed == bytes.Length
                ? unchecked((long)unsigned)
                : TryParsePrefixedInteger(bytes, out var prefixed)
                    ? unchecked((long)prefixed)
                    : throw ParseError(
                        ParseException.ErrorCode.InvalidFormat,
                        "The stored integer cannot be decoded."
                    );
    }

    /// <inheritdoc />
    protected override double DecodeFloat(DToken token)
    {
        var bytes = GetUtf8TextForToken(token);
        if (bytes.Contains(YamlSyntax.Underscore))
        {
            var size = bytes.Length;
            Span<byte> cleaned = size <= StackLimit ? stackalloc byte[size] : default;

            using var buffer = ImmediateBuffer<byte>.Using(size, ref cleaned);

            var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

            Debug.Assert(valid);

            return DecodeFloatScalarBytes(cleaned[..length]);
        }

        return DecodeFloatScalarBytes(bytes);
    }

    private static double DecodeFloatScalarBytes(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(YamlSyntax.NegativeInfinityPrefixUtf8)
               && bytes.Length >= YamlSyntax.InfinitySuffixUtf8.Length
               && Ascii.EqualsIgnoreCase(
                   bytes[^YamlSyntax.InfinitySuffixUtf8.Length..],
                   YamlSyntax.InfinitySuffixUtf8
               )
            ? double.NegativeInfinity
            : bytes.Length >= YamlSyntax.PositiveInfinitySuffixUtf8.Length
              && Ascii.EqualsIgnoreCase(
                  bytes[^YamlSyntax.PositiveInfinitySuffixUtf8.Length..],
                  YamlSyntax.PositiveInfinitySuffixUtf8
              )
                ? double.PositiveInfinity
                : bytes.Length >= YamlSyntax.NotANumberSuffixUtf8.Length
                  && Ascii.EqualsIgnoreCase(
                      bytes[^YamlSyntax.NotANumberSuffixUtf8.Length..],
                      YamlSyntax.NotANumberSuffixUtf8
                  )
                    ? double.NaN
                    : double.Parse(bytes, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        var bytes = GetUtf8TextForToken(token);

        if (bytes.Contains(YamlSyntax.Underscore))
        {
            return RemoveUnderscores(bytes);

            static ReadOnlySpan<byte> RemoveUnderscores(ReadOnlySpan<byte> bytes)
            {
                // TODO: eliminate array allocation (if possible).
                var cleaned = new byte[bytes.Length - bytes.Count(YamlSyntax.Underscore)];
                var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

                Debug.Assert(valid && length == cleaned.Length);

                return cleaned;
            }
        }
        else
        {
            return bytes;
        }
    }

    // Reads a hexadecimal ("0x1F"), octal ("0o14") or binary ("0b101") integer.
    // A text without digits, with a digit outside the base, or too large for 64 bits is not one.
    private static bool TryParsePrefixedInteger(ReadOnlySpan<byte> text, out ulong value)
    {
        value = 0;

        int shift;
        ReadOnlySpan<byte> digits;
        if (text.StartsWith(YamlSyntax.BinaryPrefixUtf8))
        {
            shift = 1;
            digits = text[YamlSyntax.BinaryPrefixUtf8.Length..];
        }
        else if (text.StartsWith(YamlSyntax.HexPrefixUtf8))
        {
            shift = 4;
            digits = text[YamlSyntax.HexPrefixUtf8.Length..];
        }
        else if (text.StartsWith(YamlSyntax.OctalPrefixUtf8))
        {
            shift = 3;
            digits = text[YamlSyntax.OctalPrefixUtf8.Length..];
        }
        else
        {
            return false;
        }

        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (var character in digits)
        {
            int digit;
            if (YamlSyntax.IsAsciiDigit(character))
            {
                digit = character - YamlSyntax.DigitZero;
            }
            else if (YamlSyntax.IsAsciiHexDigit(character))
            {
                digit =
                    character <= YamlSyntax.HexDigitUpperLast
                        ? character - YamlSyntax.HexDigitUpperFirst + YamlSyntax.HexLetterDigitValue
                        : character
                          - YamlSyntax.HexDigitLowerFirst
                          + YamlSyntax.HexLetterDigitValue;
            }
            else
            {
                return false;
            }

            // These bases are powers of two, so shifting preserves the overflow check without division.
            if (digit >= (1 << shift) || value > (ulong.MaxValue >> shift))
            {
                return false;
            }

            value = (value << shift) + (uint)digit;
        }

        return true;
    }

    // The single source of truth for "does this plain text read as a number?".
    // Shared by parsing and by YamlStringHelper (which must quote strings that would otherwise be read back as numbers).
    internal static bool TryGetNumberVariant(ReadOnlySpan<byte> text, out DTokenVariant variant)
    {
        variant = default;

        // Ordinary strings cannot be numbers, so avoid several numeric parsers on each YAML key and value.
        // This also keeps Infinity and NaN as strings: YAML accepts only their dot-prefixed spellings.
        if (text.IsEmpty || !StartsLikeDecimalNumber(text))
        {
            return false;
        }

        if (text.Contains(YamlSyntax.Underscore))
        {
            return TryGetNumberVariantWithDigitSeparators(text, out variant);
        }

        if (YamlSyntax.IsSpecialFloatScalar(text))
        {
            variant = DTokenVariant.Float;

            return true;
        }

        if (
            text.Length > 2
            && text[0] == YamlSyntax.DigitZero
            && TryParsePrefixedInteger(text, out var prefixed)
        )
        {
            variant =
                prefixed <= long.MaxValue ? DTokenVariant.Integer : DTokenVariant.IntegerUnsigned;

            return true;
        }

        if (
            Utf8Parser.TryParse(text, out long integer, out var consumed)
            && consumed == text.Length
        )
        {
            variant = DTokenVariant.Integer;

            return true;
        }

        if (Utf8Parser.TryParse(text, out ulong unsigned, out consumed) && consumed == text.Length)
        {
            variant = DTokenVariant.IntegerUnsigned;

            return true;
        }

        // BigInteger requires UTF-16. A digit-prefixed string such as "30s" cannot be an integer,
        // so exclude non-digits before allocating its UTF-16 copy.
        var digitStart = text[0] is YamlSyntax.Plus or YamlSyntax.Dash ? 1 : 0;
        if (
            !text[digitStart..].ContainsAnyExceptInRange(YamlSyntax.DigitZero, YamlSyntax.DigitNine)
            && BigInteger.TryParse(
                Encoding.UTF8.GetString(text),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            )
        )
        {
            variant = DTokenVariant.BigNumberInteger;

            return true;
        }

        if (Utf8Parser.TryParse(text, out double floating, out consumed) && consumed == text.Length)
        {
            variant = DTokenVariant.Float;

            return true;
        }

        if (
            // This fallback only accepts exponent forms; check that before the more expensive parser.
            text.ContainsAny(YamlSyntax.Exponent)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        )
        {
            variant = DTokenVariant.BigNumberFloat;

            return true;
        }

        return false;
    }

    private static bool StartsLikeDecimalNumber(ReadOnlySpan<byte> text)
    {
        var index = text[0] is YamlSyntax.Plus or YamlSyntax.Dash ? 1 : 0;

        return index < text.Length
               && (YamlSyntax.IsAsciiDigit(text[index]) || text[index] == YamlSyntax.Dot);
    }

    // YAML 1.1 allows underscores between the digits of a number ("1_000", "0x1_0", "1_000.5").
    private static bool TryGetNumberVariantWithDigitSeparators(
        ReadOnlySpan<byte> text,
        out DTokenVariant variant
    )
    {
        int size = text.Length;
        var cleaned = size <= StackLimit ? stackalloc byte[size] : default;

        using var _ = ImmediateBuffer<byte>.Using(size, ref cleaned);

        variant = default;

        return TryRemoveDigitSeparators(text, cleaned, out var length)
               && TryGetNumberVariant(cleaned[..length], out variant);
    }

    // Copies the text without its underscores.
    // An underscore is only allowed between two digits (hexadecimal digits in a hexadecimal number),
    // and not in the exponent of a float: "_1", "1_", "1__0", "0x_1", "1_.5" and "1e1_0" are not numbers.
    private static bool TryRemoveDigitSeparators(
        ReadOnlySpan<byte> text,
        Span<byte> destination,
        out int length
    )
    {
        length = 0;

        var hexadecimal = text.StartsWith(YamlSyntax.HexPrefixUtf8);
        var exponent = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == YamlSyntax.Underscore)
            {
                if (
                    exponent
                    || index == 0
                    || index + 1 == text.Length
                    || !YamlSyntax.IsDigit(text[index + 1], hexadecimal)
                    || !YamlSyntax.IsDigit(text[index - 1], hexadecimal)
                )
                {
                    return false;
                }

                continue;
            }

            if (!hexadecimal && current is YamlSyntax.ExponentLower or YamlSyntax.ExponentUpper)
            {
                exponent = true;
            }

            destination[length++] = current;
        }

        return true;
    }

    private bool TryParseNumber(ParsedScalar scalar, out uint tokenId)
    {
        tokenId = 0;

        if (
            !TryGetNumberVariant(
                _utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start),
                out var variant
            )
        )
        {
            return false;
        }

        tokenId = CreateTokenForParsedScalar(variant, scalar);

        return true;
    }

    private static void WriteNumber(
        Utf8TextWriter writer,
        in DataReader reader,
        uint tokenId,
        DToken token
    )
    {
        if (token.Kind == DTokenKind.Integer)
        {
            if (token.Variant == DTokenVariant.IntegerUnsigned)
            {
                writer.WriteUInt64(reader.ReadUInt64(tokenId));
            }
            else
            {
                writer.WriteInt64(reader.ReadInt64(tokenId));
            }
        }
        else if (token.Kind == DTokenKind.BigNumber)
        {
            writer.WriteString(reader.ReadBigNumber(tokenId));
        }
        else
        {
            // InlineFloat has no FloatKind; only Float tokens carry a decimal subkind.
            if (token.Kind == DTokenKind.Float && token.FloatKind == FloatKind.Decimal)
            {
                // Keep decimal values exact.
                // Converting a large JSON integer to double changes its decimal digits.
                writer.WriteDecimal(reader.ReadDecimal(tokenId));

                return;
            }

            var value = reader.ReadDouble(tokenId);
            if (!TryWriteSpecialFloat(writer, value))
            {
                writer.WriteDouble(value);
            }
        }
    }

    internal static bool TryWriteSpecialFloat(Utf8TextWriter writer, double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            writer.WriteString(YamlSyntax.InfinityUtf8);
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteString(YamlSyntax.NegativeInfinityUtf8);
        }
        else if (double.IsNaN(value))
        {
            writer.WriteString(YamlSyntax.NotANumberUtf8);
        }
        else if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0)
        {
            writer.WriteString(YamlSyntax.NegativeZeroUtf8);
        }
        else
        {
            return false;
        }

        return true;
    }
}