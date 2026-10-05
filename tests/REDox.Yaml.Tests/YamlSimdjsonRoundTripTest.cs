using System;
using System.IO;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlSimdjsonRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetJsonFiles))]
    public void NumberNestingAndUnicodeSimdjsonDatasets_RoundTripThroughYaml(string filePath)
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);

        using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            if (mode == YamlStringStyle.Quote)
            {
                YamlTestHelper.AssertJsonYamlJsonRoundTripPreservesValues(
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
            catch (DocumentParseException exception) when (mode == YamlStringStyle.Multiline)
            {
                YamlTestHelper.AssertMultilineStyleRefusalReportsUnsupportedFeatureAndJsonHasUnrepresentableStringValue(
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
    [MemberData(nameof(GetJsonFiles))]
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

        // This assertion checks stable YAML spelling; DeepEquals above checks the parsed data.
        Assert.True(
            yamlFromJson == yamlWrittenAgain,
            $"Writing the YAML of {Path.GetFileName(filePath)} a second time changed it."
        );
    }

    [Fact]
    public void NdjsonDatasetLines_RoundTripThroughYaml()
    {
        var path = Path.Combine(FindDataDirectory(), "amazon_cellphones.ndjson");
        var lines = File.ReadLines(path).Where(static line => line.Length > 0).ToArray();

        Assert.NotEmpty(lines);

        for (var index = 0; index < lines.Length; index++)
        {
            using var sourceJsonDocument = JsonDocument.Parse(lines[index]);

            foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
            {
                string yamlFromJson;

                try
                {
                    yamlFromJson = YamlDocument.EncodeToString(
                        sourceJsonDocument.RootElement,
                        new YamlWriteOptions { StringStyle = mode }
                    );
                }
                catch (DocumentParseException exception) when (mode == YamlStringStyle.Multiline)
                {
                    YamlTestHelper.AssertMultilineStyleRefusalReportsUnsupportedFeatureAndJsonHasUnrepresentableStringValue(
                        exception,
                        Encoding.UTF8.GetBytes(lines[index]),
                        $"line {index + 1} of the ndjson file"
                    );

                    continue;
                }

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

    // The benchmark datasets of simdjson, chosen by name:
    //   - large documents with many numbers (canada)
    //   - deep nesting and repeated structure (citm_catalog)
    //   - a lot of non-ASCII text (twitter)
    private static readonly string[] JsonFileNames =
    [
        "canada.json",
        "citm_catalog.json",
        "twitter.json",
    ];

    public static TheoryData<string> GetJsonFiles()
    {
        var directory = FindDataDirectory();

        return new TheoryData<string>(JsonFileNames.Select(name => Path.Combine(directory, name)));
    }

    internal static string FindDataDirectory()
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
