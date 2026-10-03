using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed class YamlEncodingAndNewLineTest
{
    [Fact]
    public void AutoDetectsUtf16WithBom()
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
    public void AutoDetectsOtherEncodingsWithBom(string name)
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
    public void Utf8BomIsAlwaysSkipped(bool autoDetectEncoding)
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
    public void Utf16BomIsNotConvertedWithoutAutoDetect()
    {
        var bytes = Encoding
            .Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("a: 1\n"))
            .ToArray();
        Assert.False(YamlDocument.TryParse(bytes, out var document));
        Assert.Null(document);
    }

    // The position of a parse error is an offset in the UTF-8 bytes, so characters of more than one byte before it
    // count for each of their bytes: "é" is 2 bytes and "日" is 3.
    [Theory]
    [InlineData("a: x: y\n", 4)]
    [InlineData("é日: x: y\n", 8)]
    [InlineData("😀: x: y\n", 7)]
    public void ErrorPositionIsAByteOffset(string yaml, int expectedBytePosition)
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(yaml));

        Assert.Equal(expectedBytePosition, exception.BytePosition);
    }

    // The same text in UTF-16 and UTF-32 is converted to UTF-8 first, so it reads the same and the position of an error
    // is still an offset in the UTF-8 text.
    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void ConvertedTextReadsTheSameAndReportsUtf8Positions(string name)
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
        var exception = Assert.ThrowsAny<DocumentParseException>(() =>
            YamlDocument.Parse(
                broken,
                options: new YamlDocumentOptions { AutoDetectEncoding = true }
            )
        );
        Assert.Equal(8, exception.BytePosition);
    }

    [Fact]
    public void WriterUsesRequestedNewLine()
    {
        using var document = YamlDocument.Parse("a: 1\nb: 2\n");
        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\r\n" }
        );
        Assert.Contains("\r\n", encoded);
    }
}
