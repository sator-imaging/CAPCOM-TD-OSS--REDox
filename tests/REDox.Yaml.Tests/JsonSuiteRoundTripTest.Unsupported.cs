using System;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class JsonSuiteRoundTripTest
{
    private static readonly string[] UnsupportedIsolatedSurrogateCases =
    [
        "test_transform/string_1_escaped_invalid_codepoint.json",
        "test_transform/string_2_escaped_invalid_codepoints.json",
        "test_transform/string_3_escaped_invalid_codepoints.json",
    ];

    [Theory]
    [MemberData(nameof(GetUnsupportedIsolatedSurrogateFiles))]
    public void IsolatedSurrogates_ReplacedOnSerialize(string filePath)
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var mode in Enum.GetValues<YamlStringStyle>())
        {
            var yamlFromJson = YamlDocument.EncodeToString(
                sourceJsonDocument.RootElement,
                new YamlWriteOptions { StringStyle = mode }
            );

            using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

            Assert.False(
                DValue.DeepEquals(
                    sourceJsonDocument.RootElement.AsValue(),
                    convertedYamlDocument.RootElement.AsValue()
                ),
                $"YAML unexpectedly preserved {Path.GetFileName(filePath)} in {mode} mode."
            );
        }
    }

    public static TheoryData<string> GetUnsupportedIsolatedSurrogateFiles()
    {
        var suite = YamlTestFixture.FindJsonSuiteDirectory();

        return new TheoryData<string>(
            UnsupportedIsolatedSurrogateCases.Select(path =>
                Path.Combine(suite, path.Replace('/', Path.DirectorySeparatorChar))
            )
        );
    }
}