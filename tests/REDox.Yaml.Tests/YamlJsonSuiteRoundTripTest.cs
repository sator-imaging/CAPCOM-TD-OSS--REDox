using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlJsonSuiteRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetValidJsonFiles))]
    public void ValidJsonRoundTripsThroughYaml(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        using var source = JsonDocument.Parse(bytes);

        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            string yaml;
            try
            {
                yaml = YamlDocument.EncodeToString(
                    source.RootElement,
                    new YamlWriteOptions { StringStyle = mode }
                );
            }
            catch (NotSupportedException) when (mode == YamlStringStyle.Multiline)
            {
                YamlTestHelper.AssertMultilineRefusalIsJustified(bytes, Path.GetFileName(filePath));
                continue;
            }

            using var parsed = YamlDocument.Parse(yaml);
            // Exercise the public JSON writer and parser after YAML decoding to cover the full conversion path.
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
            Assert.True(
                DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                $"YAML round-trip changed {Path.GetFileName(filePath)} in {mode} mode."
            );
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
        foreach (var mode in new[] { YamlStringStyle.Quote, YamlStringStyle.Multiline })
        {
            var yaml = YamlDocument.EncodeToString(
                source.RootElement,
                new YamlWriteOptions { StringStyle = mode }
            );
            using var parsed = YamlDocument.Parse(yaml);
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
            Assert.True(
                DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                $"YAML round-trip changed {json} in {mode} mode."
            );
        }
    }

    public static TheoryData<string> GetValidJsonFiles()
    {
        var suite = FindSuiteDirectory();
        return new TheoryData<string>(
            YamlTestHelper
                .GetJsonSuiteValidFiles()
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
        );
    }

    private static string FindSuiteDirectory() => YamlTestHelper.FindJsonSuiteDirectory();
}
