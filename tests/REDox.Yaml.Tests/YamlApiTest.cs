using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed partial class YamlApiTest
{
    private const string Sample =
        "name: \"a: b\"\nlist:\n  - 1\n  - true\n  - null\nnested:\n  key: 1.5\n";

    private const string SampleJson =
        "{\"name\":\"a: b\",\"list\":[1,true,null],\"nested\":{\"key\":1.5}}";

    private static readonly YamlWriteOptions Options = new() { NewLine = "\n" };

    private static void AssertIsSample(YamlDocument parsedYamlDocument)
    {
        using var expectedJsonDocument = Json.JsonDocument.Parse(SampleJson);

        Assert.True(
            DValue.DeepEquals(
                expectedJsonDocument.RootElement.AsValue(),
                parsedYamlDocument.RootElement.AsValue()
            )
        );
    }

    [Theory]
    [InlineData("  # Comment\n   \n\n\n")]
    [InlineData("# Comment only.\n")]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \n")]
    [InlineData("# one\n# two\n")]
    public void StreamWithoutDocument_ReturnsSingleNull(string yaml)
    {
        using var document = YamlDocument.Parse(yaml);

        YamlTestHelper.AssertEmptyYamlDocumentHasNullRoot(document);
    }

    [Fact]
    public void StringInput_ParsesYaml()
    {
        using var document = YamlDocument.Parse(Sample);

        AssertIsSample(document);
    }

    [Fact]
    public void StreamInput_ParsesYaml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample));
        using var document = YamlDocument.Parse(stream);

        AssertIsSample(document);
    }

    [Fact]
    public void ByteArrayInput_ParsesYaml()
    {
        using var document = YamlDocument.Parse(Encoding.UTF8.GetBytes(Sample));

        AssertIsSample(document);
    }

    [Fact]
    public void ReadOnlyMemoryInput_ParsesYaml()
    {
        ReadOnlyMemory<byte> memory = Encoding.UTF8.GetBytes(Sample);

        using var document = YamlDocument.Parse(memory);

        AssertIsSample(document);
    }

    [Fact]
    public void ValidInput_TryParseSucceeds()
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
    public void InvalidInput_TryParseReturnsFalseWithoutThrowing()
    {
        var parsed = YamlDocument.TryParse("a: [1, 2"u8, out var document);

        Assert.False(parsed);
        Assert.Null(document);
    }

    [Fact]
    public void NullArguments_ParseThrows()
    {
        string? text = null;
        byte[]? bytes = null;
        Stream? stream = null;

        // Parse API doesn't accept nullable values
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(text!));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(bytes!));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(stream!));
    }

    [Fact]
    public void ByteArrayOutput_EncodesYaml()
    {
        using var document = YamlDocument.Parse(Sample);

        var bytes = YamlDocument.Encode(document.RootElement, Options);

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, Options),
            Encoding.UTF8.GetString(bytes)
        );
    }

    [Fact]
    public void StreamOutput_EncodesYaml()
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
    public void BufferWriterOutput_EncodesYaml()
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
    public void EveryEntryPoint_RoundTripsSameValue()
    {
        using var document = YamlDocument.Parse(Sample);

        using var fromBytes = YamlDocument.Parse(YamlDocument.Encode(document.RootElement));

        AssertIsSample(fromBytes);
    }

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
        public List<string> Tags { get; set; } = [];
        public Dictionary<string, int> Counts { get; set; } = [];
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
            Tags = ["x", "123", "", "null"],
            Counts = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 },
        };

    [Theory]
    [InlineData(YamlStringStyle.Quote)]
    [InlineData(YamlStringStyle.Multiline)]
    public void ObjectSerialization_RoundTripsThroughYaml(YamlStringStyle mode)
    {
        var sourcePerson = CreatePerson();

        var yamlFromSource = YamlDocument.EncodeToString(
            DValue.From(sourcePerson).AsElement(),
            new YamlWriteOptions { StringStyle = mode }
        );

        using var serializedYamlDocument = YamlDocument.Parse(yamlFromSource);

        var restoredPersonFromSerializedYaml = ToObject<Person>(serializedYamlDocument.RootElement);

        Assert.Equal(sourcePerson.Name, restoredPersonFromSerializedYaml.Name);
        Assert.Equal(sourcePerson.Age, restoredPersonFromSerializedYaml.Age);
        Assert.Equal(sourcePerson.Score, restoredPersonFromSerializedYaml.Score);
        Assert.Equal(sourcePerson.Active, restoredPersonFromSerializedYaml.Active);
        Assert.Null(restoredPersonFromSerializedYaml.Optional);
        Assert.Equal(sourcePerson.Favorite, restoredPersonFromSerializedYaml.Favorite);
        Debug.Assert(sourcePerson.Address != null); // CreatePerson sets the address.
        Debug.Assert(restoredPersonFromSerializedYaml.Address != null); // The YAML has the address, so the object has it.
        Assert.Equal(sourcePerson.Address.City, restoredPersonFromSerializedYaml.Address.City);
        Assert.Equal(sourcePerson.Address.Number, restoredPersonFromSerializedYaml.Address.Number);
        Assert.Equal(sourcePerson.Tags, restoredPersonFromSerializedYaml.Tags);
        Assert.Equal(sourcePerson.Counts, restoredPersonFromSerializedYaml.Counts);
    }

    [Fact]
    public void HandWrittenYaml_DeserializesObject()
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
    public void ObjectList_RoundTripsThroughYaml()
    {
        var sourceAddresses = new List<Address>
        {
            new() { City = "A", Number = 1 },
            new() { City = "yes", Number = 2 },
        };

        var yamlFromSource = YamlDocument.EncodeToString(DValue.From(sourceAddresses).AsElement());

        using var serializedYamlDocument = YamlDocument.Parse(yamlFromSource);

        var restoredAddressesFromYaml = ToObject<List<Address>>(serializedYamlDocument.RootElement);

        Assert.Equal(new[] { "A", "yes" }, restoredAddressesFromYaml.Select(static a => a.City));
        Assert.Equal(new[] { 1, 2 }, restoredAddressesFromYaml.Select(static a => a.Number));
    }

    private static T ToObject<T>(DElement element)
        where T : class
    {
        var value = element.ToObject(typeof(T));

        Debug.Assert(value is T);

        return (T)value;
    }
}
