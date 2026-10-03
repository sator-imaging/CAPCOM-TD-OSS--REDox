using System;
using REDox;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlPrimitiveValueTest
{
    [Theory]
    [InlineData("null")]
    [InlineData("~")]
    public void ParseNull(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    public void ParseBoolean(string yaml, bool expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetBoolean());
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-12", -12L)]
    public void ParseInteger(string yaml, long expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetInt64());
    }

    // The integers of the YAML 1.2 core schema: decimal with an optional sign, "0x" hexadecimal and "0o" octal.
    // A leading zero does not make a decimal integer octal (that was YAML 1.1).
    [Theory]
    [InlineData("+12", 12L)]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("0755", 755L)]
    [InlineData("0x1F", 31L)]
    [InlineData("0xff", 255L)]
    [InlineData("0o14", 12L)]
    [InlineData("0o777", 511L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("0x7FFFFFFFFFFFFFFF", long.MaxValue)]
    public void ParseCoreSchemaInteger(string yaml, long expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.Integer, document.RootElement.Token.Kind);
        Assert.Equal(expected, document.RootElement.GetInt64());
    }

    [Theory]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("0xFFFFFFFFFFFFFFFF", ulong.MaxValue)]
    public void ParseIntegerAboveInt64(string yaml, ulong expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetUInt64());
    }

    // Integers that no 64-bit type holds keep every digit.
    [Theory]
    [InlineData("18446744073709551616")]
    [InlineData("-9223372036854775809")]
    [InlineData("123456789012345678901234567890")]
    public void ParseIntegerBeyondSixtyFourBits(string yaml)
    {
        YamlTestHelper.ParsesAs(yaml, yaml);
    }

    // The YAML 1.1 integer forms are not numbers: they are read as strings, so that a value such as a version or a
    // postal code is never altered.
    [Theory]
    [InlineData("0b101")]
    [InlineData("1_000")]
    [InlineData("190:20:30")]
    [InlineData("0o8")]
    [InlineData("0o")]
    [InlineData("0x")]
    [InlineData("0xG1")]
    [InlineData("0X1F")]
    [InlineData("0x1_0")]
    [InlineData("0xFFFFFFFFFFFFFFFFF")]
    public void YamlOneDotOneIntegerFormsAreStrings(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.String, document.RootElement.Token.Kind);
        Assert.Equal(yaml, document.RootElement.GetString());
    }

    // A string that looks like a number is written quoted, so that it is read back as the same string.
    [Theory]
    [InlineData("0x1F")]
    [InlineData("0o14")]
    [InlineData("0b101")]
    [InlineData("1_000")]
    [InlineData("+12")]
    [InlineData("190:20:30")]
    [InlineData("0755")]
    public void StringsThatLookLikeNumbersAreWrittenQuoted(string value)
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        Assert.StartsWith("\"", yaml);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(value, parsed.RootElement.GetString());
    }

    [Theory]
    [InlineData("3.5", 3.5)]
    [InlineData("1e3", 1000.0)]
    [InlineData("1E3", 1000.0)]
    [InlineData("+1.5", 1.5)]
    [InlineData("-1.5e-2", -0.015)]
    [InlineData(".5", 0.5)]
    [InlineData("-.5", -0.5)]
    [InlineData("5.", 5.0)]
    [InlineData("0.0", 0.0)]
    public void ParseFloat(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("3.5")]
    public void PrimitiveRoundTrip(string json)
    {
        using var source = JsonDocument.Parse(json);
        var yaml = YamlDocument.EncodeToString(source.RootElement);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(source.RootElement.ToJsonString(), parsed.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData(".inf", double.PositiveInfinity)]
    [InlineData(".Inf", double.PositiveInfinity)]
    [InlineData(".INF", double.PositiveInfinity)]
    [InlineData("+.inf", double.PositiveInfinity)]
    [InlineData("+.INF", double.PositiveInfinity)]
    [InlineData("-.inf", double.NegativeInfinity)]
    [InlineData("-.Inf", double.NegativeInfinity)]
    [InlineData("-.INF", double.NegativeInfinity)]
    [InlineData(".nan", double.NaN)]
    [InlineData(".NaN", double.NaN)]
    [InlineData(".NAN", double.NaN)]
    public void ParseSpecialFloat(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    // Negative zero is a float: written as an integer it would lose its sign.
    [Fact]
    public void NegativeZeroFloatKeepsItsSignWhenWritten()
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(-0.0).AsElement());

        using var parsed = YamlDocument.Parse(yaml);
        Assert.True(double.IsNegative(parsed.RootElement.GetDouble()));
        Assert.Equal(0.0, parsed.RootElement.GetDouble());
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void SpecialFloatsRoundTrip(double value)
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(value, parsed.RootElement.GetDouble());
    }

    // A symbol of any kind is written as the text it holds.
    [Theory]
    [InlineData(SymbolKind.Default)]
    [InlineData(SymbolKind.Identifier)]
    [InlineData(SymbolKind.Reference)]
    [InlineData(SymbolKind.TypeName)]
    [InlineData(SymbolKind.Metadata)]
    public void SymbolIsWrittenAsItsText(SymbolKind kind)
    {
        var value = DValue.Create(new REDox.Serialization.Utf8Symbol("name"), kind);

        var yaml = YamlDocument.EncodeToString(
            value.AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("name\n", yaml);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal("name", parsed.RootElement.GetString());
    }

    // An integer of any kind (hexadecimal, octal, binary, unsigned) is written as a number that reads back as the
    // same value.
    [Theory]
    [InlineData(IntegerKind.Default)]
    [InlineData(IntegerKind.Hexadecimal)]
    [InlineData(IntegerKind.Octal)]
    [InlineData(IntegerKind.Binary)]
    [InlineData(IntegerKind.Unsigned)]
    public void IntegerOfAnyKindReadsBackAsTheSameValue(IntegerKind kind)
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(255, kind).AsElement());

        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(255, parsed.RootElement.GetInt64());
    }

    // A float of any width (half, single, decimal) reads back as the same value.
    [Fact]
    public void FloatOfAnyWidthReadsBackAsTheSameValue()
    {
        foreach (
            var value in new[]
            {
                DValue.Create((Half)1.5),
                DValue.Create(1.5f),
                DValue.Create(1.5m),
                DValue.Create(1.5),
            }
        )
        {
            var yaml = YamlDocument.EncodeToString(value.AsElement());

            using var parsed = YamlDocument.Parse(yaml);
            Assert.Equal(1.5, parsed.RootElement.GetDouble());
        }
    }
}
