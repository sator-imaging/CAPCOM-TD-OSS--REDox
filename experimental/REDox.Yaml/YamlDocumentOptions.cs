using System;

namespace REDox.Yaml;

/// <summary>
/// Sets how YAML text is read by <see cref="YamlDocument.Parse(string, SerializerSettings, YamlDocumentOptions)"/>
/// and other reading methods of <see cref="YamlDocument"/>.
/// </summary>
public readonly record struct YamlDocumentOptions
{
    /// <inheritdoc cref="YamlDocumentOptions"/>
    /// <param name="assumedTimeZone"><see cref="AssumedTimeZone"/></param>
    /// <param name="preserveTrivia"><see cref="PreserveTrivia"/></param>
    /// <param name="readMapKeyAsString"><see cref="ReadMapKeyAsString"/></param>
    /// <exception cref="ArgumentNullException"/>
    public YamlDocumentOptions(
        TimeZoneInfo assumedTimeZone,
        bool preserveTrivia = true,
        bool readMapKeyAsString = true
    )
    {
        ArgumentNullException.ThrowIfNull(assumedTimeZone);

        AssumedTimeZone = assumedTimeZone;
        PreserveTrivia = preserveTrivia;
        ReadMapKeyAsString = readMapKeyAsString;
    }

    /// <summary>
    /// Whether validates the values while reading.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, throws if a value or a structure is not valid.
    /// </remarks>
    public bool EnableValueValidation { get; init; }

    /// <summary>
    /// Whether reads every map key as a string.
    /// </summary>
    /// <remarks>
    /// Throws if a key with no text (<c>? </c> followed by nothing or an empty key).
    /// </remarks>
    public bool ReadMapKeyAsString { get; init; }

    /// <summary>
    /// Keeps comments and whitespace around the values.
    /// </summary>
    public bool PreserveTrivia { get; init; }

    /// <summary>
    /// The deepest nesting level that is tried to read.
    /// Defaults to <c>0</c> that means no limit.
    /// </summary>
    /// <remarks>
    /// Given <c>[[1],[2]]</c>, the outer list on the root is at depth 0 and the numbers at level 2,
    /// so it needs at least 2 otherwise throws.
    /// </remarks>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Whether converts text in UTF-16 or UTF-32 to UTF-8 while reading.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, throws if the text is not in UTF-8.
    /// </remarks>
    public bool AutoDetectEncoding { get; init; }

    /// <summary>
    /// The time zone for a timestamp that has no offset (e.g., "2001-12-14 21:59:43").
    /// It changes only time zone and clock time is never changed.
    /// </summary>
    /// <remarks>
    /// The default or null is UTC which YAML 1.1 specifies.
    /// </remarks>
    public TimeZoneInfo? AssumedTimeZone { get; init; }
}