using System;
using System.Buffers;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed partial class YamlOptionsTest
{
    private const string Nested = "a:\n  b:\n    c: 1\nlist:\n  - x\n  - k: 2\n";

    private static string Encode(string yaml, YamlWriteOptions options)
    {
        using var document = YamlDocument.Parse(yaml);

        return YamlDocument.EncodeToString(document.RootElement, options);
    }

    [Fact]
    public void DefaultOutput_UsesTwoSpaceIndentationAndIndentsMapAfterBareDash()
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n" });

        Assert.Equal("a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n", yaml);
    }

    [Theory]
    [InlineData(1, "a:\n b:\n  c: 1\nlist:\n - x\n -\n  k: 2\n")]
    [InlineData(2, "a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n")]
    [InlineData(4, "a:\n    b:\n        c: 1\nlist:\n    - x\n    -\n        k: 2\n")]
    public void ConfiguredIndentSize_IndentsEachLevelAndMapAfterBareDash(int size, string expected)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n", IndentSize = size });

        Assert.Equal(expected, yaml);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void ConfiguredIndentation_RoundTripsValue(int size)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { IndentSize = size });

        YamlTestHelper.AssertYamlParsesAsJsonValueAndRoundTrips(
            yaml,
            "{\"a\":{\"b\":{\"c\":1}},\"list\":[\"x\",{\"k\":2}]}"
        );
    }

    [Fact]
    public void OutOfRangeIndentSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () =>
            new YamlWriteOptions { IndentSize = -1 }
        );

        Assert.Throws<ArgumentOutOfRangeException>(static () =>
            new YamlWriteOptions { IndentSize = 128 }
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Indentation_UsesSpacesInsteadOfTabs(int size)
    {
        var yaml = Encode(Nested, new YamlWriteOptions { NewLine = "\n", IndentSize = size });

        Assert.DoesNotContain("\t", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteOrderMark_WritesOnlyWhenRequested(bool writeBom)
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
    public void ByteOrderMark_WritesFromEveryEntryPoint()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        var options = new YamlWriteOptions { WriteBom = true };

        using var stream = new System.IO.MemoryStream();

        YamlDocument.EncodeTo(document.RootElement, stream, options);

        Assert.True(stream.ToArray().AsSpan().StartsWith(Encoding.UTF8.Preamble));

        var buffer = new ArrayBufferWriter<byte>();

        YamlDocument.EncodeTo(document.RootElement, buffer, options);
        Assert.True(buffer.WrittenSpan.StartsWith(Encoding.UTF8.Preamble));
    }

    [Theory]
    [InlineData("1", 0, true)]
    [InlineData("[1]", 1, true)]
    [InlineData("[[1]]", 2, true)]
    [InlineData("[[1]]", 1, false)]
    [InlineData("a:\n  b: 1\n", 2, true)]
    [InlineData("a:\n  b: 1\n", 1, false)]
    [InlineData("{a: {b: 1}}", 2, true)]
    [InlineData("{a: {b: 1}}", 1, false)]
    public void MaxDepth_CountsRootAsZeroAndEnforcesConfiguredLimit(
        string yaml,
        int maxDepth,
        bool allowed
    )
    {
        var options = new YamlDocumentOptions { MaxDepth = maxDepth };

        if (allowed)
        {
            using var document = YamlDocument.Parse(yaml, options: options);

            Assert.True(document.RootElement.IsValid);
        }
        else
        {
            YamlTestHelper.AssertYamlParseError(
                () => YamlDocument.Parse(yaml, options: options),
                "MaxDepthExceeded"
            );
        }
    }

    [Fact]
    public void ZeroMaxDepth_AllowsUnlimitedDepth()
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
    public void RepeatedKeysInAnyMapStyle_ValidationRejects(string yaml)
    {
        var validating = new YamlDocumentOptions { EnableValueValidation = true };

        YamlTestHelper.AssertYamlParseError(
            () => YamlDocument.Parse(yaml, options: validating),
            "DuplicateKey"
        );

        using var lenient = YamlDocument.Parse(yaml);

        Assert.True(lenient.RootElement.IsValid);
    }

    [Theory]
    [InlineData("a: 1\nb:\n  a: 2\n")]
    [InlineData("a: 1\nb: [{a: 2}]\n")]
    [InlineData("{a: 1, b: 2}")]
    public void SameKeyInDifferentMaps_ValidationAllows(string yaml)
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
    public void EmptyQuotedString_ValidationAccepts(string yaml)
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
    public void EmptyValue_ValidationAcceptsAsNull(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { EnableValueValidation = true }
        );

        Assert.True(document.RootElement.IsValid);
    }

    // The explicit indentation indicator of a block scalar at the root counts from RootIndentationLevel:
    //   - 0 by default (libyaml)
    //   - -1 as in the YAML 1.2 specification
    [Theory]
    [InlineData(0, "|2-\n    indented\n", "  indented")]
    [InlineData(-1, "|2-\n    indented\n", "   indented")]
    [InlineData(1, "|2-\n    indented\n", " indented")]
    // A line with leading spaces is more indented, so a folded scalar keeps its line breaks.
    [InlineData(0, ">1-\n  a\n  b\n", " a\n b")]
    [InlineData(-1, ">1-\n  a\n  b\n", "  a\n  b")]
    public void RootIndentationLevel_SetsBlockScalarIndicatorOrigin(
        int rootIndentationLevel,
        string yaml,
        string expected
    )
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { RootIndentationLevel = rootIndentationLevel }
        );

        Assert.Equal(expected, document.RootElement.GetString());
    }

    [Fact]
    public void RootIndentationLevel_DefaultsToZero()
    {
        Assert.Equal(0, new YamlDocumentOptions().RootIndentationLevel);
        Assert.Equal(0, default(YamlDocumentOptions).RootIndentationLevel);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void RootIndentationLevel_DoesNotChangeBlockScalarBelowMapKey(int rootIndentationLevel)
    {
        using var document = YamlDocument.Parse(
            "a: |2-\n    indented\n",
            options: new YamlDocumentOptions { RootIndentationLevel = rootIndentationLevel }
        );

        Assert.Equal("  indented", document.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void RootIndentationLevelBelowMinusOne_Throws()
    {
        YamlTestHelper.AssertYamlParseError(
            static () => new YamlDocumentOptions { RootIndentationLevel = -2 },
            "InvalidOption"
        );
    }

    [Fact]
    public void ReplacedValue_WritesUpdatedContent()
    {
        using var document = YamlDocument.Parse("a: 1\nb: two\n");

        document.RootElement.AsObject()["a"] = DValue.Create("changed");

        Assert.Equal(
            "a: changed\nb: two\n",
            YamlDocument.EncodeToString(document.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void AddedValue_WritesAfterExistingValues()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        document.RootElement.AsObject().Add("b", DValue.Create(true));

        Assert.Equal(
            "a: 1\nb: true\n",
            YamlDocument.EncodeToString(document.RootElement, NewLineLf)
        );
    }

    [Fact]
    public void AddedStringLookingLikeAnotherType_WritesQuoted()
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
    public void ListEdits_WriteUpdatedContent()
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
    public void EditedDocument_ReadsBackWithEdits()
    {
        using var document = YamlDocument.Parse("a:\n  b: 1\nlist: [1, 2]\n");

        document.RootElement.GetProperty("a").AsObject()["b"] = DValue.Create("x: y");

        document.RootElement.GetProperty("list").AsArray().Add(DValue.Create(3));

        YamlTestHelper.AssertYamlParsesAsJsonValueAndRoundTrips(
            YamlDocument.EncodeToString(document.RootElement),
            "{\"a\":{\"b\":\"x: y\"},\"list\":[1,2,3]}"
        );
    }

    [Fact]
    public void EditingDuplicate_LeavesOriginalUnchanged()
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
    public void EditingOriginal_LeavesDuplicateUnchanged()
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
    public void Duplicate_WritesSameTextAsOriginal()
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

    private static readonly YamlWriteOptions NewLineLf = new() { NewLine = "\n" };
}
