using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

internal static class YamlTestHelper
{
    // The YAML must parse to the value of the JSON text, and the value must survive writing and reading again.
    public static void ParsesAs(string yaml, string expectedJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        using var document = YamlDocument.Parse(yaml);
        Assert.True(
            DValue.DeepEquals(expected.RootElement.AsValue(), document.RootElement.AsValue()),
            $"Parsing the YAML did not give {expectedJson}. YAML: {yaml}"
        );

        var written = YamlDocument.EncodeToString(document.RootElement);
        using var reparsed = YamlDocument.Parse(written);
        Assert.True(
            DValue.DeepEquals(expected.RootElement.AsValue(), reparsed.RootElement.AsValue()),
            $"Writing and reading the YAML again changed the value. Written: {written}"
        );
    }

    // The multiline mode writes every string as a block scalar, which cannot hold a control character other than the
    // line feed and the tab, so it refuses such a string with a NotSupportedException. A round trip test calls this
    // from its catch: the refusal is accepted only when the JSON really has such a string value, so nothing is skipped
    // without a reason.
    public static void AssertMultilineRefusalIsJustified(byte[] json, string name)
    {
        Assert.True(
            HasStringValueThatABlockScalarCannotHold(json),
            $"The multiline mode refused {name}, which has no string that a block scalar cannot hold."
        );
    }

    // Only the string values count: the keys of a map are not written as block scalars.
    private static bool HasStringValueThatABlockScalarCannotHold(byte[] json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            json,
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 4096 }
        );
        return HasStringValueThatABlockScalarCannotHold(document.RootElement);
    }

    private static bool HasStringValueThatABlockScalarCannotHold(
        System.Text.Json.JsonElement element
    )
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (HasStringValueThatABlockScalarCannotHold(property.Value))
                        return true;
                }

                return false;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (HasStringValueThatABlockScalarCannotHold(item))
                        return true;
                }

                return false;
            case System.Text.Json.JsonValueKind.String:
                var text = element.GetString();
                Debug.Assert(text != null); // JsonElement.GetString returns null only for a JSON null, and this is a string.
                return text.Any(c => char.IsControl(c) && c != '\n' && c != '\t');
            default:
                return false;
        }
    }

    // A stream with no document (empty, blank, or only comments) is valid YAML, but it cannot be classified: it is
    // neither a null nor any other value of JSON, and Core has no empty document to stand for it. The parser gives
    // it a single null root (see YamlDocument.Read), so that is all that can be verified: the root is a valid
    // element, not an invalid one, and it is null.
    public static void IsEmptyDocument(YamlDocument document)
    {
        Assert.True(document.IsValid);
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
        Assert.Equal("null", JsonDocument.EncodeToString(document.RootElement));
    }

    // Which files of JSONTestSuite a test reads, decided here for every test that uses the suite.
    //
    // Files are selected by exclusion: every JSON file of the suite is used unless its name matches a pattern below,
    // so a file added to the suite is covered without a change here.

    // "n_*": JSON that a parser must reject. The JSON reader of the core validates lazily, so it rejects such a file
    // either when it reads it or when a value is first taken out of the document, which is while the YAML is written.
    // Either way the rejection comes from the JSON reader and the YAML code is not involved, so no YAML test uses
    // these files. The few the JSON reader wrongly accepts are a JSON problem and show nothing about YAML.
    public const string JsonSuiteInvalidPattern = "n_*";

    // "i_*": JSON that the specification leaves ambiguous, so a parser may accept or reject it (huge numbers, lone
    // surrogates, invalid UTF-8, 500 nested arrays, ...). Whatever is accepted need not come back equal (a lone
    // surrogate is written as the replacement character), so these files are left out of the round trip tests.
    // YamlJsonSuiteRoundTripTest.SpecAmbiguousTest checks that what the JSON reader accepts does not break YAML.
    public const string JsonSuiteSpecAmbiguousPattern = "i_*";

    private static readonly string[] JsonSuiteTestDirectories = ["test_parsing", "test_transform"];

    // Every file except the ones that need not (or cannot) round trip.
    public static IEnumerable<string> GetJsonSuiteValidFiles()
    {
        return GetJsonSuiteAllFiles()
            .Where(path =>
                !MatchesFileName(path, JsonSuiteInvalidPattern)
                && !MatchesFileName(path, JsonSuiteSpecAmbiguousPattern)
            );
    }

    public static IEnumerable<string> GetJsonSuiteFiles(string pattern)
    {
        return GetJsonSuiteAllFiles().Where(path => MatchesFileName(path, pattern));
    }

    public static string FindJsonSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, "external", "JSONTestSuite");
            if (Directory.Exists(Path.Combine(path, "test_parsing")))
                return path;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("JSONTestSuite has not been initialized.");
    }

    private static IEnumerable<string> GetJsonSuiteAllFiles()
    {
        var suite = FindJsonSuiteDirectory();
        return JsonSuiteTestDirectories
            .SelectMany(directory =>
                Directory.EnumerateFiles(Path.Combine(suite, directory), "*.json")
            )
            .Order(StringComparer.Ordinal);
    }

    private static bool MatchesFileName(string path, string pattern)
    {
        return FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path));
    }
}

public sealed class YamlTestHelperTest
{
    // The refusal is justified, and the helper passes, only for a string value with a control character other than the
    // line feed and the tab, wherever it is. A refusal of any other JSON has another reason: the helper must fail.
    [Theory]
    [InlineData("\"a\\u0000b\"", true)]
    [InlineData("\"\\r\"", true)]
    [InlineData("{\"a\":[\"x\",{\"b\":\"\\u0001\"}]}", true)]
    [InlineData("\"line1\\nline2\\ttab\"", false)]
    [InlineData("{\"a\":[\"b\",1,null]}", false)]
    // A control character in a key is not a string value: keys are not written as block scalars.
    [InlineData("{\"\\u0000\":1}", false)]
    public void RefusalIsJustifiedOnlyForAStringThatABlockScalarCannotHold(
        string json,
        bool justified
    )
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        if (justified)
        {
            YamlTestHelper.AssertMultilineRefusalIsJustified(bytes, json);
        }
        else
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                YamlTestHelper.AssertMultilineRefusalIsJustified(bytes, json)
            );
        }
    }
}
