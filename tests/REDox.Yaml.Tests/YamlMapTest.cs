using System.Diagnostics;
using System.Linq;
using REDox.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace REDox.Yaml.Tests;

public sealed class YamlMapTest
{
    [Theory]
    [InlineData("{}")]
    public void EmptyMap_ParsesAndRoundTrips(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(document.RootElement);

        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(0, document.RootElement.GetPropertyCount());
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("{a: 1, b: true}")]
    [InlineData("a: 1\nb: true\n")]
    public void SimpleMap_ParsesAndRoundTrips(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(document.RootElement);

        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(2, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
        Assert.True(document.RootElement.GetProperty("b").GetBoolean());
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("a:\n", "{\"a\":null}")]
    [InlineData("a:\nb: 1\n", "{\"a\":null,\"b\":1}")]
    [InlineData("a: ~\nb:\n", "{\"a\":null,\"b\":null}")]
    [InlineData("'a b': 1\n\"c\": 2\n", "{\"a b\":1,\"c\":2}")]
    [InlineData("'it''s': 1\n\"tab\\tkey\": 2\n", "{\"it's\":1,\"tab\\tkey\":2}")]
    [InlineData("a:\n  b:\n    c: 1\n", "{\"a\":{\"b\":{\"c\":1}}}")]
    [InlineData("a: {b: 1}\nc: [2]\n", "{\"a\":{\"b\":1},\"c\":[2]}")]
    [InlineData("{a: 1, b: 2, }", "{\"a\":1,\"b\":2}")]
    [InlineData("{'x y': 1, \"z\": 2}", "{\"x y\":1,\"z\":2}")]
    [InlineData("{a, b: 1}", "{\"a\":null,\"b\":1}")]
    [InlineData("{\n  a: 1,\n  b: [2,\n    3],\n}\n", "{\"a\":1,\"b\":[2,3]}")]
    [InlineData("a: {\n  b: 1,\n  c: 2\n }\n", "{\"a\":{\"b\":1,\"c\":2}}")]
    [InlineData("a:\n  - b: 1\n    c: 2\n  - d: 3\n", "{\"a\":[{\"b\":1,\"c\":2},{\"d\":3}]}")]
    public void BlockAndFlowMapShapes_ParseExpectedValuesAndRoundTrip(
        string yaml,
        string expectedJson
    )
    {
        YamlTestHelper.AssertYamlParsesAsJsonValueAndRoundTrips(yaml, expectedJson);
    }

    [Theory]
    [InlineData("a:\n  b: 1\n")]
    public void NestedMap_ParsesAndRoundTrips(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(document.RootElement);

        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetProperty("b").GetInt64());
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("a:\n  - 1\n  - 2\n")]
    public void MapWithArray_ParsesAndRoundTrips(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(document.RootElement);

        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.True(
            DValue.DeepEquals(new DArray(1, 2), document.RootElement.GetProperty("a").AsValue())
        );

        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    // Only what a DElement can hold is a key:
    //   null
    //   boolean
    //   number
    //   timestamp
    //   string
    // A collection as a key is not supported for now.
    [Theory]
    [InlineData("1: a\n", DTokenKind.Integer)]
    [InlineData("-7: a\n", DTokenKind.Integer)]
    [InlineData("0x1F: a\n", DTokenKind.Integer)]
    [InlineData("1.5: a\n", DTokenKind.Float)]
    [InlineData(".inf: a\n", DTokenKind.Float)]
    [InlineData("true: a\n", DTokenKind.Boolean)]
    [InlineData("false: a\n", DTokenKind.Boolean)]
    [InlineData("null: a\n", DTokenKind.Null)]
    [InlineData("~: a\n", DTokenKind.Null)]
    [InlineData("2001-12-14: a\n", DTokenKind.Timestamp)]
    [InlineData("2001-12-14T21:59:43Z: a\n", DTokenKind.Timestamp)]
    [InlineData("text: a\n", DTokenKind.String)]
    [InlineData("\"1\": a\n", DTokenKind.String)]
    [InlineData("'true': a\n", DTokenKind.String)]
    [InlineData("\"2001-12-14\": a\n", DTokenKind.String)]
    [InlineData("1.2.3: a\n", DTokenKind.String)]
    [InlineData("{1: a}", DTokenKind.Integer)]
    [InlineData("{true: a}", DTokenKind.Boolean)]
    [InlineData("[1: a]", DTokenKind.Integer)]
    public void ScalarMapKey_ResolvesLikeValue(string yaml, DTokenKind kind)
    {
        using var document = YamlDocument.Parse(yaml);

        var map =
            document.RootElement.Token.Type == DTokenType.Array
                ? document.RootElement.AsArray()[0].AsElement().AsMap()
                : document.RootElement.AsMap();

        Assert.Single(map);
        Assert.Equal(kind, map[0].Key.AsElement().Token.Kind);
        Assert.Equal("a", map[0].Value.AsElement().GetString());
    }

    [Fact]
    public void DifferentKeyTypes_CoexistInOneMap()
    {
        using var document = YamlDocument.Parse(
            "1: a\ntrue: b\nnull: c\n1.5: d\n2001-12-14: e\n\"2\": f\ng: h\n"
        );

        var kinds = document
            .RootElement.AsMap()
            .Select(static pair => pair.Key.AsElement().Token.Kind);

        Assert.Equal(
            new[]
            {
                DTokenKind.Integer,
                DTokenKind.Boolean,
                DTokenKind.Null,
                DTokenKind.Float,
                DTokenKind.Timestamp,
                DTokenKind.String,
                DTokenKind.String,
            },
            kinds
        );
    }

    [Theory]
    [InlineData("1: a\ntrue: b\n2001-12-14: c\n1.5: d\n")]
    [InlineData("\"1\": a\n\"true\": b\n\"2001-12-14\": c\n\"1.5\": d\n")]
    [InlineData("1: a\n\"1\": b\n")]
    [InlineData("null: a\ntext: b\n")]
    public void MapKeyTypes_SurviveWritingAndReading(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var written = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(yaml, written);
    }

    [Fact]
    public void MapWithNonStringKeys_WritesAndReadsBack()
    {
        var map = new DMap
        {
            { DValue.Create(1), DValue.Create("integer") },
            { DValue.Create(true), DValue.Create("boolean") },
            { DValue.Create("1"), DValue.Create("string that looks like an integer") },
            { DValue.Create(2.5), DValue.Create("float") },
        };

        var yaml = YamlDocument.EncodeToString(
            map.AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(
            "1: integer\ntrue: boolean\n\"1\": string that looks like an integer\n2.5: float\n",
            yaml
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(
            new[] { DTokenKind.Integer, DTokenKind.Boolean, DTokenKind.String, DTokenKind.Float },
            parsed.RootElement.AsMap().Select(static pair => pair.Key.AsElement().Token.Kind)
        );
    }

    [Fact]
    public void KeyValidation_DistinguishesKeyTypes()
    {
        var validating = new YamlDocumentOptions { EnableValueValidation = true };

        using var different = YamlDocument.Parse("1: a\n\"1\": b\n", options: validating);

        Assert.Equal(2, different.RootElement.AsMap().Count);

        YamlTestHelper.AssertYamlParseError(
            () => YamlDocument.Parse("1: a\n1: b\n", options: validating),
            "DuplicateKey"
        );
    }

    [Theory]
    [InlineData("11: a\n0xB: b\n")]
    [InlineData("{11: a, 0xB: b}")]
    public void EquivalentIntegerKeySpellings_ValidationRejectsDuplicates(string yaml)
    {
        YamlTestHelper.AssertYamlParseError(
            () =>
                YamlDocument.Parse(
                    yaml,
                    options: new YamlDocumentOptions { EnableValueValidation = true }
                ),
            "DuplicateKey"
        );
    }

    [Theory]
    [InlineData("? a\n: b\n", "{\"a\":\"b\"}")]
    [InlineData("? a\n", "{\"a\":null}")]
    [InlineData("? a\n? b\nc:\n", "{\"a\":null,\"b\":null,\"c\":null}")]
    [InlineData("? a\n:\n", "{\"a\":null}")]
    [InlineData("? a\n:\n  b\n", "{\"a\":\"b\"}")]
    [InlineData("? a\n: - b\n  - c\n", "{\"a\":[\"b\",\"c\"]}")]
    [InlineData("? a\n: b: c\n", "{\"a\":{\"b\":\"c\"}}")]
    [InlineData("? a  # note\n# between\n: b\n", "{\"a\":\"b\"}")]
    [InlineData("? \"quoted key\"\n: b\n", "{\"quoted key\":\"b\"}")]
    [InlineData("? |\n  block key\n: b\n", "{\"block key\\n\":\"b\"}")]
    [InlineData("? a\n  continued\n: b\n", "{\"a continued\":\"b\"}")]
    [InlineData("a: 1\n? b\n: 2\nc: 3\n", "{\"a\":1,\"b\":2,\"c\":3}")]
    [InlineData("m:\n  ? a\n  : 1\n  b: 2\n", "{\"m\":{\"a\":1,\"b\":2}}")]
    [InlineData("- ? a\n  : 1\n- ? b\n", "[{\"a\":1},{\"b\":null}]")]
    [InlineData("[? a : b]", "[{\"a\":\"b\"}]")]
    [InlineData("[\n? foo\n bar : baz\n]", "[{\"foo bar\":\"baz\"}]")]
    [InlineData("{? a : b, ? c}", "{\"a\":\"b\",\"c\":null}")]
    [InlineData("?\n: b\n", "{\"null\":\"b\"}")]
    public void ExplicitKey_ReadsLikeImplicitKey(string yaml, string expectedJson)
    {
        using var parsedYamlDocument = YamlDocument.Parse(yaml);
        using var expectedJsonDocument = JsonDocument.Parse(expectedJson);

        // JSON keys are strings, so the keys of the YAML map are compared as the text they are written with.
        Assert.Equal(
            JsonDocument.EncodeToString(expectedJsonDocument.RootElement),
            EncodeAsJsonWithTextKeys(parsedYamlDocument.RootElement)
        );
    }

    private static string EncodeAsJsonWithTextKeys(DElement element)
    {
        return element.Token.Type switch
        {
            DTokenType.Map => "{"
                + string.Join(
                    ",",
                    element
                        .AsMap()
                        .Select(pair =>
                            JsonSerializer.Serialize(GetKeyText(pair.Key.AsElement()))
                            + ":"
                            + EncodeAsJsonWithTextKeys(pair.Value.AsElement())
                        )
                )
                + "}",
            DTokenType.Array => "["
                + string.Join(
                    ",",
                    element.AsArray().Select(item => EncodeAsJsonWithTextKeys(item.AsElement()))
                )
                + "]",
            _ => JsonDocument.EncodeToString(element),
        };
    }

    private static string GetKeyText(DElement key)
    {
        if (key.Token.Kind == DTokenKind.Null)
        {
            return "null";
        }

        if (key.Token.Kind != DTokenKind.String)
        {
            return JsonDocument.EncodeToString(key);
        }

        var text = key.GetString();

        Debug.Assert(text != null); // GetString returns null only for a null token, which is handled above.

        return text;
    }

    [Theory]
    [InlineData("a:\n- 1\n- 2\nb: 3\n")]
    public void MapWithIndentlessArray_ParsesAndRoundTrips(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(document.RootElement);

        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(2, document.RootElement.GetPropertyCount());
        Assert.True(
            DValue.DeepEquals(new DArray(1, 2), document.RootElement.GetProperty("a").AsValue())
        );

        Assert.Equal(3, document.RootElement.GetProperty("b").GetInt64());
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }
}
