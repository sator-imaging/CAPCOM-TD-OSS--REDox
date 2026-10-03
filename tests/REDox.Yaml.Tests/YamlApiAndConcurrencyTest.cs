using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlApiAndConcurrencyTest
{
    private const string Sample =
        "name: \"a: b\"\nlist:\n  - 1\n  - true\n  - null\nnested:\n  key: 1.5\n";

    private const string SampleJson =
        "{\"name\":\"a: b\",\"list\":[1,true,null],\"nested\":{\"key\":1.5}}";

    private static readonly YamlWriteOptions Options = new YamlWriteOptions { NewLine = "\n" };

    private static void AssertIsSample(YamlDocument document)
    {
        using var expected = REDox.Json.JsonDocument.Parse(SampleJson);
        Assert.True(
            DValue.DeepEquals(expected.RootElement.AsValue(), document.RootElement.AsValue())
        );
    }

    // Public entry points: reading

    // A stream with no document is valid YAML, not an error (see YamlTestHelper.IsEmptyDocument for what is checked
    // and why it cannot be more). The first three are yaml-test-suite cases whose in.json is empty: 8G76, 98YD and
    // AVM7.
    [Theory]
    [InlineData("  # Comment\n   \n\n\n")]
    [InlineData("# Comment only.\n")]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \n")]
    [InlineData("# one\n# two\n")]
    public void StreamWithoutDocumentIsASingleNull(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        YamlTestHelper.IsEmptyDocument(document);
    }

    [Fact]
    public void ParseFromString()
    {
        using var document = YamlDocument.Parse(Sample);

        AssertIsSample(document);
    }

    [Fact]
    public void ParseFromStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample));
        using var document = YamlDocument.Parse(stream);

        AssertIsSample(document);
    }

    [Fact]
    public void ParseFromByteArray()
    {
        using var document = YamlDocument.Parse(Encoding.UTF8.GetBytes(Sample));

        AssertIsSample(document);
    }

    [Fact]
    public void ParseFromReadOnlyMemory()
    {
        ReadOnlyMemory<byte> memory = Encoding.UTF8.GetBytes(Sample);
        using var document = YamlDocument.Parse(memory);

        AssertIsSample(document);
    }

    [Fact]
    public void TryParseSucceeds()
    {
        var parsed = YamlDocument.TryParse(Encoding.UTF8.GetBytes(Sample), out var document);

        Assert.True(parsed);
        Assert.NotNull(document);
        using (document)
        {
            AssertIsSample(document);
        }
    }

    [Fact]
    public void TryParseFailsWithoutThrowing()
    {
        var parsed = YamlDocument.TryParse("a: [1, 2"u8, out var document);

        Assert.False(parsed);
        Assert.Null(document);
    }

    // The parameters are not nullable, so passing null is a compiler warning: it is switched off only for the three
    // calls, where the null is the point of the test. The check is for the callers that do not see the warning.
    [Fact]
    public void ParseRejectsNullArguments()
    {
        string? text = null;
        byte[]? bytes = null;
        Stream? stream = null;

#pragma warning disable CS8604
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(text));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(bytes));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(stream));
#pragma warning restore CS8604
    }

    // Public entry points: writing. Every one writes what EncodeToString writes.

    [Fact]
    public void EncodeToByteArray()
    {
        using var document = YamlDocument.Parse(Sample);

        var bytes = YamlDocument.Encode(document.RootElement, Options);

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, Options),
            Encoding.UTF8.GetString(bytes)
        );
    }

    [Fact]
    public void EncodeToStream()
    {
        using var document = YamlDocument.Parse(Sample);
        using var stream = new MemoryStream();

        YamlDocument.EncodeTo(document.RootElement, stream, Options);

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, Options),
            Encoding.UTF8.GetString(stream.ToArray())
        );
    }

    [Fact]
    public void EncodeToBufferWriter()
    {
        using var document = YamlDocument.Parse(Sample);
        var buffer = new ArrayBufferWriter<byte>();

        YamlDocument.EncodeTo(document.RootElement, buffer, Options);

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, Options),
            Encoding.UTF8.GetString(buffer.WrittenSpan)
        );
    }

    [Fact]
    public void EveryEntryPointWritesTheSameValueBack()
    {
        using var document = YamlDocument.Parse(Sample);

        using var fromBytes = YamlDocument.Parse(YamlDocument.Encode(document.RootElement));

        AssertIsSample(fromBytes);
    }

    // Concurrency: the encoders and the word sets are static and shared by every document.

    [Fact]
    public void ParseAndEncodeInParallelGiveTheSameResultAsOneThread()
    {
        var expected = SingleThreaded();

        var results = Enumerable
            .Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => Enumerable.Range(0, 50).Select(_ => SingleThreaded()).Distinct().ToArray())
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
            .Range(0, 16)
            .Select(worker =>
                Task.Run(() =>
                {
                    for (var i = 0; i < 200; i++)
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

    // Serializer: an object goes to YAML and comes back through the core's conversion.

    public enum Color
    {
        Red,
        Green,
    }

    public sealed class Address
    {
        public string City { get; set; } = "";
        public int Number { get; set; }
    }

    public sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public double Score { get; set; }
        public bool Active { get; set; }
        public int? Optional { get; set; }
        public Color Favorite { get; set; }
        public Address? Address { get; set; }
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
    }

    private static Person CreatePerson() =>
        new()
        {
            Name = "a: b\nsecond line",
            Age = 42,
            Score = 1.5,
            Active = true,
            Optional = null,
            Favorite = Color.Green,
            Address = new Address { City = "true", Number = 7 },
            Tags = new List<string> { "x", "123", "", "null" },
            Counts = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 },
        };

    [Theory]
    [InlineData(YamlStringMode.Quote)]
    [InlineData(YamlStringMode.Multiline)]
    public void ObjectRoundTripsThroughYaml(YamlStringMode mode)
    {
        var source = CreatePerson();

        var yaml = YamlDocument.EncodeToString(
            DValue.From(source).AsElement(),
            new YamlWriteOptions { StringMode = mode }
        );
        using var document = YamlDocument.Parse(yaml);
        var restored = ToObject<Person>(document.RootElement);

        Assert.Equal(source.Name, restored.Name);
        Assert.Equal(source.Age, restored.Age);
        Assert.Equal(source.Score, restored.Score);
        Assert.Equal(source.Active, restored.Active);
        Assert.Null(restored.Optional);
        Assert.Equal(source.Favorite, restored.Favorite);
        Debug.Assert(source.Address != null); // CreatePerson sets the address.
        Debug.Assert(restored.Address != null); // The YAML has the address, so the object has it.
        Assert.Equal(source.Address.City, restored.Address.City);
        Assert.Equal(source.Address.Number, restored.Address.Number);
        Assert.Equal(source.Tags, restored.Tags);
        Assert.Equal(source.Counts, restored.Counts);
    }

    [Fact]
    public void ObjectFromHandWrittenYaml()
    {
        const string yaml =
            "Name: Ann\nAge: 30\nTags:\n  - a\n  - b\nAddress:\n  City: Oslo\n  Number: 3\n";
        using var document = YamlDocument.Parse(yaml);

        var person = ToObject<Person>(document.RootElement);

        Assert.Equal("Ann", person.Name);
        Assert.Equal(30, person.Age);
        Assert.Equal(new[] { "a", "b" }, person.Tags);
        Debug.Assert(person.Address != null); // The YAML above has an Address map.
        Assert.Equal("Oslo", person.Address.City);
    }

    [Fact]
    public void ListOfObjectsRoundTrips()
    {
        var source = new List<Address>
        {
            new() { City = "A", Number = 1 },
            new() { City = "yes", Number = 2 },
        };

        var yaml = YamlDocument.EncodeToString(DValue.From(source).AsElement());
        using var document = YamlDocument.Parse(yaml);
        var restored = ToObject<List<Address>>(document.RootElement);

        Assert.Equal(new[] { "A", "yes" }, restored.Select(a => a.City));
        Assert.Equal(new[] { 1, 2 }, restored.Select(a => a.Number));
    }

    private static T ToObject<T>(DElement element)
        where T : class
    {
        var value = element.ToObject(typeof(T));
        Debug.Assert(value is T); // ToObject returns null only for a null value, and every value here is an object or a list.
        return (T)value;
    }
}
