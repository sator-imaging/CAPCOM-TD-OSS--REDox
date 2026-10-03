using System;
using System.IO;
using System.Linq;
using System.Text;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlSerializerTest
{
    // The datasets of the benchmarks (REDox.Benchmarks.Data).
    private static readonly string[] JsonFileNames =
    [
        "twitter.json",
        "canada.json",
        "citm_catalog.json",
        "numbers.json",
        "apache_builds.json",
        "github_events.json",
        "gsoc-2018.json",
        "update-center.json",
        "mesh.json",
        "marine_ik.json",
        "random.json",
        "instruments.json",
    ];

    public static TheoryData<string> GetJsonFiles()
    {
        var directory = YamlSimdjsonRoundTripTest.FindDataDirectory();
        return new TheoryData<string>(JsonFileNames.Select(name => Path.Combine(directory, name)));
    }

    [Theory]
    [MemberData(nameof(GetJsonFiles))]
    public void SerializerWritesTheSameTextAsTheDocumentWriter(string filePath)
    {
        using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var style in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var options = new YamlWriteOptions { NewLine = "\n", StringStyle = style };

            string viaDocument;
            try
            {
                viaDocument = YamlDocument.EncodeToString(source.RootElement, options);
            }
            catch (NotSupportedException) when (style == YamlStringStyle.Multiline)
            {
                Assert.Throws<NotSupportedException>(() =>
                    YamlSerializer.Serialize(source.RootElement, options: options)
                );
                continue;
            }

            var direct = Encoding.UTF8.GetString(
                YamlSerializer.Serialize(source.RootElement, options: options)
            );

            Assert.True(
                viaDocument == direct,
                $"The serializer wrote {Path.GetFileName(filePath)} differently from the document writer in {style} mode."
            );
        }
    }
}
