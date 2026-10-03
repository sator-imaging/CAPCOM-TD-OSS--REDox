// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    // Reads a hexadecimal ("0x1F") or octal ("0o14") integer of the core schema. A text without digits, with a digit
    // outside the base, or too large for 64 bits is not one.
    private static bool TryParsePrefixedInteger(ReadOnlySpan<byte> text, out ulong value)
    {
        value = 0;

        int radix;
        ReadOnlySpan<byte> digits;
        if (text.StartsWith(YamlSyntax.HexPrefixUtf8))
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
            return false;

        foreach (var character in digits)
        {
            int digit;
            if (char.IsAsciiDigit((char)character))
                digit = character - YamlSyntax.DigitZero;
            else if (char.IsAsciiHexDigit((char)character))
                digit = char.ToLowerInvariant((char)character) - 'a' + 10;
            else
                return false;

            if (digit >= radix || value > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
                return false;

            value = value * (ulong)radix + (ulong)digit;
        }

        return true;
    }

    // The single source of truth for "does this plain text read as a number", shared by parsing and by
    // YamlStringHelper (which must quote strings that would otherwise be read back as numbers).
    internal static bool TryGetNumberVariant(string text, out DTokenVariant variant)
    {
        return TryGetNumberVariant(System.Text.Encoding.UTF8.GetBytes(text), out variant);
    }

    internal static bool TryGetNumberVariant(ReadOnlySpan<byte> text, out DTokenVariant variant)
    {
        variant = default;
        if (text.IsEmpty)
            return false;

        if (YamlSyntax.IsOneOf(text, YamlSyntax.SpecialFloatWordsUtf8))
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

        // Only an integer that does not fit 64 bits gets here, so the string for BigInteger is rarely made.
        if (
            BigInteger.TryParse(
                System.Text.Encoding.UTF8.GetString(text),
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
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && text.ContainsAny(YamlSyntax.Exponent)
        )
        {
            variant = DTokenVariant.BigNumberFloat;
            return true;
        }

        return false;
    }

    private bool TryParseNumber(ParsedScalar scalar, out uint tokenId)
    {
        tokenId = 0;
        if (!TryGetNumberVariant(ScalarText(scalar), out var variant))
            return false;

        tokenId = CommitScalar(variant, scalar);
        return true;
    }

    protected override long DecodeInteger(DToken token)
    {
        var bytes = ScalarBytes(token);
        if (
            Utf8Parser.TryParse(bytes, out long value, out var consumed)
            && consumed == bytes.Length
        )
        {
            return value;
        }

        if (
            Utf8Parser.TryParse(bytes, out ulong unsigned, out consumed)
            && consumed == bytes.Length
        )
        {
            return unchecked((long)unsigned);
        }

        if (TryParsePrefixedInteger(bytes, out var prefixed))
        {
            return unchecked((long)prefixed);
        }

        throw new InvalidCastException();
    }

    protected override double DecodeFloat(DToken token)
    {
        var text = System.Text.Encoding.UTF8.GetString(ScalarBytes(token));
        if (
            text.StartsWith(YamlSyntax.NegativeInfinityPrefix, StringComparison.Ordinal)
            && text.EndsWith(YamlSyntax.InfinitySuffix, StringComparison.OrdinalIgnoreCase)
        )
            return double.NegativeInfinity;
        if (text.EndsWith(YamlSyntax.PositiveInfinitySuffix, StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;
        if (text.EndsWith(YamlSyntax.NotANumberSuffix, StringComparison.OrdinalIgnoreCase))
            return double.NaN;
        return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        return ScalarBytes(token);
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
                writer.WriteUInt64(reader.ReadUInt64(tokenId));
            else
                writer.WriteInt64(reader.ReadInt64(tokenId));
        }
        else
        {
            // InlineFloat has no FloatKind; only Float tokens carry a decimal subkind.
            if (token.Kind == DTokenKind.Float && token.FloatKind == FloatKind.Decimal)
            {
                // Keep decimal values exact; converting a large JSON integer to double changes its decimal digits.
                writer.WriteDecimal(reader.ReadDecimal(tokenId));
                return;
            }

            var value = reader.ReadDouble(tokenId);
            if (double.IsPositiveInfinity(value))
                writer.WriteString(YamlSyntax.InfinityUtf8);
            else if (double.IsNegativeInfinity(value))
                writer.WriteString(YamlSyntax.NegativeInfinityUtf8);
            else if (double.IsNaN(value))
                writer.WriteString(YamlSyntax.NotANumberUtf8);
            else if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0)
                writer.WriteString(YamlSyntax.NegativeZeroUtf8);
            else
                writer.WriteDouble(value);
        }
    }
}
