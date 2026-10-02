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
}
