using System;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlBasicPrimitivesTest
{
    [Theory]
    [InlineData("null")]
    [InlineData("~")]
    public void ParseNull(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    public void ParseBoolean(string yaml, bool expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetBoolean());
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-12", -12L)]
    public void ParseInteger(string yaml, long expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetInt64());
    }

    [Theory]
    [InlineData("3.5", 3.5)]
    [InlineData("1e3", 1000.0)]
    public void ParseFloat(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("3.5")]
    public void PrimitiveRoundTrip(string json)
    {
        using var source = JsonDocument.Parse(json);
        var yaml = YamlDocument.EncodeToString(source.RootElement);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(source.RootElement.ToJsonString(), parsed.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData(".inf", double.PositiveInfinity)]
    [InlineData("-.inf", double.NegativeInfinity)]
    [InlineData(".nan", double.NaN)]
    public void ParseSpecialFloat(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetDouble());
    }
}
