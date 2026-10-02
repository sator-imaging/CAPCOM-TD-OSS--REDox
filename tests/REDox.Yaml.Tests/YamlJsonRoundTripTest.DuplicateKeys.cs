using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonRoundTripTest
{
    private static readonly string[] DuplicateKeyCasesWithValidationDisabled =
    [
        "test_parsing/y_object_duplicated_key.json",
        "test_parsing/y_object_duplicated_key_and_value.json",
        "test_transform/object_same_key_different_values.json",
        "test_transform/object_same_key_same_value.json",
        "test_transform/object_same_key_unclear_values.json"
    ];

    [Theory]
    [MemberData(nameof(GetDuplicateKeyFiles))]
    public void DuplicateJsonKeysRoundTripWithValidationDisabled(string filePath)
    {
        using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));
        foreach (var mode in new[] { YamlStringMode.Quote, YamlStringMode.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(source.RootElement, new YamlWriteOptions
            {
                StringMode = mode
            });
            // JSON permits repeated member names; disable YAML key validation to retain every entry.
            using var parsed = YamlDocument.Parse(yaml, options: new YamlDocumentOptions
            {
                EnableValueValidation = false
            });
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
            // DeepEquals looks up map values by key, so it cannot compare repeated keys entry by entry.
            // Compare both public JSON outputs to preserve every repeated key and its position.
            Assert.Equal(JsonDocument.EncodeToString(source.RootElement),
                JsonDocument.EncodeToString(restored.RootElement));
        }
    }

    public static IEnumerable<object[]> GetDuplicateKeyFiles()
    {
        var suite = FindSuiteDirectory();
        return DuplicateKeyCasesWithValidationDisabled.Select(path => new object[]
        {
            Path.Combine(suite, path.Replace('/', Path.DirectorySeparatorChar))
        });
    }
}
