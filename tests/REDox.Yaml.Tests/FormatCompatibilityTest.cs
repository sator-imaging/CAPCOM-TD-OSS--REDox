using System;
using System.Collections.Generic;
using System.Linq;
using REDox.Cbor;
using REDox.MessagePack;

namespace REDox.Yaml.Tests;

public sealed partial class FormatCompatibilityTest
{
    private static readonly Dictionary<string, string> YamlRoundTripSourcesByValueShape = new()
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
        foreach (var targetFormatName in RoundTripFormatEncoders.Keys)
        {
            foreach (var valueShapeName in YamlRoundTripSourcesByValueShape.Keys)
            {
                roundTripCases.Add(targetFormatName, valueShapeName);
            }
        }

        return roundTripCases;
    }

    [Theory]
    [MemberData(nameof(GetYamlRoundTripCases))]
    public void YamlRoundTripViaMajorFormat_PreservesParsedValuesAndTypesAtEachStage(
        string targetFormatName,
        string valueShapeName
    )
    {
        using var sourceYamlDocument = YamlDocument.Parse(
            YamlRoundTripSourcesByValueShape[valueShapeName]
        );

        using var yamlAsTargetFormatDocument = RoundTripFormatParsers[targetFormatName]
            (RoundTripFormatEncoders[targetFormatName](sourceYamlDocument.RootElement));

        // Check the intermediate value so a later YAML conversion cannot hide a change in the target format.
        Assert.True(
            DValue.DeepEquals(
                sourceYamlDocument.RootElement.AsValue(),
                yamlAsTargetFormatDocument.RootElement.AsValue()
            ),
            $"Converting {valueShapeName} from YAML to {targetFormatName} changed its value."
        );

        var yamlFromTargetFormat = YamlDocument.EncodeToString(
            yamlAsTargetFormatDocument.RootElement
        );

        using var restoredYamlDocument = YamlDocument.Parse(yamlFromTargetFormat);

        Assert.True(
            DValue.DeepEquals(
                sourceYamlDocument.RootElement.AsValue(),
                restoredYamlDocument.RootElement.AsValue()
            ),
            $"YAML round trip through {targetFormatName} changed {valueShapeName}."
        );
    }

    private static string ConvertElementToYamlViaBinaryFormat(
        string binaryFormatName,
        DElement sourceElement
    )
    {
        using var binaryFormatDocument = RoundTripFormatParsers[binaryFormatName]
            (RoundTripFormatEncoders[binaryFormatName](sourceElement));

        return YamlDocument.EncodeToString(
            binaryFormatDocument.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void BinaryMapKeysToYaml_PreserveKeyTypesAndValues(string binaryFormatName)
    {
        var sourceMapWithScalarKeys = new DMap
        {
            { DValue.Create(1), DValue.Create("integer") },
            { DValue.Create(true), DValue.Create("boolean") },
            { DValue.Create("text"), DValue.Create("string") },
            { DValue.Create(2.5), DValue.Create("float") },
        };

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(
            binaryFormatName,
            sourceMapWithScalarKeys.AsElement()
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromBinaryFormat);

        Assert.True(
            DValue.DeepEquals(
                sourceMapWithScalarKeys.AsElement().AsValue(),
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
    public void BinaryUtcTimestampToYaml_PreservesTimestampTypeAndUtcInstant(
        string binaryFormatName
    )
    {
        // Uses UTC time
        // MessagePack keeps the offset of a DateTimeOffset in a list next to the timestamp,
        // which is its own way of storing it and not a timestamp (not spec-ed by MsgPack format).
        var sourceUtcTimestamp = new DateTime(2001, 12, 14, 21, 59, 43, DateTimeKind.Utc);

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(
            binaryFormatName,
            DValue.Create(sourceUtcTimestamp).AsElement()
        );

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromBinaryFormat);

        Assert.True(
            convertedYamlDocument.RootElement.Token.Kind == DTokenKind.Timestamp,
            $"YAML: {yamlFromBinaryFormat}"
        );

        Assert.Equal(
            sourceUtcTimestamp,
            convertedYamlDocument.RootElement.GetDateTimeOffset().UtcDateTime
        );
    }

    [Theory]
    [InlineData("cbor")]
    [InlineData("messagepack")]
    public void YamlCompatibleNestedValuesViaBinaryFormat_PreserveParsedValuesAndKinds(
        string binaryFormatName
    )
    {
        var nestedJsonSource =
            "{\"s\":\"text\",\"i\":42,\"f\":1.5,\"t\":true,\"n\":null,\"l\":[1,[2,3],{\"k\":\"v\"}],\"m\":{\"a\":{\"b\":[]}}}";

        using var sourceJsonDocument = Json.JsonDocument.Parse(nestedJsonSource);

        var yamlFromBinaryFormat = ConvertElementToYamlViaBinaryFormat(
            binaryFormatName,
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