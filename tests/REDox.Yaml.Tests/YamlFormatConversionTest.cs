using System;
using System.Collections.Generic;
using System.Linq;
using REDox.Cbor;
using REDox.MessagePack;

namespace REDox.Yaml.Tests;

public sealed partial class YamlFormatConversionTest
{
    private static readonly Dictionary<string, string> YamlRoundTripSamples = new()
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

    private static readonly Dictionary<string, Func<DElement, byte[]>> RoundTripFormatEncoders =
        new()
        {
            ["json"] = static element => Json.JsonDocument.Encode(element),
            ["dox"] = static element => DoxDocument.Encode(element),
            ["cbor"] = static element => CborDocument.Encode(element),
            ["messagepack"] = static element => MessagePackDocument.Encode(element),
        };

    private static readonly Dictionary<string, Func<byte[], Document>> RoundTripFormatParsers =
        new()
        {
            ["json"] = static bytes => Json.JsonDocument.Parse(bytes),
            ["dox"] = static bytes => DoxDocument.Parse(bytes),
            ["cbor"] = static bytes => CborDocument.Parse(bytes),
            ["messagepack"] = static bytes => MessagePackDocument.Parse(bytes),
        };

    public static TheoryData<string, string> GetYamlRoundTripCases()
    {
        var roundTripCases = new TheoryData<string, string>();
        foreach (var format in RoundTripFormatEncoders.Keys)
        {
            foreach (var sample in YamlRoundTripSamples.Keys)
            {
                roundTripCases.Add(format, sample);
            }
        }

        return roundTripCases;
    }

    [Theory]
    [MemberData(nameof(GetYamlRoundTripCases))]
    public void YamlRoundTripViaMajorFormat_PreservesValuesAndTypes(string format, string sample)
    {
        using var sourceYamlDocument = YamlDocument.Parse(YamlRoundTripSamples[sample]);
        using var yamlAsTargetFormatDocument = RoundTripFormatParsers[format]
            (RoundTripFormatEncoders[format](sourceYamlDocument.RootElement));

        // Check the intermediate value so a later YAML conversion cannot hide a change in the target format.
        Assert.True(
            DValue.DeepEquals(
                sourceYamlDocument.RootElement.AsValue(),
                yamlAsTargetFormatDocument.RootElement.AsValue()
            ),
            $"Converting {sample} from YAML to {format} changed its value."
        );

        var yamlFromTargetFormat = YamlDocument.EncodeToString(
            yamlAsTargetFormatDocument.RootElement
        );

        using var restoredYamlDocument = YamlDocument.Parse(yamlFromTargetFormat);

        // Compare the DOM values directly so a scalar changing type cannot pass the round trip.
        Assert.True(
            DValue.DeepEquals(
                sourceYamlDocument.RootElement.AsValue(),
                restoredYamlDocument.RootElement.AsValue()
            ),
            $"YAML round trip through {format} changed {sample}."
        );
    }

    // Values that only the binary formats hold, converted to YAML. CBOR and MessagePack both carry the map keys as
    // values, so a map can have integer keys; YAML reads such a key as an integer.
    private static string ConvertElementToYamlViaBinaryFormat(string format, DElement element)
    {
        using var binaryFormatDocument = RoundTripFormatParsers[format]
            (RoundTripFormatEncoders[format](element));

        return YamlDocument.EncodeToString(
            binaryFormatDocument.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void BinaryMapKeysToYaml_PreserveKeyTypes(string format)
    {
        var map = new DMap
        {
            { DValue.Create(1), DValue.Create("integer") },
            { DValue.Create(true), DValue.Create("boolean") },
            { DValue.Create("text"), DValue.Create("string") },
            { DValue.Create(2.5), DValue.Create("float") },
        };

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(format, map.AsElement());

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromBinaryFormat);

        // Checking key types alone could miss a changed key or value.
        Assert.True(
            DValue.DeepEquals(
                map.AsElement().AsValue(),
                convertedYamlDocument.RootElement.AsValue()
            )
        );

        Assert.Equal(
            new[] { DTokenKind.Integer, DTokenKind.Boolean, DTokenKind.String, DTokenKind.Float },
            convertedYamlDocument
                .RootElement.AsMap()
                .Select(static pair => pair.Key.AsElement().Token.Kind)
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void BinaryTimestampToYaml_PreservesTimestampType(string format)
    {
        // A UTC time: MessagePack keeps the offset of a DateTimeOffset in a list next to the timestamp, which is its
        // own way of storing it and not a timestamp.
        var value = new DateTime(2001, 12, 14, 21, 59, 43, DateTimeKind.Utc);

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(
            format,
            DValue.Create(value).AsElement()
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromBinaryFormat);

        // Timestamp text can change across CBOR and YAML even when the UTC time does not; DeepEquals compares that text.
        Assert.True(
            convertedYamlDocument.RootElement.Token.Kind == DTokenKind.Timestamp,
            $"YAML: {yamlFromBinaryFormat}"
        );

        Assert.Equal(value, convertedYamlDocument.RootElement.GetDateTimeOffset().UtcDateTime);
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void YamlCompatibleNestedValuesViaBinaryFormat_PreserveValueKinds(string format)
    {
        var json =
            "{\"s\":\"text\",\"i\":42,\"f\":1.5,\"t\":true,\"n\":null,\"l\":[1,[2,3],{\"k\":\"v\"}],\"m\":{\"a\":{\"b\":[]}}}";

        using var sourceJsonDocument = Json.JsonDocument.Parse(json);

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(
            format,
            sourceJsonDocument.RootElement
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromBinaryFormat);

        Assert.True(
            DValue.DeepEquals(
                sourceJsonDocument.RootElement.AsValue(),
                convertedYamlDocument.RootElement.AsValue()
            )
        );
    }
}
