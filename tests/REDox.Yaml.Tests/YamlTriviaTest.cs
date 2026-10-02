namespace REDox.Yaml.Tests;

public sealed class YamlTriviaTest
{
    [Fact]
    public void CommentIsPreservedWhenRequested()
    {
        const string yaml = "# first\na: 1\n# second\nb: 2\n";
        using var document = YamlDocument.Parse(yaml, options: new YamlDocumentOptions
        {
            PreserveTrivia = true
        });

        var encoded = YamlDocument.EncodeToString(document.RootElement, new YamlWriteOptions
        {
            PreserveTrivia = true
        });
        Assert.Contains("# first", encoded);
        Assert.Contains("# second", encoded);
    }

    [Fact]
    public void BlankLineIsPreservedWhenRequested()
    {
        using var document = YamlDocument.Parse("a: 1\n\nb: 2\n", options: new YamlDocumentOptions
        {
            PreserveTrivia = true
        });
        var encoded = YamlDocument.EncodeToString(document.RootElement, new YamlWriteOptions
        {
            PreserveTrivia = true,
            NewLine = "\n"
        });
        Assert.Contains("\n\n", encoded);
    }
}
