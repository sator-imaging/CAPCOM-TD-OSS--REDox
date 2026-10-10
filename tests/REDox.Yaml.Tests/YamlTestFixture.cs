using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Yaml.Tests;

internal static class YamlTestFixture
{
    public const string MixedScalarAndNestedCollectionsYaml =
        "name: \"a: b\"\nlist:\n  - 1\n  - true\n  - null\nnested:\n  key: 1.5\n";

    public const string ExpectedMixedScalarAndNestedCollectionsJson =
        "{\"name\":\"a: b\",\"list\":[1,true,null],\"nested\":{\"key\":1.5}}";

    public const string MixedScalarsNestedCollectionsAndTriviaYamlSource =
        "# head\nname: text # note\nnumber: 42\nfloat: 1.5\nflag: true\nnothing: null\nwhen: 2001-12-14\n"
        + "list:\n  - 1\n  - two\n  - [3, 4]\nnested:\n  key: value\n  deep:\n    - x: 1\n    - y\nblock: |\n  one\n  two\n";

    public static readonly YamlWriteOptions LineFeedWriteOptions = new() { NewLine = "\n" };

    public static readonly YamlDocumentOptions TriviaParseOptions = new() { PreserveTrivia = true };

    public static readonly YamlWriteOptions TriviaWriteOptions = new()
    {
        NewLine = "\n",
        PreserveTrivia = true,
    };

    public static void RandomizeArrayPoolBuffers(string parsedYamlSource)
    {
        var sourceByteLength = Encoding.UTF8.GetByteCount(parsedYamlSource);

        // PooledBufferWriter uses the shared array pool.
        // Randomizing buffer guarantees that YamlDocument doesn't refer it.
        for (int reuseAttemp = 0; reuseAttemp < 3; reuseAttemp++)
        {
            foreach (
                var requestedSize in new[]
                {
                    Math.Max(1, sourceByteLength),
                    Math.Max(1, sourceByteLength >> 1),
                    Math.Max(1, sourceByteLength >> 2),
                    Math.Max(1, sourceByteLength >> 3),
                    Math.Max(1, sourceByteLength >> 4),
                    Math.Max(1, sourceByteLength >> 5),
                    Math.Max(1, sourceByteLength >> 6),
                    Math.Max(1, sourceByteLength >> 7),
                    SerializerSettings.Default.DefaultBufferSize,
                }
            )
            {
                if (requestedSize == 0)
                {
                    continue;
                }

                var rentedBuffer = ArrayPool<byte>.Shared.Rent(requestedSize);

                try
                {
                    Random.Shared.NextBytes(rentedBuffer);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }
    }

    public static T ToObjectOfTypePolyfill<T>(DElement element)
        where T : class
    {
        var value = element.ToObject(typeof(T));

        Debug.Assert(value is T);

        return (T)value;
    }

    // NOTE: DocumentParseException has no ErrorCode property.
    //       For the test reliability, prefix document type and code.
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
    // A rejected case may stop YAML test only when the diagnostic belongs to that reader.
    public static void AssertParseError_FromJsonDocument(
        DocumentParseException exception,
        byte[] json
    )
    {
        Assert.Equal(nameof(JsonDocument), exception.DocumentTypeName);
        Assert.InRange(exception.BytePosition, 0, json.Length);
        Assert.True(exception.LineNumber >= 1);
        Assert.True(exception.BytePositionInLine >= 0);
    }

    public static void Assert_YamlMatchesJsonValue_YamlRoundTrips(string yaml, string expectedJson)
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

    // Matching serialized text alone cannot establish a lossless conversion.
    public static void Assert_JsonYamlJsonRoundTrip_PreservesParsedValuesAtEveryStage(
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
        convertedYamlDocument.ValidateJsonAndYamlRoundTrip(options, sourceJsonDocument.RootElement);
    }

    // The source check cannot establish which string caused the refusal.
    public static void Assert_MultilineStringReportsUnsupportedFeature_JsonHasUnrepresentableStringValue(
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

    // NOTE: Core data model has no empty document classification.
    //       To distinguish an error and empty document, this serializer uses a null root (see YamlDocument.Read).
    public static void AssertEmptyYamlDocumentHasNullRoot(YamlDocument document)
    {
        Assert.True(document.IsValid);
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
        Assert.Equal("null", JsonDocument.EncodeToString(document.RootElement));
    }

    // "n_*"
    // JSON that a parser must reject. The JSON reader of the core validates lazily, so it rejects such a file
    // either when it reads it or when a value is first taken out of the document, which is while the YAML is written.
    // Either way the rejection comes from the JSON reader and the YAML code is not involved, so no YAML test uses
    // these files. The few the JSON reader wrongly accepts are a JSON problem and show nothing about YAML.
    public const string JsonSuiteInvalidPattern = "n_*";

    // "i_*"
    // JSON that the specification leaves ambiguous, so a parser may accept or reject it (huge numbers, lone
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

    private static string[] GetJsonSuiteAllFiles()
    {
        var suite = FindJsonSuiteDirectory();

        var files = JsonSuiteTestDirectories
            .SelectMany(directory =>
                Directory.EnumerateFiles(Path.Combine(suite, directory), "*.json")
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

        // New JSONTestSuite file must be reviewed and update this assertion
        // even if it is excluded from YAML round trips.
        Assert.Equal(340, files.Length);

        return files;
    }

    private static bool MatchesFileName(string path, string pattern)
    {
        return FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path));
    }

    public enum Color
    {
        Red,
        Green,
    }

    public sealed class Address
    {
        public string City { get; set; } = "";
        public int Number { get; set; }
    }

    public sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public double Score { get; set; }
        public bool Active { get; set; }
        public int? Optional { get; set; }
        public Color Favorite { get; set; }
        public Address? Address { get; set; }
        public List<string> Tags { get; set; } = [];
        public Dictionary<string, int> Counts { get; set; } = [];
    }

    public static Person CreatePersonWithNestedAddressAndCollections() =>
        new()
        {
            Name = "a: b\nsecond line",
            Age = 42,
            Score = 1.5,
            Active = true,
            Optional = null,
            Favorite = Color.Green,
            Address = new Address { City = "true", Number = 7 },
            Tags = ["x", "123", "", "null"],
            Counts = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 },
        };
}

// Testing test fixture
public sealed class YamlTestFixtureTest
{
    [Theory]
    [InlineData("\"a\\u0000b\"", true)]
    [InlineData("\"\\r\"", true)]
    [InlineData("{\"a\":[\"x\",{\"b\":\"\\u0001\"}]}", true)]
    [InlineData("\"line1\\nline2\\ttab\"", false)]
    [InlineData("{\"a\":[\"b\",1,null]}", false)]
    // Keys are not written as block scalars, so a control character in a key cannot justify refusal.
    [InlineData("{\"\\u0000\":1}", false)]
    public void JsonStringValueWithUnsupportedBlockScalarCharacter_IsDetectedOnlyInValues(
        string json,
        bool justified
    )
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        if (justified)
        {
            YamlTestFixture.AssertJsonContainsStringValueUnrepresentableAsBlockScalar(bytes, json);
        }
        else
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                YamlTestFixture.AssertJsonContainsStringValueUnrepresentableAsBlockScalar(
                    bytes,
                    json
                )
            );
        }
    }
}