using System;
using System.Collections.Generic;
using System.Diagnostics;
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
public class YamlSerialize<T, U>
{
    private U? _root;

    [GlobalSetup]
    public void Setup()
    {
        var utf8Json = File.ReadAllBytes(DataSourceAttribute.GetFullPath<T>());

        _root = JsonSerializer.Deserialize<U>(utf8Json);

        if (REDoxYamlSerialize().Length == 0)
        {
            throw new InvalidOperationException();
        }
    }

    private U Root
    {
        get
        {
            Debug.Assert(_root != null);

            return _root;
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxYamlSerialize()
    {
        return YamlSerializer.Serialize(Root, SerializerSettings.Default);
    }
}