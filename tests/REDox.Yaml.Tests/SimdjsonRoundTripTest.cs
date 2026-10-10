using System;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class SimdJsonRoundTripTest
{
    private static readonly string[] NumericNestedAndUnicodeJsonFileNames =
    [
        "canada.json",
        "citm_catalog.json",
        "twitter.json",
    ];

    // The file also tests numbers and nesting, but its carriage returns
    // prevent a complete multiline-style round trip.
    private static readonly string[] JsonFilesWithUnrepresentableMultilineStringValues =
    [
        "twitter.json",
    ];

    private const string NdjsonSampleFileName = "amazon_cellphones.ndjson";

    [Theory]
    [MemberData(nameof(GetNumericNestedAndUnicodeSimdJsonFiles))]
    public void NumberNestingAndUnicodeSimdjsonDatasets_PreserveParsedValuesAndQuoteStyleRoundTripsOrJustifyMultilineRefusal(
        string filePath
    )
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);

        using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

        foreach (var mode in Enum.GetValues<YamlStringStyle>())
        {
            if (mode == YamlStringStyle.Quote)
            {
                YamlTestFixture.Assert_JsonYamlJsonRoundTrip_PreservesParsedValuesAtEveryStage(
                    sourceJsonDocument,
                    new YamlWriteOptions { StringStyle = mode },
                    Path.GetFileName(filePath)
                );

                continue;
            }

            string yamlFromJson;

            try
            {
                yamlFromJson = YamlDocument.EncodeToString(
                    sourceJsonDocument.RootElement,
                    new YamlWriteOptions { StringStyle = mode }
                );
            }
            catch (DocumentParseException exception)
                when (JsonFilesWithUnrepresentableMultilineStringValues.Contains(
                          Path.GetFileName(filePath),
                          StringComparer.Ordinal
                      )
                      && mode == YamlStringStyle.Multiline
                     )
            {
                YamlTestFixture.Assert_MultilineStringReportsUnsupportedFeature_JsonHasUnrepresentableStringValue(
                    exception,
                    sourceJsonBytes,
                    Path.GetFileName(filePath)
                );

                continue;
            }

            using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

            // Compare with the JSON reader before another JSON conversion can hide a YAML value change.
            Assert.True(
                DValue.DeepEquals(
                    sourceJsonDocument.RootElement.AsValue(),
                    convertedYamlDocument.RootElement.AsValue()
                ),
                $"YAML changed {Path.GetFileName(filePath)} in {mode} mode."
            );
        }
    }

    [Theory]
    [MemberData(nameof(GetNumericNestedAndUnicodeSimdJsonFiles))]
    public void SimdjsonDataset_PreservesParsedValueAndStableYamlSpelling(string filePath)
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        var options = new YamlWriteOptions { NewLine = "\n" };

        var yamlFromJson = YamlDocument.EncodeToString(sourceJsonDocument.RootElement, options);

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

        Assert.True(
            DValue.DeepEquals(
                sourceJsonDocument.RootElement.AsValue(),
                convertedYamlDocument.RootElement.AsValue()
            ),
            $"YAML changed {Path.GetFileName(filePath)}."
        );

        var yamlWrittenAgain = YamlDocument.EncodeToString(
            convertedYamlDocument.RootElement,
            options
        );

        Assert.True(
            yamlFromJson == yamlWrittenAgain,
            $"Writing the YAML of {Path.GetFileName(filePath)} a second time changed it."
        );
    }

    [Fact]
    public void NdjsonDatasetLines_PreserveParsedValuesWhenConvertedToYamlOrJustifyMultilineRefusal()
    {
        var path = Path.Combine(FindSimdJsonDatasetDirectory(), NdjsonSampleFileName);
        var lines = File.ReadLines(path).Where(static line => line.Length > 0).ToArray();

        Assert.NotEmpty(lines);

        for (var index = 0; index < lines.Length; index++)
        {
            using var sourceJsonDocument = JsonDocument.Parse(lines[index]);

            foreach (var mode in Enum.GetValues<YamlStringStyle>())
            {
                var yamlFromJson = YamlDocument.EncodeToString(
                    sourceJsonDocument.RootElement,
                    new YamlWriteOptions { StringStyle = mode }
                );

                using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

                Assert.True(
                    DValue.DeepEquals(
                        sourceJsonDocument.RootElement.AsValue(),
                        convertedYamlDocument.RootElement.AsValue()
                    ),
                    $"YAML changed line {index + 1} of the ndjson file in {mode} mode."
                );
            }
        }
    }

    public static TheoryData<string> GetNumericNestedAndUnicodeSimdJsonFiles()
    {
        var directory = FindSimdJsonDatasetDirectory();

        return new TheoryData<string>(
            NumericNestedAndUnicodeJsonFileNames.Select(name => Path.Combine(directory, name))
        );
    }

    internal static string FindSimdJsonDatasetDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "simdjson-data", "jsonexamples");
            if (Directory.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("simdjson-data has not been initialized.");
    }
}