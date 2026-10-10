using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace REDox.Yaml.Tests;

public sealed partial class YamlDocumentTest
{
    private static void AssertYamlDocumentMatchesExpectedJsonDocument(
        YamlDocument parsedYamlDocument
    )
    {
        using var expectedJsonDocument = Json.JsonDocument.Parse(
            YamlTestFixture.ExpectedMixedScalarAndNestedCollectionsJson
        );

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

        YamlTestFixture.RandomizeArrayPoolBuffers(yaml);

        YamlTestFixture.AssertEmptyYamlDocumentHasNullRoot(document);

        var writtenYaml = YamlDocument.EncodeToString(
            document.RootElement,
            YamlTestFixture.LineFeedWriteOptions
        );

        // TODO: Core data model cannot classify empty document.
        Assert.Equal("null\n", writtenYaml);
        Assert.NotEqual(yaml, writtenYaml);
    }

    [Fact]
    public void StringInput_ParsesYaml()
    {
        using var document = YamlDocument.Parse(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        AssertYamlDocumentMatchesExpectedJsonDocument(document);
    }

    [Fact]
    public void StreamInput_ParsesYaml()
    {
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(YamlTestFixture.MixedScalarAndNestedCollectionsYaml)
        );

        using var document = YamlDocument.Parse(stream);

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        AssertYamlDocumentMatchesExpectedJsonDocument(document);
    }

    [Fact]
    public void ByteArrayInput_ParsesYaml()
    {
        using var document = YamlDocument.Parse(
            Encoding.UTF8.GetBytes(YamlTestFixture.MixedScalarAndNestedCollectionsYaml)
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        AssertYamlDocumentMatchesExpectedJsonDocument(document);
    }

    [Fact]
    public void ReadOnlyMemoryInput_ParsesYaml()
    {
        ReadOnlyMemory<byte> memory = Encoding.UTF8.GetBytes(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        using var document = YamlDocument.Parse(memory);

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        AssertYamlDocumentMatchesExpectedJsonDocument(document);
    }

    [Fact]
    public void ValidInput_TryParseSucceeds()
    {
        var parsed = YamlDocument.TryParse(
            Encoding.UTF8.GetBytes(YamlTestFixture.MixedScalarAndNestedCollectionsYaml),
            out var document
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        Assert.True(parsed);
        Assert.NotNull(document);

        using (document)
        {
            AssertYamlDocumentMatchesExpectedJsonDocument(document);
        }
    }

    [Fact]
    public void InvalidInput_TryParseReturnsFalseWithoutThrowing()
    {
        var parsed = YamlDocument.TryParse("a: [1, 2"u8.ToArray(), out var document);

        Assert.False(parsed);
        Assert.Null(document);
    }

    [Fact]
    public void NullArguments_ParseThrows()
    {
        string? text = null;
        byte[]? bytes = null;
        Stream? stream = null;

        // Parse API signature doesn't accept nullable values
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(text!));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(bytes!));
        Assert.Throws<ArgumentNullException>(() => YamlDocument.Parse(stream!));
    }

    [Fact]
    public void ByteArrayOutput_EncodesYaml()
    {
        using var document = YamlDocument.Parse(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        var bytes = YamlDocument.Encode(document.RootElement, YamlTestFixture.LineFeedWriteOptions);

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions),
            Encoding.UTF8.GetString(bytes)
        );
    }

    [Fact]
    public void StreamOutput_EncodesYaml()
    {
        using var document = YamlDocument.Parse(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        using var stream = new MemoryStream();

        YamlDocument.EncodeTo(document.RootElement, stream, YamlTestFixture.LineFeedWriteOptions);

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions),
            Encoding.UTF8.GetString(stream.ToArray())
        );
    }

    [Fact]
    public void BufferWriterOutput_EncodesYaml()
    {
        using var document = YamlDocument.Parse(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        var buffer = new ArrayBufferWriter<byte>();

        YamlDocument.EncodeTo(document.RootElement, buffer, YamlTestFixture.LineFeedWriteOptions);

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        Assert.Equal(
            YamlDocument.EncodeToString(document.RootElement, YamlTestFixture.LineFeedWriteOptions),
            Encoding.UTF8.GetString(buffer.WrittenSpan)
        );
    }

    [Fact]
    public void ByteArrayEncodingAndParsing_PreserveMixedScalarAndNestedCollectionValues()
    {
        using var document = YamlDocument.Parse(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        using var fromBytes = YamlDocument.Parse(YamlDocument.Encode(document.RootElement));

        YamlTestFixture.RandomizeArrayPoolBuffers(
            YamlTestFixture.MixedScalarAndNestedCollectionsYaml
        );

        AssertYamlDocumentMatchesExpectedJsonDocument(fromBytes);
    }

    [Theory]
    [InlineData(YamlStringStyle.Quote)]
    [InlineData(YamlStringStyle.Multiline)]
    public void ObjectSerialization_RoundTripsThroughYaml(YamlStringStyle mode)
    {
        var sourcePerson = YamlTestFixture.CreatePersonWithNestedAddressAndCollections();

        var yamlFromSource = YamlDocument.EncodeToString(
            DValue.From(sourcePerson).AsElement(),
            new YamlWriteOptions { StringStyle = mode }
        );

        YamlTestFixture.RandomizeArrayPoolBuffers(yamlFromSource);

        using var serializedYamlDocument = YamlDocument.Parse(yamlFromSource);

        YamlTestFixture.RandomizeArrayPoolBuffers(yamlFromSource);

        var restoredPersonFromSerializedYaml =
            YamlTestFixture.ToObjectOfTypePolyfill<YamlTestFixture.Person>(
                serializedYamlDocument.RootElement
            );

        Debug.Assert(sourcePerson.Address != null);
        Debug.Assert(restoredPersonFromSerializedYaml.Address != null);

        Assert.Equal(sourcePerson.Name, restoredPersonFromSerializedYaml.Name);
        Assert.Equal(sourcePerson.Age, restoredPersonFromSerializedYaml.Age);
        Assert.Equal(sourcePerson.Score, restoredPersonFromSerializedYaml.Score);
        Assert.Equal(sourcePerson.Active, restoredPersonFromSerializedYaml.Active);
        Assert.Null(restoredPersonFromSerializedYaml.Optional);
        Assert.Equal(sourcePerson.Favorite, restoredPersonFromSerializedYaml.Favorite);
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

        YamlTestFixture.RandomizeArrayPoolBuffers(yaml);

        var person = YamlTestFixture.ToObjectOfTypePolyfill<YamlTestFixture.Person>(
            document.RootElement
        );

        Debug.Assert(person.Address != null);

        Assert.Equal("Ann", person.Name);
        Assert.Equal(30, person.Age);
        Assert.Equal(new[] { "a", "b" }, person.Tags);
        Assert.Equal("Oslo", person.Address.City);
    }

    [Fact]
    public void ObjectList_RoundTripsThroughYaml()
    {
        var sourceAddresses = new List<YamlTestFixture.Address>
        {
            new() { City = "A", Number = 1 },
            new() { City = "yes", Number = 2 },
        };

        var yamlFromSource = YamlDocument.EncodeToString(DValue.From(sourceAddresses).AsElement());

        YamlTestFixture.RandomizeArrayPoolBuffers(yamlFromSource);

        using var serializedYamlDocument = YamlDocument.Parse(yamlFromSource);

        YamlTestFixture.RandomizeArrayPoolBuffers(yamlFromSource);

        var restoredAddressesFromYaml = YamlTestFixture.ToObjectOfTypePolyfill<
            List<YamlTestFixture.Address>
        >(serializedYamlDocument.RootElement);

        Assert.Equal(new[] { "A", "yes" }, restoredAddressesFromYaml.Select(static a => a.City));
        Assert.Equal(new[] { 1, 2 }, restoredAddressesFromYaml.Select(static a => a.Number));
    }
}