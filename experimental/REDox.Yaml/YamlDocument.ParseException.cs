// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private DocumentParseException Error(ParseException.ErrorCode code)
    {
        // The position is a byte offset in the UTF-8 text, which is what the exception reports.
        return new ParseException(_position, code);
    }

    private sealed class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            InvalidFormat,
            InvalidString,
            MaxDepthExceeded,
            UnsupportedFeature,
        }

        public ParseException(int position, ErrorCode code)
            : base(nameof(YamlDocument), position, code.ToString()) { }
    }
}
