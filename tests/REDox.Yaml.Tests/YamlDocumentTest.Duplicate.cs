using System;
using System.Linq;

namespace REDox.Yaml.Tests;

public sealed partial class YamlDocumentTest
{
    [Fact]
    public void DisposedOriginal_DuplicateRetainsMixedScalarsNestedCollectionsAndTrivia()
    {
        var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        // Dispose original and try to take the same internal buffer and rewrite it (not perfect).
        originalYamlDocument.Dispose();
        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource
        );

        // Then, verify the duplicated data that is not affected.
        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                duplicatedYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );

        Assert.Equal("text", duplicatedYamlDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, duplicatedYamlDocument.RootElement.GetProperty("number").GetInt64());
        Assert.Equal(1.5, duplicatedYamlDocument.RootElement.GetProperty("float").GetDouble());
        Assert.True(duplicatedYamlDocument.RootElement.GetProperty("flag").GetBoolean());
        Assert.Equal(
            DTokenKind.Null,
            duplicatedYamlDocument.RootElement.GetProperty("nothing").Token.Kind
        );

        Assert.Equal(
            new DateTimeOffset(2001, 12, 14, 0, 0, 0, TimeSpan.Zero),
            duplicatedYamlDocument.RootElement.GetProperty("when").GetDateTimeOffset()
        );

        Assert.Equal(
            1,
            duplicatedYamlDocument
                .RootElement.GetProperty("list")
                .AsArray()[0]
                .AsElement()
                .GetInt64()
        );

        Assert.Equal(
            "two",
            duplicatedYamlDocument
                .RootElement.GetProperty("list")
                .AsArray()[1]
                .AsElement()
                .GetString()
        );

        Assert.Equal(
            "value",
            duplicatedYamlDocument.RootElement.GetProperty("nested").GetProperty("key").GetString()
        );

        Assert.Equal(
            "one\ntwo\n",
            duplicatedYamlDocument.RootElement.GetProperty("block").GetString()
        );
    }

    [Fact]
    public void DisposedOriginal_DuplicateRetainsNonStringKeys()
    {
        var originalYamlDocument = YamlDocument.Parse(
            "1: integer\ntrue: boolean\n2001-12-14: date\ntext: string\n"
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        originalYamlDocument.Dispose();

        Assert.Equal(
            new[]
            {
                DTokenKind.Integer,
                DTokenKind.Boolean,
                DTokenKind.Timestamp,
                DTokenKind.String,
            },
            duplicatedYamlDocument
                .RootElement.AsMap()
                .Select(static pair => pair.Key.AsElement().Token.Kind)
        );

        Assert.Equal(
            new[] { "integer", "boolean", "date", "string" },
            duplicatedYamlDocument
                .RootElement.AsMap()
                .Select(static pair => pair.Value.AsElement().GetString())
        );
    }

    [Fact]
    public void DisposedDuplicate_OriginalRemainsUsable()
    {
        using var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        duplicatedYamlDocument.Dispose();

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                originalYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );
    }

    [Fact]
    public void DuplicateOfDuplicate_RemainsIndependent()
    {
        var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        var firstDuplicate = originalYamlDocument.Duplicate();

        using var secondDuplicate = firstDuplicate.Duplicate();

        originalYamlDocument.Dispose();
        firstDuplicate.Dispose();

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                secondDuplicate.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );
    }

    [Fact]
    public void RemovingFromDuplicate_LeavesOriginalUnchanged()
    {
        using var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        Assert.True(duplicatedYamlDocument.RootElement.AsObject().Remove("number"));
        duplicatedYamlDocument.RootElement.GetProperty("list").AsArray().RemoveAt(0);
        duplicatedYamlDocument.RootElement.GetProperty("nested").AsObject().Remove("key");

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                originalYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );

        Assert.False(duplicatedYamlDocument.RootElement.AsObject().ContainsKey("number"));
        Assert.Equal(2, duplicatedYamlDocument.RootElement.GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void ReplacingDuplicateListItems_LeavesOriginalUnchanged()
    {
        using var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        duplicatedYamlDocument.RootElement.GetProperty("list").AsArray()[0] = DValue.Create(
            "replaced"
        );

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                originalYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );

        Assert.Equal(
            "replaced",
            duplicatedYamlDocument
                .RootElement.GetProperty("list")
                .AsArray()[0]
                .AsElement()
                .GetString()
        );
    }

    [Fact]
    public void EditingDuplicateNestedValues_LeavesOriginalUnchanged()
    {
        using var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        duplicatedYamlDocument.RootElement.GetProperty("nested").AsObject()["key"] = DValue.Create(
            "changed"
        );

        duplicatedYamlDocument
            .RootElement.GetProperty("nested")
            .AsObject()
            .Add("added", DValue.Create(true));

        duplicatedYamlDocument
            .RootElement.GetProperty("nested")
            .GetProperty("deep")
            .AsArray()
            .Add(DValue.Create(7));

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                originalYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );

        Assert.Equal(
            "changed",
            duplicatedYamlDocument.RootElement.GetProperty("nested").GetProperty("key").GetString()
        );
    }

    [Fact]
    public void EditingOriginalNestedValues_LeavesDuplicateUnchanged()
    {
        using var originalYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedYamlFromOriginal = YamlDocument.EncodeToString(
            originalYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        using var duplicatedYamlDocument = originalYamlDocument.Duplicate();

        originalYamlDocument.RootElement.GetProperty("nested").AsObject()["key"] = DValue.Create(
            "changed"
        );

        originalYamlDocument
            .RootElement.GetProperty("nested")
            .AsObject()
            .Add("added", DValue.Create(true));

        originalYamlDocument
            .RootElement.GetProperty("nested")
            .GetProperty("deep")
            .AsArray()
            .Add(DValue.Create(7));

        originalYamlDocument.RootElement.GetProperty("list").AsArray().RemoveAt(0);

        Assert.Equal(
            expectedYamlFromOriginal,
            YamlDocument.EncodeToString(
                duplicatedYamlDocument.RootElement,
                YamlTestFixture.TriviaWriteOptions
            )
        );
    }

    [Fact]
    public void EditingDuplicate_LeavesOriginalUnchanged()
    {
        const string yaml = "a: 1\nlist:\n  - x\ntext: hello\n";

        using var original = YamlDocument.Parse(yaml);
        using var duplicate = original.Duplicate();

        duplicate.RootElement.AsObject()["a"] = DValue.Create(2);

        duplicate.RootElement.AsObject().Add("added", DValue.Create(true));
        duplicate.RootElement.GetProperty("list").AsArray().Add(DValue.Create("y"));

        Assert.Equal(
            yaml,
            YamlDocument.EncodeToString(original.RootElement, YamlTestFixture.LineFeedWriteOptions)
        );

        Assert.Equal(2, duplicate.RootElement.GetProperty("a").GetInt64());
        Assert.Equal(2, duplicate.RootElement.GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void EditingOriginal_LeavesDuplicateUnchanged()
    {
        const string yaml = "a: 1\nlist:\n  - x\ntext: hello\n";

        using var original = YamlDocument.Parse(yaml);
        using var duplicate = original.Duplicate();

        original.RootElement.AsObject()["a"] = DValue.Create(2);

        original.RootElement.AsObject().Add("added", DValue.Create(true));
        original.RootElement.GetProperty("list").AsArray().Add(DValue.Create("y"));

        Assert.Equal(
            yaml,
            YamlDocument.EncodeToString(duplicate.RootElement, YamlTestFixture.LineFeedWriteOptions)
        );
    }

    [Fact]
    public void Duplicate_WritesSameTextAsOriginal()
    {
        const string yaml = "# head\na: 1 # one\nb:\n  - 'x'\n  - 2001-12-14\n";

        using var original = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        using var duplicate = original.Duplicate();

        var options = new YamlWriteOptions { NewLine = "\n", PreserveTrivia = true };

        Assert.Equal(
            YamlDocument.EncodeToString(original.RootElement, options),
            YamlDocument.EncodeToString(duplicate.RootElement, options)
        );
    }
}