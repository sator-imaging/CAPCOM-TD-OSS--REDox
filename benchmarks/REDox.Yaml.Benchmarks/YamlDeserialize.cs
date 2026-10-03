using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;

namespace REDox.Yaml.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter), typeof(Twitter.Root))]
[GenericTypeArguments(typeof(Canada), typeof(Canada.Root))]
[GenericTypeArguments(typeof(CitmCatalog), typeof(CitmCatalog.Root))]
[GenericTypeArguments(typeof(Numbers), typeof(double[]))]
[GenericTypeArguments(typeof(ApacheBuilds), typeof(ApacheBuilds.Root))]
[GenericTypeArguments(typeof(GitHubEvents), typeof(GitHubEvents.GitHubEvent[]))]
[GenericTypeArguments(typeof(Gsoc2018), typeof(Dictionary<int, Gsoc2018.GsocProject>))]
[GenericTypeArguments(typeof(UpdateCenterTest), typeof(UpdateCenterTest.Root))]
[GenericTypeArguments(typeof(Mesh), typeof(Mesh.Root))]
[GenericTypeArguments(typeof(MarineIk), typeof(MarineIk.Root))]
[GenericTypeArguments(
    typeof(REDox.Benchmarks.Data.Random),
    typeof(REDox.Benchmarks.Data.Random.Root)
)]
[GenericTypeArguments(typeof(Instruments), typeof(Instruments.Root))]
public class YamlDeserialize<T, U>
{
    private byte[] _yaml = [];

    [GlobalSetup]
    public void Setup()
    {
        var utf8Json = File.ReadAllBytes(DataSourceAttribute.GetFullPath<T>());

        var root = JsonSerializer.Deserialize<U>(utf8Json);
        _yaml = YamlDocument.Encode(DValue.From(root).AsElement());

        var json1 = JsonSerializer.Serialize(REDoxYamlDeserialize());
        var json2 = JsonSerializer.Serialize(root);

        if (json1 != json2)
        {
            throw new InvalidOperationException();
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxYamlDeserialize()
    {
        using var document = YamlDocument.Parse(_yaml);

        return document.RootElement.To<U>();
    }
}
