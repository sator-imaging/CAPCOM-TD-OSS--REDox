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
    public void JsonDataset_SerializerMatchesDocumentWriterOrSharesMultilineStyleRefusal(
        string filePath
    )
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var style in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var options = new YamlWriteOptions { NewLine = "\n", StringStyle = style };

            string yamlFromDocumentWriter;

            try
            {
                yamlFromDocumentWriter = YamlDocument.EncodeToString(
                    sourceJsonDocument.RootElement,
                    options
                );
            }
            catch (DocumentParseException exception)
                // A refusal skips both output comparisons, so both APIs must identify the same block scalar
                // limitation and the input must contain a string value that cannot be represented that way.
                when (style == YamlStringStyle.Multiline)
            {
                var bytes = File.ReadAllBytes(filePath);

                YamlTestHelper.AssertMultilineStyleRefusalReportsUnsupportedFeatureAndJsonHasUnrepresentableStringValue(
                    exception,
                    bytes,
                    Path.GetFileName(filePath)
                );

                var serializerException = YamlTestHelper.AssertYamlParseError(
                    () =>
                        YamlSerializer.Serialize(sourceJsonDocument.RootElement, options: options),
                    "UnsupportedFeature"
                );

                YamlTestHelper.AssertMultilineStyleRefusalReportsUnsupportedFeatureAndJsonHasUnrepresentableStringValue(
                    serializerException,
                    bytes,
                    Path.GetFileName(filePath)
                );

                continue;
            }

            var yamlFromSerializer = Encoding.UTF8.GetString(
                YamlSerializer.Serialize(sourceJsonDocument.RootElement, options: options)
            );

            using var serializerYamlDocument = YamlDocument.Parse(yamlFromSerializer);

            Assert.True(
                DValue.DeepEquals(
                    sourceJsonDocument.RootElement.AsValue(),
                    serializerYamlDocument.RootElement.AsValue()
                ),
                $"The serializer changed {Path.GetFileName(filePath)} in {style} mode."
            );

            // Compare text here because this test also requires both YAML writing APIs to produce identical output.
            Assert.True(
                yamlFromDocumentWriter == yamlFromSerializer,
                $"The serializer wrote {Path.GetFileName(filePath)} differently from the document writer in {style} mode."
            );
        }
    }

    // A float is the one value that is not written the same as the document writer writes it.
    // Serializer writes its own shortest text, and Writer writes the double that it widens to (not 0.1 for 0.1f).
    [Fact]
    public void FloatValue_SerializerWritesShortestTextWhileDocumentWriterWritesWidenedDouble()
    {
        var options = new YamlWriteOptions { NewLine = "\n" };
        var map = new Dictionary<string, float> { ["a"] = 0.1f };

        var yamlFromSerializer = Encoding.UTF8.GetString(
            YamlSerializer.Serialize(map, options: options)
        );

        var yamlFromDocumentWriter = YamlDocument.EncodeToString(
            DValue.From(map).AsElement(),
            options
        );

        Assert.Equal("a: 0.1\n", yamlFromSerializer);
        Assert.NotEqual("a: 0.1\n", yamlFromDocumentWriter);
    }
}
