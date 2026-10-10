namespace REDox.Yaml;

/// <summary>
/// Sets how a string value is written as YAML.
/// The keys of a map are always written as <see cref="Quote"/>.
/// </summary>
public enum YamlStringStyle : byte
{
    /// <summary>
    /// A string is written without quotes when it reads back as the same string.
    /// Otherwise it is written in double quotes on one line.
    /// </summary>
    // default
    Quote,

    /// <summary>
    /// Every string value is written as a literal block scalar
    /// (<c>|</c>, <c>|-</c> or <c>|+</c>, chosen by the line breaks at the end of the string).
    /// Throws if a character that a block scalar cannot hold is found (e.g., control characters).
    /// </summary>
    Multiline,

    // TODO: Mixed (use minimal representation for each string)
}