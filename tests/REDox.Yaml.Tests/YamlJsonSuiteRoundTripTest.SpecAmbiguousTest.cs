using System.IO;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetSpecAmbiguousJsonFiles))]
    public void IsRejectedByTheJsonReaderOrSurvivesYaml(string filePath)
    {
        string yaml;

        // The JSON reader may reject the file (a DocumentParseException). If it accepts it, the YAML written from it must
        // be read back, and written as JSON again, without any exception. The value need not be equal. The core validates
        // lazily, so the JSON reader can reject the file while the YAML is written as well as when it reads the file: both
        // are inside the try.
        try
        {
            using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));

            yaml = YamlDocument.EncodeToString(source.RootElement);
        }
        catch (DocumentParseException)
        {
            return;
        }

        using var parsed = YamlDocument.Parse(yaml);

        var json = JsonDocument.EncodeToString(parsed.RootElement);
        using var restored = JsonDocument.Parse(json);
    }

    public static TheoryData<string> GetSpecAmbiguousJsonFiles()
    {
        return new TheoryData<string>(
            YamlTestHelper.GetJsonSuiteFiles(YamlTestHelper.JsonSuiteSpecAmbiguousPattern)
        );
    }
}
