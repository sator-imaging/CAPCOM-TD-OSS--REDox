// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

public readonly record struct YamlDocumentOptions
{
    public bool EnableValueValidation { get; init; }

    public bool PreserveTrivia { get; init; }

    public int MaxDepth { get; init; }

    public bool AutoDetectEncoding { get; init; }

    // TODO: Parse front matter when support for embedded YAML documents is added.
    public bool EnableFrontMatter { get; init; }

    /// <summary>
    /// The time zone of a timestamp that has no offset, such as "2001-12-14 21:59:43". It is used when the value is
    /// read as a <see cref="System.DateTimeOffset" />; the written text is never changed. The default is UTC, which
    /// is what YAML 1.1 specifies.
    /// </summary>
    public System.TimeZoneInfo? TimeZoneForUnspecifiedOffset { get; init; }
}
