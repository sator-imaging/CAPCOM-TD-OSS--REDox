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
    [InlineData(">\n  a\n\n  b\n", "a\nb\n")]
    [InlineData(">\n  a\n\n\n  b\n", "a\n\nb\n")]
    [InlineData(">\n  a\n   more\n  b\n", "a\n more\nb\n")]
    [InlineData("|\n", "")]
    [InlineData(">-\n", "")]
    [InlineData("|\nroot\n", "root\n")]
    [InlineData("\"a\n  b\"", "a b")]
    [InlineData("\"a  \n\n  b\"", "a\nb")]
    [InlineData("\"a\\ \n  b\"", "a  b")]
    [InlineData("'a\n  b\n\n  c'", "a b\nc")]
    [InlineData("\"tab\there\"", "tab\there")]
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
            var yaml = YamlDocument.EncodeToString(
                source,
                new YamlWriteOptions { StringMode = mode }
            );
            using var parsed = YamlDocument.Parse(yaml);
            Assert.Equal(value, parsed.RootElement.GetString());
        }
    }

    [Theory]
    [InlineData("plain", true)]
    [InlineData("two words", true)]
    [InlineData("a:b", true)]
    [InlineData("a#b", true)]
    [InlineData("a:", false)]
    [InlineData("a: b", false)]
    [InlineData("a #b", false)]
    [InlineData("", false)]
    [InlineData(" lead", false)]
    [InlineData("trail ", false)]
    [InlineData("-dash", false)]
    [InlineData("#hash", false)]
    [InlineData("[x]", false)]
    [InlineData("true", false)]
    [InlineData("NULL", false)]
    [InlineData("123", false)]
    [InlineData("1e3", false)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("5.", false)]
    [InlineData(".5", false)]
    [InlineData("-.5", false)]
    [InlineData("-", false)]
    [InlineData("---", false)]
    [InlineData("...", false)]
    // A string that reads as a timestamp is quoted, or it would come back as a timestamp.
    [InlineData("2001-12-14", false)]
    [InlineData("2001-12-14T21:59:43.10Z", false)]
    [InlineData("2001-12-14 21:59:43.10 -5", false)]
    // Not a real date, so it stays a string and needs no quotes.
    [InlineData("2001-13-45", true)]
    [InlineData("1.2.3", true)]
    [InlineData("0.0.0", true)]
    [InlineData("1-2", true)]
    [InlineData("-1.2.3", true)]
    [InlineData("yes", false)]
    [InlineData("off", false)]
    [InlineData("a\tb", false)]
    [InlineData("a\nb", false)]
    [InlineData("a\u007fb", false)]
    public void ValueIsQuotedOnlyWhenNeeded(string value, bool plain)
    {
        var yaml = YamlDocument.EncodeToString(
            DValue.Create(value).AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(plain, !yaml.StartsWith('"'));
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(value, parsed.RootElement.GetString());
    }

    [Theory]
    [InlineData("name", true)]
    [InlineData("two words", true)]
    [InlineData("a:b", false)]
    [InlineData("a#b", false)]
    [InlineData("it's", false)]
    [InlineData("true", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    public void KeyIsQuotedOnlyWhenNeeded(string key, bool plain)
    {
        var json = "{" + System.Text.Json.JsonSerializer.Serialize(key) + ":1}";
        using var source = REDox.Json.JsonDocument.Parse(json);
        var yaml = YamlDocument.EncodeToString(
            source.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(plain, !yaml.StartsWith('"'));
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(1, parsed.RootElement.GetProperty(key).GetInt64());
    }

    [Theory]
    [InlineData(YamlStringNewlineNormalization.Remove, "a")]
    [InlineData(YamlStringNewlineNormalization.EnsureOne, "a\n")]
    public void NormalizeTerminalNewline(
        YamlStringNewlineNormalization normalization,
        string expected
    )
    {
        var source = DValue.Create("a\n\n").AsElement();
        var yaml = YamlDocument.EncodeToString(
            source,
            new YamlWriteOptions { NormalizeStringNewlineAtEnd = normalization }
        );
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(expected, parsed.RootElement.GetString());
    }

    [Fact]
    public void DuplicateKeepsStringsAfterOriginalIsDisposed()
    {
        using var duplicate = Duplicate("a: \"one\"\nb: |\n  two\n");

        Assert.Equal("one", duplicate.RootElement.GetProperty("a").GetString());
        Assert.Equal("two\n", duplicate.RootElement.GetProperty("b").GetString());
    }

    private static YamlDocument Duplicate(string yaml)
    {
        using var original = YamlDocument.Parse(yaml);
        return original.Duplicate();
    }

    [Fact]
    public void FoldedPlainScalarKeepsBlankLine()
    {
        using var document = YamlDocument.Parse("plain: a\n  b\n\n  c\n");
        Assert.Equal("a b\nc", document.RootElement.GetProperty("plain").GetString());
    }
}
