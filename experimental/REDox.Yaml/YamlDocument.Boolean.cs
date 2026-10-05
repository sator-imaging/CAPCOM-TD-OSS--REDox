// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    // A boolean needs no text of its own, so the bytes of the scalar are dropped.
    private bool TryParseBoolean(ParsedScalar scalar, out uint tokenId)
    {
        var text = _scalars.GetWrittenSpanFromOffset(scalar.Start);
        if (YamlSyntax.TryParseBooleanScalar(text, out var value))
        {
            _scalars.TruncateTo(scalar.Start);

            tokenId = AllocToken(
                DToken.Make(value ? DTokenVariant.BooleanTrue : DTokenVariant.BooleanFalse, 0)
            );

            return true;
        }

        tokenId = 0;

        return false;
    }
}
