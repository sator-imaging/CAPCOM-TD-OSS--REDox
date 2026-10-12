using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlSerializerTest
{
    // These files contain control-character strings that prevent a complete round trip with *multiline-style* option.
    private static readonly string[] JsonFilesWithUnrepresentableMultilineStringValues =
    [
        "apache_builds.json",
        "github_events.json",
        "gsoc-2018.json",
        "twitter.json",
    ];

    [Fact]
    public void ParsedYamlElement_DeserializesWithRuntimeType()
    {
        using var document = YamlDocument.Parse("answer: 42\n");

        var result = YamlSerializer.Deserialize(
            document.RootElement,
            typeof(Dictionary<string, int>)
        );

        Assert.Equal(42, Assert.IsType<Dictionary<string, int>>(result)["answer"]);
    }

    [Fact]
    public void ParsedYamlElement_DeserializesWithGenericType()
    {
        using var document = YamlDocument.Parse("answer: 42\n");

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(document.RootElement);

        Assert.NotNull(result);
        Assert.Equal(42, result["answer"]);
    }

    [Fact]
    public void ParsedYamlElement_DeserializesIntoRuntimeTypedTarget()
    {
        using var document = YamlDocument.Parse("answer: 42\n");

        var target = new Dictionary<string, int>();

        var result = YamlSerializer.DeserializeTo(
            document.RootElement,
            typeof(Dictionary<string, int>),
            target
        );

        Assert.Equal(42, Assert.IsType<Dictionary<string, int>>(result)["answer"]);
        Assert.Equal(42, target["answer"]);
    }

    [Fact]
    public void ParsedYamlElement_DeserializesIntoGenericTarget()
    {
        using var document = YamlDocument.Parse("answer: 42\n");

        var target = new Dictionary<string, int>();

        var result = YamlSerializer.DeserializeTo(document.RootElement, target);

        Assert.Equal(42, result["answer"]);
        Assert.Equal(42, target["answer"]);
    }

    private static readonly string[] SerializerComparisonJsonFileNames =
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

    public static TheoryData<string> GetSerializerComparisonJsonFiles()
    {
        var directory = SimdJsonRoundTripTest.FindSimdJsonDatasetDirectory();

        return new TheoryData<string>(
            SerializerComparisonJsonFileNames.Select(name => Path.Combine(directory, name))
        );
    }

    [Theory]
    [MemberData(nameof(GetSerializerComparisonJsonFiles))]
    public void JsonDataset_SerializerPreservesParsedValuesAndMatchesDocumentWriterOrSharesMultilineRefusal(
        string filePath
    )
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var style in Enum.GetValues<YamlStringStyle>())
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
                when (JsonFilesWithUnrepresentableMultilineStringValues.Contains(
                          Path.GetFileName(filePath),
                          StringComparer.Ordinal
                      )
                      && style == YamlStringStyle.Multiline
                     )
            {
                var bytes = File.ReadAllBytes(filePath);

                YamlTestFixture.Assert_MultilineStringReportsUnsupportedFeature_JsonHasUnrepresentableStringValue(
                    exception,
                    bytes,
                    Path.GetFileName(filePath)
                );

                var serializerException = YamlTestFixture.AssertYamlParseError(
                    () =>
                        YamlSerializer.Serialize(sourceJsonDocument.RootElement, options: options),
                    "UnsupportedFeature"
                );

                YamlTestFixture.Assert_MultilineStringReportsUnsupportedFeature_JsonHasUnrepresentableStringValue(
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

    [Fact]
    public void FloatValue_SerializerWritesShortestTextWhileDocumentWriterWritesWidenedDouble()
    {
        var options = new YamlWriteOptions { NewLine = "\n" };
        var mapWithSinglePrecisionFloat = new Dictionary<string, float> { ["a"] = 0.1f };

        var yamlFromSerializer = Encoding.UTF8.GetString(
            YamlSerializer.Serialize(mapWithSinglePrecisionFloat, options: options)
        );

        var yamlFromDocumentWriter = YamlDocument.EncodeToString(
            DValue.From(mapWithSinglePrecisionFloat).AsElement(),
            options
        );

        Assert.Equal("a: 0.1\n", yamlFromSerializer);
        Assert.NotEqual("a: 0.1\n", yamlFromDocumentWriter);
    }
}