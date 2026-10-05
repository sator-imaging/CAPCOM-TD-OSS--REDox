using System.Linq;

namespace REDox.Yaml.Tests;

public sealed class YamlTriviaTest
{
    [Fact]
    public void RequestedCommentPreservation_KeepsComments()
    {
        const string yaml = "# first\na: 1\n# second\nb: 2\n";

        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true }
        );

        Assert.Contains("# first", encoded);
        Assert.Contains("# second", encoded);
    }

    [Theory]
    [InlineData("a: 1 # note\nb: 2\n", "# note")]
    [InlineData("list:\n  # head\n  - 1\n  - 2 # two\n", "# head")]
    [InlineData("list:\n  # head\n  - 1\n  - 2 # two\n", "# two")]
    [InlineData("[1, # one\n 2]\n", "# one")]
    public void NestedAndTrailingComments_RemainInOutput(string yaml, string comment)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true }
        );

        Assert.Contains(comment, encoded);

        using var reparsed = YamlDocument.Parse(encoded);
    }

    [Theory]
    [InlineData("a: some       # comment\nb: 2\n")]
    [InlineData("a: some\t# comment\nb: 2\n")]
    [InlineData("a: some \t  \t # comment\nb: 2\n")]
    [InlineData("a: 1\nb: some       # last\n")]
    [InlineData("- 1     # one\n- 2          # two\n")]
    [InlineData("a:\n  b: 1    # inner\n  c: 2\n")]
    [InlineData("some       # comment\n")]
    [InlineData("list:\n  - x      # x\n  - z\t\t# z\n")]
    public void SpacesBeforeTrailingComment_RemainInOutput(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        Assert.Equal(yaml, encoded);
    }

    [Fact]
    public void TrailingComment_StaysOnItsLine()
    {
        using var document = YamlDocument.Parse(
            "a: 1 # note\nb: 2\n",
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        Assert.Equal("a: 1 # note\nb: 2\n", encoded);
    }

    // A trailing comment must remain trivia when a string becomes a block scalar. Appending it to the
    // final content line changes the value, including for the last value nested inside a collection.
    [Theory]
    [InlineData("a: text # note\nb: next\n")]
    [InlineData("a: text # note\n")]
    [InlineData("- text # note\n- next\n")]
    [InlineData("- text # note\n")]
    [InlineData("text # note\n")]
    [InlineData("outer:\n  - text # note\nnext: value\n")]
    [InlineData("\"\" # note\n")]
    [InlineData("\"text\\n\" # note\n")]
    [InlineData("\"text\\n\\n\" # note\n")]
    [InlineData("\"first\\nsecond\" # note\n")]
    public void MultilineOutput_KeepsTrailingCommentsOutsideStringValues(string yaml)
    {
        var parseOptions = new YamlDocumentOptions { PreserveTrivia = true };

        using var document = YamlDocument.Parse(yaml, options: parseOptions);

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions
            {
                PreserveTrivia = true,
                StringStyle = YamlStringStyle.Multiline,
                NewLine = "\n",
            }
        );

        using var reparsed = YamlDocument.Parse(encoded, options: parseOptions);

        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), reparsed.RootElement.AsValue()),
            encoded
        );

        // Writing the reparsed value in quote style proves that the comment survived as trivia,
        // rather than merely checking for a hash inside the block scalar's source text.
        var encodedAgain = YamlDocument.EncodeToString(
            reparsed.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        Assert.Contains("# note", encodedAgain);
    }

    [Theory]
    [InlineData("1: 1 # one\n2: 2 # two\n", "1: 1 # one\n2: 2 # two\n")]
    [InlineData("- 1 # one\n- 2 # two\n", "- 1 # one\n- 2 # two\n")]
    [InlineData("1:\n  2: 3 # inner\n4: 5\n", "1:\n  2: 3 # inner\n4: 5\n")]
    [InlineData("1:\n  # head\n  - 2\n", "1:\n  # head\n  - 2\n")]
    // A comment on the key's line moves to its own line below it.
    // Core data model cannot attach it to the key.
    [InlineData("1: # key\n  - 2\n", "1:\n  # key\n  - 2\n")]
    [InlineData("# top\n1: 2\n\n# bottom\n", "# top\n1: 2\n\n# bottom\n")]
    public void CommentPositions_RoundTrip(string yaml, string expected)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void HashInsideStrings_RemainsStringContent()
    {
        const string yaml = "a: |\n  x # not a comment\n  y\nb: \"c # d\"\nc: 'e #f'\n";

        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true }
        );

        using var reparsed = YamlDocument.Parse(encoded);

        Assert.Equal("x # not a comment\ny\n", reparsed.RootElement.GetProperty("a").GetString());
        Assert.Equal("c # d", reparsed.RootElement.GetProperty("b").GetString());
        Assert.Equal("e #f", reparsed.RootElement.GetProperty("c").GetString());
        Assert.Equal(3, reparsed.RootElement.GetPropertyCount());
    }

    [Theory]
    [InlineData("# only a comment\n")]
    [InlineData("# first\n\n# second\n")]
    [InlineData("  # indented\n   \n\n")]
    public void CommentOnlyInput_PreservesCommentsAsTriviaAndNullRootAfterRoundTrip(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        // Core has no empty-document value, so the null root owns the comments.
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);

        var comments = document
            .RootElement.EnumerateTrivia()
            .Where(static trivia => trivia.Kind == TriviaKind.LineComment)
            .Select(static trivia => trivia.GetString())
            .ToArray();

        var expected = yaml.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith('#'))
            .ToArray();

        Assert.Equal(expected, comments);

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        foreach (var comment in expected)
        {
            Assert.Contains(comment, encoded);
        }

        using var reparsed = YamlDocument.Parse(encoded);

        Assert.Equal(DTokenKind.Null, reparsed.RootElement.Token.Kind);
    }

    [Fact]
    public void RequestedTriviaPreservation_KeepsBlankLine()
    {
        using var document = YamlDocument.Parse(
            "a: 1\n\nb: 2\n",
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        var encoded = YamlDocument.EncodeToString(
            document.RootElement,
            new YamlWriteOptions { PreserveTrivia = true, NewLine = "\n" }
        );

        Assert.Contains("\n\n", encoded);
    }
}
