using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    private static readonly string[] DuplicateKeyCasesWithValidationDisabled =
    [
        "test_parsing/y_object_duplicated_key.json",
        "test_parsing/y_object_duplicated_key_and_value.json",
        "test_transform/object_same_key_different_values.json",
        "test_transform/object_same_key_same_value.json",
        "test_transform/object_same_key_unclear_values.json",
    ];

    [Theory]
    [MemberData(nameof(GetDuplicateKeyFiles))]
    public void DuplicateJsonKeysWithValidationDisabled_RoundTrip(string filePath)
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var yamlFromJson = YamlDocument.EncodeToString(
                sourceJsonDocument.RootElement,
                new YamlWriteOptions { StringStyle = mode }
            );

            // JSON permits repeated member names; disable YAML key validation to retain every entry.
            using var convertedYamlDocument = YamlDocument.Parse(
                yamlFromJson,
                options: new YamlDocumentOptions { EnableValueValidation = false }
            );

            // DeepEquals resolves each map entry by key, so duplicate names can resolve to the same occurrence
            // and hide a changed or reordered entry. This test must preserve every occurrence and its position.
            var sourceJsonMap = sourceJsonDocument.RootElement.AsMap();
            var convertedYamlMap = convertedYamlDocument.RootElement.AsMap();

            Assert.Equal(sourceJsonMap.Count, convertedYamlMap.Count);

            for (var index = 0; index < sourceJsonMap.Count; index++)
            {
                Assert.True(
                    DValue.DeepEquals(sourceJsonMap[index].Key, convertedYamlMap[index].Key),
                    $"YAML changed key {index} of {Path.GetFileName(filePath)} in {mode} mode."
                );

                Assert.True(
                    DValue.DeepEquals(sourceJsonMap[index].Value, convertedYamlMap[index].Value),
                    $"YAML changed value {index} of {Path.GetFileName(filePath)} in {mode} mode."
                );
            }
        }
    }

    public static TheoryData<string> GetDuplicateKeyFiles()
    {
        var suite = FindSuiteDirectory();

        return new TheoryData<string>(
            DuplicateKeyCasesWithValidationDisabled.Select(path =>
                Path.Combine(suite, path.Replace('/', Path.DirectorySeparatorChar))
            )
        );
    }
}
