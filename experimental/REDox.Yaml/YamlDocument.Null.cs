namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    private uint CreateNullToken() => AllocToken(DToken.Make(DTokenVariant.Null, payload: 0));

    private bool TryCreateNullToken(ParsedScalarIntermediate scalar, out uint tokenId)
    {
        if (YamlSyntax.IsNullScalar(_utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start)))
        {
            _utf8TextForTokens.TruncateTo(scalar.Start);

            tokenId = CreateNullToken();

            return true;
        }

        tokenId = 0;

        return false;
    }
}