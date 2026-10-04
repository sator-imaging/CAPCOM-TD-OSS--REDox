using System.Text;

namespace REDox.Yaml.Tests;

public sealed class YamlDeepNestingTest
{
    private const int Depth = 300;

    [Fact]
    public void DeeplyNestedCollectionsRoundTrip()
    {
        var blockMaps = new StringBuilder();
        for (var level = 0; level < Depth; level++)
        {
            blockMaps.Append(' ', level * 2).Append("key:\n");
        }

        blockMaps.Append(' ', Depth * 2).Append("leaf: 1\n");

        var blockSequences = new StringBuilder();
        for (var level = 0; level < Depth; level++)
        {
            blockSequences.Append("- ");
        }

        blockSequences.Append("1\n");

        var flowSequences = new string('[', Depth) + "1" + new string(']', Depth) + "\n";

        var flowMaps = new StringBuilder();
        for (var level = 0; level < Depth; level++)
        {
            flowMaps.Append("{key: ");
        }

        flowMaps.Append('1').Append('}', Depth).Append('\n');

        AssertRoundTrip(blockMaps.ToString(), Depth + 1);
        AssertRoundTrip(blockSequences.ToString(), Depth);
        AssertRoundTrip(flowSequences, Depth);
        AssertRoundTrip(flowMaps.ToString(), Depth);
    }

    private static void AssertRoundTrip(string yaml, int expectedDepth)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expectedDepth, DepthOf(document.RootElement));

        var written = YamlDocument.EncodeToString(document.RootElement);
        using var restored = YamlDocument.Parse(written);

        Assert.Equal(expectedDepth, DepthOf(restored.RootElement));
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), restored.RootElement.AsValue()),
            "The document read back from the written YAML is not equal."
        );
    }

    private static int DepthOf(DElement element)
    {
        var depth = 0;
        while (true)
        {
            if (element.Token.Type == DTokenType.Array)
            {
                element = element.AsArray()[0].AsElement();
            }
            else if (element.Token.Type == DTokenType.Map)
            {
                element = element.AsMap()[0].Value.AsElement();
            }
            else
            {
                return depth;
            }

            depth++;
        }
    }
}
