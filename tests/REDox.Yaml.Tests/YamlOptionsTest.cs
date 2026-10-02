using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed class YamlOptionsTest
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
