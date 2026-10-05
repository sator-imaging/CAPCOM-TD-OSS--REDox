using System;
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
    public void CborByteStringToYaml_WritesString()
    {
        using var cborDocument = CborDocument.Parse(CborByteString);

        var yamlFromCbor = YamlDocument.EncodeToString(cborDocument.RootElement, LineFeedOptions);

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromCbor);

        Assert.Equal(DTokenKind.String, convertedYamlDocument.RootElement.Token.Kind);
        Assert.True(
            DValue.DeepEquals(DValue.Create("AQID"), convertedYamlDocument.RootElement.AsValue())
        );
    }

    [Fact]
    public void MessagePackBinaryValueToYaml_WritesString()
    {
        using var messagePackDocument = MessagePackDocument.Parse(MessagePackBin);

        var yamlFromMessagePack = YamlDocument.EncodeToString(
            messagePackDocument.RootElement,
            LineFeedOptions
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromMessagePack);

        Assert.Equal(DTokenKind.String, convertedYamlDocument.RootElement.Token.Kind);
        Assert.True(
            DValue.DeepEquals(DValue.Create("AQID"), convertedYamlDocument.RootElement.AsValue())
        );
    }

    [Fact]
    public void BinaryValueInsideCollectionToYaml_WritesString()
    {
        using var cborDocument = CborDocument.Parse(CborArrayOfByteString);
        using var messagePackDocument = MessagePackDocument.Parse(MessagePackArrayOfBin);

        using var yamlFromCborDocument = YamlDocument.Parse(
            YamlDocument.EncodeToString(cborDocument.RootElement, LineFeedOptions)
        );

        using var yamlFromMessagePackDocument = YamlDocument.Parse(
            YamlDocument.EncodeToString(messagePackDocument.RootElement, LineFeedOptions)
        );

        var expectedYamlArray = new DArray(1, "AQID");

        // Binary values lose their binary type in YAML; compare the values after YAML has parsed them as strings.
        Assert.True(
            DValue.DeepEquals(
                expectedYamlArray.AsElement().AsValue(),
                yamlFromCborDocument.RootElement.AsValue()
            )
        );

        Assert.True(
            DValue.DeepEquals(
                expectedYamlArray.AsElement().AsValue(),
                yamlFromMessagePackDocument.RootElement.AsValue()
            )
        );
    }

    // MessagePack keeps the offset of a DateTimeOffset in a list next to the timestamp. YAML does not combine them: the
    // list is written as it is, with the timestamp as a timestamp without an offset, so the offset is the number of
    // minutes that follows, and the timestamp reads in the assumed time zone like any timestamp without an offset.
    [Fact]
    public void MessagePackOffsetToYaml_PreservesListAndUsesAssumedTimeZone()
    {
        var value = new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.FromHours(9));
        var tokyo = TimeZoneInfo.CreateCustomTimeZone(
            "Tokyo",
            TimeSpan.FromHours(9),
            "Tokyo",
            "Tokyo"
        );

        var yamlFromMessagePack = ConvertElementToYamlViaBinaryFormat(
            "messagepack",
            DValue.Create(value).AsElement()
        );

        using var yamlWithUtcTimeZone = YamlDocument.Parse(yamlFromMessagePack);

        Assert.Equal(2, yamlWithUtcTimeZone.RootElement.GetArrayLength());
        Assert.Equal(
            DTokenKind.Timestamp,
            yamlWithUtcTimeZone.RootElement.AsArray()[0].AsElement().Token.Kind
        );

        var yamlDateTimeUtc = yamlWithUtcTimeZone
            .RootElement.AsArray()[0]
            .AsElement()
            .GetDateTimeOffset();

        Assert.Equal(value.DateTime, yamlDateTimeUtc.DateTime);
        Assert.Equal(TimeSpan.Zero, yamlDateTimeUtc.Offset);

        using var yamlWithTokyoTimeZone = YamlDocument.Parse(
            yamlFromMessagePack,
            options: new YamlDocumentOptions { AssumedTimeZone = tokyo }
        );

        var yamlDateTimeTokyo = yamlWithTokyoTimeZone
            .RootElement.AsArray()[0]
            .AsElement()
            .GetDateTimeOffset();

        Assert.Equal(value, yamlDateTimeTokyo);
        Assert.Equal(value.DateTime, yamlDateTimeTokyo.DateTime);
        Assert.Equal(540, yamlDateTimeTokyo.Offset.TotalMinutes);
        Assert.Equal(540, yamlWithUtcTimeZone.RootElement.AsArray()[1].AsElement().GetInt64());
    }

    // CBOR's "undefined" (0xf7) has no YAML form of its own: it is a null.
    [Fact]
    public void CborUndefinedToYaml_WritesNull()
    {
        using var cborDocument = CborDocument.Parse([0xf7]);

        var yamlFromCbor = YamlDocument.EncodeToString(
            cborDocument.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromCbor);

        Assert.Equal(DTokenKind.Null, convertedYamlDocument.RootElement.Token.Kind);
    }
}
