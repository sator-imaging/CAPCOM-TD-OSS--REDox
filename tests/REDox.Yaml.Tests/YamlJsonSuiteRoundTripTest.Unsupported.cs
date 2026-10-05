using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    private static readonly string[] UnsupportedIsolatedSurrogateCases =
    [
        "test_transform/string_1_escaped_invalid_codepoint.json",
        "test_transform/string_2_escaped_invalid_codepoints.json",
        "test_transform/string_3_escaped_invalid_codepoints.json",
    ];

    [Theory]
    [MemberData(nameof(GetUnsupportedIsolatedSurrogateFiles))]
    public void IsolatedSurrogates_ChangeDuringJsonRoundTripThroughYaml(string filePath)
    {
        using var sourceJsonDocument = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var yamlFromJson = YamlDocument.EncodeToString(
                sourceJsonDocument.RootElement,
                new YamlWriteOptions { StringStyle = mode }
            );

            using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

            // The JSON reader's isolated surrogate cannot survive YAML's UTF-8 representation. This assertion
            // records that the value changed; it does not verify which replacement value YAML produced.
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
        var suite = FindSuiteDirectory();

        return new TheoryData<string>(
            UnsupportedIsolatedSurrogateCases.Select(path =>
                Path.Combine(suite, path.Replace('/', Path.DirectorySeparatorChar))
            )
        );
    }
}
