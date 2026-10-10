using System.Text;

namespace REDox.Yaml.Tests;

public sealed class DeepNestingTest
{
    private const int NestDepth = 300;

    [Fact]
    public void DeeplyNestedCollections_RoundTrip()
    {
        var blockMaps = new StringBuilder();
        for (var level = 0; level < NestDepth; level++)
        {
            blockMaps.Append(' ', level * 2).Append("key:\n");
        }

        blockMaps.Append(' ', NestDepth * 2).Append("leaf: 1\n");

        var blockSequences = new StringBuilder();
        for (var level = 0; level < NestDepth; level++)
        {
            blockSequences.Append("- ");
        }

        blockSequences.Append("1\n");

        var flowSequences = new string('[', NestDepth) + "1" + new string(']', NestDepth) + "\n";

        var flowMaps = new StringBuilder();
        for (var level = 0; level < NestDepth; level++)
        {
            flowMaps.Append("{key: ");
        }

        flowMaps.Append('1').Append('}', NestDepth).Append('\n');

        AssertDeeplyNestedYamlRoundTripsAtDepth(blockMaps.ToString(), NestDepth + 1);
        AssertDeeplyNestedYamlRoundTripsAtDepth(blockSequences.ToString(), NestDepth);
        AssertDeeplyNestedYamlRoundTripsAtDepth(flowSequences, NestDepth);
        AssertDeeplyNestedYamlRoundTripsAtDepth(flowMaps.ToString(), NestDepth);
    }

    private static void AssertDeeplyNestedYamlRoundTripsAtDepth(string yaml, int expectedDepth)
    {
        using var document = YamlDocument.Parse(yaml);

        Assert.Equal(expectedDepth, GetNestedCollectionDepth(document.RootElement));

        var written = YamlDocument.EncodeToString(document.RootElement);

        using var restored = YamlDocument.Parse(written);

        Assert.Equal(expectedDepth, GetNestedCollectionDepth(restored.RootElement));
        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), restored.RootElement.AsValue()),
            "The document read back from the written YAML is not equal."
        );
    }

    private static int GetNestedCollectionDepth(DElement element)
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