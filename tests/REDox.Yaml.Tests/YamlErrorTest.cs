using System;
using REDox;

namespace REDox.Yaml.Tests;

public sealed class YamlErrorTest
{
    [Theory]
    [InlineData("&anchor value")]
    [InlineData("*alias")]
    [InlineData("!tag value")]
    [InlineData("%YAML 1.2")]
    [InlineData("? complex\n: key\n")]
    [InlineData("first\n---\nsecond\n")]
    [InlineData("a: [1, 2")]
    public void UnsupportedOrMalformedSyntaxFails(string yaml)
    {
        Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(yaml));
    }

    [Fact]
    public void DepthLimitFails()
    {
        Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse("[[1]]", options:
            new YamlDocumentOptions { MaxDepth = 1 }));
    }

    [Fact]
    public void ValidationRejectsDuplicateKeys()
    {
        Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse("a: 1\na: 2\n", options:
            new YamlDocumentOptions { EnableValueValidation = true }));
    }

    [Fact]
    public void MultilineRejectsUnrepresentableControlCharacter()
    {
        var value = DValue.Create("a\rb").AsElement();
        Assert.ThrowsAny<NotSupportedException>(() => YamlDocument.EncodeToString(value,
            new YamlWriteOptions { StringMode = YamlStringMode.Multiline }));
        YamlDocument.EncodeToString(value, new YamlWriteOptions
        {
            StringMode = YamlStringMode.Quote
        });
    }
}
