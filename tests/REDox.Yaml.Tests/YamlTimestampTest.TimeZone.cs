using System;
using System.Runtime.CompilerServices;

namespace REDox.Yaml.Tests;

public sealed partial class YamlTimestampTest
{
    // Test must not pass because the machine it runs on happens to be in the zone the test expects.
    // To do so, the local zone is set to UTC-12 ("Etc/GMT+12"), the middle of the Pacific, west of
    // the date line. Only uninhabited islands (Baker and Howland Islands) keep that time,
    // so it is the zone of no country, no developer PC, CI runner or locale (UTC, Japan, the US, Europe, ...) is in it.
    private const string LocalZoneId = "Etc/GMT+12";

    // UTC-12, the same offset as the local zone.
    private static readonly TimeZoneInfo MidPacific = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MidPacific",
        TimeSpan.FromHours(-12),
        "Mid-Pacific (UTC-12)",
        "Mid-Pacific (UTC-12)"
    );

    // UTC+14, the largest offset there is, on the other side of the date line: 26 hours from the local zone, so the
    // same clock reading is a different instant, and the offset of no common locale. A custom zone, so it exists on
    // every platform.
    private static readonly TimeZoneInfo FarPacificEast = TimeZoneInfo.CreateCustomTimeZone(
        "Test/FarPacificEast",
        TimeSpan.FromHours(14),
        "Far Pacific East (UTC+14)",
        "Far Pacific East (UTC+14)"
    );

    private static bool LocalZoneIsMidPacific =>
        TimeZoneInfo.Local.BaseUtcOffset == TimeSpan.FromHours(-12);

    [ModuleInitializer]
    internal static void SetLocalZone()
    {
        Environment.SetEnvironmentVariable("TZ", LocalZoneId);

        // The local zone is read once and cached; discard what was read before this runs.
        TimeZoneInfo.ClearCachedData();
    }

    private static YamlDocumentOptions CreateParseOptionsWithTimeZone(TimeZoneInfo zone) =>
        new() { AssumedTimeZone = zone };

    [Fact]
    public void TestProcessLocalZone_IsMidPacific()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() == false,
            "Windows ignores the TZ variable, so the local zone of the process cannot be set."
        );

        Assert.Equal(TimeSpan.FromHours(-12), TimeZoneInfo.Local.BaseUtcOffset);
    }

    // The default is UTC, which is what YAML 1.1 specifies.
    [Theory]
    [InlineData("2001-12-14 21:59:43")]
    [InlineData("2001-12-14T21:59:43")]
    [InlineData("2001-12-14")]
    public void TimestampWithoutOffset_DefaultsToUtcInsteadOfLocalZone(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        var value = document.RootElement.GetDateTimeOffset();

        Assert.Equal(TimeSpan.Zero, value.Offset);

        if (LocalZoneIsMidPacific)
        {
            Assert.NotEqual(TimeZoneInfo.Local.GetUtcOffset(value), value.Offset);
        }
    }

    [Theory]
    [InlineData("2001-12-14 21:59:43", 2001, 12, 14, 21, 59, 43)]
    [InlineData("2001-12-14T21:59:43", 2001, 12, 14, 21, 59, 43)]
    [InlineData("2001-12-14", 2001, 12, 14, 0, 0, 0)]
    [InlineData("2001-12-31T23:59:59", 2001, 12, 31, 23, 59, 59)]
    [InlineData("2001-01-01T00:00:00", 2001, 1, 1, 0, 0, 0)]
    public void TimestampWithoutOffset_UsesGivenZoneOffset(
        string yaml,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second
    )
    {
        foreach (var zone in new[] { MidPacific, FarPacificEast, TimeZoneInfo.Utc })
        {
            using var document = YamlDocument.Parse(
                yaml,
                options: CreateParseOptionsWithTimeZone(zone)
            );

            var value = document.RootElement.GetDateTimeOffset();

            Assert.Equal(zone.BaseUtcOffset, value.Offset);
            Assert.Equal(new DateTime(year, month, day, hour, minute, second), value.DateTime);
        }
    }

    [Fact]
    public void SameTimestampText_RepresentsDifferentInstantInEachZone()
    {
        const string yaml = "2001-12-14 21:59:43";

        using var inMidPacific = YamlDocument.Parse(
            yaml,
            options: CreateParseOptionsWithTimeZone(MidPacific)
        );

        using var inFarEast = YamlDocument.Parse(
            yaml,
            options: CreateParseOptionsWithTimeZone(FarPacificEast)
        );

        var west = inMidPacific.RootElement.GetDateTimeOffset();
        var east = inFarEast.RootElement.GetDateTimeOffset();

        Assert.Equal(new DateTime(2001, 12, 15, 9, 59, 43), west.UtcDateTime);
        Assert.Equal(new DateTime(2001, 12, 14, 7, 59, 43), east.UtcDateTime);
        Assert.Equal(TimeSpan.FromHours(26), west - east);
    }

    [Theory]
    [InlineData("2001-12-14T21:59:43Z", 0)]
    [InlineData("2001-12-14T21:59:43+09:30", 570)]
    [InlineData("2001-12-14T21:59:43-05:00", -300)]
    [InlineData("2001-12-14 21:59:43 -5", -300)]
    [InlineData("2001-12-14T21:59:43+14:00", 840)]
    [InlineData("2001-12-14T21:59:43-12:00", -720)]
    public void TimestampWithOffset_IgnoresGivenZone(string yaml, int offsetMinutes)
    {
        foreach (var zone in new[] { MidPacific, FarPacificEast, TimeZoneInfo.Utc })
        {
            using var document = YamlDocument.Parse(
                yaml,
                options: CreateParseOptionsWithTimeZone(zone)
            );

            Assert.Equal(
                TimeSpan.FromMinutes(offsetMinutes),
                document.RootElement.GetDateTimeOffset().Offset
            );
        }
    }

    [Fact]
    public void DateTimeWithoutOffset_PreservesClockAndSetsKindFromAssumedTimeZone()
    {
        const string yaml = "2001-12-14 21:59:43";

        var clock = new DateTime(2001, 12, 14, 21, 59, 43);

        using var inUtc = YamlDocument.Parse(yaml);
        using var inMidPacific = YamlDocument.Parse(
            yaml,
            options: CreateParseOptionsWithTimeZone(MidPacific)
        );

        Assert.Equal(clock, inUtc.RootElement.GetDateTime());
        Assert.Equal(DateTimeKind.Utc, inUtc.RootElement.GetDateTime().Kind);
        Assert.Equal(clock, inMidPacific.RootElement.GetDateTime());
        Assert.Equal(DateTimeKind.Unspecified, inMidPacific.RootElement.GetDateTime().Kind);
    }

    // As a DateTime, a timestamp with an offset is the clock as written, whatever the offset is: that is how the core
    // reads a DateTimeOffset as a DateTime. The offset is only available from GetDateTimeOffset.
    [Theory]
    [InlineData("2001-12-14T21:59:43Z")]
    [InlineData("2001-12-14T21:59:43-12:00")]
    [InlineData("2001-12-14T21:59:43+14:00")]
    public void DateTimeWithOffset_PreservesWrittenClock(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: CreateParseOptionsWithTimeZone(MidPacific)
        );

        var value = document.RootElement.GetDateTime();

        Assert.Equal(new DateTime(2001, 12, 14, 21, 59, 43), value);
    }

    [Theory]
    [InlineData("2001-12-14 21:59:43", false)]
    [InlineData("2001-12-14", false)]
    [InlineData("2001-12-14T21:59:43-05:00", true)]
    public void AssumedTimeZone_DoesNotChangeWrittenText(string timestamp, bool hasOffset)
    {
        var yaml = "when: " + timestamp + "\n";
        foreach (var zone in new[] { MidPacific, FarPacificEast, TimeZoneInfo.Utc })
        {
            using var document = YamlDocument.Parse(
                yaml,
                options: CreateParseOptionsWithTimeZone(zone)
            );

            var offset = document.RootElement.GetProperty("when").GetDateTimeOffset().Offset;

            Assert.Equal(hasOffset ? TimeSpan.FromHours(-5) : zone.BaseUtcOffset, offset);

            var written = YamlDocument.EncodeToString(
                document.RootElement,
                new YamlWriteOptions { NewLine = "\n" }
            );

            Assert.Equal(yaml, written);
        }
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(14)]
    [InlineData(0)]
    [InlineData(9)]
    public void OffsetFromAnotherFormat_SurvivesYamlWritingAndReading(int offsetHours)
    {
        var value = new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.FromHours(offsetHours));

        var yaml = YamlDocument.EncodeToString(DValue.Create(value).AsElement());

        using var parsed = YamlDocument.Parse(
            yaml,
            options: CreateParseOptionsWithTimeZone(MidPacific)
        );

        var restored = parsed.RootElement.GetDateTimeOffset();

        Assert.Equal(value, restored);
        Assert.Equal(value.Offset, restored.Offset);
    }

    [Fact]
    public void ProcessLocalZone_DoesNotOverrideAssumedZoneOrExplicitOffset()
    {
        using var withOffset = YamlDocument.Parse("2001-12-14T21:59:43+09:00");
        using var withoutOffset = YamlDocument.Parse("2001-12-14T21:59:43");

        Assert.Equal(TimeSpan.FromHours(9), withOffset.RootElement.GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.Zero, withoutOffset.RootElement.GetDateTimeOffset().Offset);
    }
}
