// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

/// <summary>
/// How a string value is written as YAML. The keys of a map are not affected: they are written plain when that reads
/// back as the same string, and in double quotes otherwise.
/// </summary>
public enum YamlStringStyle
{
    /// <summary>
    /// A string is written without quotes when it reads back as the same string. Otherwise it is written in double
    /// quotes, on one line, with escapes: that is the case for a string that would read back as another type (such as
    /// "true", "123" or a date), an empty string, and a string with a line break or another special character.
    /// </summary>
    Quote,

    /// <summary>
    /// Every string value is written as a literal block scalar (<c>|</c>, <c>|-</c> or <c>|+</c>, chosen by the line
    /// breaks at the end of the string), line by line. A string that a block scalar cannot hold, one with a control
    /// character other than the line feed and the tab, makes the writing methods throw a
    /// <see cref="DocumentParseException" />.
    /// </summary>
    Multiline,
}
