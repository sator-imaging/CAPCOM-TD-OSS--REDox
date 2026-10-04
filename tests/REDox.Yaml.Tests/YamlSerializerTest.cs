using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlSerializerTest
{
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

    // A float is the one value that is not written the same as the document writer writes it: the serializer writes its
    // own shortest text, and the document writer writes the double that it widens to (not 0.1 for 0.1f).
    [Fact]
    public void SingleIsNotWrittenAsTheDocumentWriterWritesIt()
    {
        var options = new YamlWriteOptions { NewLine = "\n" };
        var map = new Dictionary<string, float> { ["a"] = 0.1f };

        var direct = Encoding.UTF8.GetString(YamlSerializer.Serialize(map, options: options));
        var viaDocument = YamlDocument.EncodeToString(DValue.From(map).AsElement(), options);

        Assert.Equal("a: 0.1\n", direct);
        Assert.NotEqual("a: 0.1\n", viaDocument);
    }
}
