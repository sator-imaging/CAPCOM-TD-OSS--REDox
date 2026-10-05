using System;
using System.Globalization;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed partial class YamlPrimitiveValueTest
{
    [Theory]
    [InlineData("null")]
    [InlineData("~")]
    public void NullLiteral_ParsesAsNull(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    [InlineData("y", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public void BooleanLiteral_ParsesExpectedValue(string yaml, bool expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetBoolean());
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-12", -12L)]
    public void IntegerLiteral_ParsesExpectedValue(string yaml, long expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetInt64());
    }

    // The integers of the YAML 1.2 core schema:
    //   - decimal with an optional sign,
    //   - "0x" hexadecimal
    //   - "0o" octal
    // TODO: A leading zero does not make a decimal integer octal (that was YAML 1.1).
    //       This is intentionally dropped for now ("0o" can be used instead).
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
    public void Yaml12DecimalHexAndOctalIntegers_ParseExpectedValue(string yaml, long expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.Integer, document.RootElement.Token.Kind);
        Assert.Equal(expected, document.RootElement.GetInt64());
    }

    [Theory]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("0xFFFFFFFFFFFFFFFF", ulong.MaxValue)]
    public void IntegerAboveInt64_ParsesAsUnsignedValue(string yaml, ulong expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetUInt64());
    }

    [Theory]
    [InlineData("18446744073709551616")]
    [InlineData("-9223372036854775809")]
    [InlineData("123456789012345678901234567890")]
    public void IntegerBeyondSixtyFourBits_PreservesDigits(string yaml)
    {
        YamlTestHelper.AssertYamlParsesAsJsonValueAndRoundTrips(yaml, yaml);
    }

    // The YAML 1.1 binary integers and the underscores between digits are numbers (an underscore is for reading only).
    // The other YAML 1.1 forms are not: "190:20:30" and "0755" as an octal.
    [Theory]
    [InlineData("0b101", 5L)]
    [InlineData("0b1_01", 5L)]
    [InlineData("1_000", 1000L)]
    [InlineData("-1_000", -1000L)]
    [InlineData("+1_0", 10L)]
    [InlineData("1_0_0", 100L)]
    [InlineData("0x1_0", 16L)]
    [InlineData("0xF_F", 255L)]
    [InlineData("0o1_4", 12L)]
    [InlineData("9_223_372_036_854_775_807", long.MaxValue)]
    public void BinaryAndDigitSeparatedYaml11Integers_ParseAsNumbersAndWriteWithoutSeparators(
        string yaml,
        long expected
    )
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.Integer, document.RootElement.Token.Kind);
        Assert.Equal(expected, document.RootElement.GetInt64());

        Assert.Equal(
            expected.ToString(CultureInfo.InvariantCulture) + "\n",
            YamlDocument.EncodeToString(
                document.RootElement,
                new YamlWriteOptions { NewLine = "\n" }
            )
        );
    }

    [Theory]
    [InlineData("1_000.5", 1000.5)]
    [InlineData("1_0.0_1", 10.01)]
    [InlineData("-1_0.5", -10.5)]
    [InlineData("1_0e3", 10000.0)]
    [InlineData("1_0.5E-1", 1.05)]
    public void FloatWithDigitSeparators_ParsesExpectedValue(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.Float, document.RootElement.Token.Kind);
        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    [Fact]
    public void BigIntegerWithDigitSeparators_ParsesWithoutSeparators()
    {
        using var document = YamlDocument.Parse("1_000_000_000_000_000_000_000");

        Assert.Equal(DTokenKind.BigNumber, document.RootElement.Token.Kind);
        Assert.Equal("1000000000000000000000", JsonDocument.EncodeToString(document.RootElement));
    }

    [Fact]
    public void UnsignedIntegerWithDigitSeparators_ParsesExpectedValue()
    {
        using var document = YamlDocument.Parse("18_446_744_073_709_551_615");

        Assert.Equal(ulong.MaxValue, document.RootElement.GetUInt64());
    }

    // An underscore stands between two digits (hexadecimal digits in a hexadecimal number), not in an exponent.
    [Theory]
    [InlineData("_1")]
    [InlineData("1_")]
    [InlineData("1__0")]
    [InlineData("0x_1")]
    [InlineData("0x1_")]
    [InlineData("0b_1")]
    [InlineData("0b1_")]
    [InlineData("1_.5")]
    [InlineData("1._5")]
    [InlineData("1_e5")]
    [InlineData("1e1_0")]
    [InlineData("1_a")]
    [InlineData("a_1")]
    [InlineData("0b12")]
    [InlineData("0b1_2")]
    [InlineData("0xG_1")]
    [InlineData("-_1")]
    public void InvalidNumericSeparatorOrDigit_ParsesAsString(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, document.RootElement.Token.Kind);
        Assert.Equal(yaml, document.RootElement.GetString());
    }

    // The YAML 1.1 integer forms that are still not numbers are read as strings, so that a value such as a version or
    // a postal code is never altered.
    [Theory]
    [InlineData("190:20:30")]
    [InlineData("0o8")]
    [InlineData("0o")]
    [InlineData("0x")]
    [InlineData("0b")]
    [InlineData("0xG1")]
    [InlineData("0X1F")]
    [InlineData("0B101")]
    [InlineData("0xFFFFFFFFFFFFFFFFF")]
    [InlineData("0b11111111111111111111111111111111111111111111111111111111111111111")]
    public void UnsupportedYaml11IntegerForms_PreserveTextAsStrings(string yaml)
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
    public void NumericLookingStrings_WriteQuotedAndRoundTripAsStrings(string value)
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
    public void FloatLiteral_ParsesExpectedValue(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("3.5")]
    public void PrimitiveValue_RoundTripsThroughYaml(string json)
    {
        using var sourceJsonDocument = JsonDocument.Parse(json);

        var yamlFromJson = YamlDocument.EncodeToString(sourceJsonDocument.RootElement);

        using var convertedYamlDocument = YamlDocument.Parse(yamlFromJson);

        Assert.Equal(
            sourceJsonDocument.RootElement.ToJsonString(),
            convertedYamlDocument.RootElement.ToJsonString()
        );
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
    public void YamlSpecialFloatSpellings_ParseExpectedValue(string yaml, double expected)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expected, document.RootElement.GetDouble());
    }

    // YAML has only these spellings: ".inf", ".Inf" and ".INF" with an optional sign, and ".nan", ".NaN" and ".NAN"
    // without one. Any other word is a string, although a number parser may read it (as .NET does "Infinity").
    [Theory]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("+Infinity")]
    [InlineData("infinity")]
    [InlineData("INFINITY")]
    [InlineData("inf")]
    [InlineData("NaN")]
    [InlineData("nan")]
    [InlineData("NAN")]
    [InlineData("-nan")]
    [InlineData(".iNf")]
    [InlineData(".Nan")]
    [InlineData(".infinity")]
    [InlineData("+.nan")]
    [InlineData("-.nan")]
    public void NonSpecialFloatWords_ParseAsStrings(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(DTokenKind.String, document.RootElement.Token.Kind);
        Assert.Equal(yaml, document.RootElement.GetString());
    }

    // Negative zero is a float: written as an integer it would lose its sign.
    [Fact]
    public void NegativeZeroFloat_PreservesSignWhenWritten()
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
    public void SpecialFloats_RoundTrip(double value)
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(value, parsed.RootElement.GetDouble());
    }

    [Theory]
    [InlineData(IntegerKind.Default)]
    [InlineData(IntegerKind.Hexadecimal)]
    [InlineData(IntegerKind.Octal)]
    [InlineData(IntegerKind.Binary)]
    [InlineData(IntegerKind.Unsigned)]
    public void IntegerOfAnyKind_ReadsBackAsSameValue(IntegerKind kind)
    {
        var yaml = YamlDocument.EncodeToString(DValue.Create(255, kind).AsElement());

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal(255, parsed.RootElement.GetInt64());
    }

    [Fact]
    public void FloatOfAnyWidth_ReadsBackAsSameValue()
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
