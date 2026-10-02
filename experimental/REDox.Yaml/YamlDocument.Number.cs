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
    // The single source of truth for "does this plain text read as a number", shared by parsing and by
    // YamlStringHelper (which must quote strings that would otherwise be read back as numbers).
    internal static bool TryGetNumberVariant(string text, out DTokenVariant variant)
    {
        variant = default;
        if (text.Length == 0)
            return false;

        if (
            text
            is ".inf"
                or ".Inf"
                or ".INF"
                or "+.inf"
                or "+.Inf"
                or "+.INF"
                or "-.inf"
                or "-.Inf"
                or "-.INF"
                or ".nan"
                or ".NaN"
                or ".NAN"
        )
        {
            variant = DTokenVariant.Float;
            return true;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (
            Utf8Parser.TryParse(bytes, out long integer, out var consumed)
            && consumed == bytes.Length
        )
        {
            variant = DTokenVariant.Integer;
            return true;
        }

        if (
            Utf8Parser.TryParse(bytes, out ulong unsigned, out consumed)
            && consumed == bytes.Length
        )
        {
            variant = DTokenVariant.IntegerUnsigned;
            return true;
        }

        if (BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            variant = DTokenVariant.BigNumberInteger;
            return true;
        }

        if (
            Utf8Parser.TryParse(bytes, out double floating, out consumed)
            && consumed == bytes.Length
        )
        {
            variant = DTokenVariant.Float;
            return true;
        }

        if (
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && text.IndexOfAny('e', 'E') >= 0
        )
        {
            variant = DTokenVariant.BigNumberFloat;
            return true;
        }

        return false;
    }

    private bool TryParseNumber(string text, out uint tokenId)
    {
        tokenId = 0;
        if (!TryGetNumberVariant(text, out var variant))
            return false;

        tokenId = StoreScalar(variant, text);
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

        throw new InvalidCastException();
    }

    protected override double DecodeFloat(DToken token)
    {
        var text = System.Text.Encoding.UTF8.GetString(ScalarBytes(token));
        if (
            text.StartsWith("-.", StringComparison.Ordinal)
            && text.EndsWith("inf", StringComparison.OrdinalIgnoreCase)
        )
            return double.NegativeInfinity;
        if (text.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;
        if (text.EndsWith(".nan", StringComparison.OrdinalIgnoreCase))
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
                writer.WriteString(".inf"u8);
            else if (double.IsNegativeInfinity(value))
                writer.WriteString("-.inf"u8);
            else if (double.IsNaN(value))
                writer.WriteString(".nan"u8);
            // YAML resolves -0 as an integer, so include a decimal point to preserve floating negative zero.
            else if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0)
                writer.WriteString("-0.0"u8);
            else
                writer.WriteDouble(value);
        }
    }
}
