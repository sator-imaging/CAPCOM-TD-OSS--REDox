using System;
using System.Buffers;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed partial class YamlOptionsTest
{
    private const string NestedMapAndSequenceYamlSource =
        "a:\n  b:\n    c: 1\nlist:\n  - x\n  - k: 2\n";

    private static string ParseYamlAndEncodeToYamlAgain(string yaml, YamlWriteOptions options)
    {
        using var document = YamlDocument.Parse(yaml);

        return YamlDocument.EncodeToString(document.RootElement, options);
    }

    [Fact]
    public void DefaultOutput_UsesTwoSpaceIndentationAndIndentsMapAfterBareDash()
    {
        var yaml = ParseYamlAndEncodeToYamlAgain(
            NestedMapAndSequenceYamlSource,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n", yaml);
    }

    [Theory]
    [InlineData(1, "a:\n b:\n  c: 1\nlist:\n - x\n -\n  k: 2\n")]
    [InlineData(2, "a:\n  b:\n    c: 1\nlist:\n  - x\n  -\n    k: 2\n")]
    [InlineData(4, "a:\n    b:\n        c: 1\nlist:\n    - x\n    -\n        k: 2\n")]
    public void ConfiguredIndentSize_IndentsEachLevelAndMapAfterBareDash(int size, string expected)
    {
        var yaml = ParseYamlAndEncodeToYamlAgain(
            NestedMapAndSequenceYamlSource,
            new YamlWriteOptions { NewLine = "\n", IndentSize = size }
        );

        Assert.Equal(expected, yaml);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void ConfiguredIndentation_RoundTripsValue(int size)
    {
        var yaml = ParseYamlAndEncodeToYamlAgain(
            NestedMapAndSequenceYamlSource,
            new YamlWriteOptions { IndentSize = size }
        );

        YamlTestFixture.Assert_YamlMatchesJsonValue_YamlRoundTrips(
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
        var yaml = ParseYamlAndEncodeToYamlAgain(
            NestedMapAndSequenceYamlSource,
            new YamlWriteOptions { NewLine = "\n", IndentSize = size }
        );

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
            YamlTestFixture.AssertYamlParseError(
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

        YamlTestFixture.AssertYamlParseError(
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
}