using System;
using REDox;
using REDox.Cbor;
using REDox.MessagePack;

namespace REDox.Yaml.Tests;

public sealed partial class YamlFormatConversionTest
{
    // Binary data (a CBOR byte string, a MessagePack bin) has no YAML form without the !!binary tag, and tags are not
    // supported: converting it to YAML throws a NotSupportedException, alone or inside a collection.
    private static readonly byte[] CborByteString = [0x43, 0x01, 0x02, 0x03];
    private static readonly byte[] MessagePackBin = [0xc4, 0x03, 0x01, 0x02, 0x03];
    private static readonly byte[] CborArrayOfByteString = [0x82, 0x01, 0x43, 0x01, 0x02, 0x03];
    private static readonly byte[] MessagePackArrayOfBin =
    [
        0x92,
        0x01,
        0xc4,
        0x03,
        0x01,
        0x02,
        0x03,
    ];

    [Fact]
    public void CborByteStringIsRefused()
    {
        using var document = CborDocument.Parse(CborByteString);

        var exception = Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeToString(document.RootElement)
        );

        Assert.Contains("ByteString", exception.Message);
    }

    [Fact]
    public void MessagePackBinIsRefused()
    {
        using var document = MessagePackDocument.Parse(MessagePackBin);

        var exception = Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeToString(document.RootElement)
        );

        Assert.Contains("ByteString", exception.Message);
    }

    [Fact]
    public void ByteStringInsideACollectionIsRefused()
    {
        using var cbor = CborDocument.Parse(CborArrayOfByteString);
        using var messagePack = MessagePackDocument.Parse(MessagePackArrayOfBin);

        Assert.Throws<NotSupportedException>(() => YamlDocument.EncodeToString(cbor.RootElement));
        Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeToString(messagePack.RootElement)
        );
    }
}
