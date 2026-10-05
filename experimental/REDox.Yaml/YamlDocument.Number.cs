// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

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
    // Reads a hexadecimal ("0x1F"), octal ("0o14") or binary ("0b101") integer.
    // A text without digits, with a digit outside the base, or too large for 64 bits is not one.
    private static bool TryParsePrefixedInteger(ReadOnlySpan<byte> text, out ulong value)
    {
        value = 0;

        int radix;
        ReadOnlySpan<byte> digits;
        if (text.StartsWith(YamlSyntax.BinaryPrefixUtf8))
        {
            radix = 2;
            digits = text[YamlSyntax.BinaryPrefixUtf8.Length..];
        }
        else if (text.StartsWith(YamlSyntax.HexPrefixUtf8))
        {
            radix = 16;
            digits = text[YamlSyntax.HexPrefixUtf8.Length..];
        }
        else if (text.StartsWith(YamlSyntax.OctalPrefixUtf8))
        {
            radix = 8;
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

            if (digit >= radix || value > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
            {
                return false;
            }

            value = value * (ulong)radix + (ulong)digit;
        }

        return true;
    }

    // The single source of truth for "does this plain text read as a number".
    // Shared by parsing and by YamlStringHelper (which must quote strings that would otherwise be read back as numbers).
    internal static bool TryGetNumberVariant(ReadOnlySpan<byte> text, out DTokenVariant variant)
    {
        variant = default;

        if (text.IsEmpty)
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

        if (TryParsePrefixedInteger(text, out var prefixed))
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

        // BigInteger requires UTF-16; keep the string conversion local to this check.
        if (
            BigInteger.TryParse(
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

        // The parsers below also read the words "Infinity" and "NaN", which YAML does not: it has ".inf" and ".nan".
        if (!StartsLikeDecimalNumber(text))
        {
            return false;
        }

        if (Utf8Parser.TryParse(text, out double floating, out consumed) && consumed == text.Length)
        {
            variant = DTokenVariant.Float;

            return true;
        }

        if (
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && text.ContainsAny(YamlSyntax.Exponent)
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

    // The longest text that is checked on the stack.
    // A longer one, which a number with underscores hardly is, gets an array.
    private const int StackLimit = 128;

    // YAML 1.1 allows underscores between the digits of a number ("1_000", "0x1_0", "1_000.5"). They are for reading
    // only: the text is a number when it is one without them, and the value does not have them.
    private static bool TryGetNumberVariantWithDigitSeparators(
        ReadOnlySpan<byte> text,
        out DTokenVariant variant
    )
    {
        variant = default;

        var cleaned =
            text.Length <= StackLimit ? stackalloc byte[StackLimit] : new byte[text.Length];

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
                    || !IsDigit(text[index + 1], hexadecimal)
                    || !IsDigit(text[index - 1], hexadecimal)
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

    private static bool IsDigit(byte value, bool hexadecimal) =>
        hexadecimal ? YamlSyntax.IsAsciiHexDigit(value) : YamlSyntax.IsAsciiDigit(value);

    // The text of a number that was read with underscores, without them. It was checked when it was read.
    private static byte[] RemoveDigitSeparators(ReadOnlySpan<byte> text)
    {
        var cleaned = new byte[text.Length];
        var valid = TryRemoveDigitSeparators(text, cleaned, out var length);

        Debug.Assert(valid);

        return cleaned[..length];
    }

    private bool TryParseNumber(ParsedScalar scalar, out uint tokenId)
    {
        tokenId = 0;

        if (!TryGetNumberVariant(_scalars.GetWrittenSpanFromOffset(scalar.Start), out var variant))
        {
            return false;
        }

        tokenId = CommitScalar(variant, scalar);

        return true;
    }

    /// <inheritdoc />
    protected override long DecodeInteger(DToken token)
    {
        var bytes = GetStoredScalarBytes(token);
        if (bytes.Contains(YamlSyntax.Underscore))
        {
            bytes = RemoveDigitSeparators(bytes);
        }

        return Utf8Parser.TryParse(bytes, out long value, out var consumed)
            && consumed == bytes.Length
                ? value
            : Utf8Parser.TryParse(bytes, out ulong unsigned, out consumed)
            && consumed == bytes.Length
                ? unchecked((long)unsigned)
            : TryParsePrefixedInteger(bytes, out var prefixed) ? unchecked((long)prefixed)
            : throw ParseError(
                ParseException.ErrorCode.InvalidFormat,
                "The stored integer cannot be decoded."
            );
    }

    /// <inheritdoc />
    protected override double DecodeFloat(DToken token)
    {
        var bytes = GetStoredScalarBytes(token);
        if (bytes.Contains(YamlSyntax.Underscore))
        {
            bytes = RemoveDigitSeparators(bytes);
        }

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
        var bytes = GetStoredScalarBytes(token);

        return bytes.Contains(YamlSyntax.Underscore)
            ? (ReadOnlySpan<byte>)RemoveDigitSeparators(bytes)
            : bytes;
    }

    private static void WriteNumber(Utf8TextWriter writer, in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);
        if (token.Kind == DTokenKind.BigNumber)
        {
            writer.WriteString(reader.ReadBigNumber(tokenId));

            return;
        }

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
