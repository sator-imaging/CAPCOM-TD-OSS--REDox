using System;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;
using REDox.Json;

namespace REDox.Yaml.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter))]
[GenericTypeArguments(typeof(Canada))]
[GenericTypeArguments(typeof(CitmCatalog))]
public class YamlParse<T>
{
    private byte[] _utf8Json = [];
    private string _yaml = string.Empty;
    private byte[] _utf8Yaml = [];
    private byte[] _utf16Yaml = [];

    // The YAML is made once from the JSON of the dataset, so no data file of its own is needed.
    [GlobalSetup]
    public void Setup()
    {
        _utf8Json = File.ReadAllBytes(DataSourceAttribute.GetFullPath<T>());

        using var json = JsonDocument.Parse(_utf8Json);
        _yaml = YamlDocument.EncodeToString(json.RootElement);
        _utf8Yaml = Encoding.UTF8.GetBytes(_yaml);
        _utf16Yaml = Encoding
            .Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(_yaml))
            .ToArray();

        var expected = JsonParseForScale();
        if (
            ParseUtf8Bytes() != expected
            || ParseString() != expected
            || ParseUtf16Bytes() != expected
        )
        {
            throw new InvalidOperationException();
        }
    }

    // JSON of the same dataset, for the scale of the numbers.
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int JsonParseForScale()
    {
        using var doc = JsonDocument.Parse(_utf8Json);

        return Count(doc.RootElement);
    }

    // UTF-8 bytes are read where they are.
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int ParseUtf8Bytes()
    {
        using var doc = YamlDocument.Parse(_utf8Yaml);

        return Count(doc.RootElement);
    }

    // The string is converted to UTF-8 first.
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int ParseString()
    {
        using var doc = YamlDocument.Parse(_yaml);

        return Count(doc.RootElement);
    }

    // UTF-16 is converted to UTF-8 in a rented buffer first.
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int ParseUtf16Bytes()
    {
        using var doc = YamlDocument.Parse(
            _utf16Yaml,
            options: new YamlDocumentOptions { AutoDetectEncoding = true }
        );

        return Count(doc.RootElement);
    }

    private static int Count(DElement root)
    {
        if (root.Token.Type == DTokenType.Array)
        {
            return root.GetArrayLength();
        }

        return root.GetPropertyCount();
    }
}
