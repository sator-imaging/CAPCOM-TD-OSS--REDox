using REDox;

namespace REDox.Yaml.Tests;

public sealed class YamlMapTest
{
    [Theory]
    [InlineData("{}")]
    public void ParseEmptyMap(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(0, document.RootElement.GetPropertyCount());
        Assert.True(DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue()));
    }

    [Theory]
    [InlineData("{a: 1, b: true}")]
    [InlineData("a: 1\nb: true\n")]
    public void ParseSimpleMap(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(2, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt64());
        Assert.True(document.RootElement.GetProperty("b").GetBoolean());
        Assert.True(DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue()));
    }

    [Theory]
    [InlineData("a:\n  b: 1\n")]
    public void ParseNestedMap(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetPropertyCount());
        Assert.Equal(1, document.RootElement.GetProperty("a").GetProperty("b").GetInt64());
        Assert.True(DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue()));
    }

    [Theory]
    [InlineData("a:\n  - 1\n  - 2\n")]
    public void ParseMapWithArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(1, document.RootElement.GetPropertyCount());
        Assert.True(DValue.DeepEquals(new DArray(1, 2), document.RootElement.GetProperty("a").AsValue()));
        Assert.True(DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue()));
    }

    [Theory]
    [InlineData("a:\n- 1\n- 2\nb: 3\n")]
    public void ParseMapWithIndentlessArray(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);
        var encoded = YamlDocument.EncodeToString(document.RootElement);
        using var parsed = YamlDocument.Parse(encoded);

        Assert.Equal(2, document.RootElement.GetPropertyCount());
        Assert.True(DValue.DeepEquals(new DArray(1, 2), document.RootElement.GetProperty("a").AsValue()));
        Assert.Equal(3, document.RootElement.GetProperty("b").GetInt64());
        Assert.True(DValue.DeepEquals(document.RootElement.AsValue(), parsed.RootElement.AsValue()));
    }
}
