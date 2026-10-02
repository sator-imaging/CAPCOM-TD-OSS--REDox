using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlTestSuiteTest
{
    // Each entry names a case directory; subcase paths keep their numeric component.
    private static readonly string[] SupportedCaseIds =
    [
        "229Q",
        "36F6",
        "4WA9",
        "54T7",
        "5C5M",
        "65WH",
        "7ZZ5",
        "9J7A",
        "FQ7F",
        "G4RS",
        "J5UC",
        "KH5V/00",
        "KH5V/01",
        "4H7K",
        "6JTT",
        "VJP3/00"
    ];

    [Theory]
    [MemberData(nameof(GetSupportedCases))]
    public void SupportedYamlCasesMatchExpectedJsonOrFail(string caseDirectory)
    {
        var yaml = File.ReadAllText(Path.Combine(caseDirectory, "in.yaml"));
        if (File.Exists(Path.Combine(caseDirectory, "error")))
        {
            Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(yaml));
            return;
        }

        using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(caseDirectory, "in.json")));
        using var actualDocument = YamlDocument.Parse(yaml);
        using var actual = JsonDocument.Parse(JsonDocument.EncodeToString(actualDocument.RootElement));
        Assert.True(DValue.DeepEquals(expected.RootElement.AsValue(), actual.RootElement.AsValue()),
            $"YAML suite case {Path.GetRelativePath(FindSuiteDirectory(), caseDirectory)} changed its value.");
    }

    public static IEnumerable<object[]> GetSupportedCases()
    {
        var suite = FindSuiteDirectory();
        return SupportedCaseIds.Select(caseId => new object[]
        {
            Path.Combine(suite, caseId.Replace('/', Path.DirectorySeparatorChar))
        });
    }

    private static string FindSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "yaml-test-suite");
            // The pinned data release stores case IDs directly at the repository root.
            if (Directory.Exists(Path.Combine(path, "FQ7F"))) return path;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("yaml-test-suite has not been initialized.");
    }
}
