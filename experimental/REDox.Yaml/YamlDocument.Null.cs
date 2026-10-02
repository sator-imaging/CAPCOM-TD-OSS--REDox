// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private uint ParseNull() => AllocToken(DToken.Make(DTokenVariant.Null, 0));

    internal static bool IsNullText(string text) =>
        text.Length == 0 || text is "null" or "Null" or "NULL" or "~";

    private bool TryParseNull(string text, out uint tokenId)
    {
        if (IsNullText(text))
        {
            tokenId = ParseNull();
            return true;
        }

        tokenId = 0;
        return false;
    }

    private static void WriteNull(Utf8TextWriter writer) => writer.WriteNull();
}
