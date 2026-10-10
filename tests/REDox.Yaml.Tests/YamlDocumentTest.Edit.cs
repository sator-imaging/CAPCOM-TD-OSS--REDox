namespace REDox.Yaml.Tests;

public sealed partial class YamlDocumentTest
{
    [Fact]
    public void ReplacedValue_WritesUpdatedContent()
    {
        using var document = YamlDocument.Parse("a: 1\nb: two\n");

        document.RootElement.AsObject()["a"] = DValue.Create("changed");

        Assert.Equal(
            "a: changed\nb: two\n",
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions)
        );
    }

    [Fact]
    public void AddedValue_WritesAfterExistingValues()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        document.RootElement.AsObject().Add("b", DValue.Create(true));

        Assert.Equal(
            "a: 1\nb: true\n",
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions)
        );
    }

    [Fact]
    public void AddedStringLookingLikeAnotherType_WritesQuoted()
    {
        using var document = YamlDocument.Parse("a: 1\n");

        document.RootElement.AsObject().Add("b", DValue.Create("true"));
        document.RootElement.AsObject().Add("c", DValue.Create("2001-12-14"));

        var yaml = YamlDocument.EncodeToString(
            document.RootElement,
            YamlTestFixture.LineFeedWriteOptions
        );

        using var parsed = YamlDocument.Parse(yaml);

        Assert.Equal("true", parsed.RootElement.GetProperty("b").GetString());
        Assert.Equal("2001-12-14", parsed.RootElement.GetProperty("c").GetString());
    }

    [Fact]
    public void ListEdits_WriteUpdatedContent()
    {
        using var document = YamlDocument.Parse("- 1\n- 2\n- 3\n");

        var array = document.RootElement.AsArray();

        array.Add(DValue.Create(4));
        array.RemoveAt(0);

        array[0] = DValue.Create("two");

        Assert.Equal(
            "- two\n- 3\n- 4\n",
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions)
        );
    }

    [Fact]
    public void EditedDocument_ReadsBackWithEdits()
    {
        using var document = YamlDocument.Parse("a:\n  b: 1\nlist: [1, 2]\n");

        document.RootElement.GetProperty("a").AsObject()["b"] = DValue.Create("x: y");

        document.RootElement.GetProperty("list").AsArray().Add(DValue.Create(3));

        YamlTestFixture.Assert_YamlMatchesJsonValue_YamlRoundTrips(
            YamlDocument.EncodeToString(document.RootElement),
            "{\"a\":{\"b\":\"x: y\"},\"list\":[1,2,3]}"
        );
    }
}