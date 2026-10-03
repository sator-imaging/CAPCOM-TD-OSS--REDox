using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox;
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
    public void IsolatedSurrogatesFailJsonRoundTripThroughYaml(string filePath)
    {
        using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));

        foreach (var mode in new[] { YamlStringMode.Quote, YamlStringMode.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(
                source.RootElement,
                new YamlWriteOptions { StringMode = mode }
            );
            using var parsed = YamlDocument.Parse(yaml);
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);

            // UTF-8 output replaces isolated UTF-16 surrogates, so these cases cannot preserve JSON values.
            Assert.False(
                DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
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
