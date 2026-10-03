using System;
using System.Linq;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlApiAndConcurrencyTest
{
    // Every kind of value, a nested map and list, and comments, so that nothing the duplicate holds is left out.
    private const string DuplicateSource =
        "# head\nname: text # note\nnumber: 42\nfloat: 1.5\nflag: true\nnothing: null\nwhen: 2001-12-14\n"
        + "list:\n  - 1\n  - two\n  - [3, 4]\nnested:\n  key: value\n  deep:\n    - x: 1\n    - y\nblock: |\n  one\n  two\n";

    private static readonly YamlDocumentOptions TriviaParseOptions = new()
    {
        PreserveTrivia = true,
    };

    private static readonly YamlWriteOptions TriviaWriteOptions = new()
    {
        NewLine = "\n",
        PreserveTrivia = true,
    };

    private static string TextOf(YamlDocument document) =>
        YamlDocument.EncodeToString(document.RootElement, TriviaWriteOptions);

    // The original is disposed: its buffers go back to the pool, which another document may take. The duplicate has
    // buffers of its own, so it still holds every value.
    [Fact]
    public void DuplicateHoldsEveryKindOfValueAfterTheOriginalIsDisposed()
    {
        var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        using var duplicate = original.Duplicate();

        original.Dispose();
        // Another document takes the buffer that the original returned, and writes over it.
        using var other = YamlDocument.Parse(
            "name: 0123456789\nnumber: 0123456789\nlist: [0123456789, 0123456789, 0123456789]\n"
        );

        Assert.Equal(expected, TextOf(duplicate));
        Assert.Equal("text", duplicate.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, duplicate.RootElement.GetProperty("number").GetInt64());
        Assert.Equal(1.5, duplicate.RootElement.GetProperty("float").GetDouble());
        Assert.True(duplicate.RootElement.GetProperty("flag").GetBoolean());
        Assert.Equal(DTokenKind.Null, duplicate.RootElement.GetProperty("nothing").Token.Kind);
        Assert.Equal(
            new DateTimeOffset(2001, 12, 14, 0, 0, 0, TimeSpan.Zero),
            duplicate.RootElement.GetProperty("when").GetDateTimeOffset()
        );
        Assert.Equal(
            1,
            duplicate.RootElement.GetProperty("list").AsArray()[0].AsElement().GetInt64()
        );
        Assert.Equal(
            "two",
            duplicate.RootElement.GetProperty("list").AsArray()[1].AsElement().GetString()
        );
        Assert.Equal(
            "value",
            duplicate.RootElement.GetProperty("nested").GetProperty("key").GetString()
        );
        Assert.Equal("one\ntwo\n", duplicate.RootElement.GetProperty("block").GetString());
    }

    [Fact]
    public void DuplicateHoldsKeysOfOtherTypesAfterTheOriginalIsDisposed()
    {
        var original = YamlDocument.Parse(
            "1: integer\ntrue: boolean\n2001-12-14: date\ntext: string\n"
        );
        using var duplicate = original.Duplicate();

        original.Dispose();

        Assert.Equal(
            new[]
            {
                DTokenKind.Integer,
                DTokenKind.Boolean,
                DTokenKind.Timestamp,
                DTokenKind.String,
            },
            duplicate.RootElement.AsMap().Select(pair => pair.Key.AsElement().Token.Kind)
        );
        Assert.Equal(
            new[] { "integer", "boolean", "date", "string" },
            duplicate.RootElement.AsMap().Select(pair => pair.Value.AsElement().GetString())
        );
    }

    [Fact]
    public void DisposingTheDuplicateLeavesTheOriginalAlone()
    {
        using var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        var duplicate = original.Duplicate();

        duplicate.Dispose();

        Assert.Equal(expected, TextOf(original));
    }

    [Fact]
    public void DuplicateOfADuplicateIsIndependent()
    {
        var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        var first = original.Duplicate();
        using var second = first.Duplicate();

        original.Dispose();
        first.Dispose();

        Assert.Equal(expected, TextOf(second));
    }

    [Fact]
    public void RemovingFromTheDuplicateLeavesTheOriginalAlone()
    {
        using var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        using var duplicate = original.Duplicate();

        Assert.True(duplicate.RootElement.AsObject().Remove("number"));
        duplicate.RootElement.GetProperty("list").AsArray().RemoveAt(0);
        duplicate.RootElement.GetProperty("nested").AsObject().Remove("key");

        Assert.Equal(expected, TextOf(original));
        Assert.False(duplicate.RootElement.AsObject().ContainsKey("number"));
        Assert.Equal(2, duplicate.RootElement.GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void ReplacingListItemsInTheDuplicateLeavesTheOriginalAlone()
    {
        using var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        using var duplicate = original.Duplicate();

        duplicate.RootElement.GetProperty("list").AsArray()[0] = DValue.Create("replaced");

        Assert.Equal(expected, TextOf(original));
        Assert.Equal(
            "replaced",
            duplicate.RootElement.GetProperty("list").AsArray()[0].AsElement().GetString()
        );
    }

    [Fact]
    public void EditingNestedValuesOfTheDuplicateLeavesTheOriginalAlone()
    {
        using var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        using var duplicate = original.Duplicate();

        duplicate.RootElement.GetProperty("nested").AsObject()["key"] = DValue.Create("changed");
        duplicate.RootElement.GetProperty("nested").AsObject().Add("added", DValue.Create(true));
        duplicate
            .RootElement.GetProperty("nested")
            .GetProperty("deep")
            .AsArray()
            .Add(DValue.Create(7));

        Assert.Equal(expected, TextOf(original));
        Assert.Equal(
            "changed",
            duplicate.RootElement.GetProperty("nested").GetProperty("key").GetString()
        );
    }

    [Fact]
    public void EditingNestedValuesOfTheOriginalLeavesTheDuplicateAlone()
    {
        using var original = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(original);
        using var duplicate = original.Duplicate();

        original.RootElement.GetProperty("nested").AsObject()["key"] = DValue.Create("changed");
        original.RootElement.GetProperty("nested").AsObject().Add("added", DValue.Create(true));
        original
            .RootElement.GetProperty("nested")
            .GetProperty("deep")
            .AsArray()
            .Add(DValue.Create(7));
        original.RootElement.GetProperty("list").AsArray().RemoveAt(0);

        Assert.Equal(expected, TextOf(duplicate));
    }
}
