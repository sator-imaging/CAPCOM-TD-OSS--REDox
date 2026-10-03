// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

/// <summary>
/// How YAML text is written by <see cref="YamlDocument.EncodeToString(DElement, YamlWriteOptions)" /> and the other
/// writing methods of <see cref="YamlDocument" />. The output is block style YAML, indented with spaces.
/// </summary>
public readonly record struct YamlWriteOptions
{
    private readonly Utf8TextWriteOptions _textWriteOptions;

    /// <summary>
    /// Writes a UTF-8 byte order mark at the start of the output.
    /// </summary>
    public bool WriteBom
    {
        get => _textWriteOptions.WriteBom;
        init => _textWriteOptions = _textWriteOptions with { WriteBom = value };
    }

    /// <summary>
    /// The line break that is written between the lines: "\n" or "\r\n".
    /// </summary>
    public string NewLine
    {
        get => _textWriteOptions.NewLine;
        init => _textWriteOptions = _textWriteOptions with { NewLine = value };
    }

    /// <summary>
    /// The number of spaces that each nested level is indented by, from 0 to 127. The default is 2. A value outside
    /// the range throws an <see cref="System.ArgumentOutOfRangeException" /> when it is set.
    /// </summary>
    public int IndentSize
    {
        get => _textWriteOptions.IndentSize;
        init => _textWriteOptions = _textWriteOptions with { IndentSize = value };
    }

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;

    /// <summary>
    /// Writes the comments and the white space that were kept by <see cref="YamlDocumentOptions.PreserveTrivia" />
    /// when the document was read, exactly as they were written.
    /// </summary>
    public bool PreserveTrivia { get; init; }

    /// <summary>
    /// How a string value is written. See <see cref="YamlStringMode" />.
    /// </summary>
    public YamlStringMode StringMode { get; init; }
}
