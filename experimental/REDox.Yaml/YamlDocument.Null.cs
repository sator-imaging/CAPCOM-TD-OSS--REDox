// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private uint ParseNull() => AllocToken(DToken.Make(DTokenVariant.Null, 0));

    internal static bool IsNullText(string text) =>
        text.Length == 0 || YamlSyntax.NullWords.Contains(text);

    private static bool IsNullText(ReadOnlySpan<byte> text) =>
        text.IsEmpty || YamlSyntax.IsOneOf(text, YamlSyntax.NullWordsUtf8);

    // A null needs no text of its own, so the bytes of the scalar are dropped.
    private bool TryParseNull(ParsedScalar scalar, out uint tokenId)
    {
        if (IsNullText(ScalarText(scalar)))
        {
            DiscardScalar(scalar);
            tokenId = ParseNull();
            return true;
        }

        tokenId = 0;
        return false;
    }

    private static void WriteNull(Utf8TextWriter writer) => writer.WriteNull();
}
