using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using REDox;
using REDox.Serialization;

namespace REDox.Yaml.Tests;

public sealed partial class YamlApiTest
{
    // Concurrency: the encoders and the word sets are static and shared by every document.

    [Fact]
    public void ParseAndEncodeInParallelGiveTheSameResultAsOneThread()
    {
        var expected = SingleThreaded();

        var results = Enumerable
            .Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ =>
                Enumerable.Range(0, 100).Select(_ => SingleThreaded()).Distinct().ToArray()
            )
            .ToArray();

        Assert.All(results, texts => Assert.Equal(new[] { expected }, texts));
    }

    [Fact]
    public async Task StringsThatNeedQuotingAreWrittenInParallelWithoutMixingThemUp()
    {
        var values = new[]
        {
            "plain",
            "true",
            "123",
            "a: b",
            "line\nbreak",
            "tab\there",
            "emoji " + char.ConvertFromUtf32(0x1f600),
            "2001-12-14",
            "yes",
            "",
        };

        var tasks = Enumerable
            .Range(0, 8)
            .Select(worker =>
                Task.Run(() =>
                {
                    for (var i = 0; i < 100; i++)
                    {
                        var value = values[(worker + i) % values.Length];
                        var yaml = YamlDocument.EncodeToString(
                            DValue.Create(value).AsElement(),
                            Options
                        );
                        using var parsed = YamlDocument.Parse(yaml);
                        Assert.Equal(value, parsed.RootElement.GetString());
                    }
                })
            );

        await Task.WhenAll(tasks);
    }

    private static string SingleThreaded()
    {
        using var document = YamlDocument.Parse(Sample);
        return YamlDocument.EncodeToString(document.RootElement, Options);
    }

    // The same document, read by many threads at once: nothing is written to it after it is parsed, so every thread
    // sees every value.
    [Fact]
    public void OneDocumentReadByManyThreadsGivesTheSameValues()
    {
        using var document = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);

        Parallel.For(
            0,
            500,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            _ =>
            {
                Assert.Equal("text", document.RootElement.GetProperty("name").GetString());
                Assert.Equal(42, document.RootElement.GetProperty("number").GetInt64());
                Assert.Equal(1.5, document.RootElement.GetProperty("float").GetDouble());
                Assert.True(document.RootElement.GetProperty("flag").GetBoolean());
                Assert.Equal(
                    "two",
                    document.RootElement.GetProperty("list").AsArray()[1].AsElement().GetString()
                );
                Assert.Equal(
                    "value",
                    document.RootElement.GetProperty("nested").GetProperty("key").GetString()
                );
                Assert.Equal("one\ntwo\n", document.RootElement.GetProperty("block").GetString());
            }
        );
    }

    // The same element, written by many threads at once.
    [Fact]
    public void OneElementWrittenByManyThreadsGivesTheSameText()
    {
        using var document = YamlDocument.Parse(DuplicateSource, options: TriviaParseOptions);
        var expected = TextOf(document);

        var texts = Enumerable
            .Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .SelectMany(_ => Enumerable.Range(0, 100).Select(_ => TextOf(document)))
            .Distinct()
            .ToArray();

        Assert.Equal(new[] { expected }, texts);
    }

    // The automatic parallel deserialization of the README: the elements of a large array are made into objects by
    // many threads, which all read the one document. The result is the same as the sequential one.
    [Fact]
    public void ParallelDeserializationOfOneDocumentGivesTheSequentialResult()
    {
        var persons = Enumerable
            .Range(0, 3000)
            .Select(index => new Person
            {
                Name = $"person {index}: first\nsecond",
                Age = index,
                Score = index + 0.5,
                Active = index % 2 == 0,
                Optional = index % 3 == 0 ? index : null,
                Favorite = index % 2 == 0 ? Color.Green : Color.Red,
                Address = new Address { City = $"city {index}", Number = index },
                Tags = new List<string> { $"tag{index}", "123", "" },
                Counts = new Dictionary<string, int> { ["a"] = index, ["b"] = -index },
            })
            .ToArray();
        var yaml = YamlDocument.EncodeToString(DValue.From(persons).AsElement());
        var parallel = new DoxSerializerSettings
        {
            ParallelOptions = new ParallelDeserializeOptions { ParallelDeserializeEnabled = true },
        };

        using var sequentialDocument = YamlDocument.Parse(yaml);
        using var parallelDocument = YamlDocument.Parse(yaml, parallel);
        var sequentialResult = ToObject<Person[]>(sequentialDocument.RootElement);
        var parallelResult = ToObject<Person[]>(parallelDocument.RootElement);

        Assert.Equal(persons.Length, parallelResult.Length);
        for (var index = 0; index < persons.Length; index++)
        {
            AssertSamePerson(persons[index], parallelResult[index]);
            AssertSamePerson(sequentialResult[index], parallelResult[index]);
        }
    }

    private static void AssertSamePerson(Person expected, Person actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Age, actual.Age);
        Assert.Equal(expected.Score, actual.Score);
        Assert.Equal(expected.Active, actual.Active);
        Assert.Equal(expected.Optional, actual.Optional);
        Assert.Equal(expected.Favorite, actual.Favorite);
        Assert.Equal(expected.Address?.City, actual.Address?.City);
        Assert.Equal(expected.Address?.Number, actual.Address?.Number);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.Counts, actual.Counts);
    }
}
