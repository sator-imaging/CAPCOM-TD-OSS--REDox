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

            using var _ = ImmediateBuffer<byte>.Using(size, ref cleaned);

            var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

            Debug.Assert(valid);

            return DecodeIntegerScalarBytes(cleaned[..length]);
        }

        return DecodeIntegerScalarBytes(bytes);
    }

    private long DecodeIntegerScalarBytes(ReadOnlySpan<byte> bytes)
    {
        // Prefixed integers cannot be consumed in full by the decimal parsers, so decode them first.
        return bytes.Length > 2
               && bytes[0] == YamlSyntax.DigitZero
               && (
                   bytes[1]
                       is YamlSyntax.BinaryPrefixLetter
                       or YamlSyntax.HexPrefixLetter
                       or YamlSyntax.OctalPrefixLetter
               )
               && TryParsePrefixedInteger(bytes, out var prefixedValue)
            ? unchecked((long)prefixedValue)
            : Utf8Parser.TryParse(bytes, out long value, out var consumed)
              && consumed == bytes.Length
                ? value
                : Utf8Parser.TryParse(bytes, out ulong unsigned, out consumed)
                  && consumed == bytes.Length
                    ? unchecked((long)unsigned)
                    : TryParsePrefixedInteger(bytes, out var prefixed)
                        ? unchecked((long)prefixed)
                        : throw CreateParseError(
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

            using var _ = ImmediateBuffer<byte>.Using(size, ref cleaned);

            var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

            Debug.Assert(valid);

            return DecodeFloatScalarBytes(cleaned[..length]);
        }

        return DecodeFloatScalarBytes(bytes);
    }

    private static double DecodeFloatScalarBytes(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
        {
            // Ordinary decimals end in a digit, so their last byte excludes every YAML special float.
            switch ((byte)(bytes[^1] | YamlSyntax.AsciiLowercaseMask))
            {
                case YamlSyntax.InfinityLastLetter:
                    if (
                        bytes.StartsWith(YamlSyntax.NegativeInfinityPrefixUtf8)
                        && bytes.Length >= YamlSyntax.InfinitySuffixUtf8.Length
                        && Ascii.EqualsIgnoreCase(
                            bytes[^YamlSyntax.InfinitySuffixUtf8.Length..],
                            YamlSyntax.InfinitySuffixUtf8
                        )
                    )
                    {
                        return double.NegativeInfinity;
                    }

                    // Decode against the same spellings used for output so parsing and writing cannot drift.
                    if (
                        bytes.Length >= YamlSyntax.InfinityUtf8.Length
                        && Ascii.EqualsIgnoreCase(
                            bytes[^YamlSyntax.InfinityUtf8.Length..],
                            YamlSyntax.InfinityUtf8
                        )
                    )
                    {
                        return double.PositiveInfinity;
                    }

                    break;

                case YamlSyntax.NotANumberLastLetter:
                    if (
                        bytes.Length >= YamlSyntax.NotANumberUtf8.Length
                        && Ascii.EqualsIgnoreCase(
                            bytes[^YamlSyntax.NotANumberUtf8.Length..],
                            YamlSyntax.NotANumberUtf8
                        )
                    )
                    {
                        return double.NaN;
                    }

                    break;
            }
        }

        return double.Parse(bytes, CultureInfo.InvariantCulture);
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
                var cleaned = new byte[bytes.Length - 1];
                var valid = TryRemoveDigitSeparators(bytes, cleaned, out var length);

                Debug.Assert(valid && length <= cleaned.Length);

                return cleaned.AsSpan(0, length);
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

        if (text.Length < 3 || text[0] != YamlSyntax.DigitZero)
        {
            return false;
        }

        // The three supported prefixes differ only in their second byte, so one lookup replaces three comparisons.
        var shift = text[1] switch
        {
            YamlSyntax.BinaryPrefixLetter => 1,
            YamlSyntax.HexPrefixLetter => 4,
            YamlSyntax.OctalPrefixLetter => 3,
            _ => 0,
        };

        if (shift == 0)
        {
            return false;
        }

        // These bases are powers of two. Their digit and overflow limits stay fixed for every digit.
        var radix = 1 << shift;
        var maxValueBeforeShift = ulong.MaxValue >> shift;
        foreach (var character in text[2..])
        {
            int digit;
            if (YamlSyntax.IsAsciiDigit(character))
            {
                digit = character - YamlSyntax.DigitZero;
            }
            // The decimal branch already handled digits; decode hex letters without a second digit-set lookup.
            else if (
                character is >= YamlSyntax.HexDigitUpperFirst and <= YamlSyntax.HexDigitUpperLast
            )
            {
                digit = character - YamlSyntax.HexDigitUpperFirst + YamlSyntax.HexLetterDigitValue;
            }
            else if (
                character is >= YamlSyntax.HexDigitLowerFirst and <= YamlSyntax.HexDigitLowerLast
            )
            {
                digit = character - YamlSyntax.HexDigitLowerFirst + YamlSyntax.HexLetterDigitValue;
            }
            else
            {
                return false;
            }

            if (digit >= radix || value > maxValueBeforeShift)
            {
                return false;
            }

            value = (value << shift) + (uint)digit;
        }

        return true;
    }

    // The single source of truth for "does this plain text read as a number?".
    // Shared by parsing and by YamlStringHelper (which must quote strings that would otherwise be read back as numbers).
    internal static bool TryParseNumberVariant(ReadOnlySpan<byte> text, out DTokenVariant variant)
    {
        variant = default;

        // Ordinary strings cannot be numbers, so avoid several numeric parsers on each YAML key and value.
        // This also keeps Infinity and NaN as strings: YAML accepts only their dot-prefixed spellings.
        if (text.IsEmpty || !StartsLikeDecimalNumber(text))
        {
            return false;
        }

        if (YamlSyntax.IsSpecialFloatScalar(text))
        {
            variant = DTokenVariant.Float;

            return true;
        }

        var hasPrefixedIntegerSpelling =
            text.Length > 2
            && text[0] == YamlSyntax.DigitZero
            && (
                text[1]
                    is YamlSyntax.BinaryPrefixLetter
                    or YamlSyntax.HexPrefixLetter
                    or YamlSyntax.OctalPrefixLetter
            );

        if (hasPrefixedIntegerSpelling && TryParsePrefixedInteger(text, out var prefixed))
        {
            variant =
                prefixed <= long.MaxValue ? DTokenVariant.Integer : DTokenVariant.IntegerUnsigned;

            return true;
        }

        var parsedSignedInteger = Utf8Parser.TryParse(text, out long integer, out var consumed);
        if (parsedSignedInteger && consumed == text.Length)
        {
            variant = DTokenVariant.Integer;

            return true;
        }

        // Once the integer prefix ends at '_', no remaining parser can consume the original text.
        // Check the separator rules directly instead of repeating those failed parses.
        if (
            parsedSignedInteger
            && consumed < text.Length
            && text[consumed] == YamlSyntax.Underscore
        )
        {
            return TryParseNumberVariantWithDigitSeparators(text, out variant);
        }

        // An integer parser stopping at a decimal point or exponent proves that neither remaining
        // integer parser can consume the whole scalar; common decimals can go straight to float parsing.
        var hasFloatSuffixAfterIntegerPrefix =
            parsedSignedInteger
            && consumed < text.Length
            && text[consumed]
                is YamlSyntax.Dot
                or YamlSyntax.ExponentLower
                or YamlSyntax.ExponentUpper;

        // Other suffixes cannot become numbers. Keep prefixed forms here because removing '_'
        // can still turn a spelling such as "0x1_0" into a valid integer.
        if (
            parsedSignedInteger
            && consumed < text.Length
            && !hasFloatSuffixAfterIntegerPrefix
            && !hasPrefixedIntegerSpelling
        )
        {
            return false;
        }

        if (
            !hasFloatSuffixAfterIntegerPrefix
            && Utf8Parser.TryParse(text, out ulong unsigned, out consumed)
            && consumed == text.Length
        )
        {
            variant = DTokenVariant.IntegerUnsigned;

            return true;
        }

        var digitStart = text[0] is YamlSyntax.Plus or YamlSyntax.Dash ? 1 : 0;
        if (
            !hasFloatSuffixAfterIntegerPrefix
            && !text[digitStart..]
                .ContainsAnyExceptInRange(YamlSyntax.DigitZero, YamlSyntax.DigitNine)
        )
        {
            var characters = text.Length <= StackLimit ? stackalloc char[text.Length] : default;

            using var __ = ImmediateBuffer<char>.Using(text.Length, ref characters);

            for (int i = 0; i < text.Length; i++)
            {
                characters[i] = (char)text[i];
            }

            if (
                BigInteger.TryParse(
                    characters,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _
                )
            )
            {
                variant = DTokenVariant.BigNumberInteger;

                return true;
            }
        }

        if (Utf8Parser.TryParse(text, out double floating, out consumed) && consumed == text.Length)
        {
            variant = DTokenVariant.Float;

            return true;
        }

        if (text.Contains(YamlSyntax.Underscore))
        {
            return TryParseNumberVariantWithDigitSeparators(text, out variant);
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
    private static bool TryParseNumberVariantWithDigitSeparators(
        ReadOnlySpan<byte> text,
        out DTokenVariant variant
    )
    {
        // Both callers require an underscore, so removing separators needs at most one fewer byte.
        int size = text.Length - 1;
        var cleaned = size <= StackLimit ? stackalloc byte[size] : default;

        using var _ = ImmediateBuffer<byte>.Using(size, ref cleaned);

        variant = default;

        return TryRemoveDigitSeparators(text, cleaned, out var length)
               && TryParseNumberVariant(cleaned[..length], out variant);
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

    private bool TryCreateNumberToken(ParsedScalarIntermediate scalar, out uint tokenId)
    {
        tokenId = 0;

        if (
            !TryParseNumberVariant(
                _utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start),
                out var variant
            )
        )
        {
            return false;
        }

        tokenId = CreateTokenFromParsedScalar(variant, scalar);

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
        // Ordinary finite values need no special spelling.
        // Negative zero is the exception because writing it as "-0" would make YAML read it back as an integer.
        if (double.IsFinite(value))
        {
            if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0)
            {
                writer.WriteString(YamlSyntax.NegativeZeroUtf8);

                return true;
            }

            return false;
        }

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
        else
        {
            return false;
        }

        return true;
    }
}