using System;

namespace REDox.Yaml;

/// <summary>
/// Writes as YAML and reads objects from parsed YAML elements.
/// </summary>
public sealed class YamlSerializer : Serializer
{
    /// <summary>
    /// Writes a value as YAML to a new array of UTF-8 bytes.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="settings">The serializer settings.</param>
    /// <param name="options">How the YAML is written.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] Serialize<TValue>(
        TValue value,
        SerializerSettings? settings = null,
        YamlWriteOptions options = default
    )
    {
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<YamlWriter>.Get(static () => new YamlWriter());

        var writer = cache.Value;

        writer.Reset(settings, options);
        writer.WriteValue(value);

        return writer.Encode();
    }

    /// <summary>
    /// Reads a parsed YAML element as the specified runtime type.
    /// </summary>
    /// <param name="value">The element to read.</param>
    /// <param name="type">The type of the result.</param>
    /// <returns>The deserialized value or null when the YAML value is null.</returns>
    public static object? Deserialize(DElement value, Type type) =>
        DeserializeInternal(value, type);

    /// <summary>
    /// Reads a parsed YAML element into an existing target of the specified runtime type.
    /// </summary>
    /// <param name="value">The element to read.</param>
    /// <param name="type">The type of the target.</param>
    /// <param name="target">The existing target to populate.</param>
    /// <returns>The deserialized result.</returns>
    public static object DeserializeTo(DElement value, Type type, object target) =>
        DeserializeToInternal(value, type, target);

    /// <summary>
    /// Reads a parsed YAML element as the specified type.
    /// </summary>
    /// <param name="value">The element to read.</param>
    /// <returns>The deserialized value or the default value when the YAML value is null.</returns>
    public static TValue? Deserialize<TValue>(DElement value) => DeserializeInternal<TValue>(value);

    /// <summary>
    /// Reads a parsed YAML element into an existing target.
    /// </summary>
    /// <param name="value">The element to read.</param>
    /// <param name="target">The existing target to populate.</param>
    /// <returns>The deserialized result.</returns>
    public static TValue DeserializeTo<TValue>(DElement value, TValue target) =>
        DeserializeToInternal(value, target);
}