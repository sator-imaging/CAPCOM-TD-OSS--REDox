using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

internal static class YamlTestHelper
{
    // NOTE: DocumentParseException has no error-code property.
    //       Prefix the code with the document type for this assertion.
    public static void AssertYamlParseError(
        DocumentParseException exception,
        string expectedErrorCode,
        int expectedBytePosition = -1
    )
    {
        var expectedPrefix =
            expectedBytePosition >= 0
                ? $"{nameof(YamlDocument)} Error ({expectedErrorCode}): BytePosition: {expectedBytePosition}"
                : $"{nameof(YamlDocument)} Error ({expectedErrorCode}): BytePosition: ";

        Assert.StartsWith(expectedPrefix, exception.Message);
    }

    public static DocumentParseException AssertYamlParseError(
        Action action,
        string expectedErrorCode,
        int expectedBytePosition = -1
    )
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(action);

        AssertYamlParseError(exception, expectedErrorCode, expectedBytePosition);

        return exception;
    }

    // These inputs may be accepted or rejected by the JSON reader, including during lazy value access.
    // A rejected case may stop this YAML test only when the diagnostic belongs to that reader.
    public static void AssertParseErrorCameFromJsonDocument(
        DocumentParseException exception,
        byte[] json
    )
    {
        Assert.Equal(nameof(JsonDocument), exception.DocumentTypeName);
        Assert.InRange(exception.BytePosition, 0, json.Length);
        Assert.True(exception.LineNumber >= 1);
        Assert.True(exception.BytePositionInLine >= 0);
    }

    public static void AssertYamlParsesAsJsonValueAndRoundTrips(string yaml, string expectedJson)
    {
        using var expectedJsonDocument = JsonDocument.Parse(expectedJson);
        using var parsedYamlDocument = YamlDocument.Parse(yaml);

        Assert.True(
            DValue.DeepEquals(
                expectedJsonDocument.RootElement.AsValue(),
                parsedYamlDocument.RootElement.AsValue()
            ),
            $"Parsing the YAML did not give {expectedJson}. YAML: {yaml}"
        );

        var writtenYaml = YamlDocument.EncodeToString(parsedYamlDocument.RootElement);

        using var roundTrippedYamlDocument = YamlDocument.Parse(writtenYaml);

        Assert.True(
            DValue.DeepEquals(
                expectedJsonDocument.RootElement.AsValue(),
                roundTrippedYamlDocument.RootElement.AsValue()
            ),
            $"Writing and reading the YAML again changed the value. Written: {writtenYaml}"
        );
    }

    // Each parsed document must retain the value; matching serialized text alone cannot establish a lossless conversion.
    public static void AssertJsonYamlJsonRoundTripPreservesValues(
        JsonDocument sourceJsonDocument,
        YamlWriteOptions options,
        string caseName
    )
    {
        var yamlFromJson = YamlDocument.EncodeToString(sourceJsonDocument.RootElement, options);

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

        Assert.True(
            DValue.DeepEquals(
                sourceJsonDocument.RootElement.AsValue(),
                convertedYamlDocument.RootElement.AsValue()
            ),
            $"Converting {caseName} from JSON to YAML changed its value."
        );

        // The API compares the JSON read back with both this YAML and the original JSON source.
        convertedYamlDocument.ValidateJsonYamlRoundTripPreservesValues(
            options,
            sourceJsonDocument.RootElement
        );
    }

    // The source check cannot establish which string caused the refusal.
    public static void AssertMultilineStyleRefusalReportsUnsupportedFeatureAndJsonHasUnrepresentableStringValue(
        DocumentParseException exception,
        byte[] json,
        string name
    )
    {
        AssertYamlParseError(exception, "UnsupportedFeature");
        Assert.EndsWith(
            " (This string cannot be written as a YAML block scalar.)",
            exception.Message
        );

        AssertJsonContainsStringValueUnrepresentableAsBlockScalar(json, name);
    }

    // JSON keys are not written as block scalars, so only a string value can justify this refusal.
    public static void AssertJsonContainsStringValueUnrepresentableAsBlockScalar(
        byte[] json,
        string name
    )
    {
        Assert.True(
            HasStringValueThatABlockScalarCannotHold(json),
            $"The multiline mode refused {name}, which has no string that a block scalar cannot hold."
        );
    }

    private static bool HasStringValueThatABlockScalarCannotHold(byte[] json)
    {
        using var jsonDocument = System.Text.Json.JsonDocument.Parse(
            json,
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 4096 }
        );

        return HasStringValueThatABlockScalarCannotHold(jsonDocument.RootElement);
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
                    {
                        return true;
                    }
                }

                return false;

            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (HasStringValueThatABlockScalarCannotHold(item))
                    {
                        return true;
                    }
                }

                return false;

            case System.Text.Json.JsonValueKind.String:
                var text = element.GetString();

                Debug.Assert(text != null); // JsonElement.GetString returns null only for a JSON null, and this is a string.

                return text.Any(static c => char.IsControl(c) && c != '\n' && c != '\t');

            default:
                return false;
        }
    }

    // Core has no empty-document value, so an empty YAML stream uses a null root (see YamlDocument.Read).
    public static void AssertEmptyYamlDocumentHasNullRoot(YamlDocument document)
    {
        Assert.True(document.IsValid);
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
        Assert.Equal("null", JsonDocument.EncodeToString(document.RootElement));
    }

    // "n_*": JSON that a parser must reject. The JSON reader of the core validates lazily, so it rejects such a file
    // either when it reads it or when a value is first taken out of the document, which is while the YAML is written.
    // Either way the rejection comes from the JSON reader and the YAML code is not involved, so no YAML test uses
    // these files. The few the JSON reader wrongly accepts are a JSON problem and show nothing about YAML.
    public const string JsonSuiteInvalidPattern = "n_*";

    // "i_*": JSON that the specification leaves ambiguous, so a parser may accept or reject it (huge numbers, lone
    // surrogates, invalid UTF-8, 500 nested arrays, ...). Whatever is accepted need not come back equal (a lone
    // surrogate is written as the replacement character), so these files are left out of the round trip tests.
    // YamlJsonSuiteRoundTripTest.SpecAmbiguousTest only checks that accepted files produce parseable YAML;
    // rejected files do not exercise YAML, and accepted values are not compared.
    public const string JsonSuiteSpecAmbiguousPattern = "i_*";

    private static readonly string[] JsonSuiteTestDirectories = ["test_parsing", "test_transform"];

    public static IEnumerable<string> GetJsonSuiteFilesEligibleForYamlRoundTrip()
    {
        return GetJsonSuiteAllFiles()
            .Where(static path =>
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
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("JSONTestSuite has not been initialized.");
    }

    private static IEnumerable<string> GetJsonSuiteAllFiles()
    {
        var suite = FindJsonSuiteDirectory();

        var files = JsonSuiteTestDirectories
            .SelectMany(directory =>
                Directory.EnumerateFiles(Path.Combine(suite, directory), "*.json")
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

        // A new JSONTestSuite file must be reviewed even if it is excluded from YAML round trips.
        Assert.Equal(340, files.Length);

        return files;
    }

    private static bool MatchesFileName(string path, string pattern)
    {
        return FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path));
    }
}

internal static class TestExtensions
{
    public static string? AsString(this DValue value) => (string?)value;

    public static int AsInt32(this DValue value) => (int)value;

    public static long AsInt64(this DValue value) => (long)value;

    public static ulong AsUInt64(this DValue value) => (ulong)value;

    public static double AsDouble(this DValue value) => (double)value;

    public static bool AsBoolean(this DValue value) => (bool)value;

    public static DateTimeOffset AsDateTimeOffset(this DValue value) => (DateTimeOffset)value;

    public static DTokenKind AsTokenKind(this DValue value) => value.AsElement().Token.Kind;
}

public sealed class YamlTestHelperTest
{
    // The helper passes only when a string value contains a control character other than LF or TAB. It does not
    // inspect an exception or prove that this character caused a multiline writer to refuse the document.
    [Theory]
    [InlineData("\"a\\u0000b\"", true)]
    [InlineData("\"\\r\"", true)]
    [InlineData("{\"a\":[\"x\",{\"b\":\"\\u0001\"}]}", true)]
    [InlineData("\"line1\\nline2\\ttab\"", false)]
    [InlineData("{\"a\":[\"b\",1,null]}", false)]
    // A control character in a key is not a string value: keys are not written as block scalars.
    [InlineData("{\"\\u0000\":1}", false)]
    public void JsonStringValueUnrepresentableAsBlockScalar_IsRequiredForMultilineRefusal(
        string json,
        bool justified
    )
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        if (justified)
        {
            YamlTestHelper.AssertJsonContainsStringValueUnrepresentableAsBlockScalar(bytes, json);
        }
        else
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                YamlTestHelper.AssertJsonContainsStringValueUnrepresentableAsBlockScalar(
                    bytes,
                    json
                )
            );
        }
    }
}
