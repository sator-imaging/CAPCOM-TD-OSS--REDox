using System;

namespace REDox.Yaml;

internal static class TextEncodingDetector
{
    private const byte Utf8BomLeadingByte = 0xef;
    private const byte Utf32BigEndianBomLeadingByte = 0x00;
    private const byte Utf16BigEndianBomLeadingByte = 0xfe;
    private const byte UtfLittleEndianBomLeadingByte = 0xff;
    private static readonly byte[] Utf8Bom = [Utf8BomLeadingByte, 0xbb, 0xbf];

    private static readonly byte[] Utf32LittleEndianBom =
    [
        UtfLittleEndianBomLeadingByte,
        0xfe,
        0x00,
        0x00,
    ];

    private static readonly byte[] Utf32BigEndianBom =
    [
        Utf32BigEndianBomLeadingByte,
        0x00,
        0xfe,
        0xff,
    ];

    private static readonly byte[] Utf16LittleEndianBom = [UtfLittleEndianBomLeadingByte, 0xfe];
    private static readonly byte[] Utf16BigEndianBom = [Utf16BigEndianBomLeadingByte, 0xff];

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
        if (!bytes.IsEmpty)
        {
            switch (bytes[0])
            {
                case Utf8BomLeadingByte when bytes.StartsWith(Utf8Bom):
                    kind = TextEncodingKind.Utf8;
                    skipBytes = 3; // use const. Utf8Bom.Length;

                    return true;

                case UtfLittleEndianBomLeadingByte:
                    // UTF-32 LE must be checked before UTF-16 LE because its mark starts with the same two bytes.
                    if (bytes.StartsWith(Utf32LittleEndianBom))
                    {
                        kind = TextEncodingKind.Utf32LittleEndian;
                        skipBytes = 4; // use const. Utf32LittleEndianBom.Length;

                        return true;
                    }

                    if (bytes.StartsWith(Utf16LittleEndianBom))
                    {
                        kind = TextEncodingKind.Utf16LittleEndian;
                        skipBytes = 2; // use const. Utf16LittleEndianBom.Length;

                        return true;
                    }

                    break;

                case Utf32BigEndianBomLeadingByte when bytes.StartsWith(Utf32BigEndianBom):
                    kind = TextEncodingKind.Utf32BigEndian;
                    skipBytes = 4; // use const. Utf32BigEndianBom.Length;

                    return true;

                case Utf16BigEndianBomLeadingByte when bytes.StartsWith(Utf16BigEndianBom):
                    kind = TextEncodingKind.Utf16BigEndian;
                    skipBytes = 2; // use const. Utf16BigEndianBom.Length;

                    return true;
            }
        }

        kind = default;
        skipBytes = 0;

        return false;
    }
}