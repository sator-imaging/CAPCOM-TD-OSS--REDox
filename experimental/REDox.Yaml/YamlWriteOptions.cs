namespace REDox.Yaml;

/// <summary>
/// Sets how YAML text is written by <see cref="YamlDocument.EncodeToString(DElement, YamlWriteOptions)"/>
/// and the other writing methods of <see cref="YamlDocument"/>.
/// </summary>
public readonly record struct YamlWriteOptions
{
    private readonly Utf8TextWriteOptions _textWriteOptions;

    /// <inheritdoc cref="Utf8TextWriteOptions.WriteBom"/>
    public bool WriteBom
    {
        get => _textWriteOptions.WriteBom;
        init => _textWriteOptions = _textWriteOptions with { WriteBom = value };
    }

    /// <inheritdoc cref="Utf8TextWriteOptions.NewLine"/>
    public string NewLine
    {
        get => _textWriteOptions.NewLine;
        init => _textWriteOptions = _textWriteOptions with { NewLine = value };
    }

    /// <inheritdoc cref="Utf8TextWriteOptions.IndentSize"/>
    public int IndentSize
    {
        get => _textWriteOptions.IndentSize;
        init => _textWriteOptions = _textWriteOptions with { IndentSize = value };
    }

    /// <inheritdoc cref="YamlDocumentOptions.PreserveTrivia"/>
    public bool PreserveTrivia { get; init; }

    /// <inheritdoc cref="YamlStringStyle"/>
    public YamlStringStyle StringStyle { get; init; }

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;
}