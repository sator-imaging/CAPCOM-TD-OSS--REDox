using System;
using REDox;

namespace REDox.Yaml.Tests;

public sealed partial class YamlPrimitiveValueTest
{
    private static readonly byte[] Bytes = [1, 2, 3];

    // Binary data (a byte string, of any kind, and a Guid, which is one) has no YAML form without the !!binary tag, and
    // tags are not supported: writing it throws a NotSupportedException that names the kind.
    public static TheoryData<string> GetBinaryValues() =>
        new() { "default", "base64", "base64url", "base16", "raw", "guid" };

    private static DValue CreateBinary(string name) =>
        name switch
        {
            "default" => DValue.Create(Bytes, ByteStringKind.Default),
            "base64" => DValue.Create(Bytes, ByteStringKind.Base64),
            "base64url" => DValue.Create(Bytes, ByteStringKind.Base64Url),
            "base16" => DValue.Create(Bytes, ByteStringKind.Base16),
            "raw" => DValue.Create(Bytes, ByteStringKind.Raw),
            _ => DValue.Create(Guid.NewGuid()),
        };

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryValueIsRefused(string name)
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeToString(CreateBinary(name).AsElement())
        );

        Assert.Contains("ByteString", exception.Message);
    }

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryValueInsideACollectionIsRefused(string name)
    {
        var map = new DObject();
        map.Add("before", DValue.Create(1));
        map.Add("data", CreateBinary(name));
        var list = new DArray(DValue.Create(1), CreateBinary(name));

        Assert.Throws<NotSupportedException>(() => YamlDocument.EncodeToString(map.AsElement()));
        Assert.Throws<NotSupportedException>(() => YamlDocument.EncodeToString(list.AsElement()));
    }

    [Theory]
    [MemberData(nameof(GetBinaryValues))]
    public void BinaryKeyIsRefused(string name)
    {
        var map = new DMap();
        map.Add(CreateBinary(name), DValue.Create("value"));

        Assert.Throws<NotSupportedException>(() => YamlDocument.EncodeToString(map.AsElement()));
    }

    // Nothing is written to a stream or buffer for a value that is refused before the first line.
    [Fact]
    public void RefusedBinaryValueWritesNothingToAStream()
    {
        using var stream = new System.IO.MemoryStream();

        Assert.Throws<NotSupportedException>(() =>
            YamlDocument.EncodeTo(CreateBinary("default").AsElement(), stream)
        );

        Assert.Equal(0, stream.Length);
    }
}
