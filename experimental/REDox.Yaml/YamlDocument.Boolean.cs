namespace REDox.Yaml;

public sealed partial class YamlDocument
{
    // A boolean needs no text of its own, so the bytes of the scalar are dropped.
    private bool TryCreateBooleanToken(ParsedScalarIntermediate scalar, out uint tokenId)
    {
        var text = _utf8TextForTokens.GetWrittenSpanFromOffset(scalar.Start);
        if (YamlSyntax.TryParseBooleanScalar(text, out var value))
        {
            _utf8TextForTokens.TruncateTo(scalar.Start);

            tokenId = AllocToken(
                DToken.Make(
                    value ? DTokenVariant.BooleanTrue : DTokenVariant.BooleanFalse,
                    payload: 0
                )
            );

            return true;
        }

        tokenId = 0;

        return false;
    }
}