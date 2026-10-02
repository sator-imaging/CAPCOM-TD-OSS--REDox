using REDox;

namespace REDox.Yaml.Tests;

public sealed class YamlStringsTest
{
    [Theory]
    [InlineData("'it''s fine'", "it's fine")]
    [InlineData("\"quoted\\nline\"", "quoted\nline")]
    [InlineData("plain text", "plain text")]
    [InlineData("|\n  first\n  second\n", "first\nsecond\n")]
    [InlineData("|-\n  first\n", "first")]
    [InlineData(">\n  first\n  second\n", "first second\n")]
    [InlineData("|+\n  first\n  \n", "first\n\n")]
    [InlineData("|2-\n    indented\n", "  indented")]
    [InlineData("\"escaped \\x41\"", "escaped A")]
    [InlineData("\"emoji \\uD83D\\uDE00\"", "emoji 😀")]
    public void ParseStringStyles(string yaml, string expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetString());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("a\nb\n\n")]
    [InlineData(" leading")]
    [InlineData("")]
    public void StringModesPreserveValue(string value)
    {
        var source = DValue.Create(value).AsElement();
        foreach (var mode in new[] { YamlStringMode.Quote, YamlStringMode.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(source, new YamlWriteOptions { StringMode = mode });
            using var parsed = YamlDocument.Parse(yaml);
            Assert.Equal(value, parsed.RootElement.GetString());
        }
    }

    [Theory]
    [InlineData(YamlStringNewlineNormalization.Remove, "a")]
    [InlineData(YamlStringNewlineNormalization.EnsureOne, "a\n")]
    public void NormalizeTerminalNewline(YamlStringNewlineNormalization normalization, string expected)
    {
        var source = DValue.Create("a\n\n").AsElement();
        var yaml = YamlDocument.EncodeToString(source,
            new YamlWriteOptions { NormalizeStringNewlineAtEnd = normalization });
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(expected, parsed.RootElement.GetString());
    }

    [Fact]
    public void FoldedPlainScalarKeepsBlankLine()
    {
        using var document = YamlDocument.Parse("plain: a\n  b\n\n  c\n");
        Assert.Equal("a b\nc", document.RootElement.GetProperty("plain").GetString());
    }
}
