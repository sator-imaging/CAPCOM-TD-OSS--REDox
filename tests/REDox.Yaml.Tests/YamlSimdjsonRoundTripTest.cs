using System;
using System.IO;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlSimdjsonRoundTripTest
{
    [Theory]
    [MemberData(nameof(GetJsonFiles))]
    public void JsonRoundTripsThroughYaml(string filePath)
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
            using var restored = JsonDocument.Parse(
                JsonDocument.EncodeToString(parsed.RootElement)
            );

            Assert.True(
                DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                $"YAML round-trip changed {Path.GetFileName(filePath)} in {mode} mode."
            );
        }
    }

    [Theory]
    [MemberData(nameof(GetJsonFiles))]
    public void YamlOutputIsStable(string filePath)
    {
        using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));

        var options = new YamlWriteOptions { NewLine = "\n" };

        var yaml = YamlDocument.EncodeToString(source.RootElement, options);
        using var parsed = YamlDocument.Parse(yaml);

        var again = YamlDocument.EncodeToString(parsed.RootElement, options);

        Assert.True(
            yaml == again,
            $"Writing the YAML of {Path.GetFileName(filePath)} a second time changed it."
        );
    }

    [Fact]
    public void EveryLineOfTheNdjsonFileRoundTripsThroughYaml()
    {
        var path = Path.Combine(FindDataDirectory(), "amazon_cellphones.ndjson");
        var lines = File.ReadLines(path).Where(line => line.Length > 0).ToArray();

        Assert.NotEmpty(lines);

        for (var index = 0; index < lines.Length; index++)
        {
            using var source = JsonDocument.Parse(lines[index]);

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
                    YamlTestHelper.AssertMultilineRefusalIsJustified(
                        Encoding.UTF8.GetBytes(lines[index]),
                        $"line {index + 1} of the ndjson file"
                    );

                    continue;
                }

                using var parsed = YamlDocument.Parse(yaml);
                using var restored = JsonDocument.Parse(
                    JsonDocument.EncodeToString(parsed.RootElement)
                );

                Assert.True(
                    DValue.DeepEquals(source.RootElement.AsValue(), restored.RootElement.AsValue()),
                    $"YAML round-trip changed line {index + 1} of the ndjson file in {mode} mode."
                );
            }
        }
    }

    // The benchmark datasets of simdjson, chosen by name:
    //   - large documents with many numbers (canada)
    //   - deep nesting and repeated structure (citm_catalog)
    //   - a lot of non-ASCII text (twitter)
    private static readonly string[] JsonFileNames =
    [
        "canada.json",
        "citm_catalog.json",
        "twitter.json",
    ];

    public static TheoryData<string> GetJsonFiles()
    {
        var directory = FindDataDirectory();

        return new TheoryData<string>(JsonFileNames.Select(name => Path.Combine(directory, name)));
    }

    internal static string FindDataDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "simdjson-data", "jsonexamples");
            if (Directory.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("simdjson-data has not been initialized.");
    }
}
