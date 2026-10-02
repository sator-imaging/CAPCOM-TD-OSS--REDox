using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed class YamlOptionsTest
{
    [Fact]
    public void AutoDetectsUtf16WithBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("a: 1\n")).ToArray();
        using var document = YamlDocument.Parse(bytes, options: new YamlDocumentOptions
        {
            AutoDetectEncoding = true
        });
        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
    }

    [Fact]
    public void WriterUsesRequestedNewLine()
    {
        using var document = YamlDocument.Parse("a: 1\nb: 2\n");
        var encoded = YamlDocument.EncodeToString(document.RootElement, new YamlWriteOptions
        {
            NewLine = "\r\n"
        });
        Assert.Contains("\r\n", encoded);
    }
}
