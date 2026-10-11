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
internal readonly struct MapKeyContentComparer : IEqualityComparer<DElement>
{
    public bool Equals(DElement left, DElement right)
    {
        var valueKind = GetScalarValueKind(left.Token);

        return valueKind == GetScalarValueKind(right.Token)
               && valueKind switch
               {
                   DTokenKind.Null => true,
                   DTokenKind.Boolean => left.GetBoolean() == right.GetBoolean(),
                   DTokenKind.Integer => GetComparableIntegerValue(left)
                                         == GetComparableIntegerValue(right),
                   DTokenKind.Float => left.GetDouble().Equals(right.GetDouble()),
                   DTokenKind.Timestamp => left.GetDateTimeOffset().Equals(right.GetDateTimeOffset()),
                   DTokenKind.String => new DataReader(left)
                       .ReadUtf8String(left.Id)
                       .SequenceEqual(new DataReader(right).ReadUtf8String(right.Id)),
                   _ => throw YamlDocument.CreateGenericError(
                       YamlDocument.ParseException.ErrorCode.UnsupportedFeature,
                       "YAML key comparison requires a supported scalar value."
                   ),
               };

        static BigInteger GetComparableIntegerValue(DElement element)
        {
            if (element.Token.Kind == DTokenKind.BigNumber)
            {
                var utf8 = element.GetBigNumber();

                // UTF-8 decoding produces no more UTF-16 code units than input bytes, even with replacements,
                // so the temporary span avoids both a count pass and a string allocation.
                var characters =
                    utf8.Length <= YamlDocument.StackLimit ? stackalloc char[utf8.Length] : default;

                using var _ = ImmediateBuffer<char>.Using(utf8.Length, ref characters);

                var written = Encoding.UTF8.GetChars(utf8, characters);

                return BigInteger.Parse(
                    characters[..written],
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

    public int GetHashCode(DElement element)
    {
        // TODO: Core data model doesn't have document-independent comparison feature.
        //       To address it, hash only value kind here without decoding or allocating.
        //       Same-kind collisions are intentional, HashSet resolves them through Equals.
        var valueKind = GetScalarValueKind(element.Token);
        if (valueKind == DTokenKind.String)
        {
            // Ordinary mappings have many string keys. Hashing their UTF-8 bytes avoids same-kind collisions.
            var utf8 = new DataReader(element).ReadUtf8String(element.Id);

            return ComputeUtf8Hash(valueKind, utf8);

            static int ComputeUtf8Hash(DTokenKind valueKind, ReadOnlySpan<byte> utf8)
            {
                var hash = new HashCode();

                hash.Add(valueKind);

                if (!utf8.IsEmpty)
                {
                    // perf: take middle, first and last latter only for hashing.
                    // TODO: profiling
                    int len = utf8.Length;
                    if (len >= 6)
                    {
                        // 6: 0
                        hash.Add(utf8[0]);
                    }

                    if (len >= 5)
                    {
                        // 6: 0, 1..4
                        // 5: 0..3
                        int offset = (len - 4) >> 1;

                        hash.AddBytes(utf8[offset..(offset + 4)]);
                    }

                    // 6: 0, 1..4, 5
                    // 5: 0..3, 4
                    // < 5: last letter only
                    //      Xpos, Yrot, Zscl are rare.
                    //      posX, rotY, sclZ are usual. last letter matters
                    hash.Add(utf8[len - 1]);
                }

                return hash.ToHashCode();
            }
        }

        // Hash these decoded values as Equals compares them, so distinct scalar keys avoid kind-only collisions.
        // Integers retain a kind-only hash until signed, unsigned and big-number storage share one canonical hash.
        return valueKind switch
        {
            DTokenKind.Boolean => HashCode.Combine(valueKind, element.GetBoolean()),
            DTokenKind.Float => HashCode.Combine(valueKind, element.GetDouble()),
            DTokenKind.Timestamp => HashCode.Combine(valueKind, element.GetDateTimeOffset()),
            _ => (int)valueKind,
        };
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
}