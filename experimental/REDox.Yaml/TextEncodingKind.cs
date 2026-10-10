namespace REDox.Yaml;

internal enum TextEncodingKind : byte
{
    // default
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,
    Utf32LittleEndian,
    Utf32BigEndian,
}