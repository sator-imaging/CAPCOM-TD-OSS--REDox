using System;
using System.Text.Json;

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
    // At the root the default RootIndentationLevel of 0 counts "|2" from column 0 (see YamlOptionsTest for -1).
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
    public void StringStylesPreserveValue(string value)
    {
        var source = DValue.Create(value).AsElement();
        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(
                source,
                new YamlWriteOptions { StringStyle = mode }
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
        var json = "{" + JsonSerializer.Serialize(key) + ":1}";
        using var source = Json.JsonDocument.Parse(json);

        var yaml = YamlDocument.EncodeToString(
            source.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(plain, !yaml.StartsWith('"'));

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(1, parsed.RootElement.GetProperty(key).GetInt64());
    }

    // Characters YAML cannot hold as they are (DEL, the C1 controls, the non-characters) are escaped in a quoted
    // string; the others that need care (line and paragraph separators, the byte order mark) are kept. In every
    // case the string comes back unchanged. The characters are built from their code points, so that no invisible
    // character appears in the source.
    [Theory]
    [InlineData(0x7f, true)]
    [InlineData(0x80, true)]
    [InlineData(0x84, true)]
    [InlineData(0x85, false)]
    [InlineData(0x86, true)]
    [InlineData(0x9f, true)]
    [InlineData(0xfffe, true)]
    [InlineData(0xffff, true)]
    [InlineData(0x2028, false)]
    [InlineData(0x2029, false)]
    [InlineData(0xfeff, false)]
    [InlineData(0xa0, false)]
    [InlineData(0x3000, false)]
    public void SpecialCharacterRoundTripsInQuotedString(int codePoint, bool escaped)
    {
        var character = ((char)codePoint).ToString();
        var value = "a" + character + "b";

        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        Assert.Equal(escaped, !yaml.Contains(character));

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(value, parsed.RootElement.GetString());
    }

    // The same characters as the key of a map.
    [Theory]
    [InlineData(0x7f)]
    [InlineData(0x85)]
    [InlineData(0x2028)]
    [InlineData(0xfeff)]
    public void SpecialCharacterRoundTripsInKey(int codePoint)
    {
        var key = "k" + ((char)codePoint) + "k";
        var json = "{" + JsonSerializer.Serialize(key) + ":1}";
        using var source = Json.JsonDocument.Parse(json);

        var yaml = YamlDocument.EncodeToString(source.RootElement);

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(1, parsed.RootElement.GetProperty(key).GetInt64());
    }

    // A block scalar cannot hold DEL or the C1 controls: the multiline mode refuses them rather than writing a
    // string that would come back different.
    [Theory]
    [InlineData(0x7f)]
    [InlineData(0x80)]
    [InlineData(0x85)]
    [InlineData(0x9f)]
    public void MultilineModeRefusesControlCharacter(int codePoint)
    {
        var value = "a" + (char)codePoint + "b";

        Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeToString(
                DValue.Create(value).AsElement(),
                new YamlWriteOptions { StringStyle = YamlStringStyle.Multiline }
            )
        );
    }

    // The separators and the byte order mark are ordinary text in a block scalar.
    [Theory]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0xfeff)]
    [InlineData(0x3000)]
    public void MultilineModeKeepsOrdinaryCharacter(int codePoint)
    {
        var value = "a" + (char)codePoint + "b\nc";

        var yaml = YamlDocument.EncodeToString(
            DValue.Create(value).AsElement(),
            new YamlWriteOptions { StringStyle = YamlStringStyle.Multiline }
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(value, parsed.RootElement.GetString());
    }

    // An isolated surrogate is not text: UTF-8 cannot hold it, so it is written as the replacement character. The
    // document stays valid and the rest of the string is untouched.
    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    [InlineData(0xdfff)]
    public void IsolatedSurrogateIsWrittenAsReplacementCharacter(int surrogate)
    {
        var value = "a" + (char)surrogate + "b";

        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal("a" + (char)0xfffd + "b", parsed.RootElement.GetString());
    }

    [Fact]
    public void PairedSurrogatesRoundTrip()
    {
        var value = "a" + char.ConvertFromUtf32(0x1f600) + char.ConvertFromUtf32(0x10ffff) + "b";

        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(value, parsed.RootElement.GetString());
    }

    // An indentation indicator that is smaller than the indentation of the lines is valid: the extra spaces are the
    // start of the content.
    [Theory]
    [InlineData("a: |1\n    text\n", "   text\n")]
    [InlineData("a: |2\n    text\n", "  text\n")]
    [InlineData("a: |4\n    text\n", "text\n")]
    [InlineData("a: >1\n    text\n", "   text\n")]
    [InlineData("a: |9\n          text\n", " text\n")]
    public void IndicatorSmallerThanTheIndentationKeepsTheExtraSpaces(string yaml, string expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetProperty("a").GetString());
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
