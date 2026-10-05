using System;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    // These cover distinct JSON value shapes without repeating the longer format conversion for every suite file.
    private static readonly string[] LosslessConversionCaseNames =
    [
        "y_array_heterogeneous.json",
        "y_object_basic.json",
        "y_number_simple_real.json",
        "y_string_unicode.json",
    ];

    [Theory]
    [MemberData(nameof(GetValidJsonFiles))]
    public void ValidJsonSuiteFile_RoundTripsThroughYaml(string filePath)
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);

        using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            if (
                mode == YamlStringStyle.Quote
                && LosslessConversionCaseNames.Contains(
                    Path.GetFileName(filePath),
                    StringComparer.Ordinal
                )
            )
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

            // Check YAML directly against the JSON reader so another conversion cannot hide a value change.
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
    [InlineData("1.0")]
    [InlineData("1e5")]
    [InlineData("-0")]
    [InlineData("18446744073709551616")]
    public void NumericJson_RoundTripsThroughYaml(string json)
    {
        using var sourceJsonDocument = JsonDocument.Parse(json);

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
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
                $"YAML changed {json} in {mode} mode."
            );
        }
    }

    public static TheoryData<string> GetValidJsonFiles()
    {
        var suite = FindSuiteDirectory();

        var files = YamlTestHelper
            .GetJsonSuiteFilesEligibleForYamlRoundTrip()
            .Where(path =>
                !DuplicateKeyCasesWithValidationDisabled.Contains(
                    Path.GetRelativePath(suite, path).Replace(Path.DirectorySeparatorChar, '/'),
                    StringComparer.Ordinal
                )
                && !UnsupportedIsolatedSurrogateCases.Contains(
                    Path.GetRelativePath(suite, path).Replace(Path.DirectorySeparatorChar, '/'),
                    StringComparer.Ordinal
                )
            )
            .ToArray();

        Assert.Equal(109, files.Length);

        return new TheoryData<string>(files);
    }

    private static string FindSuiteDirectory() => YamlTestHelper.FindJsonSuiteDirectory();
}
