using System.IO;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class JsonSuiteRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetSpecAmbiguousJsonFiles))]
    public void SpecAmbiguousJsonFile_IsRejectedByJsonReaderOrProducesParseableYaml(string filePath)
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);
        string yamlFromJson;

        // The JSON specification permits either accepting or rejecting these i_* files.
        // A JSON rejection ends this test case without exercising YAML.
        // The core validates lazily, so rejection can occur during YAML writing too.
        try
        {
            using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

            yamlFromJson = YamlDocument.EncodeToString(sourceJsonDocument.RootElement);
        }
        catch (DocumentParseException exception)
        {
            YamlTestFixture.AssertParseError_FromJsonDocument(exception, sourceJsonBytes);

            return;
        }

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);
    }

    public static TheoryData<string> GetSpecAmbiguousJsonFiles()
    {
        var cases = new TheoryData<string>(
            YamlTestFixture.GetJsonSuiteFiles(YamlTestFixture.JsonSuiteSpecAmbiguousPattern)
        );

        Assert.Equal(35, cases.Count);

        return cases;
    }
}