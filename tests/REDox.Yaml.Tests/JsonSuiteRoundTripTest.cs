using System;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class JsonSuiteRoundTripTest
{
    private static readonly string[] LosslessConvertibleCaseNames =
    [
        "y_array_heterogeneous.json",
        "y_object_basic.json",
        "y_number_simple_real.json",
        "y_string_unicode.json",
    ];

    // These files contain control-character strings that prevent a complete round trip with *multiline-style* option.
    private static readonly string[] JsonFilesWithUnrepresentableMultilineStringValues =
    [
        "string_with_escaped_NULL.json",
        "y_string_allowed_escapes.json",
        "y_string_escaped_control_character.json",
        "y_string_null_escape.json",
        "y_string_unescaped_char_delete.json",
        "y_string_with_del_character.json",
    ];

    [Theory]
    [MemberData(nameof(GetValidJsonSuiteFilesEligibleForYamlRoundTrip))]
    public void ValidJsonSuiteFile_PreservesParsedValuesAndSelectedRoundTripsOrJustifiesMultilineRefusal(
        string filePath
    )
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);

        using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

        foreach (var mode in Enum.GetValues<YamlStringStyle>())
        {
            if (
                mode == YamlStringStyle.Quote
                && LosslessConvertibleCaseNames.Contains(
                    Path.GetFileName(filePath),
                    StringComparer.Ordinal
                )
            )
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

            Assert.True(
                DValue.DeepEquals(
                    sourceJsonDocument.RootElement.AsValue(),
                    convertedYamlDocument.RootElement.AsValue()
                ),
                $"YAML changed {Path.GetFileName(filePath)} in {mode} mode."
            );
        }
    }

    public static TheoryData<string> GetValidJsonSuiteFilesEligibleForYamlRoundTrip()
    {
        var suite = YamlTestFixture.FindJsonSuiteDirectory();

        var files = YamlTestFixture
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
}