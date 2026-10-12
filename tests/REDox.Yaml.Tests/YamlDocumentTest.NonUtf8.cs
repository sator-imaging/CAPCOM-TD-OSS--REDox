using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace REDox.Yaml.Tests;

public sealed partial class YamlDocumentTest
{
    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16be")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public async Task NonUtf8InputParsedWhilePoolBuffersAreRandomized_PreservesValues(
        string encodingName
    )
    {
        var payload = new string('日', 200_000) + "é";
        var yaml = $"payload: \"{payload}\"\ncount: 42\n";
        var encoding = Encoding.GetEncoding(encodingName);
        var encodedYaml = encoding.GetPreamble().Concat(encoding.GetBytes(yaml)).ToArray();

        using var signal = new ManualResetEventSlim();

        var parseTask = Task.Run(
            () =>
            {
                signal.Wait();

                return YamlDocument.Parse(
                    encodedYaml,
                    options: new YamlDocumentOptions { AutoDetectEncoding = true }
                );
            },
            TestContext.Current.CancellationToken
        );

        signal.Set();

        do
        {
            YamlTestFixture.RandomizeArrayPoolBuffers(yaml);
            await Task.Yield();
        } while (!parseTask.IsCompleted);

        using var document = await parseTask;

        Assert.Equal(payload, document.RootElement.GetProperty("payload").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("count").GetInt64());
    }
}