// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.RegularExpressions;

namespace REDox.Yaml;

static partial class YamlStringHelper
{
    // A plain scalar must not start with a YAML indicator character.
    private const string IndicatorStartCharacters = "-?:,[]{}#&*!|>'\"%@`";

    // Numbers as the YAML 1.2 core schema reads them (decimal integers and floats). Other parsers resolve these
    // as numbers even where this library's parser would not, e.g. ".5" and "5.".
    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex CoreNumberPattern();

    /// <summary>
    /// Decides whether a string can be written without quotes and still be read back, unchanged, as a string.
    /// </summary>
    /// <remarks>
    /// The rules are deliberately conservative: when in doubt the string is quoted. Besides this library's own
    /// parser, other YAML parsers must read the text as the same string, so words that YAML 1.1 reads as
    /// booleans and anything that looks like a number or a date are quoted too.
    /// </remarks>
    /// <param name="value">The string to write.</param>
    /// <param name="isKey">Whether the string is a mapping key, which the parser scans more strictly than a value.</param>
    public static bool CanWritePlain(string value, bool isKey)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // Strings made only of '-', digits and '.' that do not read as another type (such as the version "1.2.3")
        // are written plain. They are checked first because the rules below quote anything that starts with an
        // indicator, a digit or a dot.
        if (IsDashDigitDot(value))
        {
            return !ReadsAsAnotherType(value);
        }

        var first = value[0];

        if (first is ' ' or '\t' || value[^1] is ' ' or '\t')
        {
            return false;
        }

        // '.' and '+' start numbers such as ".5" and "+1"; a leading digit may be a number or a date.
        if (
            IndicatorStartCharacters.Contains(first)
            || first is '.' or '+'
            || char.IsAsciiDigit(first)
        )
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsSurrogate(c))
            {
                // Only a well-formed surrogate pair is printable.
                if (
                    !char.IsHighSurrogate(c)
                    || i + 1 >= value.Length
                    || !char.IsLowSurrogate(value[i + 1])
                )
                {
                    return false;
                }

                i++;
                continue;
            }

            if (IsNonPrintable(c))
            {
                return false;
            }

            if (c == ':')
            {
                // The key scanner stops at any ':'; a value's ':' only ends it before a space or a flow delimiter.
                if (isKey || i + 1 >= value.Length || value[i + 1] is ' ' or ',' or ']' or '}')
                {
                    return false;
                }
            }
            else if (c == '#')
            {
                // " #" starts a comment. A key is also cut at any '#' when the parser looks for "key:".
                if (isKey || value[i - 1] == ' ')
                {
                    return false;
                }
            }
            else if (isKey && c is '\'' or '"')
            {
                // A quote inside a key makes the parser treat the rest of the line as a quoted string.
                return false;
            }
        }

        return !ReadsAsAnotherType(value);
    }

    // Line breaks, tabs and other control characters need an escape or a block scalar, and the characters
    // below are not printable in YAML (DEL, C1 controls, line/paragraph separators, BOM and non-characters).
    // Code points are written as numbers on purpose, so that no invisible character appears in the source.
    private static bool IsNonPrintable(char c)
    {
        return c < 0x20
            || (int)c is >= 0x7f and <= 0x9f or 0x2028 or 0x2029 or 0xfeff or 0xfffe or 0xffff;
    }

    // Only '-', digits and '.', and not a lone "-" or the document markers "---" and "...", which are indicators.
    private static bool IsDashDigitDot(string value)
    {
        foreach (var c in value)
        {
            if (c != '-' && c != '.' && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return value != "-"
            && !value.StartsWith("---", StringComparison.Ordinal)
            && !value.StartsWith("...", StringComparison.Ordinal);
    }

    private static bool ReadsAsAnotherType(string value)
    {
        if (
            YamlDocument.IsNullText(value)
            || YamlDocument.IsTrueText(value)
            || YamlDocument.IsFalseText(value)
            || YamlDocument.TryGetNumberVariant(value, out _)
            || CoreNumberPattern().IsMatch(value)
            || YamlTimestampHelper.TryGetKind(value, out _)
        )
        {
            return true;
        }

        // Not read as such by this parser, but by YAML 1.1 parsers and as the merge key.
        return value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value is "=" or "<<";
    }
}
