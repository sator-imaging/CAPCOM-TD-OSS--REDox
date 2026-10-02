using REDox;

namespace REDox.Yaml.Tests;

public sealed class YamlArrayTest
{
    [Theory]
    [InlineData("[]")]
    public void ParseEmptyArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(0, document.RootElement.GetArrayLength());
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("[1, true, null]")]
    public void ParseMixedArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.True(
            DValue.DeepEquals(new DArray(1, true, DValue.Null), document.RootElement.AsValue())
        );
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("- 1\n- 2\n")]
    public void ParseBlockArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.True(DValue.DeepEquals(new DArray(1, 2), document.RootElement.AsValue()));
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }

    [Theory]
    [InlineData("- [1, 2]\n- []\n")]
    public void ParseNestedArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        // Verify nested values independently of the YAML collection style used by the writer.
        Assert.True(
            DValue.DeepEquals(
                new DArray(new DArray(1, 2), new DArray()),
                document.RootElement.AsValue()
            )
        );
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue())
        );
    }
}
