using System;

namespace REDox.Yaml.Tests;

public sealed partial class YamlTimestampTest
{
    [Theory]
    [InlineData("2001-12-14", DTokenVariant.TimestampLocalDate)]
    [InlineData("2001-1-1", DTokenVariant.TimestampLocalDate)]
    [InlineData("2001-12-14T21:59:43", DTokenVariant.TimestampLocalDateTime)]
    [InlineData("2001-12-14 21:59:43.10", DTokenVariant.TimestampLocalDateTime)]
    [InlineData("2001-12-14T21:59:43Z", DTokenVariant.TimestampOffsetDateTime)]
    [InlineData("2001-12-14t21:59:43.10-05:00", DTokenVariant.TimestampOffsetDateTime)]
    [InlineData("2001-12-14 21:59:43.10 -5", DTokenVariant.TimestampOffsetDateTime)]
    public void PlainTimestampsAreTimestamps(string text, DTokenVariant variant)
    {
        using var document = YamlDocument.Parse(text);
        Assert.Equal(DTokenKind.Timestamp, document.RootElement.Token.Kind);
        Assert.Equal(variant, document.RootElement.Token.Variant);
    }

    [Theory]
    [InlineData("2001-13-45")]
    [InlineData("2001-02-30")]
    [InlineData("2001-12-14 25:00:00")]
    [InlineData("2001-12-14T21:59:43+15:00")]
    [InlineData("2001-12-14T21:59")]
    // YAML 1.1 only knows an upper-case Z.
    [InlineData("2001-12-14T21:59:43z")]
    [InlineData("12:30")]
    [InlineData("\"2001-12-14\"")]
    [InlineData("'2001-12-14'")]
    public void OtherTextsStayStrings(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.String, document.RootElement.Token.Kind);
    }

    // A string that reads as a timestamp is written quoted, or it would come back as a timestamp. A text that only
    // looks like one (not a real date) stays a string and needs no quotes.
    [Theory]
    [InlineData("2001-12-14", false)]
    [InlineData("2001-12-14T21:59:43.10Z", false)]
    [InlineData("2001-12-14 21:59:43.10 -5", false)]
    [InlineData("2001-13-45", true)]
    public void StringThatLooksLikeTimestampIsQuotedOnlyWhenItIsOne(string value, bool plain)
    {
        var yaml = YamlDocument.EncodeToString(
            DValue.Create(value).AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(plain, !yaml.StartsWith('"'));
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(value, parsed.RootElement.GetString());
    }

    // The text is never normalized: spacing, the separator, the case of T and Z, and a fraction longer than a
    // DateTime can hold all come back as they were written.
    [Theory]
    [InlineData("2001-12-14")]
    [InlineData("2001-1-1")]
    [InlineData("2001-12-14t21:59:43.10-5")]
    [InlineData("2001-12-14 21:59:43.10 -5")]
    [InlineData("2001-12-14   21:59:43   +09:30")]
    [InlineData("2001-12-14T21:59:43.1234567890123Z")]
    [InlineData("2001-12-14T21:59:43.Z")]
    [InlineData("2001-12-14T21:59:43")]
    public void TimestampsRoundTripAsWritten(string timestamp)
    {
        var yaml = "map:\n  key: " + timestamp + "\nlist:\n  - " + timestamp + "\n";
        using var document = YamlDocument.Parse(yaml);

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal(yaml, encoded);
    }

    [Fact]
    public void RootTimestampRoundTripsAsWritten()
    {
        using var document = YamlDocument.Parse("2001-12-14 21:59:43.10 -5\n");

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("2001-12-14 21:59:43.10 -5\n", encoded);
    }

    [Fact]
    public void OffsetTimestampIsReadWithItsOffset()
    {
        using var document = YamlDocument.Parse("2001-12-14 21:59:43.10 -5");

        Assert.Equal(
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, 100, TimeSpan.FromHours(-5)),
            document.RootElement.GetDateTimeOffset()
        );
    }

    [Fact]
    public void TimestampWithoutOffsetIsUtcByDefault()
    {
        using var document = YamlDocument.Parse("2001-12-14 21:59:43");

        Assert.Equal(
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.Zero),
            document.RootElement.GetDateTimeOffset()
        );
    }

    [Fact]
    public void TimestampWithoutOffsetUsesTheGivenTimeZone()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone(
            "Tokyo",
            TimeSpan.FromHours(9),
            "Tokyo",
            "Tokyo"
        );
        var options = new YamlDocumentOptions { TimeZoneForUnspecifiedOffset = tokyo };

        using var document = YamlDocument.Parse(
            "local: 2001-12-14 21:59:43\nexplicit: 2001-12-14 21:59:43Z\ndate: 2001-12-14\n",
            options: options
        );

        Assert.Equal(
            TimeSpan.FromHours(9),
            document.RootElement.GetProperty("local").GetDateTimeOffset().Offset
        );
        Assert.Equal(
            TimeSpan.Zero,
            document.RootElement.GetProperty("explicit").GetDateTimeOffset().Offset
        );
        Assert.Equal(
            TimeSpan.FromHours(9),
            document.RootElement.GetProperty("date").GetDateTimeOffset().Offset
        );

        // The option changes how the value is read, never the written text.
        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { NewLine = "\n" }
        );
        Assert.Equal(
            "local: 2001-12-14 21:59:43\nexplicit: 2001-12-14 21:59:43Z\ndate: 2001-12-14\n",
            encoded
        );
    }

    // YAML has no time of day without a date: a local time of another format is written as its text, and it reads
    // back as a string, because "12:30:00" is not a timestamp (and the library reads no sexagesimal numbers).
    [Fact]
    public void LocalTimeIsWrittenAsTextAndReadsBackAsString()
    {
        var value = DValue.Create(new DateTime(1, 1, 1, 12, 30, 0), TimestampKind.LocalTime);

        var yaml = YamlDocument.EncodeToString(
            value.AsElement(),
            new YamlWriteOptions { NewLine = "\n" }
        );

        Assert.Equal("12:30:00\n", yaml);
        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.String, parsed.RootElement.Token.Kind);
        Assert.Equal("12:30:00", parsed.RootElement.GetString());
    }

    [Fact]
    public void TimestampOfAnotherFormatIsWrittenAndReadBack()
    {
        var value = new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.FromHours(9));

        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(yaml);
        Assert.Equal(DTokenKind.Timestamp, parsed.RootElement.Token.Kind);
        Assert.Equal(value, parsed.RootElement.GetDateTimeOffset());
    }
}
