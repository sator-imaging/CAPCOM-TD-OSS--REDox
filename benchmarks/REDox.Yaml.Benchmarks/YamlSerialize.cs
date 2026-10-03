using System;
using System.Diagnostics;
using System.IO;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;
using REDox.Json;

namespace REDox.Yaml.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter))]
[GenericTypeArguments(typeof(Canada))]
[GenericTypeArguments(typeof(CitmCatalog))]
public class YamlSerialize<T>
{
    private JsonDocument? _json;

    [GlobalSetup]
    public void Setup()
    {
        _json = JsonDocument.Parse(File.ReadAllBytes(DataSourceAttribute.GetFullPath<T>()));

        if (EncodeToString().Length != EncodeToBytes().Length)
        {
            throw new InvalidOperationException();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _json?.Dispose();
    }

    private DElement Root
    {
        get
        {
            Debug.Assert(_json != null); // Setup sets it before any benchmark runs.
            return _json.RootElement;
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public string EncodeToString()
    {
        return YamlDocument.EncodeToString(Root);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] EncodeToBytes()
    {
        return YamlDocument.Encode(Root);
    }
}
