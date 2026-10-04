// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    // NOTE: Declare exception and its enum as nested type to align to other packages.
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

    [DoesNotReturn]
    private void ThrowParseError(ParseException.ErrorCode code) => throw ParseError(code);

    private ParseException ParseError(ParseException.ErrorCode code)
    {
        // The position is a byte offset in the UTF-8 text which is what the exception reports.
        return new ParseException(_position, code);
    }
}
