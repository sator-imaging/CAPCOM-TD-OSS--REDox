using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetValidJsonFiles))]
    public void ValidJsonRoundTripsThroughYaml(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        using var source = JsonDocument.Parse(bytes);

        foreach (var mode in new[] { YamlStringMode.Quote, YamlStringMode.Multiline })
        {
            string yaml;
            try
            {
                yaml = YamlDocument.EncodeToString(source.RootElement, new YamlWriteOptions
                {
                    StringMode = mode
                });
            }
            catch (NotSupportedException) when (mode == YamlStringMode.Multiline)
            {
                // The explicit multiline mode rejects strings that block scalars cannot represent.
                continue;
            }

            using var parsed = YamlDocument.Parse(yaml);
            // Exercise the public JSON writer and parser after YAML decoding to cover the full conversion path.
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
            Assert.True(DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                $"YAML round-trip changed {Path.GetFileName(filePath)} in {mode} mode.");
        }
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e5")]
    [InlineData("-0")]
    [InlineData("18446744073709551616")]
    public void NumericJsonRoundTripsThroughPublicFormats(string json)
    {
        using var source = JsonDocument.Parse(json);
        foreach (var mode in new[] { YamlStringMode.Quote, YamlStringMode.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(source.RootElement, new YamlWriteOptions
            {
                StringMode = mode
            });
            using var parsed = YamlDocument.Parse(yaml);
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
            Assert.True(DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                $"YAML round-trip changed {json} in {mode} mode.");
        }
    }

    public static IEnumerable<object[]> GetValidJsonFiles()
    {
        var suite = FindSuiteDirectory();
        return Directory.EnumerateFiles(Path.Combine(suite, "test_parsing"), "y_*.json")
            .Concat(Directory.EnumerateFiles(Path.Combine(suite, "test_transform"), "*.json"))
            .Where(path => !DuplicateKeyCasesWithValidationDisabled.Contains(
                               Path.GetRelativePath(suite, path).Replace(Path.DirectorySeparatorChar, '/'),
                               StringComparer.Ordinal) &&
                           !UnsupportedIsolatedSurrogateCases.Contains(
                               Path.GetRelativePath(suite, path).Replace(Path.DirectorySeparatorChar, '/'),
                               StringComparer.Ordinal))
            .Select(path => new object[] { path });
    }

    private static string FindSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "JSONTestSuite");
            if (Directory.Exists(Path.Combine(path, "test_parsing"))) return path;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("JSONTestSuite has not been initialized.");
    }
}
