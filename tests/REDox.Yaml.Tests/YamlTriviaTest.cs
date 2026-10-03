using System.Linq;

namespace REDox.Yaml.Tests;

public sealed class YamlTriviaTest
{
    [Fact]
    public void CommentIsPreservedWhenRequested()
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
    public void NestedAndTrailingCommentsAreKept(string yaml, string comment)
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

    // The white space in front of a trailing comment is kept as it was written, however long it is.
    [Theory]
    [InlineData("a: some       # comment\nb: 2\n")]
    [InlineData("a: some\t# comment\nb: 2\n")]
    [InlineData("a: some \t  \t # comment\nb: 2\n")]
    [InlineData("a: 1\nb: some       # last\n")]
    [InlineData("- 1     # one\n- 2          # two\n")]
    [InlineData("a:\n  b: 1    # inner\n  c: 2\n")]
    [InlineData("some       # comment\n")]
    [InlineData("list:\n  - x      # x\n  - z\t\t# z\n")]
    public void SpacesBeforeATrailingCommentAreKept(string yaml)
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
    public void TrailingCommentStaysOnItsLine()
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

    // Keys are always written double-quoted, so the expected text quotes them too.
    [Theory]
    [InlineData("1: 1 # one\n2: 2 # two\n", "1: 1 # one\n2: 2 # two\n")]
    [InlineData("- 1 # one\n- 2 # two\n", "- 1 # one\n- 2 # two\n")]
    [InlineData("1:\n  2: 3 # inner\n4: 5\n", "1:\n  2: 3 # inner\n4: 5\n")]
    [InlineData("1:\n  # head\n  - 2\n", "1:\n  # head\n  - 2\n")]
    // A comment on the key's line moves to its own line below it: Core cannot attach it to the key.
    [InlineData("1: # key\n  - 2\n", "1:\n  # key\n  - 2\n")]
    [InlineData("# top\n1: 2\n\n# bottom\n", "# top\n1: 2\n\n# bottom\n")]
    public void CommentPositionsRoundTrip(string yaml, string expected)
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
    public void HashInsideStringsIsNotAComment()
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
    public void CommentsOfAnInputWithoutContentAreKept(string yaml)
    {
        using var document = YamlDocument.Parse(
            yaml,
            options: new YamlDocumentOptions { PreserveTrivia = true }
        );

        // The root is the null that stands in for the missing content, and it carries the comments.
        Assert.Equal(DTokenKind.Null, document.RootElement.Token.Kind);
        var comments = document
            .RootElement.EnumerateTrivia()
            .Where(trivia => trivia.Kind == TriviaKind.LineComment)
            .Select(trivia => trivia.GetString())
            .ToArray();
        var expected = yaml.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('#'))
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

        // Written back, the comments must still be read as comments only.
        using var reparsed = YamlDocument.Parse(encoded);
        Assert.Equal(DTokenKind.Null, reparsed.RootElement.Token.Kind);
    }

    [Fact]
    public void BlankLineIsPreservedWhenRequested()
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
