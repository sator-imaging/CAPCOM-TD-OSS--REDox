// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Yaml;

/// <summary>
/// Writes an object as YAML, without building a document first.
/// </summary>
public sealed class YamlSerializer : Serializer
{
    /// <summary>
    /// Writes a value as YAML, in block style, to a new array of UTF-8 bytes.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="settings">The serializer settings. The default settings when null.</param>
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
}
