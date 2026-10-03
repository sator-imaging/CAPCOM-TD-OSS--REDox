// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Yaml;

/// <summary>
/// How YAML text is read by <see cref="YamlDocument.Parse(string, SerializerSettings?, YamlDocumentOptions)" /> and
/// the other reading methods of <see cref="YamlDocument" />.
/// </summary>
public readonly record struct YamlDocumentOptions
{
    /// <summary>
    /// Options with the time zone that is assumed for a timestamp that has no offset. Comments and white space are
    /// kept and every map key is read as a string, unless the arguments say otherwise: this is what a document that a
    /// person writes and edits needs. The other options have their defaults and can be set as usual. (The options
    /// that are not made by this constructor, such as <c>default</c>, have both off.)
    /// </summary>
    /// <param name="assumedTimeZone">The value of <see cref="AssumedTimeZone" />.</param>
    /// <param name="preserveTrivia">The value of <see cref="PreserveTrivia" />.</param>
    /// <param name="readMapKeyAsString">The value of <see cref="ReadMapKeyAsString" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assumedTimeZone" /> is null.</exception>
    public YamlDocumentOptions(
        TimeZoneInfo assumedTimeZone,
        bool preserveTrivia = true,
        bool readMapKeyAsString = true
    )
    {
        ArgumentNullException.ThrowIfNull(assumedTimeZone);
        AssumedTimeZone = assumedTimeZone;
        PreserveTrivia = preserveTrivia;
        ReadMapKeyAsString = readMapKeyAsString;
    }

    /// <summary>
    /// Validates the values while reading. When true, a value or a structure that is not valid fails with a
    /// <see cref="DocumentParseException" /> instead of being accepted. When false, the text is read without these
    /// checks.
    /// </summary>
    public bool EnableValueValidation { get; init; }

    /// <summary>
    /// Reads every map key as a string, the way it is written: <c>1: a</c> has the key "1", <c>null: a</c> the key
    /// "null" and <c>2001-12-14: a</c> the key "2001-12-14". By default a plain key is resolved like a plain value, so
    /// <c>1: a</c> has the integer 1 as its key. A key with no text (<c>? </c> followed by nothing, or an empty key)
    /// has no string to read, so it fails with a <see cref="DocumentParseException" />.
    /// </summary>
    public bool ReadMapKeyAsString { get; init; }

    /// <summary>
    /// Keeps the comments and the white space around the values, so that writing the document with
    /// <see cref="YamlWriteOptions.PreserveTrivia" /> gives them back as they were written.
    /// </summary>
    public bool PreserveTrivia { get; init; }

    /// <summary>
    /// The deepest nesting that is read. The root is at depth 0, every level below it is one deeper, and a scalar
    /// counts as a level: in "[[1]]" the outer list is at depth 0, the inner list at 1 and the number at 2, so it needs a
    /// limit of at least 2. Text that is deeper fails with a
    /// <see cref="DocumentParseException" />. 0, the default, means no limit.
    /// </summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Converts text in UTF-16 or UTF-32 that starts with a byte order mark to UTF-8 while reading. A UTF-8 byte
    /// order mark is always skipped, whatever this is. When false, text in another encoding is not converted and
    /// fails with a <see cref="DocumentParseException" />.
    /// </summary>
    public bool AutoDetectEncoding { get; init; }

    /// <summary>
    /// The indentation level of the root node, from which the explicit indentation indicator of a block scalar at the
    /// root is counted: in "|2-" the content is indented by 2 more than this level. The default is 0, which is what libyaml and
    /// the libraries built on it (such as PyYAML) use, so "|2-" followed by a line with 4 spaces reads as a string
    /// with 2 leading spaces. The value the YAML 1.2 specification gives is -1: the root node is the block node
    /// s-l+block-node(-1, block-in) of l-bare-document, and the content indentation of a block scalar is n+m (8.1.1.1,
    /// where n is the indentation of the parent node and m is the indicator), which is -1+2 = 1, so the same text
    /// reads as a string with 3 leading spaces. Set -1 to follow the specification. Block scalars that are not at the
    /// root are not affected: their parent has an indentation of 0 or more, and the two readings agree. Only -1 and
    /// larger values are accepted.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than -1.</exception>
    public int RootIndentationLevel
    {
        get;
        init
        {
            if (value < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "RootIndentationLevel must be -1 or larger."
                );
            }

            field = value;
        }
    }

    /// <summary>
    /// The time zone that is assumed for a timestamp that has no offset, such as "2001-12-14 21:59:43". It is used when
    /// the value is read as a <see cref="DateTimeOffset" />; the written text is never changed. A timestamp with
    /// an offset keeps its own. The default, null, is UTC, which is what YAML 1.1 specifies; it is never the time zone
    /// of the machine.
    /// </summary>
    public TimeZoneInfo? AssumedTimeZone { get; init; }
}
