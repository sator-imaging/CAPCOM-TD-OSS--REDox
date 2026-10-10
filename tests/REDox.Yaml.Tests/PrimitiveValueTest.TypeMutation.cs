using System;
using REDox.Serialization;

namespace REDox.Yaml.Tests;

public sealed partial class PrimitiveValueTest
{
    // The values that change their type when they are written.
    // YAML serializer has no form for them (the !!binary tag is not  supported),
    // so each is written as the string it has in text formats, and reads back as that string.
    private static readonly byte[] BinaryValueBytes = [1, 2, 3];
    private static readonly Guid BinaryConversionGuid = new("12345678-90ab-cdef-1234-567890abcdef");

    public static TheoryData<string, string> GetBinaryKindsAndExpectedYamlStrings() =>
        new()
        {
            { "default", "AQID" },
            { "base64", "AQID" },
            { "base64url", "AQID" },
            { "base16", "010203" },
            { "raw", "AQID" },
            { "guid", "12345678-90ab-cdef-1234-567890abcdef" },
        };

    private static DValue CreateBinaryValueForKind(string binaryKindName) =>
        binaryKindName switch
        {
            "default" => DValue.Create(BinaryValueBytes, ByteStringKind.Default),
            "base64" => DValue.Create(BinaryValueBytes, ByteStringKind.Base64),
            "base64url" => DValue.Create(BinaryValueBytes, ByteStringKind.Base64Url),
            "base16" => DValue.Create(BinaryValueBytes, ByteStringKind.Base16),
            "raw" => DValue.Create(BinaryValueBytes, ByteStringKind.Raw),
            _ => DValue.Create(BinaryConversionGuid),
        };

    [Theory]
    [MemberData(nameof(GetBinaryKindsAndExpectedYamlStrings))]
    public void BinaryValue_WritesAndReadsBackAsString(
        string binaryKindName,
        string expectedYamlString
    )
    {
        var yaml = YamlDocument.EncodeToString(
            CreateBinaryValueForKind(binaryKindName).AsElement()
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
        Assert.Equal(expectedYamlString, parsed.RootElement.GetString());
    }

    [Theory]
    [MemberData(nameof(GetBinaryKindsAndExpectedYamlStrings))]
    public void BinaryValueInsideCollection_WritesAndReadsBackAsString(
        string binaryKindName,
        string expectedYamlString
    )
    {
        var mapWithBinaryValue = new DObject
        {
            { "before", DValue.Create(1) },
            { "data", CreateBinaryValueForKind(binaryKindName) },
        };

        using var parsedMap = YamlDocument.Parse(
            YamlDocument.EncodeToString(mapWithBinaryValue.AsElement())
        );

        Assert.Equal(1, parsedMap.RootElement.GetProperty("before").GetInt64());
        Assert.Equal(expectedYamlString, parsedMap.RootElement.GetProperty("data").GetString());

        var listWithBinaryValue = new DArray(
            DValue.Create(1),
            CreateBinaryValueForKind(binaryKindName)
        );

        using var parsedList = YamlDocument.Parse(
            YamlDocument.EncodeToString(listWithBinaryValue.AsElement())
        );

        Assert.Equal(1, parsedList.RootElement.AsArray()[0].AsElement().GetInt64());
        Assert.Equal(
            expectedYamlString,
            parsedList.RootElement.AsArray()[1].AsElement().GetString()
        );
    }

    [Theory]
    [MemberData(nameof(GetBinaryKindsAndExpectedYamlStrings))]
    public void BinaryKey_WritesAndReadsBackAsString(
        string binaryKindName,
        string expectedYamlString
    )
    {
        var mapWithBinaryKey = new DMap
        {
            { CreateBinaryValueForKind(binaryKindName), DValue.Create("value") },
        };

        using var parsed = YamlDocument.Parse(
            YamlDocument.EncodeToString(mapWithBinaryKey.AsElement())
        );

        var key = parsed.RootElement.AsMap()[0].Key.AsElement();

        Assert.Equal(DTokenKind.String, key.Token.Kind);
        Assert.Equal(expectedYamlString, key.GetString());
        Assert.Equal("value", parsed.RootElement.AsMap()[0].Value.AsElement().GetString());
    }

    [Theory]
    [MemberData(nameof(GetBinaryKindsAndExpectedYamlStrings))]
    public void BinaryValueWithMultilineStyle_ReadsBackAsOriginalText(
        string binaryKindName,
        string expectedYamlString
    )
    {
        var yaml = YamlDocument.EncodeToString(
            CreateBinaryValueForKind(binaryKindName).AsElement(),
            new YamlWriteOptions { StringStyle = YamlStringStyle.Multiline }
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(expectedYamlString, parsed.RootElement.GetString());
    }

    [Fact]
    public void NumericLookingBinaryText_WritesQuotedAndReadsBackAsString()
    {
        var yaml = YamlDocument.EncodeToString(
            CreateBinaryValueForKind("base16").AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("\"010203\"\n", yaml);

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal("010203", parsed.RootElement.GetString());
    }

    [Theory]
    [InlineData(SymbolKind.Default)]
    [InlineData(SymbolKind.Identifier)]
    [InlineData(SymbolKind.Reference)]
    [InlineData(SymbolKind.TypeName)]
    [InlineData(SymbolKind.Metadata)]
    public void Symbol_WritesAndReadsBackAsItsText(SymbolKind kind)
    {
        var value = DValue.Create(new Utf8Symbol("name"), kind);

        var yaml = YamlDocument.EncodeToString(
            value.AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("name\n", yaml);

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
        Assert.Equal("name", parsed.RootElement.GetString());
    }
}