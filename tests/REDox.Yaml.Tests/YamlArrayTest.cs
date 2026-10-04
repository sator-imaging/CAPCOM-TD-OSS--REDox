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

    // The block and flow shapes of an array, each checked against the value it must stand for.
    [Theory]
    [InlineData("- a: 1\n  b: 2\n- c: 3\n", "[{\"a\":1,\"b\":2},{\"c\":3}]")]
    [InlineData("-\n  a: 1\n  b: 2\n", "[{\"a\":1,\"b\":2}]")]
    [InlineData("- - 1\n  - 2\n- - 3\n", "[[1,2],[3]]")]
    [InlineData("- - - 1\n", "[[[1]]]")]
    [InlineData("-\n  - 1\n", "[[1]]")]
    [InlineData("-\n- 1\n", "[null,1]")]
    [InlineData("- 'single'\n- \"double\"\n- plain\n", "[\"single\",\"double\",\"plain\"]")]
    [InlineData("- a:\n    - 1\n    - 2\n", "[{\"a\":[1,2]}]")]
    [InlineData("[1, 2, ]", "[1,2]")]
    [InlineData("[a, 'b', \"c\"]", "[\"a\",\"b\",\"c\"]")]
    [InlineData("[[1], {a: 2}]", "[[1],{\"a\":2}]")]
    [InlineData("[1,\n 2,\n [3,\n  4]]", "[1,2,[3,4]]")]
    [InlineData("[\n  1,\n  2,\n]\n", "[1,2]")]
    [InlineData("- [1,\n  2]\n- [3]\n", "[[1,2],[3]]")]
    public void ArrayShapes(string yaml, string expectedJson)
    {
        YamlTestHelper.ParsesAs(yaml, expectedJson);
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
