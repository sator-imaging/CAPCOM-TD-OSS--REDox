using System;

namespace REDox.Yaml;

internal static class TextEncodingDetector
{
    private static readonly byte[] Utf8Bom = [0xef, 0xbb, 0xbf];
    private static readonly byte[] Utf32LittleEndianBom = [0xff, 0xfe, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianBom = [0x00, 0x00, 0xfe, 0xff];
    private static readonly byte[] Utf16LittleEndianBom = [0xff, 0xfe];
    private static readonly byte[] Utf16BigEndianBom = [0xfe, 0xff];

    /// <summary>
    /// Detects the encoding from a leading byte order mark only.
    /// </summary>
    /// <param name="bytes">The beginning of the text.</param>
    /// <param name="kind">The detected encoding.</param>
    /// <param name="skipBytes">The length of the byte order mark to skip or 0 when none is found.</param>
    /// <returns><see langword="true" /> if a byte order mark was found.</returns>
    public static bool TryDetect(
        ReadOnlySpan<byte> bytes,
        out TextEncodingKind kind,
        out int skipBytes
    )
    {
        // UTF-32 LE must be checked before UTF-16 LE because its mark starts with the same two bytes.
        if (bytes.StartsWith(Utf8Bom))
        {
            kind = TextEncodingKind.Utf8;
            skipBytes = 3; // use const. Utf8Bom.Length;
        }
        else if (bytes.StartsWith(Utf32LittleEndianBom))
        {
            kind = TextEncodingKind.Utf32LittleEndian;
            skipBytes = 4; // use const. Utf32LittleEndianBom.Length;
        }
        else if (bytes.StartsWith(Utf32BigEndianBom))
        {
            kind = TextEncodingKind.Utf32BigEndian;
            skipBytes = 4; // use const. Utf32BigEndianBom.Length;
        }
        else if (bytes.StartsWith(Utf16LittleEndianBom))
        {
            kind = TextEncodingKind.Utf16LittleEndian;
            skipBytes = 2; // use const. Utf16LittleEndianBom.Length;
        }
        else if (bytes.StartsWith(Utf16BigEndianBom))
        {
            kind = TextEncodingKind.Utf16BigEndian;
            skipBytes = 2; // use const. Utf16BigEndianBom.Length;
        }
        else
        {
            kind = default;
            skipBytes = 0;

            return false;
        }

        return true;
    }
}