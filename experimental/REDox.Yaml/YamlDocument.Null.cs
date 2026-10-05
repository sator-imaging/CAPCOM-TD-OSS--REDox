// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private uint ParseNull() => AllocToken(DToken.Make(DTokenVariant.Null, 0));

    private bool TryParseNull(ParsedScalar scalar, out uint tokenId)
    {
        if (YamlSyntax.IsNullScalar(_scalars.GetWrittenSpanFromOffset(scalar.Start)))
        {
            _scalars.TruncateTo(scalar.Start);

            tokenId = ParseNull();

            return true;
        }

        tokenId = 0;

        return false;
    }
}
