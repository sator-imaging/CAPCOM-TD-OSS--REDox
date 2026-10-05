// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

// NOTE: Compare resolved scalar values, excluding the document, ID, trivia and original spelling in DElement.
//
// YAML 1.2.2 section 3.2.1.3 compares scalar keys by tag and canonical value:
//   https://yaml.org/spec/1.2.2/#3213-node-comparison
// The parser currently rejects collection keys; YAML itself permits them. If that restriction is lifted,
// add structural comparison here. Scalar comparison is sufficient today, so DeepEquals is unnecessary.
internal readonly struct YamlMapKeyContentComparer : IEqualityComparer<DElement>
{
    public bool Equals(DElement left, DElement right)
    {
        var valueKind = GetScalarValueKind(left.Token);

        return valueKind == GetScalarValueKind(right.Token)
            && valueKind switch
            {
                DTokenKind.Null => true,
                DTokenKind.Boolean => left.GetBoolean() == right.GetBoolean(),
                DTokenKind.Integer => ReadIntegerValue(left) == ReadIntegerValue(right),
                DTokenKind.Float => left.GetDouble().Equals(right.GetDouble()),
                DTokenKind.Timestamp => left.GetDateTimeOffset().Equals(right.GetDateTimeOffset()),
                DTokenKind.String => new DataReader(left)
                    .ReadUtf8String(left.Id)
                    .SequenceEqual(new DataReader(right).ReadUtf8String(right.Id)),
                _ => throw YamlDocument.GenericError(
                    YamlDocument.ParseException.ErrorCode.UnsupportedFeature,
                    "YAML key comparison requires a supported scalar value."
                ),
            };
    }

    public int GetHashCode(DElement element)
    {
        // TODO: Hash only the value kind without decoding or allocating.
        //       Same-kind collisions are intentional, HashSet resolves them through Equals,
        //       and equivalent spellings must share a hash.
        return (int)GetScalarValueKind(element.Token);
    }

    private static DTokenKind GetScalarValueKind(DToken token)
    {
        if (token.IsContainer)
        {
            YamlDocument.ThrowGenericError(
                YamlDocument.ParseException.ErrorCode.UnsupportedFeature,
                "YAML collection keys require structural comparison."
            );
        }

        // Storage width does not change the YAML tag: a big integer is still an integer.
        return token.Kind == DTokenKind.BigNumber
            ? token.BigNumberKind == BigNumberKind.Integer
                ? DTokenKind.Integer
                : DTokenKind.Float
            : token.Kind;
    }

    private static BigInteger ReadIntegerValue(DElement element)
    {
        if (element.Token.Kind == DTokenKind.BigNumber)
        {
            return BigInteger.Parse(
                Encoding.UTF8.GetString(element.GetBigNumber()),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture
            );
        }

        // Preserve unsigned values above Int64.MaxValue rather than wrapping them into negative integers.
        return element.Token.Variant == DTokenVariant.IntegerUnsigned
            ? new BigInteger(element.GetUInt64())
            : new BigInteger(element.GetInt64());
    }
}
