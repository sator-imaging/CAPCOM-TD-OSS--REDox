using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlJsonInputRobustnessTest
{
    [Theory]
    [MemberData(nameof(GetInvalidJsonFiles))]
    public void InvalidJsonInputsDoNotCrash(string filePath) => ExercisePublicConversion(filePath);

    [Theory]
    [MemberData(nameof(GetImplementationDefinedJsonFiles))]
    public void ImplementationDefinedJsonInputsDoNotCrash(string filePath) => ExercisePublicConversion(filePath);

    private static void ExercisePublicConversion(string filePath)
    {
        try
        {
            using var source = JsonDocument.Parse(File.ReadAllBytes(filePath));
            var yaml = YamlDocument.EncodeToString(source.RootElement);
            using var parsed = YamlDocument.Parse(yaml);
            var restoredJson = JsonDocument.EncodeToString(parsed.RootElement);
            using var restored = JsonDocument.Parse(restoredJson);
        }
        catch (Exception)
        {
            // Invalid and implementation-defined inputs may be rejected at any conversion step.
            // Keep this separate from round-trip tests, which require equivalent values.
        }
    }

    public static IEnumerable<object[]> GetInvalidJsonFiles()
    {
        return Directory.EnumerateFiles(FindParsingDirectory(), "n_*.json")
            .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> GetImplementationDefinedJsonFiles()
    {
        return Directory.EnumerateFiles(FindParsingDirectory(), "i_*.json")
            .Select(path => new object[] { path });
    }

    private static string FindParsingDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "JSONTestSuite", "test_parsing");
            if (Directory.Exists(path)) return path;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("JSONTestSuite has not been initialized.");
    }
}
