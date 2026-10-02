using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class YamlTestSuiteTest
{
    // Every case in yaml-test-suite runs unless it is excluded here, so a case that starts to work is covered
    // without any change. Case IDs name a case directory; subcases keep their numeric component (e.g. "KH5V/00").
    // The groups up to "Explicit and complex keys" are YAML features the library does not support on purpose; the groups after
    // them are in scope but fail today, so this list doubles as the to-do list: delete an ID once its case passes.
    private static readonly HashSet<string> ExcludedCases =
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
        // Explicit and complex keys (? key, { flow }: value)
        "5WE3",
        "6PBE",
        "7W2P",
        "A2M4",
        "CT4Q",
        "DFF7",
        "GH63",
        "JTV5",
        "KK5P",
        "M2N8/01",
        "M5DY",
        "RR7F",
        "S9E8",
        "X8DW",
        "4FJ6",
        "Q9WF",
        // Block scalars (empty scalar chomping)
        "K858",
        // Flow collections (implicit keys, multiline keys, single pairs, many lines)
        "4MUZ/00",
        "4MUZ/01",
        "4MUZ/02",
        "87E4",
        "8KB6",
        "8UDB",
        "9BXH",
        "CFD4",
        "HM87/01",
        "L9U5",
        "LQZ7",
        "LX3P",
        "NJ66",
        "QF4Y",
        "S7BG",
        "SBG9",
        "VJP3/01",
        // Unusual keys and colons (empty keys, adjacent values, special characters)
        "2EBW",
        "5MUD",
        "8CWC",
        "9MMW",
        "K3WX",
        "M2N8/00",
        "UKK6/01",
        "V9D5",
        // Other (lookahead, characters allowed in plain scalars)
        "AZW3",
        "FBC9",
    ];

    [Theory]
    [MemberData(nameof(GetCases))]
    public void CaseMatchesExpectedJsonOrFails(string caseId)
    {
        RunCase(caseId);
    }

    // A case with an "error" file must fail to parse. Otherwise it must parse, and its value must equal in.json
    // when the case has one (some valid cases cannot be expressed as JSON).
    private static void RunCase(string caseId)
    {
        var caseDirectory = Path.Combine(
            FindSuiteDirectory(),
            caseId.Replace('/', Path.DirectorySeparatorChar)
        );
        var yaml = File.ReadAllText(Path.Combine(caseDirectory, "in.yaml"));
        if (File.Exists(Path.Combine(caseDirectory, "error")))
        {
            Assert.ThrowsAny<DocumentParseException>(() => YamlDocument.Parse(yaml));
            return;
        }

        using var actualDocument = YamlDocument.Parse(yaml);
        var jsonPath = Path.Combine(caseDirectory, "in.json");

        // An empty in.json means the stream holds no document. Core has no empty document, so the parser gives
        // such input a null root (see YamlDocument.Read).
        if (File.Exists(jsonPath) && new FileInfo(jsonPath).Length == 0)
        {
            Assert.Equal(DTokenKind.Null, actualDocument.RootElement.Token.Kind);
            return;
        }

        // Without in.json the value cannot be compared; parsing successfully is all that can be checked.
        if (!File.Exists(jsonPath))
        {
            return;
        }

        using var expected = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        using var actual = JsonDocument.Parse(
            JsonDocument.EncodeToString(actualDocument.RootElement)
        );
        Assert.True(
            DValue.DeepEquals(expected.RootElement.AsValue(), actual.RootElement.AsValue()),
            $"YAML suite case {caseId} changed its value."
        );
    }

    public static IEnumerable<object[]> GetCases()
    {
        foreach (var directory in Directory.EnumerateDirectories(FindSuiteDirectory()).Order())
        {
            var id = Path.GetFileName(directory);

            // "name" and "tags" only hold links to the cases and would run each of them twice.
            if (id.StartsWith('.') || id is "name" or "tags")
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, "in.yaml")))
            {
                if (!ExcludedCases.Contains(id))
                {
                    yield return new object[] { id };
                }

                continue;
            }

            foreach (var subcase in Directory.EnumerateDirectories(directory).Order())
            {
                var subcaseId = $"{id}/{Path.GetFileName(subcase)}";
                if (
                    File.Exists(Path.Combine(subcase, "in.yaml"))
                    && !ExcludedCases.Contains(subcaseId)
                )
                {
                    yield return new object[] { subcaseId };
                }
            }
        }
    }

    private static string FindSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "yaml-test-suite");
            // The pinned data release stores case IDs directly at the repository root.
            if (Directory.Exists(Path.Combine(path, "FQ7F")))
                return path;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("yaml-test-suite has not been initialized.");
    }
}
