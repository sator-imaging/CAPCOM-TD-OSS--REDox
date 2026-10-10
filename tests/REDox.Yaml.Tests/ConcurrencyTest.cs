using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using REDox.Serialization;

namespace REDox.Yaml.Tests;

public sealed class ConcurrencyTest
{
    [Fact]
    public void ParallelParseAndEncode_MatchesSingleThreadedResult()
    {
        static string ParseAndEncodeMixedScalarAndNestedCollectionsOnCurrentThread()
        {
            using var mixedScalarAndNestedCollectionsDocument = YamlDocument.Parse(
                YamlTestFixture.MixedScalarAndNestedCollectionsYaml
            );

            return YamlDocument.EncodeToString(
                mixedScalarAndNestedCollectionsDocument.RootElement,
                YamlTestFixture.LineFeedWriteOptions
            );
        }

        var expectedSingleThreadedYaml =
            ParseAndEncodeMixedScalarAndNestedCollectionsOnCurrentThread();

        var parallelYamlOutputs = Enumerable
            .Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ =>
                Enumerable
                    .Range(0, 100)
                    .Select(_ => ParseAndEncodeMixedScalarAndNestedCollectionsOnCurrentThread())
                    .Distinct()
                    .ToArray()
            )
            .ToArray();

        Assert.All(
            parallelYamlOutputs,
            workerYamlOutputs =>
                Assert.Equal(new[] { expectedSingleThreadedYaml }, workerYamlOutputs)
        );
    }

    [Fact]
    public async Task ParallelQuotedStringWrites_DoNotMixValues()
    {
        var quotedStringValues = new[]
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

        var writeTasks = Enumerable
            .Range(0, 8)
            .Select(worker =>
                Task.Run(() =>
                {
                    for (var writeIndex = 0; writeIndex < 100; writeIndex++)
                    {
                        var quotedStringValue = quotedStringValues[
                            (worker + writeIndex) % quotedStringValues.Length
                        ];

                        var encodedYaml = YamlDocument.EncodeToString(
                            DValue.Create(quotedStringValue).AsElement(),
                            YamlTestFixture.LineFeedWriteOptions
                        );

                        using var parsedYamlDocument = YamlDocument.Parse(encodedYaml);

                        Assert.Equal(quotedStringValue, parsedYamlDocument.RootElement.GetString());
                    }
                })
            );

        await Task.WhenAll(writeTasks);
    }

    [Fact]
    public void SharedDocumentRead_ReturnsSameValuesAcrossThreads()
    {
        using var sharedYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        Parallel.For(
            0,
            500,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            _ =>
            {
                Assert.Equal(
                    "text",
                    sharedYamlDocument.RootElement.GetProperty("name").GetString()
                );

                Assert.Equal(42, sharedYamlDocument.RootElement.GetProperty("number").GetInt64());
                Assert.Equal(1.5, sharedYamlDocument.RootElement.GetProperty("float").GetDouble());
                Assert.True(sharedYamlDocument.RootElement.GetProperty("flag").GetBoolean());
                Assert.Equal(
                    "two",
                    sharedYamlDocument
                        .RootElement.GetProperty("list")
                        .AsArray()[1]
                        .AsElement()
                        .GetString()
                );

                Assert.Equal(
                    "value",
                    sharedYamlDocument
                        .RootElement.GetProperty("nested")
                        .GetProperty("key")
                        .GetString()
                );

                Assert.Equal(
                    "one\ntwo\n",
                    sharedYamlDocument.RootElement.GetProperty("block").GetString()
                );
            }
        );
    }

    [Fact]
    public void SharedElementWrite_ReturnsSameTextAcrossThreads()
    {
        using var sharedYamlDocument = YamlDocument.Parse(
            YamlTestFixture.MixedScalarsNestedCollectionsAndTriviaYamlSource,
            options: YamlTestFixture.TriviaParseOptions
        );

        var expectedSingleThreadedYaml = YamlDocument.EncodeToString(
            sharedYamlDocument.RootElement,
            YamlTestFixture.TriviaWriteOptions
        );

        var parallelYamlOutputs = Enumerable
            .Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .SelectMany(_ =>
                Enumerable
                    .Range(0, 100)
                    .Select(_ =>
                        YamlDocument.EncodeToString(
                            sharedYamlDocument.RootElement,
                            YamlTestFixture.TriviaWriteOptions
                        )
                    )
            )
            .Distinct()
            .ToArray();

        Assert.Equal(new[] { expectedSingleThreadedYaml }, parallelYamlOutputs);
    }

    [Fact]
    public void ParallelDeserialization_MatchesSequentialResult()
    {
        var sourcePersons = Enumerable
            .Range(0, 3000)
            .Select(static index => new YamlTestFixture.Person
            {
                Name = $"person {index}: first\nsecond",
                Age = index,
                Score = index + 0.5,
                Active = index % 2 == 0,
                Optional = index % 3 == 0 ? index : null,
                Favorite = index % 2 == 0 ? YamlTestFixture.Color.Green : YamlTestFixture.Color.Red,
                Address = new YamlTestFixture.Address { City = $"city {index}", Number = index },
                Tags = [$"tag{index}", "123", ""],
                Counts = new Dictionary<string, int> { ["a"] = index, ["b"] = -index },
            })
            .ToArray();

        var encodedPersonsYaml = YamlDocument.EncodeToString(
            DValue.From(sourcePersons).AsElement()
        );

        var parallelDeserializeSettings = new DoxSerializerSettings
        {
            ParallelOptions = new ParallelDeserializeOptions { ParallelDeserializeEnabled = true },
        };

        using var sequentialDocument = YamlDocument.Parse(encodedPersonsYaml);
        using var parallelDocument = YamlDocument.Parse(
            encodedPersonsYaml,
            parallelDeserializeSettings
        );

        var sequentialResult = YamlTestFixture.ToObjectOfTypePolyfill<YamlTestFixture.Person[]>(
            sequentialDocument.RootElement
        );

        var parallelResult = YamlTestFixture.ToObjectOfTypePolyfill<YamlTestFixture.Person[]>(
            parallelDocument.RootElement
        );

        Assert.Equal(sourcePersons.Length, parallelResult.Length);

        for (var index = 0; index < sourcePersons.Length; index++)
        {
            AssertSamePerson(sourcePersons[index], parallelResult[index]);
            AssertSamePerson(sequentialResult[index], parallelResult[index]);

            static void AssertSamePerson(
                YamlTestFixture.Person expected,
                YamlTestFixture.Person actual
            )
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
    }
}