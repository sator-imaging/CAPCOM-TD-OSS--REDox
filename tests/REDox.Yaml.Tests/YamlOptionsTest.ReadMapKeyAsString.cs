using System.Linq;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlOptionsTest
{
    // ReadMapKeyAsString reads every key as the text it is written with, however the key is written.
    [Theory]
    [InlineData("1: a\n", "1")]
    [InlineData("1.50: a\n", "1.50")]
    [InlineData("0x1F: a\n", "0x1F")]
    [InlineData("true: a\n", "true")]
    [InlineData("null: a\n", "null")]
    [InlineData("~: a\n", "~")]
    [InlineData("2001-12-14: a\n", "2001-12-14")]
    [InlineData("text: a\n", "text")]
    [InlineData("\"1\": a\n", "1")]
    [InlineData("{1: a}", "1")]
    [InlineData("{? 1 : a}", "1")]
    [InlineData("[true: a]", "true")]
    [InlineData("? 1\n: a\n", "1")]
    [InlineData("? null\n: a\n", "null")]
    [InlineData("? 2001-12-14\n: a\n", "2001-12-14")]
    [InlineData("? 1\n  2\n: a\n", "1 2")]
    public void ReadMapKeyAsStringReadsEveryKeyAsItsText(string yaml, string key)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { ReadMapKeyAsString = true }
        );
        var map =
            document.RootElement.Token.Type == DTokenType.Array
                ? document.RootElement.AsArray()[0].AsElement().AsMap()
                : document.RootElement.AsMap();

        Assert.Equal(DTokenKind.String, map[0].Key.AsElement().Token.Kind);
        Assert.Equal(key, map[0].Key.AsElement().GetString());
        Assert.Equal("a", map[0].Value.AsElement().GetString());
    }

    [Fact]
    public void ReadMapKeyAsStringLetsGetPropertyFindTheKey()
    {
        using var document = YamlDocument.Parse(
            "1: a\ntrue: b\n",
            options: new YamlDocumentOptions { ReadMapKeyAsString = true }
        );

        Assert.Equal("a", document.RootElement.GetProperty("1").GetString());
        Assert.Equal("b", document.RootElement.GetProperty("true").GetString());
    }

    [Fact]
    public void ReadMapKeyAsStringIsOffByDefault()
    {
        Assert.False(new YamlDocumentOptions().ReadMapKeyAsString);

        using var document = YamlDocument.Parse("1: a\n");

        Assert.Equal(
            DTokenKind.Integer,
            document.RootElement.AsMap()[0].Key.AsElement().Token.Kind
        );
    }

    // The values are not touched: only the keys are read as strings.
    [Fact]
    public void ReadMapKeyAsStringLeavesTheValuesAlone()
    {
        using var document = YamlDocument.Parse(
            "1: 2\ntrue: false\n? 3\n: null\n",
            options: new YamlDocumentOptions { ReadMapKeyAsString = true }
        );

        var kinds = document.RootElement.AsMap().Select(pair => pair.Value.AsElement().Token.Kind);

        Assert.Equal(new[] { DTokenKind.Integer, DTokenKind.Boolean, DTokenKind.Null }, kinds);
    }

    // A key with no text has no string to read.
    [Theory]
    [InlineData("?\n: a\n")]
    [InlineData("? a\n? \n: b\n")]
    [InlineData("{? : a}")]
    [InlineData("{?, b: c}")]
    [InlineData("[? : a]")]
    public void ReadMapKeyAsStringRefusesAKeyWithNoText(string yaml)
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
            YamlDocument.Parse(yaml, options: new YamlDocumentOptions { ReadMapKeyAsString = true })
        );

        // Without the option the same text has a null key.
        using var document = YamlDocument.Parse(yaml);
        Assert.True(document.RootElement.IsValid);
    }
}
