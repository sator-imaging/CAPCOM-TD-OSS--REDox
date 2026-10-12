using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class ConformanceTest
{
    // Case IDs are case directory name. Subcases keep their numeric component (e.g. "KH5V/00").
    private static readonly HashSet<string> UnsupportedAndAmbiguousCaseIds =
    [
        // Anchors and aliases (&a, *a)
        "26DV",
        "2SXE",
        "3GZX",
        "3R3P",
        "6BFJ",
        "6KGN",
        "6M2F",
        "7BMT",
        "7BUB",
        "8XYN",
        "CN3R",
        "E76Z",
        "FTA2",
        "JS2J",
        "KSS4",
        "PW8X",
        "RZP5",
        "SKE5",
        "U3XV",
        "V55R",
        "W5VH",
        "X38W",
        "XW4D",
        "Y2GN",
        "ZH7C",
        "ZWK4",
        // Tags (!!str, !local, !<verbatim>)
        "2AUY",
        "2XXW",
        "33X3",
        "35KP",
        "52DL",
        "565N",
        "57H4",
        "6JWB",
        "735Y",
        "74H7",
        "7FWL",
        "8MK2",
        "9KAX",
        "BU8L",
        "CUP7",
        "EHF6",
        "F2C7",
        "FH7J",
        "HMQ5",
        "J7PZ",
        "L94M",
        "LE5A",
        "M5C3",
        "S4JQ",
        "UGM3",
        "UKK6/02",
        "WZ62",
        "Z67P",
        // Directives (%YAML, %TAG)
        "27NA",
        "2LFX",
        "5TYM",
        "6CK3",
        "6LVF",
        "6WLZ",
        "6ZKB",
        "9DXL",
        "9WXW",
        "BEC7",
        "C4HZ",
        "CC74",
        "DK95/07",
        "M7A3",
        "MUS6/02",
        "MUS6/03",
        "MUS6/04",
        "MUS6/05",
        "MUS6/06",
        "P76L",
        "RTP8",
        "U3C3",
        "UT92",
        "W4TN",
        "Z9M4",
        // Multiple documents and document markers (---, ...)
        "6FWR",
        "6XDY",
        "753E",
        "7Z25",
        "HWV9",
        "JHB9",
        "L383",
        "M29M",
        "MYW6",
        "NKF9",
        "PUW8",
        "QT73",
        "RZT7",
        "S4T7",
        "U9NS",
        // Complex keys: a collection as a key (? - a, [ a ]: b, { a: b }: c). A scalar key, explicit or not, is supported.
        "6PBE",
        "KK5P",
        "M2N8/01",
        "M5DY",
        "4FJ6",
        "Q9WF",
        "LX3P",
        "SBG9",
        "9MMW",
        "M2N8/00",
        "V9D5",
        // Ambiguous Spec tests are ignored. Tests expected values may or may not align to the YAML spec.
        // "Always adds newline" rule is simple, but it can lead unexpected user data mutation.
        // (Current behavior: don't add newline for a block string at the end of file WITHOUT newline before EOF)
        //
        // Both cases are a block scalar whose last line the text ends without a line break (the line break after the
        // indicator, which ends the header line, is ignored: it is not part of the value), and both expect the end of the
        // text to be converted to a line break: a line feed is inserted after the last line ("|+": "\n", "|": "x\n \n").
        // This does not match the YAML 1.2 specification: the last line ends with a line break or with the end of the
        // text, and only a line break gives a line feed (b-chomped-last). The library follows the specification and
        // inserts none (""; "x\n "). This difference is on purpose.
        "JEF9/02",
        "L24T/01",
    ];

    [Theory]
    [MemberData(nameof(GetSupportedYamlSpecificationCases))]
    public void SpecSuiteCase_RejectsExpectedErrorsOrMatchesAvailableReferenceValue(string caseId)
    {
        var caseDirectory = Path.Combine(
            FindYamlSpecificationSuiteDirectory(),
            caseId.Replace('/', Path.DirectorySeparatorChar)
        );

        var sourceYaml = File.ReadAllText(Path.Combine(caseDirectory, "in.yaml"));
        if (File.Exists(Path.Combine(caseDirectory, "error")))
        {
            Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(sourceYaml));

            return;
        }

        using var parsedYamlDocument = YamlDocument.Parse(sourceYaml);

        var expectedResultJsonPath = Path.Combine(caseDirectory, "in.json");

        if (File.Exists(expectedResultJsonPath) && new FileInfo(expectedResultJsonPath).Length == 0)
        {
            YamlTestFixture.AssertEmptyYamlDocumentHasNullRoot(parsedYamlDocument);

            return;
        }

        // Without expected json file, the value cannot be written as JSON (a null key, for one).
        if (!File.Exists(expectedResultJsonPath))
        {
            Assert.True(parsedYamlDocument.IsValid);

            var expectedPath = new[] { "out.yaml", "emit.yaml" }
                .Select(name => Path.Combine(caseDirectory, name))
                .FirstOrDefault(File.Exists);

            if (expectedPath != null)
            {
                using var expectedYamlDocument = YamlDocument.Parse(File.ReadAllText(expectedPath));

                Assert.True(
                    DValue.DeepEquals(
                        expectedYamlDocument.RootElement.AsValue(),
                        parsedYamlDocument.RootElement.AsValue()
                    ),
                    $"YAML suite case {caseId} does not hold the value of {Path.GetFileName(expectedPath)}."
                );
            }
        }
        else
        {
            using var expectedJsonDocument = JsonDocument.Parse(
                File.ReadAllBytes(expectedResultJsonPath)
            );

            using var yamlAsJsonDocument = JsonDocument.Parse(
                JsonDocument.EncodeToString(parsedYamlDocument.RootElement)
            );

            Assert.True(
                DValue.DeepEquals(
                    expectedJsonDocument.RootElement.AsValue(),
                    yamlAsJsonDocument.RootElement.AsValue()
                ),
                $"YAML suite case {caseId} changed its value."
            );
        }
    }

    public static TheoryData<string> GetSupportedYamlSpecificationCases()
    {
        var suiteDirectory = FindYamlSpecificationSuiteDirectory();

        var suiteCaseCount = 0;
        var supportedCases = new TheoryData<string>();
        foreach (var directory in Directory.EnumerateDirectories(suiteDirectory).Order())
        {
            var caseId = Path.GetFileName(directory);

            // "name" and "tags" only hold links to the cases and would run each of them twice.
            if (caseId.StartsWith('.') || caseId is "name" or "tags")
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, "in.yaml")))
            {
                suiteCaseCount++;

                if (!UnsupportedAndAmbiguousCaseIds.Contains(caseId))
                {
                    supportedCases.Add(caseId);
                }

                continue;
            }

            foreach (var subcase in Directory.EnumerateDirectories(directory).Order())
            {
                var subcaseId = $"{caseId}/{Path.GetFileName(subcase)}";
                if (File.Exists(Path.Combine(subcase, "in.yaml")))
                {
                    suiteCaseCount++;

                    if (!UnsupportedAndAmbiguousCaseIds.Contains(subcaseId))
                    {
                        supportedCases.Add(subcaseId);
                    }
                }
            }
        }

        Assert.Equal(402, suiteCaseCount);
        Assert.Equal(295, supportedCases.Count);

        return supportedCases;
    }

    private static string FindYamlSpecificationSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var yamlSpecificationSuitePath = Path.Combine(
                current.FullName,
                "external",
                "yaml-test-suite"
            );

            // The pinned data release stores case IDs directly at the repository root.
            if (Directory.Exists(Path.Combine(yamlSpecificationSuitePath, "FQ7F")))
            {
                return yamlSpecificationSuitePath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("yaml-test-suite has not been initialized.");
    }
}