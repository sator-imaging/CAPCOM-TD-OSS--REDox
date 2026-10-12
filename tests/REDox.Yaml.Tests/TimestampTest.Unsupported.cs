using System;

namespace REDox.Yaml.Tests;

public sealed partial class TimestampTest
{
    // YAML 1.1 speced sexagesimal numbers but this serializer won't support it.
    [Fact]
    public void LocalTime_WritesAsTextAndReadsBackAsString()
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