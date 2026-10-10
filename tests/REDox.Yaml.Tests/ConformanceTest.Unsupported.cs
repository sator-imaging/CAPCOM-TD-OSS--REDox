using System;

namespace REDox.Yaml.Tests;

public sealed partial class ConformanceTest
{
    [Theory]
    [InlineData("&anchor value", "UnsupportedFeature")]
    [InlineData("*alias", "UnsupportedFeature")]
    [InlineData("!tag value", "UnsupportedFeature")]
    [InlineData("%YAML 1.2", "UnsupportedFeature")]
    // A collection as a key (a scalar key, explicit or not, is supported)
    [InlineData("? - a\n  - b\n: value\n", "UnsupportedFeature")]
    [InlineData("?\n- a\n: value\n", "UnsupportedFeature")]
    [InlineData("? [a, b]\n: value\n", "UnsupportedFeature")]
    [InlineData("? { a: b }\n: value\n", "UnsupportedFeature")]
    [InlineData("? a: b\n: value\n", "UnsupportedFeature")]
    // These leave extra text after the root node, so the parser reports an invalid structure.
    [InlineData("[a, b]: value\n", "InvalidFormat")]
    [InlineData("{ a: b }: value\n", "InvalidFormat")]
    [InlineData("first\n---\nsecond\n", "InvalidFormat")]
    [InlineData("a: [1, 2", "InvalidFormat")]
    // An indentation indicator that is larger than the indentation of the lines: the lines are not part of the scalar
    [InlineData("a: |5\n  text\n", "InvalidFormat")]
    [InlineData("a: >3\n  text\n", "InvalidFormat")]
    // A leading empty line with more spaces than the content indentation of the indicator
    [InlineData("a: |2\n     \n  text\n", "InvalidString")]
    // An indentation indicator is one digit from 1 to 9
    [InlineData("a: |10\n  text\n", "InvalidString")]
    [InlineData("a: |0\n  text\n", "InvalidString")]
    [InlineData("a: b: c\n", "InvalidFormat")]
    [InlineData("a: - b\n", "InvalidFormat")]
    public void UnsupportedOrMalformedSyntax_FailsParsing(string yaml, string expectedCode)
    {
        YamlTestFixture.AssertYamlParseError(() => YamlDocument.Parse(yaml), expectedCode);
    }

    [Fact]
    public void InvalidUtf8_TryParseReturnsFalse()
    {
        Assert.False(
            YamlDocument.TryParse(new Memory<byte>([0x61, 0x3a, 0x20, 0xff]), out var document)
        );

        Assert.Null(document);
    }

    [Fact]
    public void ExceededDepthLimit_FailsParsing()
    {
        YamlTestFixture.AssertYamlParseError(
            static () =>
                YamlDocument.Parse("[[1]]", options: new YamlDocumentOptions { MaxDepth = 1 }),
            "MaxDepthExceeded"
        );
    }

    // YAML 1.2.2 section 5.1 forbids raw NUL in every scalar style; valid UTF-8 alone is insufficient.
    [Theory]
    [InlineData("'a\0b'")]
    [InlineData("a\0b")]
    [InlineData("|\n  a\0b\n")]
    [InlineData(">\n  a\0b\n")]
    public void RawNullInScalarSource_ValidationRejects(string yaml)
    {
        var options = new YamlDocumentOptions { EnableValueValidation = true };

        YamlTestFixture.AssertYamlParseError(
            () => YamlDocument.Parse(yaml, options: options),
            "InvalidString"
        );
    }

    [Fact]
    public void DuplicateKeys_ValidationRejects()
    {
        YamlTestFixture.AssertYamlParseError(
            static () =>
                YamlDocument.Parse(
                    "a: 1\na: 2\n",
                    options: new YamlDocumentOptions { EnableValueValidation = true }
                ),
            "DuplicateKey"
        );
    }

    [Fact]
    public void UnrepresentableControlCharacter_MultilineStyleRejects()
    {
        var value = DValue.Create("a\rb").AsElement();

        YamlTestFixture.AssertYamlParseError(
            () =>
                YamlDocument.EncodeToString(
                    value,
                    new YamlWriteOptions { StringStyle = YamlStringStyle.Multiline }
                ),
            "UnsupportedFeature"
        );

        YamlDocument.EncodeToString(
            value,
            new YamlWriteOptions { StringStyle = YamlStringStyle.Quote }
        );
    }
}