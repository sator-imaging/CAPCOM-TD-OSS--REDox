using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using REDox;
using REDox.Cbor;
using REDox.MessagePack;

namespace REDox.Yaml.Tests;

public sealed partial class YamlFormatConversionTest
{
    // JSON is the hub: every sample is JSON text, and it is converted to each format and back to JSON. The comparison
    // is loose on purpose (see Flatten): the point is that every key, every list item and every nesting level
    // survives the conversion, not that a number stays a number.
    private static readonly Dictionary<string, string> Samples = new()
    {
        ["scalars"] =
            "{\"s\":\"text\",\"i\":42,\"f\":1.5,\"t\":true,\"u\":false,\"n\":null,\"neg\":-7}",
        ["nested-objects"] = "{\"a\":{\"b\":{\"c\":{\"d\":1,\"e\":\"deep\"}}}}",
        ["list-of-scalars"] = "{\"list\":[1,2,3],\"words\":[\"a\",\"b\"]}",
        ["array-of-maps"] = "{\"items\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]}",
        ["map-of-arrays"] = "{\"m\":{\"x\":[1,2],\"y\":[\"a\",\"b\"]}}",
        ["nested-arrays"] = "{\"grid\":[[1,2],[3,4],[5]]}",
        ["map-in-map-in-array"] = "{\"a\":[{\"b\":{\"c\":[{\"d\":1}]}}]}",
        ["mixed-array"] = "{\"mix\":[1,\"two\",true,null,{\"k\":\"v\"},[1]]}",
        ["empty-containers"] = "{\"e1\":[],\"e2\":{}}",
        ["special-strings"] =
            "{\"q\":\"a: b\",\"multi\":\"line1\\nline2\",\"uni\":\"日本語\",\"quote\":\"say \\\"hi\\\"\",\"num\":\"123\",\"bool\":\"true\",\"e\":\"\"}",
        ["root-array"] = "[1,2,{\"a\":1}]",
        ["root-scalar"] = "\"text\"",
    };

    // Each format as "JSON text in, JSON text out": the JSON is written in the format, read back from it, and written
    // as JSON again.
    private static readonly Dictionary<string, Func<string, string>> Formats = new()
    {
        ["yaml"] = json =>
            Through(json, e => YamlDocument.Encode(e), bytes => YamlDocument.Parse(bytes)),
        ["json5"] = json =>
            Through(
                json,
                e => REDox.Json.Json5Document.Encode(e),
                bytes => REDox.Json.Json5Document.Parse(bytes)
            ),
        ["dox"] = json =>
            Through(json, e => DoxDocument.Encode(e), bytes => DoxDocument.Parse(bytes)),
        ["cbor"] = json =>
            Through(json, e => CborDocument.Encode(e), bytes => CborDocument.Parse(bytes)),
        ["messagepack"] = json =>
            Through(
                json,
                e => MessagePackDocument.Encode(e),
                bytes => MessagePackDocument.Parse(bytes)
            ),
    };

    private static string Through(
        string json,
        Func<DElement, byte[]> encode,
        Func<byte[], Document> parse
    )
    {
        using var source = REDox.Json.JsonDocument.Parse(json);
        var bytes = encode(source.RootElement);
        using var document = parse(bytes);
        return REDox.Json.JsonDocument.EncodeToString(document.RootElement);
    }

    public static TheoryData<string, string> GetCombinations()
    {
        var data = new TheoryData<string, string>();
        foreach (var format in Formats.Keys)
        {
            foreach (var sample in Samples.Keys)
            {
                data.Add(format, sample);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GetCombinations))]
    public void JsonConvertsToTheFormatAndBack(string format, string sample)
    {
        var json = Samples[sample];

        var converted = Formats[format](json);

        Assert.Equal(Flatten(json), Flatten(converted));
    }

    // The JSON goes through YAML first and then through the other format, so the YAML reader and writer sit in the
    // middle of the conversion.
    [Theory]
    [MemberData(nameof(GetCombinations))]
    public void JsonConvertsThroughYamlToTheFormatAndBack(string format, string sample)
    {
        var json = Samples[sample];
        var viaYaml = Formats["yaml"](json);

        var converted = Formats[format](viaYaml);

        Assert.Equal(Flatten(json), Flatten(converted));
    }

    // A loose form of the JSON for comparing: one "path = text" line per scalar, in path order. A number, a string that
    // looks like it, a boolean and a string are all just their text, so formats that keep no types (XML, CSV, INI) can
    // still be compared. An empty container is a line of its own, so that it is not lost.
    private static string Flatten(string json)
    {
        using var document = JsonDocument.Parse(json);
        var lines = new List<string>();
        Flatten(document.RootElement, "$", lines);
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    private static void Flatten(JsonElement element, string path, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                lines.Add($"{path} = {{}}");
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, $"{path}.{property.Name}", lines);
                }

                break;
            case JsonValueKind.Array:
                lines.Add($"{path} = []");
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{path}[{index++}]", lines);
                }

                break;
            case JsonValueKind.Number:
                lines.Add(
                    $"{path} = {element.GetDouble().ToString("R", CultureInfo.InvariantCulture)}"
                );
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                Debug.Assert(text != null); // JsonElement.GetString returns null only for a JSON null, and this is a string.
                lines.Add(
                    $"{path} = {(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        ? number.ToString("R", CultureInfo.InvariantCulture)
                        : text)}"
                );
                break;
            default:
                lines.Add($"{path} = {element.GetRawText()}");
                break;
        }
    }

    // Values that only the binary formats hold, converted to YAML. CBOR and MessagePack both carry the map keys as
    // values, so a map can have integer keys; YAML reads such a key as an integer.
    private static readonly Dictionary<string, Func<DElement, byte[]>> BinaryEncoders = new()
    {
        ["cbor"] = element => CborDocument.Encode(element),
        ["messagepack"] = element => MessagePackDocument.Encode(element),
    };

    private static readonly Dictionary<string, Func<byte[], Document>> BinaryParsers = new()
    {
        ["cbor"] = bytes => CborDocument.Parse(bytes),
        ["messagepack"] = bytes => MessagePackDocument.Parse(bytes),
    };

    private static string YamlThrough(string format, DElement element)
    {
        using var document = BinaryParsers[format](BinaryEncoders[format](element));
        return YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void MapWithKeysOfOtherTypesBecomesYamlWithThoseKeys(string format)
    {
        var map = new DMap();
        map.Add(DValue.Create(1), DValue.Create("integer"));
        map.Add(DValue.Create(true), DValue.Create("boolean"));
        map.Add(DValue.Create("text"), DValue.Create("string"));
        map.Add(DValue.Create(2.5), DValue.Create("float"));

        var yaml = YamlThrough(format, map.AsElement());

        Assert.Equal("1: integer\ntrue: boolean\ntext: string\n2.5: float\n", yaml);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(
            new[] { DTokenKind.Integer, DTokenKind.Boolean, DTokenKind.String, DTokenKind.Float },
            parsed.RootElement.AsMap().Select(pair => pair.Key.AsElement().Token.Kind)
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void TimestampBecomesAYamlTimestamp(string format)
    {
        // A UTC time: MessagePack keeps the offset of a DateTimeOffset in a list next to the timestamp, which is its
        // own way of storing it and not a timestamp.
        var value = new DateTime(2001, 12, 14, 21, 59, 43, DateTimeKind.Utc);

        var yaml = YamlThrough(format, DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);
        Assert.True(parsed.RootElement.Token.Kind == DTokenKind.Timestamp, $"YAML: {yaml}");
        Assert.Equal(value, parsed.RootElement.GetDateTimeOffset().UtcDateTime);
    }

    // Nested values of every kind that YAML can hold survive the trip through a binary format.
    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void NestedValuesOfEveryKindSurviveTheBinaryFormat(string format)
    {
        var json =
            "{\"s\":\"text\",\"i\":42,\"f\":1.5,\"t\":true,\"n\":null,\"l\":[1,[2,3],{\"k\":\"v\"}],\"m\":{\"a\":{\"b\":[]}}}";
        using var source = REDox.Json.JsonDocument.Parse(json);

        var yaml = YamlThrough(format, source.RootElement);

        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(
            Flatten(json),
            Flatten(REDox.Json.JsonDocument.EncodeToString(parsed.RootElement))
        );
    }

    // CBOR's "undefined" (0xf7) has no YAML form of its own: it is a null.
    [Fact]
    public void CborUndefinedBecomesNull()
    {
        using var document = CborDocument.Parse([0xf7]);

        var yaml = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("null\n", yaml);
    }
}
