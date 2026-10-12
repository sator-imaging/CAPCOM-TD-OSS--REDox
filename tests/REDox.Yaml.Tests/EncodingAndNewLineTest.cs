using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed class EncodingAndNewLineTest
{
    [Fact]
    public void Utf16WithBom_AutoDetectsEncoding()
    {
        var bytes = Encoding
            .Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("a: 1\n"))
            .ToArray();

        using var document = YamlDocument.Parse(
            bytes,
            options: new YamlDocumentOptions { AutoDetectEncoding = true }
        );

        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
    }

    [Theory]
    [InlineData("utf-16be")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void OtherEncodingsWithBom_AutoDetectEncoding(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("a: 1\n")).ToArray();

        using var document = YamlDocument.Parse(
            bytes,
            options: new YamlDocumentOptions { AutoDetectEncoding = true }
        );

        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf8Bom_SkipsRegardlessOfAutoDetectOption(bool autoDetectEncoding)
    {
        var bytes = new byte[] { 0xef, 0xbb, 0xbf }
            .Concat(Encoding.UTF8.GetBytes("a: 1\n"))
            .ToArray();

        using var document = YamlDocument.Parse(
            bytes,
            options: new YamlDocumentOptions { AutoDetectEncoding = autoDetectEncoding }
        );

        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
    }

    [Fact]
    public void Utf16BomWithoutAutoDetect_FailsParsing()
    {
        var bytes = Encoding
            .Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("a: 1\n"))
            .ToArray();

        Assert.False(YamlDocument.TryParse(bytes, out var document));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("a: x: y\n", 4)]
    [InlineData("é日: x: y\n", 8)]
    [InlineData("😀: x: y\n", 7)]
    public void AsciiAndUnicodeBeforeParseError_ReportUtf8ByteOffset(
        string yaml,
        int expectedBytePosition
    )
    {
        var exception = YamlTestFixture.AssertYamlParseError(
            () => YamlDocument.Parse(yaml),
            "InvalidFormat",
            expectedBytePosition
        );

        Assert.Equal(expectedBytePosition, exception.BytePosition);
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void Utf16AndUtf32Input_PreserveValuesAndReportUtf8BytePositions(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        var yaml = "é日: \"😀 \\u00e9\"\nlist:\n  - 1\n";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(yaml)).ToArray();

        using var document = YamlDocument.Parse(
            bytes,
            options: new YamlDocumentOptions { AutoDetectEncoding = true }
        );

        Assert.Equal("😀 é", document.RootElement.GetProperty("é日").GetString());
        Assert.Equal(
            1,
            document.RootElement.GetProperty("list").AsArray()[0].AsElement().GetInt64()
        );

        var broken = encoding.GetPreamble().Concat(encoding.GetBytes("é日: x: y\n")).ToArray();
        var exception = YamlTestFixture.AssertYamlParseError(
            () =>
                YamlDocument.Parse(
                    broken,
                    options: new YamlDocumentOptions { AutoDetectEncoding = true }
                ),
            "InvalidFormat",
            8
        );

        Assert.Equal(8, exception.BytePosition);
    }

    [Fact]
    public void RequestedNewLine_AppearsInWriterOutput()
    {
        using var document = YamlDocument.Parse("a: 1\nb: 2\n");

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\r\n" }
        );

        Assert.Contains("\r\n", encoded);
    }
}