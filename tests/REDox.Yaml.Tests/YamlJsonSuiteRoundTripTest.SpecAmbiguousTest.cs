using System.IO;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetSpecAmbiguousJsonFiles))]
    public void SpecAmbiguousJsonFile_IsRejectedOrSurvivesYamlConversion(string filePath)
    {
        var sourceJsonBytes = File.ReadAllBytes(filePath);
        string yamlFromJson;

        // The JSON specification permits either accepting or rejecting these i_* files. A JSON rejection ends this
        // case without exercising YAML. The core validates lazily, so rejection can occur during YAML writing too.
        // For accepted files this checks only that the YAML parses: ambiguous values may change, and no value
        // comparison is made here.
        try
        {
            using var sourceJsonDocument = JsonDocument.Parse(sourceJsonBytes);

            yamlFromJson = YamlDocument.EncodeToString(sourceJsonDocument.RootElement);
        }
        catch (DocumentParseException exception)
        {
            YamlTestHelper.AssertParseErrorCameFromJsonDocument(exception, sourceJsonBytes);

            return;
        }

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);
    }

    public static TheoryData<string> GetSpecAmbiguousJsonFiles()
    {
        var cases = new TheoryData<string>(
            YamlTestHelper.GetJsonSuiteFiles(YamlTestHelper.JsonSuiteSpecAmbiguousPattern)
        );

        Assert.Equal(35, cases.Count);

        return cases;
    }
}
