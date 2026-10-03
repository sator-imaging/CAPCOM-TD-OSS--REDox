// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    internal static bool IsTrueText(string text) => YamlSyntax.TrueWords.Contains(text);

    internal static bool IsFalseText(string text) => YamlSyntax.FalseWords.Contains(text);

    // A boolean needs no text of its own, so the bytes of the scalar are dropped.
    private bool TryParseBoolean(ParsedScalar scalar, out uint tokenId)
    {
        var text = ScalarText(scalar);
        if (YamlSyntax.IsOneOf(text, YamlSyntax.TrueWordsUtf8))
        {
            DiscardScalar(scalar);
            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanTrue, 0));
            return true;
        }

        if (YamlSyntax.IsOneOf(text, YamlSyntax.FalseWordsUtf8))
        {
            DiscardScalar(scalar);
            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanFalse, 0));
            return true;
        }

        tokenId = 0;
        return false;
    }

    private static void WriteBoolean(
        Utf8TextWriter writer,
        in REDox.Serialization.DataReader reader,
        uint tokenId
    ) => writer.WriteBoolean(reader.ReadBoolean(tokenId));
}
