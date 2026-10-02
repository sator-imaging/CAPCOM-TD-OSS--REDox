// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private enum ParseErrorCode
    {
        InvalidFormat,
        InvalidString,
        MaxDepthExceeded,
        UnsupportedFeature
    }

    private DocumentParseException Error(ParseErrorCode code)
    {
        var position = System.Text.Encoding.UTF8.GetByteCount(_text.AsSpan(0, _position));
        return new ParseException(position, code);
    }

    private sealed class ParseException : DocumentParseException
    {
        public ParseException(int position, ParseErrorCode code)
            : base(nameof(YamlDocument), position, code.ToString())
        {
        }
    }
}
