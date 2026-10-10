using System;
using System.Text;

namespace REDox.Yaml;

internal static class YamlStringHelper
{
    /// <summary>
    /// Decides whether a string can be written without quotes and still be read back without change.
    /// </summary>
    /// <remarks>
    /// The rules are deliberately conservative when in doubt the string is quoted.
    /// Besides this library's own parser, other YAML libraries must read the text as the same string,
    /// so words that YAML 1.1 reads as booleans and anything that looks like a number or a date are quoted too.
    /// </remarks>
    /// <param name="value">The string to write.</param>
    /// <param name="isKey">Whether the string is a map key which the parser scans more strictly than a value.</param>
    public static bool CanWriteStringAsPlainScalar(ReadOnlySpan<byte> value, bool isKey)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // WriteStringValue validates and replaces invalid UTF-8 before calling this method.
        // Repeating that full scan for each scalar would add work without changing the result.
        //   if (!Utf8.IsValid(value))

        // Strings made only of dashes, digits and dots that do not read as another type (such as the version "1.2.3")
        // are written plain. They are checked first because the rules below quote anything that starts with an
        // indicator, a digit or a dot.
        // Only these leading bytes can make the entire scalar dash, digit and dot text.
        // Most YAML keys start with letters, so avoid scanning them for this special case.
        if (
            (value[0] is YamlSyntax.Dash or YamlSyntax.Dot || YamlSyntax.IsAsciiDigit(value[0]))
            && IsDashDigitDotTextWithoutYamlIndicators(value)
        )
        {
            return !MayResolveAsNonStringScalar(value);
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

        // These ASCII bytes cannot introduce a YAML delimiter or an unprintable character.
        // Common keys need only this scan instead of separate printable and syntax scans.
        if (!value.ContainsAnyExcept(YamlSyntax.PlainAsciiSafeBytes))
        {
            // The first-byte check excluded numbers and timestamps, and every implicit keyword is at most five bytes.
            // TODO: date time i18n support.
            return value.Length > 5 || !MayResolveAsNonStringScalar(value);
        }

        return CanWritePlainAfterAsciiCheck(value, isKey);

        static bool CanWritePlainAfterAsciiCheck(ReadOnlySpan<byte> value, bool isKey)
        {
            if (YamlSyntax.ContainsNonPrintableCharacters(value))
            {
                return false;
            }

            // Most bytes cannot change plain-scalar syntax, so search only for its delimiters.
            ReadOnlySpan<byte> syntaxCharacters = isKey ? ":#'\""u8 : ":#"u8;
            var i = 0;
            while (i < value.Length)
            {
                var next = value[i..].IndexOfAny(syntaxCharacters);
                if (next < 0)
                {
                    break;
                }

                i += next;

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

                i++;
            }

            return !MayResolveAsNonStringScalar(value);
        }
    }

    // Only dashes, digits and dots, and not a lone "-" or the document markers "---" and "...", which are indicators.
    private static bool IsDashDigitDotTextWithoutYamlIndicators(ReadOnlySpan<byte> value)
    {
        return !value.ContainsAnyExcept(YamlSyntax.DashDigitDot)
               && !value.SequenceEqual(YamlSyntax.DashTextUtf8)
               && !value.StartsWith(YamlSyntax.DocumentStartUtf8)
               && !value.StartsWith(YamlSyntax.DocumentEndUtf8);
    }

    private static bool MayResolveAsNonStringScalar(ReadOnlySpan<byte> value)
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

            // Neither the core number grammar nor a timestamp can match after these byte checks,
            // so avoid decoding strings such as "30s" solely to reject them again.
            var couldBeCoreNumber = !value.ContainsAnyExcept(YamlSyntax.CoreNumberCandidateBytes);
            var couldBeTimestamp = TimestampHelper.StartsLikeTimestamp(value);
            if (couldBeCoreNumber || couldBeTimestamp)
            {
                var text = Encoding.UTF8.GetString(value);
                if (
                    couldBeCoreNumber && YamlSyntax.CoreNumberPattern().IsMatch(text)
                    || couldBeTimestamp && TimestampHelper.TryGetKind(text, out _)
                )
                {
                    return true;
                }
            }
        }

        // Also quote the remaining YAML 1.1 reserved words conservatively for other parsers.
        return YamlSyntax.IsYaml11ReservedWord(value);
    }
}