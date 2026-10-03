// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

/// <summary>
/// How a string value is written as YAML. A string that would read back as another type (such as "true", "123" or a
/// date) is always quoted, so that it stays a string.
/// </summary>
public enum YamlStringMode
{
    Quote,
    Multiline,
}
