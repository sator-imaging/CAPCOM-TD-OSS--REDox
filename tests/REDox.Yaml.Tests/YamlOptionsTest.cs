using System;
using System.Linq;
using System.Text;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlOptionsTest
{
    private const string Nested = "a:\n  b:\n    c: 1\nlist:\n  - x\n  - k: 2\n";

    private static string Encode(string yaml, YamlWriteOptions options)
    {
        using var document = YamlDocument.Parse(yaml);
        return YamlDocument.EncodeToString(document.RootElement, options);
    }

    // Writing: the text that comes out

    [Fact]
    public void DefaultOutputIsIndentedByTwoSpaces()
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n" });

        Assert.Equal("a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n", yaml);
    }

    // A map inside a list starts on the line after the dash, one level deeper.
    [Theory]
    [InlineData(1, "a:\n b:\n  c: 1\nlist:\n - x\n -\n  k: 2\n")]
    [InlineData(2, "a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n")]
    [InlineData(4, "a:\n    b:\n        c: 1\nlist:\n    - x\n    -\n        k: 2\n")]
    public void IndentSizeIsTheWidthOfEachLevel(int size, string expected)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n", IndentSize = size });

        Assert.Equal(expected, yaml);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void IndentedOutputReadsBackToTheSameValue(int size)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { IndentSize = size });

        YamlTestHelper.ParsesAs(yaml, "{\"a\":{\"b\":{\"c\":1}},\"list\":[\"x\",{\"k\":2}]}");
    }

    [Fact]
    public void IndentSizeOutOfRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlWriteOptions { IndentSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlWriteOptions { IndentSize = 128 });
    }

    // YAML does not allow a tab as indentation, so the output never holds one at the start of a line.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void IndentationIsAlwaysSpaces(int size)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n", IndentSize = size });

        Assert.DoesNotContain("\t", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteOrderMarkIsWrittenOnlyWhenRequested(bool writeBom)
    {
        using var document = YamlDocument.Parse("a: 1\n");

        var bytes = YamlDocument.Encode(
            document.RootElement,
            new YamlWriteOptions { WriteBom = writeBom }
        );

        Assert.Equal(writeBom, bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        using var parsed = YamlDocument.Parse(bytes);
        Assert.Equal(1, parsed.RootElement.GetProperty("a").GetInt64());
    }

    [Fact]
    public void ByteOrderMarkIsWrittenByEveryEntryPoint()
    {
        using var document = YamlDocument.Parse("a: 1\n");
        var options = new YamlWriteOptions { WriteBom = true };

        using var stream = new System.IO.MemoryStream();
        YamlDocument.EncodeTo(document.RootElement, stream, options);

        Assert.True(stream.ToArray().AsSpan().StartsWith(Encoding.UTF8.Preamble));

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        YamlDocument.EncodeTo(document.RootElement, buffer, options);
        Assert.True(buffer.WrittenSpan.StartsWith(Encoding.UTF8.Preamble));
    }

    // Reading: limits and validation

    // The root is depth 0, and every value below it, a scalar included, is one deeper.
    [Theory]
    [InlineData("1", 0, true)]
    [InlineData("[1]", 1, true)]
    [InlineData("[[1]]", 2, true)]
    [InlineData("[[1]]", 1, false)]
    [InlineData("a:\n  b: 1\n", 2, true)]
    [InlineData("a:\n  b: 1\n", 1, false)]
    [InlineData("{a: {b: 1}}", 2, true)]
    [InlineData("{a: {b: 1}}", 1, false)]
    public void MaxDepthAllowsExactlyTheLimit(string yaml, int maxDepth, bool allowed)
    {
        var options = new YamlDocumentOptions { MaxDepth = maxDepth };

        if (allowed)
        {
            using var document = YamlDocument.Parse(yaml, options: options);
            Assert.True(document.RootElement.IsValid);
        }
        else
        {
            Assert.ThrowsAny<DocumentParseException>(() =>
                YamlDocument.Parse(yaml, options: options)
            );
        }
    }

    [Fact]
    public void MaxDepthZeroMeansNoLimit()
    {
        var yaml = new string('[', 200) + new string(']', 200);

        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { MaxDepth = 0 }
        );

        Assert.True(document.RootElement.IsValid);
    }

    [Theory]
    [InlineData("a: 1\na: 2\n")]
    [InlineData("{a: 1, a: 2}")]
    [InlineData("x:\n  a: 1\n  a: 2\n")]
    [InlineData("- {a: 1, a: 2}\n")]
    public void ValidationRejectsRepeatedKeysInEveryMapStyle(string yaml)
    {
        var validating = new YamlDocumentOptions { EnableValueValidation = true };

        Assert.ThrowsAny<DocumentParseException>(() =>
            YamlDocument.Parse(yaml, options: validating)
        );
        using var lenient = YamlDocument.Parse(yaml);
        Assert.True(lenient.RootElement.IsValid);
    }

    [Theory]
    [InlineData("a: 1\nb:\n  a: 2\n")]
    [InlineData("a: 1\nb: [{a: 2}]\n")]
    [InlineData("{a: 1, b: 2}")]
    public void ValidationAllowsTheSameKeyInDifferentMaps(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { EnableValueValidation = true }
        );

        Assert.True(document.RootElement.IsValid);
    }

    [Theory]
    [InlineData("a: 1\nb: \"\"\n")]
    [InlineData("a: 'text'\nb: ''\n")]
    [InlineData("a: [\"\", x]\n")]
    public void ValidationKeepsAnEmptyQuotedString(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { EnableValueValidation = true }
        );

        Assert.True(document.RootElement.IsValid);
    }

    [Theory]
    [InlineData("a:\nb: 1\n")]
    [InlineData("a: ~\n")]
    [InlineData("- \n- 1\n")]
    public void ValidationKeepsAnEmptyValueAsNull(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { EnableValueValidation = true }
        );

        Assert.True(document.RootElement.IsValid);
    }

    // The explicit indentation indicator of a block scalar at the root counts from RootIndent: 0 by default (libyaml),
    // -1 as in the YAML 1.2 specification.
    [Theory]
    [InlineData(0, "|2-\n    indented\n", "  indented")]
    [InlineData(-1, "|2-\n    indented\n", "   indented")]
    [InlineData(1, "|2-\n    indented\n", " indented")]
    // A line with leading spaces is more indented, so a folded scalar keeps its line breaks.
    [InlineData(0, ">1-\n  a\n  b\n", " a\n b")]
    [InlineData(-1, ">1-\n  a\n  b\n", "  a\n  b")]
    public void RootIndentSetsWhereTheIndicatorOfARootBlockScalarCounts(
        int rootIndent,
        string yaml,
        string expected
    )
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { RootIndent = rootIndent }
        );

        Assert.Equal(expected, document.RootElement.GetString());
    }

    [Fact]
    public void RootIndentDefaultsToZero()
    {
        Assert.Equal(0, new YamlDocumentOptions().RootIndent);
        Assert.Equal(0, default(YamlDocumentOptions).RootIndent);
    }

    // A block scalar below a key is not at the root, so RootIndent has no effect on it.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void RootIndentDoesNotChangeANestedBlockScalar(int rootIndent)
    {
        using var document = YamlDocument.Parse(
            "a: |2-\n    indented\n",
            options: new YamlDocumentOptions { RootIndent = rootIndent }
        );

        Assert.Equal("  indented", document.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void RootIndentLessThanMinusOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new YamlDocumentOptions { RootIndent = -2 }
        );
    }

    // Editing a parsed document

    [Fact]
    public void ReplacedValueIsWritten()
    {
        using var document = YamlDocument.Parse("a: 1\nb: two\n");

        document.RootElement.AsObject()["a"] = DValue.Create("changed");

        Assert.Equal(
            "a: changed\nb: two\n",
            YamlDocument.EncodeToString(document.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void AddedValueIsWrittenAfterTheExistingOnes()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        document.RootElement.AsObject().Add("b", DValue.Create(true));

        Assert.Equal(
            "a: 1\nb: true\n",
            YamlDocument.EncodeToString(document.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void AddedStringThatLooksLikeAnotherTypeIsQuoted()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        document.RootElement.AsObject().Add("b", DValue.Create("true"));
        document.RootElement.AsObject().Add("c", DValue.Create("2001-12-14"));

        var yaml = YamlDocument.EncodeToString(document.RootElement, NewLineLf);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal("true", parsed.RootElement.GetProperty("b").GetString());
        Assert.Equal("2001-12-14", parsed.RootElement.GetProperty("c").GetString());
    }

    [Fact]
    public void ListEditsAreWritten()
    {
        using var document = YamlDocument.Parse("- 1\n- 2\n- 3\n");

        var array = document.RootElement.AsArray();
        array.Add(DValue.Create(4));
        array.RemoveAt(0);
        array[0] = DValue.Create("two");

        Assert.Equal(
            "- two\n- 3\n- 4\n",
            YamlDocument.EncodeToString(document.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void EditedDocumentReadsBackAsEdited()
    {
        using var document = YamlDocument.Parse("a:\n  b: 1\nlist: [1, 2]\n");

        document.RootElement.GetProperty("a").AsObject()["b"] = DValue.Create("x: y");
        document.RootElement.GetProperty("list").AsArray().Add(DValue.Create(3));

        YamlTestHelper.ParsesAs(
            YamlDocument.EncodeToString(document.RootElement),
            "{\"a\":{\"b\":\"x: y\"},\"list\":[1,2,3]}"
        );
    }

    [Fact]
    public void EditingTheDuplicateLeavesTheOriginalAlone()
    {
        using var original = YamlDocument.Parse("a: 1\nlist:\n  - x\ntext: hello\n");
        using var duplicate = original.Duplicate();

        duplicate.RootElement.AsObject()["a"] = DValue.Create(2);
        duplicate.RootElement.AsObject().Add("added", DValue.Create(true));
        duplicate.RootElement.GetProperty("list").AsArray().Add(DValue.Create("y"));

        Assert.Equal(
            "a: 1\nlist:\n  - x\ntext: hello\n",
            YamlDocument.EncodeToString(original.RootElement, NewLineLf)
        );
        Assert.Equal(2, duplicate.RootElement.GetProperty("a").GetInt64());
        Assert.Equal(2, duplicate.RootElement.GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void EditingTheOriginalLeavesTheDuplicateAlone()
    {
        using var original = YamlDocument.Parse("a: 1\nlist:\n  - x\ntext: hello\n");
        using var duplicate = original.Duplicate();

        original.RootElement.AsObject()["a"] = DValue.Create(2);
        original.RootElement.AsObject().Add("added", DValue.Create(true));
        original.RootElement.GetProperty("list").AsArray().Add(DValue.Create("y"));

        Assert.Equal(
            "a: 1\nlist:\n  - x\ntext: hello\n",
            YamlDocument.EncodeToString(duplicate.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void DuplicateWritesTheSameTextAsTheOriginal()
    {
        const string yaml = "# head\na: 1 # one\nb:\n  - 'x'\n  - 2001-12-14\n";
        using var original = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );
        using var duplicate = original.Duplicate();
        var options = new YamlWriteOptions { NewLine = "\n", PreserveTrivia = true };

        Assert.Equal(
            YamlDocument.EncodeToString(original.RootElement, options),
            YamlDocument.EncodeToString(duplicate.RootElement, options)
        );
    }

    private static readonly YamlWriteOptions NewLineLf = new YamlWriteOptions { NewLine = "\n" };
}
