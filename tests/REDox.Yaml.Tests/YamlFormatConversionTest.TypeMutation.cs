using REDox.Cbor;
using REDox.MessagePack;

namespace REDox.Yaml.Tests;

public sealed partial class YamlFormatConversionTest
{
    // Binary data (a CBOR byte string, a MessagePack bin) has no YAML form (the !!binary tag is not supported): it is
    // written as its Base64 text, alone or inside a collection, and reads back as a string.
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

    private static readonly YamlWriteOptions LineFeedOptions = new() { NewLine = "\n" };

    [Fact]
    public void CborByteStringBecomesAString()
    {
        using var document = CborDocument.Parse(CborByteString);

        var yaml = YamlDocument.EncodeToString(document.RootElement, LineFeedOptions);

        Assert.Equal("AQID\n", yaml);

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
    }

    [Fact]
    public void MessagePackBinBecomesAString()
    {
        using var document = MessagePackDocument.Parse(MessagePackBin);

        var yaml = YamlDocument.EncodeToString(document.RootElement, LineFeedOptions);

        Assert.Equal("AQID\n", yaml);

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
    }

    [Fact]
    public void ByteStringInsideACollectionBecomesAString()
    {
        using var cbor = CborDocument.Parse(CborArrayOfByteString);
        using var messagePack = MessagePackDocument.Parse(MessagePackArrayOfBin);

        Assert.Equal(
            "- 1\n- AQID\n",
            YamlDocument.EncodeToString(cbor.RootElement, LineFeedOptions)
        );

        Assert.Equal(
            "- 1\n- AQID\n",
            YamlDocument.EncodeToString(messagePack.RootElement, LineFeedOptions)
        );
    }
}
