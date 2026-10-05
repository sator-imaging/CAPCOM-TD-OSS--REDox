// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using System.Text.Unicode;

namespace REDox.Yaml;

internal static class YamlStringHelper
{
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
    public static bool CanWritePlain(ReadOnlySpan<byte> value, bool isKey)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // UTF-8 validity replaces UTF-16 surrogate-pair checks; individual UTF-8 bytes are not characters.
        if (!Utf8.IsValid(value))
        {
            return false;
        }

        // Strings made only of dashes, digits and dots that do not read as another type (such as the version "1.2.3")
        // are written plain. They are checked first because the rules below quote anything that starts with an
        // indicator, a digit or a dot.
        if (IsDashDigitDotTextWithoutYamlIndicators(value))
        {
            return !ReadsAsAnotherType(value);
        }

        var first = value[0];

        if (
            first is YamlSyntax.Space or YamlSyntax.Tab
            || value[^1] is YamlSyntax.Space or YamlSyntax.Tab
        )
        {
            return false;
        }

        // A dot and a plus start numbers such as ".5" and "+1"; a leading digit may be a number or a date.
        if (
            YamlSyntax.IndicatorStarts.Contains(first)
            || first is YamlSyntax.Dot or YamlSyntax.Plus
            || YamlSyntax.IsAsciiDigit(first)
        )
        {
            return false;
        }

        if (YamlSyntax.ContainsNonPrintableCharacters(value))
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == YamlSyntax.MappingValueIndicator)
            {
                // The key scanner stops at any colon; a value's colon only ends it before a space or a flow delimiter.
                if (
                    isKey
                    || i + 1 >= value.Length
                    || value[i + 1]
                        is YamlSyntax.Space
                            or YamlSyntax.FlowSeparator
                            or YamlSyntax.FlowSequenceEnd
                            or YamlSyntax.FlowMappingEnd
                )
                {
                    return false;
                }
            }
            else if (c == YamlSyntax.CommentIndicator)
            {
                // A space and a hash start a comment. A key is also cut at any hash when the parser looks for "key:".
                if (isKey || value[i - 1] == YamlSyntax.Space)
                {
                    return false;
                }
            }
            else if (isKey && c is YamlSyntax.SingleQuote or YamlSyntax.DoubleQuote)
            {
                // A quote inside a key makes the parser treat the rest of the line as a quoted string.
                return false;
            }
        }

        return !ReadsAsAnotherType(value);
    }

    // Only dashes, digits and dots, and not a lone "-" or the document markers "---" and "...", which are indicators.
    private static bool IsDashDigitDotTextWithoutYamlIndicators(ReadOnlySpan<byte> value)
    {
        return !value.ContainsAnyExcept(YamlSyntax.DashDigitDot)
            && !value.SequenceEqual(YamlSyntax.DashTextUtf8)
            && !value.StartsWith(YamlSyntax.DocumentStartUtf8)
            && !value.StartsWith(YamlSyntax.DocumentEndUtf8);
    }

    private static bool ReadsAsAnotherType(ReadOnlySpan<byte> value)
    {
        if (YamlSyntax.IsNullScalar(value) || YamlSyntax.TryParseBooleanScalar(value, out _))
        {
            return true;
        }

        // Only a text that starts with a digit, a sign or a dot can be a number or a timestamp.
        var first = value[0];
        if (
            YamlSyntax.IsAsciiDigit(first)
            || first is YamlSyntax.Plus or YamlSyntax.Dash or YamlSyntax.Dot
        )
        {
            if (YamlDocument.TryGetNumberVariant(value, out _))
            {
                return true;
            }

            // Only the remaining regex and timestamp checks need UTF-16; output must keep the original UTF-8.
            var text = Encoding.UTF8.GetString(value);
            if (
                YamlSyntax.CoreNumberPattern().IsMatch(text)
                || YamlTimestampHelper.TryGetKind(text, out _)
            )
            {
                return true;
            }
        }

        // Also quote the remaining YAML 1.1 reserved words conservatively for other parsers.
        return YamlSyntax.IsYaml11ReservedWord(value);
    }
}
