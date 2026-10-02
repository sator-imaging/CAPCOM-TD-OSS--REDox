// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    internal static bool IsTrueText(string text) => text is "true" or "True" or "TRUE";

    internal static bool IsFalseText(string text) => text is "false" or "False" or "FALSE";

    private bool TryParseBoolean(string text, out uint tokenId)
    {
        if (IsTrueText(text))
        {
            tokenId = AllocToken(DToken.Make(DTokenVariant.BooleanTrue, 0));
            return true;
        }

        if (IsFalseText(text))
        {
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
