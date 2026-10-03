using System;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlSpecConformanceTest
{
    [Theory]
    [InlineData("&anchor value")]
    [InlineData("*alias")]
    [InlineData("!tag value")]
    [InlineData("%YAML 1.2")]
    // A collection as a key (a scalar key, explicit or not, is supported)
    [InlineData("? - a\n  - b\n: value\n")]
    [InlineData("?\n- a\n: value\n")]
    [InlineData("? [a, b]\n: value\n")]
    [InlineData("? { a: b }\n: value\n")]
    [InlineData("? a: b\n: value\n")]
    [InlineData("[a, b]: value\n")]
    [InlineData("{ a: b }: value\n")]
    [InlineData("first\n---\nsecond\n")]
    [InlineData("a: [1, 2")]
    // An indentation indicator that is larger than the indentation of the lines: the lines are not part of the scalar
    [InlineData("a: |5\n  text\n")]
    [InlineData("a: >3\n  text\n")]
    // A leading empty line with more spaces than the content indentation of the indicator
    [InlineData("a: |2\n     \n  text\n")]
    // An indentation indicator is one digit from 1 to 9
    [InlineData("a: |10\n  text\n")]
    [InlineData("a: |0\n  text\n")]
    [InlineData("a: b: c\n")]
    [InlineData("a: - b\n")]
    public void UnsupportedOrMalformedSyntaxFails(string yaml)
    {
        Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(yaml));
    }

    [Fact]
    public void TryParseReturnsFalseForInvalidUtf8()
    {
        Assert.False(YamlDocument.TryParse([0x61, 0x3a, 0x20, 0xff], out var document));
        Assert.Null(document);
    }

    [Fact]
    public void DepthLimitFails()
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
            YamlDocument.Parse("[[1]]", options: new YamlDocumentOptions { MaxDepth = 1 })
        );
    }

    [Fact]
    public void ValidationRejectsDuplicateKeys()
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
            YamlDocument.Parse(
                "a: 1\na: 2\n",
                options: new YamlDocumentOptions { EnableValueValidation = true }
            )
        );
    }

    [Fact]
    public void MultilineRejectsUnrepresentableControlCharacter()
    {
        var value = DValue.Create("a\rb").AsElement();
        Assert.ThrowsAny<NotSupportedException>(() =>
            YamlDocument.EncodeToString(
                value,
                new YamlWriteOptions { StringMode = YamlStringMode.Multiline }
            )
        );
        YamlDocument.EncodeToString(
            value,
            new YamlWriteOptions { StringMode = YamlStringMode.Quote }
        );
    }
}
