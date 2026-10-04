using System;

namespace REDox.Yaml.Tests;

public sealed partial class YamlTimestampTest
{
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
}
