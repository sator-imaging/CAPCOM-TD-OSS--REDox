using System;
using REDox.Serialization;

namespace REDox.Yaml.Tests;

public sealed partial class YamlPrimitiveValueTest
{
    // The values that change their type when they are written: YAML has no form for them (the !!binary tag is not
    // supported), so each is written as the string it has in text formats, and reads back as that string.
    private static readonly byte[] Bytes = [1, 2, 3];
    private static readonly Guid SampleGuid = new("12345678-90ab-cdef-1234-567890abcdef");

    // A kind of binary data and the string it is written as: Base64, Base64Url and Base16, and the Guid in its usual form.
    public static TheoryData<string, string> GetBinaryValues() =>
        new()
        {
            { "default", "AQID" },
            { "base64", "AQID" },
            { "base64url", "AQID" },
            { "base16", "010203" },
            { "raw", "AQID" },
            { "guid", "12345678-90ab-cdef-1234-567890abcdef" },
        };

    private static DValue CreateBinary(string name) =>
        name switch
        {
            "default" => DValue.Create(Bytes, ByteStringKind.Default),
            "base64" => DValue.Create(Bytes, ByteStringKind.Base64),
            "base64url" => DValue.Create(Bytes, ByteStringKind.Base64Url),
            "base16" => DValue.Create(Bytes, ByteStringKind.Base16),
            "raw" => DValue.Create(Bytes, ByteStringKind.Raw),
            _ => DValue.Create(SampleGuid),
        };

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryValue_WritesAndReadsBackAsString(string name, string text)
    {
        var yaml = YamlDocument.EncodeToString(CreateBinary(name).AsElement());

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
        Assert.Equal(text, parsed.RootElement.GetString());
    }

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryValueInsideCollection_WritesAsString(string name, string text)
    {
        var map = new DObject { { "before", DValue.Create(1) }, { "data", CreateBinary(name) } };

        var list = new DArray(DValue.Create(1), CreateBinary(name));

        using var parsedMap = YamlDocument.Parse(YamlDocument.EncodeToString(map.AsElement()));
        using var parsedList = YamlDocument.Parse(YamlDocument.EncodeToString(list.AsElement()));

        Assert.Equal(1, parsedMap.RootElement.GetProperty("before").GetInt64());
        Assert.Equal(text, parsedMap.RootElement.GetProperty("data").GetString());
        Assert.Equal(1, parsedList.RootElement.AsArray()[0].AsElement().GetInt64());
        Assert.Equal(text, parsedList.RootElement.AsArray()[1].AsElement().GetString());
    }

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryKey_WritesAsString(string name, string text)
    {
        var map = new DMap { { CreateBinary(name), DValue.Create("value") } };

        using var parsed = YamlDocument.Parse(YamlDocument.EncodeToString(map.AsElement()));

        var key = parsed.RootElement.AsMap()[0].Key.AsElement();

        Assert.Equal(DTokenKind.String, key.Token.Kind);
        Assert.Equal(text, key.GetString());
        Assert.Equal("value", parsed.RootElement.AsMap()[0].Value.AsElement().GetString());
    }

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryValueWithMultilineStyle_WritesAsBlockScalar(string name, string text)
    {
        var yaml = YamlDocument.EncodeToString(
            CreateBinary(name).AsElement(),
            new YamlWriteOptions { StringStyle = YamlStringStyle.Multiline }
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(text, parsed.RootElement.GetString());
    }

    [Fact]
    public void NumericLookingBinaryText_WritesQuoted()
    {
        var yaml = YamlDocument.EncodeToString(
            CreateBinary("base16").AsElement(),
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
    public void Symbol_WritesAsItsText(SymbolKind kind)
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
