// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

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
    public static bool CanWritePlain(string value, bool isKey)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // Strings made only of dashes, digits and dots that do not read as another type (such as the version "1.2.3")
        // are written plain. They are checked first because the rules below quote anything that starts with an
        // indicator, a digit or a dot.
        if (IsDashDigitDot(value))
        {
            return !ReadsAsAnotherType(value);
        }

        var first = value[0];

        if (
            first is (char)YamlSyntax.Space or (char)YamlSyntax.Tab
            || value[^1] is (char)YamlSyntax.Space or (char)YamlSyntax.Tab
        )
        {
            return false;
        }

        // A dot and a plus start numbers such as ".5" and "+1"; a leading digit may be a number or a date.
        if (
            YamlSyntax.IndicatorStarts.Contains(first)
            || first is (char)YamlSyntax.Dot or (char)YamlSyntax.Plus
            || char.IsAsciiDigit(first)
        )
        {
            return false;
        }

        if (value.AsSpan().ContainsAny(YamlSyntax.NonPrintable))
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

            if (c == YamlSyntax.MappingValueIndicator)
            {
                // The key scanner stops at any colon; a value's colon only ends it before a space or a flow delimiter.
                if (
                    isKey
                    || i + 1 >= value.Length
                    || value[i + 1]
                        is (char)YamlSyntax.Space
                            or (char)YamlSyntax.FlowSeparator
                            or (char)YamlSyntax.FlowSequenceEnd
                            or (char)YamlSyntax.FlowMappingEnd
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
            else if (isKey && c is (char)YamlSyntax.SingleQuote or (char)YamlSyntax.DoubleQuote)
            {
                // A quote inside a key makes the parser treat the rest of the line as a quoted string.
                return false;
            }
        }

        return !ReadsAsAnotherType(value);
    }

    // Only dashes, digits and dots, and not a lone "-" or the document markers "---" and "...", which are indicators.
    private static bool IsDashDigitDot(string value)
    {
        return !value.AsSpan().ContainsAnyExcept(YamlSyntax.DashDigitDot)
            && value != YamlSyntax.DashText
            && !value.StartsWith(YamlSyntax.DocumentStart, StringComparison.Ordinal)
            && !value.StartsWith(YamlSyntax.DocumentEnd, StringComparison.Ordinal);
    }

    private static bool ReadsAsAnotherType(string value)
    {
        if (
            YamlDocument.IsNullText(value)
            || YamlDocument.IsTrueText(value)
            || YamlDocument.IsFalseText(value)
            || YamlDocument.TryGetNumberVariant(value, out _)
            || YamlSyntax.CoreNumberPattern().IsMatch(value)
            || YamlTimestampHelper.TryGetKind(value, out _)
        )
        {
            return true;
        }

        // Not read as such by this parser, but by YAML 1.1 parsers.
        return YamlSyntax.Yaml11Words.Contains(value);
    }
}
