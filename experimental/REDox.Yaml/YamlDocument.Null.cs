namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private uint ParseNull() => AllocToken(DToken.Make(DTokenVariant.Null, payload: 0));

    private bool TryParseNull(ParsedScalar scalar, out uint tokenId)
    {
        if (YamlSyntax.IsNullScalar(_utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start)))
        {
            _utf8TextForTokens.TruncateTo(scalar.Start);

            tokenId = ParseNull();

            return true;
        }

        tokenId = 0;

        return false;
    }
}